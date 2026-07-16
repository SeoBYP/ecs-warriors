using System.Collections.Generic;
using System.Text;
using Simulation.Components;
using Unity.Entities;
using UnityEngine;

namespace Benchmark
{
    /// <summary>
    /// 적 수를 순차로 바꿔가며 프레임타임을 자동 측정하는 스윕 하니스.
    /// 각 구간: 목표 수 세팅 → 실제 스폰/디스폰 완료 대기 → 워밍업 → N프레임 샘플 → [BENCH] 로그.
    /// 전 구간 종료 시 [BENCH-CSV] 블록으로 한 번에 출력한다.
    ///
    /// 주의: 에디터가 비포커스면 Unity가 게임 루프를 스로틀하므로 그 프레임은 샘플에서 제외한다.
    ///       (측정 중에는 Game 뷰를 클릭해 포커스를 유지할 것)
    ///
    /// 방법론: grid/naive 두 브랜치가 동일한 스윕 프로토콜(같은 Counts·워밍업·샘플 수)을 쓰므로
    ///        절대값보다 "같은 조건에서의 A/B와 기울기"가 유효한 비교 대상이다.
    /// </summary>
    public class BenchmarkHarness : MonoBehaviour
    {
        [Tooltip("측정 구분 라벨 (grid / naive)")]
        public string Label = "grid";

        [Tooltip("측정할 적 수 구간 (오름차순 권장)")]
        public int[] Counts = { 1000, 2500, 5000, 10000 };

        [Tooltip("구간마다 샘플 전 건너뛸 프레임 (스폰·수렴·Burst 워밍업)")]
        public int WarmupFrames = 300;

        [Tooltip("구간마다 평균을 낼 샘플 프레임 수")]
        public int SampleFrames = 120;

        readonly List<float> _samples = new List<float>();
        readonly List<string> _csv = new List<string>();

        int _step = -1;
        int _warmupLeft;
        bool _done;
        EntityManager _em;
        bool _ready;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            _em = world.EntityManager;
            _ready = true;
            _csv.Add("label,enemies,avg_ms,p95_ms,min_ms,max_ms,avg_fps");
        }

        void Update()
        {
            if (_done || !_ready) return;
            if (!Application.isFocused) return;      // 스로틀 프레임 제외

            // SubScene 비동기 로드 대기
            if (!TryGetConfigEntity(out var cfgEntity)) return;

            if (_step < 0) { BeginStep(0, cfgEntity); return; }

            // 목표 수만큼 실제로 스폰/디스폰 될 때까지 대기
            if (CountEnemies() != Counts[_step]) return;

            if (_warmupLeft > 0) { _warmupLeft--; return; }

            _samples.Add(Time.unscaledDeltaTime * 1000f);
            if (_samples.Count < SampleFrames) return;

            ReportStep(Counts[_step]);

            if (_step + 1 < Counts.Length) BeginStep(_step + 1, cfgEntity);
            else { DumpCsv(); _done = true; }
        }

        void BeginStep(int i, Entity cfgEntity)
        {
            _step = i;
            _warmupLeft = WarmupFrames;
            _samples.Clear();

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
            float min = _samples[0];
            float max = _samples[_samples.Count - 1];
            float fps = 1000f / avg;

            Debug.Log($"[BENCH] label={Label} enemies={enemies} samples={_samples.Count} " +
                      $"avg={avg:0.00}ms p95={p95:0.00}ms min={min:0.00}ms max={max:0.00}ms avgFps={fps:0.0}");

            _csv.Add($"{Label},{enemies},{avg:0.00},{p95:0.00},{min:0.00},{max:0.00},{fps:0.0}");
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
    }
}
