using System.Collections.Generic;
using Simulation.Components;
using Unity.Entities;
using UnityEngine;

namespace Benchmark
{
    /// <summary>
    /// 프레임타임을 자동 수집해 평균/p95를 콘솔에 1회 출력하는 벤치마크 하니스.
    /// - 워밍업 프레임을 건너뛴 뒤 N프레임을 샘플링하고 결과를 Debug.Log("[BENCH] ...")로 남긴다.
    /// - 에디터가 비포커스일 때 Unity는 게임 루프를 스로틀하므로, 그 프레임은 샘플에서 제외한다.
    ///   (제외 안 하면 실제 작업시간이 아니라 스로틀 대기시간이 찍혀 수치가 무의미해짐)
    /// 사용법: 씬 아무 GameObject에 붙이고 Play → Game 뷰를 클릭해 포커스 유지 → 콘솔의 [BENCH] 확인.
    /// </summary>
    public class BenchmarkHarness : MonoBehaviour
    {
        [Tooltip("측정 구분용 라벨 (예: grid / naive)")]
        public string Label = "grid";

        [Tooltip("샘플링 전 건너뛸 프레임 수 (스폰·수렴·Burst 워밍업 대기)")]
        public int WarmupFrames = 300;

        [Tooltip("평균을 낼 샘플 프레임 수")]
        public int SampleFrames = 300;

        readonly List<float> _samples = new List<float>();
        int _warmupLeft;
        bool _done;

        void Start()
        {
            _warmupLeft = WarmupFrames;
            _samples.Capacity = SampleFrames;
        }

        void Update()
        {
            if (_done) return;

            // 비포커스 프레임은 스로틀된 가짜 수치라 버린다.
            if (!Application.isFocused) return;

            if (_warmupLeft > 0)
            {
                _warmupLeft--;
                return;
            }

            _samples.Add(Time.unscaledDeltaTime * 1000f);   // ms

            if (_samples.Count >= SampleFrames)
            {
                Report();
                _done = true;
            }
        }

        void Report()
        {
            _samples.Sort();
            float sum = 0f;
            foreach (var s in _samples) sum += s;

            float avg = sum / _samples.Count;
            float p95 = _samples[Mathf.Clamp(Mathf.RoundToInt(_samples.Count * 0.95f) - 1, 0, _samples.Count - 1)];
            float min = _samples[0];
            float max = _samples[_samples.Count - 1];

            Debug.Log($"[BENCH] label={Label} enemies={CountEnemies()} samples={_samples.Count} " +
                      $"avg={avg:0.00}ms p95={p95:0.00}ms min={min:0.00}ms max={max:0.00}ms avgFps={1000f / avg:0.0}");
        }

        static int CountEnemies()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return -1;
            var q = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Enemy>());
            return q.CalculateEntityCount();
        }
    }
}
