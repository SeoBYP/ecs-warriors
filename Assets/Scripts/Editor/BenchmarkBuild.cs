#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Benchmark.EditorTools
{
    /// <summary>
    /// 벤치 실측용 스탠드얼론 빌드.
    ///
    /// **Development Build를 켜는 이유**: `ProfilerRecorder` 카운터(GPU Frame Time · BRG Draw Calls ·
    /// 시스템 마커)는 릴리스 빌드에서 제공되지 않는다. 프레임타임만 필요하면 릴리스로 재도 되지만,
    /// 지금까지의 CSV 컬럼을 그대로 채우려면 Development가 필요하다.
    /// (Development 오버헤드가 있으므로 **에디터 수치와 비교할 때가 아니라 빌드끼리 비교**할 것.)
    ///
    /// 메뉴: Tools > Benchmark > Build Player (IL2CPP · Development)
    /// CLI:  Unity.exe -quit -batchmode -projectPath . -executeMethod Benchmark.EditorTools.BenchmarkBuild.BuildFromCLI
    /// </summary>
    public static class BenchmarkBuild
    {
        const string OutDir = "Builds/Bench";
        const string ExeName = "ecs-warriors-bench.exe";

        [MenuItem("Tools/Benchmark/Build Player (IL2CPP · Development)")]
        public static void BuildMenu() => Build(il2cpp: true, development: true);

        [MenuItem("Tools/Benchmark/Build Player (Mono · Development · 빠른 빌드)")]
        public static void BuildMenuMono() => Build(il2cpp: false, development: true);

        public static void BuildFromCLI()
        {
            var args = System.Environment.GetCommandLineArgs();
            bool mono = args.Contains("-benchMono");
            Build(il2cpp: !mono, development: true);
        }

        /// <summary>
        /// IL2CPP **플레이어 변형**이 설치돼 있는지. 에디터의 `Data/il2cpp`(툴체인)만 보고 판단하면 안 된다 —
        /// 실제 빌드에 필요한 건 `PlaybackEngines/windowsstandalonesupport/Variations/win64_player_*_il2cpp`다.
        ///
        /// 없으면 SBP가 `EnabledBuildButton()==false`로 판정해 엔티티 씬 콘텐츠 아카이브 빌드 단계에서
        /// "Unable to build with the current configuration"이라는 **원인을 알 수 없는 메시지**로 죽는다.
        /// (Windows Build Support (IL2CPP) 모듈 미설치가 진짜 원인)
        /// </summary>
        static bool HasIl2CppPlayers()
        {
            var dir = Path.Combine(EditorApplication.applicationContentsPath,
                                   "PlaybackEngines/windowsstandalonesupport/Variations");
            return Directory.Exists(dir) &&
                   Directory.GetDirectories(dir).Any(d => d.Contains("il2cpp", System.StringComparison.OrdinalIgnoreCase));
        }

        static void Build(bool il2cpp, bool development)
        {
            if (il2cpp && !HasIl2CppPlayers())
            {
                Debug.LogError(
                    "[BENCH-BUILD] IL2CPP 플레이어가 설치돼 있지 않습니다(Mono만 있음).\n" +
                    "  → Unity Hub > Installs > 6000.5.4f1 > Add modules > 'Windows Build Support (IL2CPP)' 설치\n" +
                    "  → 또는 Mono로 재기: 메뉴 Tools/Benchmark/Build Player (Mono ...) · CLI 인자 -benchMono · 스크립트 -Mono\n" +
                    "  (이 검사가 없으면 SBP가 'Unable to build with the current configuration'으로만 죽어 원인을 알 수 없다)");
                return;
            }

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                // 빌드 세팅이 비어 있으면 Main 씬을 자동 등록 — 벤치 때문에 수동 설정을 강요하지 않는다
                const string main = "Assets/Scenes/Main.unity";
                if (!File.Exists(main))
                {
                    Debug.LogError($"[BENCH-BUILD] 빌드할 씬이 없습니다: {main}");
                    return;
                }
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(main, true) };
                scenes = new[] { main };
                Debug.Log($"[BENCH-BUILD] 빌드 세팅이 비어 있어 {main}을 등록했습니다.");
            }

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,
                il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;

            Directory.CreateDirectory(OutDir);

            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(OutDir, ExeName),
                target = BuildTarget.StandaloneWindows64,
                options = development
                    ? BuildOptions.Development | BuildOptions.ConnectWithProfiler
                    : BuildOptions.None,
            };

            Debug.Log($"[BENCH-BUILD] 시작: backend={(il2cpp ? "IL2CPP" : "Mono")} dev={development} → {opts.locationPathName}");
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;

            if (s.result == BuildResult.Succeeded)
                Debug.Log($"[BENCH-BUILD] 성공: {s.outputPath} ({s.totalSize / 1048576}MB, {s.totalTime.TotalMinutes:0.0}분)\n" +
                          $"실행 예: {ExeName} -bench -label grid -counts 1000,2500,5000,10000 -warmup 300 -sample 120 -out bench-grid.csv -quit");
            else
                Debug.LogError($"[BENCH-BUILD] 실패: {s.result} (에러 {s.totalErrors})");
        }
    }
}
#endif
