using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Simulation.Components;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using Unity.Transforms;
using UnityEngine;

namespace Benchmark
{
    /// <summary>
    /// 적 수를 자동 스윕하며 프레임타임 + 프로파일러 카운터를 측정해 CSV로 남긴다.
    /// **에디터와 빌드(IL2CPP) 양쪽에서 동일 프로토콜로 돈다.**
    ///
    /// 측정 항목(샘플 창 평균):
    ///   avg/p95 프레임타임 · fps · CPU Main Thread · GPU Frame Time
    ///   · 시스템별 마커(ms, 병렬 잡은 SumAllSamplesInFrame으로 워커 합산)
    ///   · BRG 드로우콜 · SetPass · 삼각형 수
    ///
    /// ⚠️ 프로파일러 카운터는 **Development Build**에서만 유효하다(릴리스 빌드에선 0).
    ///    프레임타임(avg/p95/fps)은 릴리스에서도 유효.
    ///
    /// 실행(빌드):
    ///   ecs-warriors.exe -bench -label grid -counts 1000,2500,5000,10000 -warmup 300 -sample 120 -out C:\out\grid.csv -quit
    /// 실행(에디터): 씬에 이 컴포넌트를 얹거나, 아무 것도 안 해도 인스펙터 값으로 Play 시 동작.
    /// </summary>
    public class BenchmarkHarness : MonoBehaviour
    {
        [Tooltip("CSV 첫 열. 브랜치/구성 이름(cs-naive · single · parallel · grid · vat …)")]
        public string Label = "grid";

        [Tooltip("스윕할 적 수")]
        public int[] Counts = { 1000, 2500, 5000, 10000 };

        [Tooltip("각 단계에서 버리는 프레임(스폰 직후 튀는 구간 제외)")]
        public int WarmupFrames = 300;

        [Tooltip("각 단계에서 실제로 재는 프레임")]
        public int SampleFrames = 120;

        [Tooltip("ms 단위로 뽑을 시스템 마커 전체 이름")]
        public string[] TrackedSystems =
        {
            "Simulation.Systems.SpatialHashSystem",
            "Simulation.Systems.MovementSystem",
        };

        [Tooltip("측정 종료 후 CSV를 쓸 경로. 비우면 persistentDataPath/bench-<label>.csv")]
        public string OutputPath = "";

        [Tooltip("측정 종료 후 애플리케이션 종료(빌드 자동화용)")]
        public bool QuitWhenDone = false;

        EntityManager _em;
        bool _ready, _done;

        readonly List<string> _csv = new List<string>();
        readonly List<float> _samples = new List<float>();

        ProfilerRecorder _mainMs, _gpuMs, _brgDraws, _setPass, _tris;
        List<ProfilerRecorder>[] _sysRecs;
        bool _sysReady;

        double _mainSum, _gpuSum, _trisSum;
        long _drawSum, _setPassSum;
        double[] _sysSum;
        int _profN;

        int _step = -1;
        int _warmupLeft;

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

            // 측정 위생 — 프레임 언캡 + VSync off(안 끄면 main_ms에 대기가 섞인다) + 비포커스에도 계속
            Application.targetFrameRate = -1;
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;

            // 결과 UI가 뜨면 오버레이·입력 정지가 측정에 섞인다(리더를 지우면 즉시 CLEAR 판정)
            var stageUI = FindAnyObjectByType<Gameplay.StageUIBridge>();
            if (stageUI != null) stageUI.enabled = false;

            var head = new StringBuilder("label,enemies,avg_ms,p95_ms,avg_fps,main_ms,gpu_ms");
            for (int i = 0; i < _sysRecs.Length; i++) head.Append(',').Append(ShortName(TrackedSystems[i])).Append("_ms");
            head.Append(",brg_draws,setpass,tris");
            _csv.Add(head.ToString());

            Debug.Log($"[BENCH] start label={Label} counts=[{string.Join(",", Counts)}] " +
                      $"warmup={WarmupFrames} sample={SampleFrames} dev={Debug.isDebugBuild} out={ResolveOutputPath()}");
        }

        void Update()
        {
            if (_done || !_ready) return;

            TryInitSystemRecorders();

            // SubScene 비동기 로드 대기 (스폰 풀이 준비돼야 적을 만들 수 있다)
            if (!TryGetPool(out var prefabs)) return;

            if (_step < 0) { BeginStep(0, prefabs); return; }

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
                    foreach (var r in _sysRecs[i]) v += Ns2Ms(r.LastValue);
                    _sysSum[i] += v;
                }
            _profN++;

            if (_samples.Count < SampleFrames) return;

            ReportStep(Counts[_step]);

            if (_step + 1 < Counts.Length) BeginStep(_step + 1, prefabs);
            else { DumpCsv(); _done = true; if (QuitWhenDone) Quit(); }
        }

        // 시스템 마커는 시스템이 한 번은 실행돼야 등록된다 → 잡을 때까지 매 프레임 시도
        void TryInitSystemRecorders()
        {
            if (_sysReady || _sysRecs.Length == 0) return;

            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);

            bool all = true;
            for (int i = 0; i < _sysRecs.Length; i++)
            {
                if (_sysRecs[i] != null && _sysRecs[i].Count > 0) continue;

                var list = new List<ProfilerRecorder>();
                foreach (var h in handles)
                {
                    var d = ProfilerRecorderHandle.GetDescription(h);
                    // 실제 마커는 "Default World Simulation.Systems.MovementSystem"처럼 월드 접두사가 붙는다.
                    // 정확 일치만 보면 영영 못 잡으므로(=마커 0.000) 접미 일치도 허용한다.
                    if (d.Name == TrackedSystems[i] || d.Name.EndsWith(TrackedSystems[i]))
                        // ★ 병렬 잡은 워커별로 마커가 여러 번 발생 → 합산해야 N에 비례하는 실값이 나온다
                        list.Add(ProfilerRecorder.StartNew(d.Category, d.Name, 1, ProfilerRecorderOptions.SumAllSamplesInFrame));
                }
                _sysRecs[i] = list;
                if (list.Count == 0) all = false;
            }
            _sysReady = all;
        }

        void BeginStep(int i, NativeArray<Entity> prefabs)
        {
            _step = i;
            _warmupLeft = WarmupFrames;
            _samples.Clear();
            _mainSum = _gpuSum = _trisSum = 0; _drawSum = _setPassSum = 0; _profN = 0;
            for (int k = 0; k < _sysSum.Length; k++) _sysSum[k] = 0;

            SetEnemyCount(Counts[i], prefabs);
        }

        /// <summary>
        /// 적을 정확히 target 마리로 맞춘다.
        /// SpawnSystem은 Week7부터 **편성 1회성**이라 더 이상 수를 조절하지 않으므로, 벤치는 직접 만든다.
        /// (리더도 함께 지워 순수 군중만 남긴다 — 예전 측정과 조건을 맞추기 위함.)
        /// </summary>
        void SetEnemyCount(int target, NativeArray<Entity> prefabs)
        {
            _em.CompleteAllTrackedJobs();

            // ⚠️ DestroyEntity(EntityQuery)는 다중 submesh 적의 LinkedEntityGroup 때문에 예외 → 배열 오버로드
            var q = _em.CreateEntityQuery(ComponentType.ReadOnly<Enemy>());
            using (var existing = q.ToEntityArray(Allocator.Temp))
                _em.DestroyEntity(existing);

            if (target <= 0 || prefabs.Length == 0) return;

            var rnd = Unity.Mathematics.Random.CreateFromIndex((uint)(target + 1));
            int per = target / prefabs.Length;
            int rest = target - per * prefabs.Length;

            for (int p = 0; p < prefabs.Length; p++)
            {
                int n = per + (p == prefabs.Length - 1 ? rest : 0);
                if (n <= 0) continue;

                using var ents = _em.Instantiate(prefabs[p], n, Allocator.Temp);
                for (int k = 0; k < ents.Length; k++)
                {
                    // 원점 중심 원반 — 밀도가 카운트에 비례해야 분리(separation) 부하가 정직하게 잡힌다
                    float ang = rnd.NextFloat(0f, 2f * math.PI);
                    float r = math.sqrt(rnd.NextFloat(0f, 1f)) * 60f;
                    _em.SetComponentData(ents[k], LocalTransform.FromPosition(new float3(math.cos(ang) * r, 0f, math.sin(ang) * r)));
                    _em.SetComponentData(ents[k], new VATAnimStart { Value = rnd.NextFloat(0f, 2f) });
                }
            }
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
               .Append(F(avg)).Append(',').Append(F(p95)).Append(',').Append(F1(fps)).Append(',')
               .Append(F(main)).Append(',').Append(F(gpu));

            var sysLog = new StringBuilder();
            for (int i = 0; i < _sysRecs.Length; i++)
            {
                double v = _sysSum[i] / n;
                row.Append(',').Append(v.ToString("0.000", CultureInfo.InvariantCulture));
                sysLog.Append(' ').Append(ShortName(TrackedSystems[i])).Append('=').Append(F(v)).Append("ms");
            }
            row.Append(',').Append(draws).Append(',').Append(setpass).Append(',').Append((long)tris);

            Debug.Log($"[BENCH] label={Label} enemies={enemies} samples={_samples.Count} avg={F(avg)}ms " +
                      $"p95={F(p95)}ms fps={F1(fps)} main={F(main)}ms gpu={F(gpu)}ms{sysLog} " +
                      $"brgDraws={draws} setpass={setpass} sysReady={_sysReady}");

            _csv.Add(row.ToString());
        }

        void DumpCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[BENCH-CSV]");
            foreach (var line in _csv) sb.AppendLine(line);
            Debug.Log(sb.ToString());

            var path = ResolveOutputPath();
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllLines(path, _csv);
                Debug.Log($"[BENCH] CSV 저장: {path}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[BENCH] CSV 저장 실패({path}): {e.Message}");
            }
        }

        string ResolveOutputPath()
        {
            if (!string.IsNullOrEmpty(OutputPath)) return OutputPath;
            return Path.Combine(Application.persistentDataPath, $"bench-{Label}.csv");
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        bool TryGetPool(out NativeArray<Entity> prefabs)
        {
            prefabs = default;
            var q = _em.CreateEntityQuery(ComponentType.ReadOnly<SpawnConfig>(), ComponentType.ReadOnly<SpawnPrefab>());
            if (q.IsEmpty) return false;

            var buf = _em.GetBuffer<SpawnPrefab>(q.GetSingletonEntity(), true);
            if (buf.Length == 0) return false;

            prefabs = new NativeArray<Entity>(buf.Length, Allocator.Temp);
            for (int i = 0; i < buf.Length; i++) prefabs[i] = buf[i].Prefab;
            return true;
        }

        static double Ns2Ms(long ns) => ns * 1e-6;
        static string F(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        static string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);

        // "Default World Simulation.Systems.SpatialHashSystem" → "SpatialHashSystem"
        static string ShortName(string fullMarker)
        {
            int dot = fullMarker.LastIndexOf('.');
            return dot >= 0 ? fullMarker.Substring(dot + 1) : fullMarker;
        }
    }
}
