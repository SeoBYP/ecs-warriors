using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace Benchmark
{
    /// <summary>
    /// VAT A/B의 "전(前)" 측정 — 전통적 SkinnedMeshRenderer + Animator 좀비를 N마리 스폰해
    /// 프레임타임이 N에 어떻게 무너지는지 잰다. ECS VAT 경로(후)와 같은 카운트·같은 지표로 비교.
    ///
    /// 게임 로직 0: 제자리에서 walk만 재생 → "렌더+애니 비용"만 격리한다.
    /// 각 SMR은 매 프레임 CPU 스키닝(뼈→정점) + 개별 드로우콜(인스턴싱 안 됨)이라
    /// VAT(정점 텍스처 lookup + GPU 인스턴싱 1드로우콜)와 정반대 비용 구조.
    ///
    /// 지표는 Render 카테고리만(ECS 시스템 마커 없음): main_ms · draw calls · setpass · tris.
    /// 헤드리스 무인 측정을 위해 isFocused 가드는 runInBackground이면 통과(BenchmarkHarness와 동일 정책).
    /// </summary>
    public class SkinnedBenchHarness : MonoBehaviour
    {
        [Tooltip("측정 라벨")]
        public string Label = "smr";

        [Tooltip("스폰할 SkinnedMeshRenderer 모델 (M_Zombie_01.FBX 등)")]
        public GameObject ZombieModel;

        [Tooltip("walk 재생용 AnimatorController")]
        public RuntimeAnimatorController WalkController;

        [Tooltip("측정할 마리 수 구간 (오름차순)")]
        public int[] Counts = { 250, 500, 1000, 2000, 5000 };

        [Tooltip("구간마다 샘플 전 건너뛸 프레임 (스폰 안정화)")]
        public int WarmupFrames = 60;

        [Tooltip("구간마다 평균낼 샘플 프레임 수")]
        public int SampleFrames = 60;

        [Tooltip("그리드 간격")]
        public float Spacing = 1.2f;

        readonly List<float> _samples = new List<float>();
        readonly List<string> _csv = new List<string>();

        ProfilerRecorder _mainMs, _gpuMs, _brgDraws, _setPass, _tris;

        double _mainSum, _gpuSum, _trisSum;
        long _drawSum, _setPassSum;
        int _profN;

        int _step = -1;
        int _warmupLeft;
        bool _done, _ready;
        Transform _root;

        void OnEnable()
        {
            _mainMs   = ProfilerRecorder.StartNew(ProfilerCategory.Render, "CPU Main Thread Frame Time");
            _gpuMs    = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GPU Frame Time");
            _brgDraws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _setPass  = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _tris     = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        }

        void OnDisable()
        {
            _mainMs.Dispose(); _gpuMs.Dispose(); _brgDraws.Dispose(); _setPass.Dispose(); _tris.Dispose();
        }

        void Start()
        {
            if (ZombieModel == null || WalkController == null)
            {
                Debug.LogError("[SMR-BENCH] ZombieModel/WalkController 미할당 → 중단");
                enabled = false;
                return;
            }
            Application.targetFrameRate = -1;
            if (QualitySettings.vSyncCount != 0)
                Debug.LogWarning("[SMR-BENCH] VSync 켜짐 → main_ms 부풀 수 있음. Don't Sync 권장.");

            var go = new GameObject("SMR_Bench_Root");
            _root = go.transform;

            _csv.Add("label,zombies,avg_ms,p95_ms,avg_fps,main_ms,gpu_ms,draws,setpass,tris");
            _ready = true;
        }

        void Update()
        {
            if (_done || !_ready) return;
            if (!Application.isFocused && !Application.runInBackground) return;

            if (_step < 0) { BeginStep(0); return; }

            if (_warmupLeft > 0) { _warmupLeft--; return; }

            _samples.Add(Time.unscaledDeltaTime * 1000f);
            _mainSum    += Ns2Ms(_mainMs.LastValue);
            _gpuSum     += Ns2Ms(_gpuMs.LastValue);
            _drawSum    += _brgDraws.LastValue;
            _setPassSum += _setPass.LastValue;
            _trisSum    += _tris.LastValue;
            _profN++;

            if (_samples.Count < SampleFrames) return;

            ReportStep(Counts[_step]);

            if (_step + 1 < Counts.Length) BeginStep(_step + 1);
            else { DumpCsv(); _done = true; }
        }

        void BeginStep(int i)
        {
            _step = i;
            _warmupLeft = WarmupFrames;
            _samples.Clear();
            _mainSum = _gpuSum = _trisSum = 0; _drawSum = _setPassSum = 0; _profN = 0;
            Spawn(Counts[i]);
        }

        void Spawn(int n)
        {
            // 이전 스텝 정리
            for (int c = _root.childCount - 1; c >= 0; c--)
                Destroy(_root.GetChild(c).gameObject);

            int side = Mathf.CeilToInt(Mathf.Sqrt(n));
            float half = side * Spacing * 0.5f;
            for (int k = 0; k < n; k++)
            {
                int gx = k % side, gz = k / side;
                var pos = new Vector3(gx * Spacing - half, 0f, gz * Spacing - half);
                var inst = Instantiate(ZombieModel, pos, Quaternion.identity, _root);

                var anim = inst.GetComponentInChildren<Animator>();
                if (anim == null) anim = inst.AddComponent<Animator>();
                anim.runtimeAnimatorController = WalkController;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // 화면 밖도 스키닝(공정)
            }
        }

        void ReportStep(int zombies)
        {
            _samples.Sort();
            float sum = 0f; foreach (var s in _samples) sum += s;
            float avg = sum / _samples.Count;
            float p95 = _samples[Mathf.Clamp(Mathf.RoundToInt(_samples.Count * 0.95f) - 1, 0, _samples.Count - 1)];
            float fps = 1000f / avg;

            int n = Mathf.Max(_profN, 1);
            double main = _mainSum / n, gpu = _gpuSum / n, tris = _trisSum / n;
            long draws = _drawSum / n, setpass = _setPassSum / n;

            var row = new StringBuilder();
            row.Append(Label).Append(',').Append(zombies).Append(',')
               .Append(avg.ToString("0.00")).Append(',').Append(p95.ToString("0.00")).Append(',')
               .Append(fps.ToString("0.0")).Append(',')
               .Append(main.ToString("0.00")).Append(',').Append(gpu.ToString("0.00")).Append(',')
               .Append(draws).Append(',').Append(setpass).Append(',').Append((long)tris);
            _csv.Add(row.ToString());

            Debug.Log($"[SMR-BENCH] label={Label} zombies={zombies} samples={_samples.Count} avg={avg:0.00}ms " +
                      $"p95={p95:0.00}ms fps={fps:0.0} main={main:0.00}ms gpu={gpu:0.00}ms draws={draws} setpass={setpass} tris={(long)tris}");
        }

        void DumpCsv()
        {
            var sb = new StringBuilder("[SMR-CSV]\n");
            foreach (var line in _csv) sb.Append(line).Append('\n');
            Debug.Log(sb.ToString());
        }

        static double Ns2Ms(long ns) => ns * 1e-6;
    }
}
