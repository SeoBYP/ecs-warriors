using UnityEngine;

namespace Benchmark
{
    /// <summary>
    /// 빌드에서 `-bench` 인자가 있을 때만 하니스를 띄운다. **씬은 건드리지 않는다** —
    /// 벤치 오브젝트를 씬에 박아두면 일반 플레이에도 딸려오고(예전에 Main 씬에 남아 있던 문제),
    /// 커밋마다 씬 diff가 생긴다.
    ///
    /// 사용:
    ///   ecs-warriors.exe -bench -label grid -counts 1000,2500,5000,10000 \
    ///                    -warmup 300 -sample 120 -out "C:\out\grid.csv" -quit
    ///
    /// 인자
    ///   -bench            벤치 모드 활성(없으면 아무 일도 안 함)
    ///   -label <s>        CSV 첫 열 이름
    ///   -counts a,b,c     스윕할 적 수
    ///   -warmup <n>       버릴 프레임
    ///   -sample <n>       측정할 프레임
    ///   -out <path>       CSV 경로(생략 시 persistentDataPath)
    ///   -quit             측정 끝나면 종료
    /// </summary>
    public static class BenchBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var args = System.Environment.GetCommandLineArgs();
            if (!Has(args, "-bench")) return;

            var go = new GameObject("[BenchmarkHarness]");
            Object.DontDestroyOnLoad(go);
            var h = go.AddComponent<BenchmarkHarness>();

            var label = Get(args, "-label");
            if (!string.IsNullOrEmpty(label)) h.Label = label;

            var counts = Get(args, "-counts");
            if (!string.IsNullOrEmpty(counts))
            {
                var parts = counts.Split(',');
                var list = new System.Collections.Generic.List<int>(parts.Length);
                foreach (var p in parts)
                    if (int.TryParse(p.Trim(), out var v) && v > 0) list.Add(v);
                if (list.Count > 0) h.Counts = list.ToArray();
            }

            if (int.TryParse(Get(args, "-warmup"), out var w) && w >= 0) h.WarmupFrames = w;
            if (int.TryParse(Get(args, "-sample"), out var s) && s > 0) h.SampleFrames = s;

            var outPath = Get(args, "-out");
            if (!string.IsNullOrEmpty(outPath)) h.OutputPath = outPath;

            h.QuitWhenDone = Has(args, "-quit");

            Debug.Log($"[BENCH] bootstrap: label={h.Label} counts=[{string.Join(",", h.Counts)}] " +
                      $"warmup={h.WarmupFrames} sample={h.SampleFrames} quit={h.QuitWhenDone}");
        }

        static bool Has(string[] args, string flag)
        {
            foreach (var a in args) if (a == flag) return true;
            return false;
        }

        static string Get(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == key) return args[i + 1];
            return null;
        }
    }
}
