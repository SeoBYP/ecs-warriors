using System.Collections.Generic;
using System.Text;
using Simulation.Components;
using Unity.Entities;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace Benchmark
{
    /// <summary>
    /// 적 수를 순차로 바꿔가며 프레임타임 + 프로파일러 카운터를 자동 측정하는 스윕 하니스.
    /// 각 구간: 목표 수 세팅 → 실제 스폰/디스폰 완료 대기 → 워밍업 → N프레임 샘플 → [BENCH] 로그.
    /// 전 구간 종료 시 [BENCH-CSV] 블록으로 한 번에 출력한다.
    ///
    /// W5 확장 — "총 프레임타임"만으로는 단계별(①naive→③burst→⑤render) 스토리가 안 보인다.
    ///   그래서 ProfilerRecorder로 아래를 샘플 창 동안 같이 누적한다:
    ///   · main_ms  = CPU Main Thread Frame Time  → 병렬화(잡으로 빠지는지). naive는 total과 붙고, burst-job은 뚝 떨어짐
    ///   · gpu_ms   = GPU Frame Time              → 에디터에선 0일 수 있음(빌드에서 확보)
    ///   · <System>_ms = 각 ECS 시스템의 CPU 총비용 → 근접탐색 실비용을 프레임이 아니라 시스템 단위로 격리
    ///                   (이름이 같은 모든 카테고리 = Scripts[스케줄] + Burst Jobs[연산] + Managed Jobs 합산.
    ///                    Burst on/off 브랜치가 마커 카테고리를 바꿔도 총비용은 일관되게 잡힌다)
    ///   · brg_draws = BRG Draw Calls Count       → 1만 엔티티가 몇 개의 드로우콜로 접히는가(Entities Graphics 스토리)
    ///   · setpass / tris                          → 보조 지표
    ///
    /// 주의:
    ///   - 에디터가 비포커스면 Unity가 게임 루프를 스로틀 → 그 프레임 제외(Application.isFocused).
    ///   - 시간 카운터는 ns → ×1e-6로 ms 변환. count 카운터는 그대로.
    ///   - VSync가 켜져 있으면 main_ms에 대기가 섞여 부풀어진다 → Start에서 경고.
    ///
    /// 방법론: 각 벤치 브랜치가 동일 프로토콜(같은 Counts·워밍업·샘플 수)로 돌므로
    ///        절대값보다 "같은 조건에서의 단계별 A/B와 기울기"가 유효한 비교 대상이다.
    /// </summary>
    public class BenchmarkHarness : MonoBehaviour
    {
        [Tooltip("측정 구분 라벨 (naive / ecs-single / burst-job / grid …)")]
        public string Label = "grid";

        [Tooltip("측정할 적 수 구간 (오름차순 권장)")]
        public int[] Counts = { 1000, 2500, 5000, 10000 };

        [Tooltip("구간마다 샘플 전 건너뛸 프레임 (스폰·수렴·Burst 워밍업)")]
        public int WarmupFrames = 300;

        [Tooltip("구간마다 평균을 낼 샘플 프레임 수")]
        public int SampleFrames = 120;

        [Tooltip("격리 측정할 ECS 시스템의 마커 이름. 이름이 같은 모든 카테고리를 합산한다. 브랜치에 없으면 0.")]
        public string[] TrackedSystems =
        {
            "Default World Simulation.Systems.SpatialHashSystem",
            "Default World Simulation.Systems.MovementSystem",
        };

        readonly List<float> _samples = new List<float>();
        readonly List<string> _csv = new List<string>();

        // 프레임 레벨 레코더 (Render 카테고리 — 항상 존재)
        ProfilerRecorder _mainMs, _gpuMs, _brgDraws, _setPass, _tris;
        // 시스템 레벨 레코더 — 시스템별로 "이름이 같은 모든 카테고리" 마커들. 시스템이 한 번은 돌아야 등록됨(지연 생성).
        List<ProfilerRecorder>[] _sysRecs;
        bool _sysReady;

        // 스텝 누적기
        double _mainSum, _gpuSum, _trisSum;
        double[] _sysSum;
        long _drawSum, _setPassSum;
        int _profN;

        int _step = -1;
        int _warmupLeft;
        bool _done;
        EntityManager _em;
        bool _ready;

        void OnEnable()
        {
            _mainMs   = ProfilerRecorder.StartNew(ProfilerCategory.Render, "CPU Main Thread Frame Time");
            _gpuMs    = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GPU Frame Time");
            _brgDraws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "BRG Draw Calls Count");
            _setPass  = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _tris     = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");

            int n = TrackedSystems != null ? TrackedSystems.Length : 0;
            _sysRecs = new List<ProfilerRecorder>[n];
            _sysSum  = new double[n];
        }

        void OnDisable()
        {
            _mainMs.Dispose(); _gpuMs.Dispose(); _brgDraws.Dispose(); _setPass.Dispose(); _tris.Dispose();
            if (_sysRecs != null)
                foreach (var list in _sysRecs)
                    if (list != null)
                        foreach (var r in list) r.Dispose();
            _sysReady = false;
        }

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            _em = world.EntityManager;
            _ready = true;

            Application.targetFrameRate = -1;   // 프레임 언캡 (비영구)
            if (QualitySettings.vSyncCount != 0)
                Debug.LogWarning("[BENCH] VSync가 켜져 있습니다 → main_ms에 대기가 섞여 부풀어집니다. Quality에서 VSync Count=Don't Sync 권장.");

            var head = new StringBuilder("label,enemies,avg_ms,p95_ms,avg_fps,main_ms,gpu_ms");
            for (int i = 0; i < _sysRecs.Length; i++) head.Append(',').Append(ShortName(TrackedSystems[i])).Append("_ms");
            head.Append(",brg_draws,setpass,tris");
            _csv.Add(head.ToString());
        }

        void Update()
        {
            if (_done || !_ready) return;
            if (!Application.isFocused) return;      // 스로틀 프레임 제외

            TryInitSystemRecorders();

            // SubScene 비동기 로드 대기
            if (!TryGetConfigEntity(out var cfgEntity)) return;

            if (_step < 0) { BeginStep(0, cfgEntity); return; }

            // 목표 수만큼 실제로 스폰/디스폰 될 때까지 대기
            if (CountEnemies() != Counts[_step]) return;

            if (_warmupLeft > 0) { _warmupLeft--; return; }

            // 샘플 프레임: 프레임타임 + 프로파일러 카운터 동시 누적
            _samples.Add(Time.unscaledDeltaTime * 1000f);
            _mainSum    += Ns2Ms(_mainMs.LastValue);
            _gpuSum     += Ns2Ms(_gpuMs.LastValue);
            _drawSum    += _brgDraws.LastValue;
            _setPassSum += _setPass.LastValue;
            _trisSum    += _tris.LastValue;
            if (_sysReady)
                for (int i = 0; i < _sysRecs.Length; i++)
                {
                    double v = 0;
                    foreach (var r in _sysRecs[i]) v += Ns2Ms(r.LastValue);   // 같은 이름의 모든 카테고리 합산
                    _sysSum[i] += v;
                }
            _profN++;

            if (_samples.Count < SampleFrames) return;

            ReportStep(Counts[_step]);

            if (_step + 1 < Counts.Length) BeginStep(_step + 1, cfgEntity);
            else { DumpCsv(); _done = true; }
        }

        // 시스템 마커는 시스템이 한 번은 실행돼야 등록된다 → 모든 추적 시스템이 최소 1개 마커를 잡을 때까지 매 프레임 시도
        void TryInitSystemRecorders()
        {
            if (_sysReady || _sysRecs.Length == 0) return;

            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);

            bool all = true;
            for (int i = 0; i < _sysRecs.Length; i++)
            {
                if (_sysRecs[i] != null && _sysRecs[i].Count > 0) continue;   // 이미 잡음

                var list = new List<ProfilerRecorder>();
                foreach (var h in handles)
                {
                    var d = ProfilerRecorderHandle.GetDescription(h);
                    if (d.Name == TrackedSystems[i])
                        list.Add(ProfilerRecorder.StartNew(d.Category, d.Name));   // 이 이름의 모든 카테고리
                }
                _sysRecs[i] = list;
                if (list.Count == 0) all = false;
            }
            _sysReady = all;
        }

        void BeginStep(int i, Entity cfgEntity)
        {
            _step = i;
            _warmupLeft = WarmupFrames;
            _samples.Clear();
            _mainSum = _gpuSum = _trisSum = 0; _drawSum = _setPassSum = 0; _profN = 0;
            for (int k = 0; k < _sysSum.Length; k++) _sysSum[k] = 0;

            var cfg = _em.GetComponentData<SpawnConfig>(cfgEntity);
            cfg.Count = Counts[i];
            _em.SetComponentData(cfgEntity, cfg);     // SpawnSystem이 이 목표에 맞춰 스폰/디스폰
        }

        void ReportStep(int enemies)
        {
            _samples.Sort();
            float sum = 0f;
            foreach (var s in _samples) sum += s;

            float avg = sum / _samples.Count;
            float p95 = _samples[Mathf.Clamp(Mathf.RoundToInt(_samples.Count * 0.95f) - 1, 0, _samples.Count - 1)];
            float fps = 1000f / avg;

            int n = Mathf.Max(_profN, 1);
            double main = _mainSum / n, gpu = _gpuSum / n, tris = _trisSum / n;
            long draws = _drawSum / n, setpass = _setPassSum / n;

            var row = new StringBuilder();
            row.Append(Label).Append(',').Append(enemies).Append(',')
               .Append(avg.ToString("0.00")).Append(',').Append(p95.ToString("0.00")).Append(',')
               .Append(fps.ToString("0.0")).Append(',')
               .Append(main.ToString("0.00")).Append(',').Append(gpu.ToString("0.00"));

            var sysLog = new StringBuilder();
            for (int i = 0; i < _sysRecs.Length; i++)
            {
                double v = _sysSum[i] / n;
                row.Append(',').Append(v.ToString("0.000"));
                sysLog.Append(' ').Append(ShortName(TrackedSystems[i])).Append('=').Append(v.ToString("0.000")).Append("ms");
            }
            row.Append(',').Append(draws).Append(',').Append(setpass).Append(',').Append((long)tris);

            Debug.Log($"[BENCH] label={Label} enemies={enemies} samples={_samples.Count} avg={avg:0.00}ms " +
                      $"p95={p95:0.00}ms fps={fps:0.0} main={main:0.00}ms gpu={gpu:0.00}ms{sysLog} " +
                      $"brgDraws={draws} setpass={setpass} sysReady={_sysReady}");

            _csv.Add(row.ToString());
        }

        void DumpCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[BENCH-CSV]");
            foreach (var line in _csv) sb.AppendLine(line);
            Debug.Log(sb.ToString());
        }

        bool TryGetConfigEntity(out Entity e)
        {
            e = Entity.Null;
            var q = _em.CreateEntityQuery(ComponentType.ReadWrite<SpawnConfig>());
            if (q.IsEmpty) return false;
            e = q.GetSingletonEntity();
            return true;
        }

        int CountEnemies()
        {
            var q = _em.CreateEntityQuery(ComponentType.ReadOnly<Enemy>());
            return q.CalculateEntityCount();
        }

        static double Ns2Ms(long ns) => ns * 1e-6;

        // "Default World Simulation.Systems.SpatialHashSystem" → "SpatialHashSystem"
        static string ShortName(string fullMarker)
        {
            int dot = fullMarker.LastIndexOf('.');
            return dot >= 0 ? fullMarker.Substring(dot + 1) : fullMarker;
        }
    }
}
