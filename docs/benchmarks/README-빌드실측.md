# 빌드 실측 가이드 (IL2CPP Standalone)

지금까지의 측정(`week2-separation` ~ `week6-vat-ab`)은 **전부 에디터 플레이 기준**이다.
포폴 신뢰도를 위해 **같은 프로토콜을 빌드에서 다시 돌릴 수 있게** 만든 설정과 절차.

---

## 1. 구성

| 파일 | 역할 |
|---|---|
| `Assets/Scripts/Benchmark/BenchmarkHarness.cs` | 적 수 스윕 + 프레임타임·프로파일러 카운터 측정 → CSV 저장 |
| `Assets/Scripts/Benchmark/BenchBootstrap.cs` | 빌드에서 `-bench` 인자가 있을 때만 하니스 생성(**씬은 안 건드림**) |
| `Assets/Scripts/Editor/BenchmarkBuild.cs` | IL2CPP + Development 빌드 (메뉴 / CLI) |
| `tools/bench/run-build-bench.ps1` | **브랜치별** 체크아웃 → 빌드 → 실행 → CSV 수집 자동화 |

## 2. 왜 Development Build인가

`ProfilerRecorder`가 주는 카운터(**GPU Frame Time · BRG 드로우콜 · SetPass · 시스템 마커**)는
**릴리스 빌드에서 제공되지 않는다**(값이 0). 지금까지의 CSV 컬럼을 그대로 채우려면 Development가 필요하다.

- 프레임타임(`avg_ms`·`p95_ms`·`fps`)만 필요하면 릴리스로 재도 된다.
- Development는 약간의 오버헤드가 있으므로 **빌드끼리** 비교할 것. 에디터 수치와 직접 비교 금지(Week 3에서 배운 cross-session 비교 금지와 같은 이유).

## 3. 한 번만 재기 (에디터에서 빌드)

1. Unity 메뉴 **Tools > Benchmark > Build Player (IL2CPP · Development)**
   → `Builds/Bench/ecs-warriors-bench.exe`
2. 실행:

```bash
Builds/Bench/ecs-warriors-bench.exe -bench -label grid -counts 1000,2500,5000,10000 -warmup 300 -sample 120 -out bench-grid.csv -quit
```

인자: `-bench`(활성) · `-label`(CSV 첫 열) · `-counts` · `-warmup` · `-sample` · `-out` · `-quit`
`-out`을 생략하면 `%USERPROFILE%/AppData/LocalLow/DefaultCompany/ecs-warriors/bench-<label>.csv`.

## 4. 브랜치별로 재기 (래더용) ★

래더(cs-naive → single → parallel → grid)는 **코드가 다른 변형**이라 브랜치로 나뉜다.
스크립트가 변형마다 체크아웃 → 빌드 → 실행 → CSV 수집을 반복한다.

```powershell
# main(grid) 하나만
./tools/bench/run-build-bench.ps1 -Variants "grid=main"

# 래더 4단
./tools/bench/run-build-bench.ps1 `
  -Variants "cs-naive=bench/build-cs-naive","single=bench/build-single","parallel=bench/build-parallel","grid=main" `
  -Counts "1000,2000,3000"
```

결과: `docs/benchmarks/build/<label>.csv`

### ⚠️ 전제
- **Unity 에디터를 닫을 것** — 배치모드 빌드가 프로젝트 락을 잡는다.
- **워킹트리가 깨끗할 것** — 스크립트가 체크아웃하므로 작업이 날아갈 수 있다(스크립트가 먼저 검사하고 거부한다).
- 측정 중 다른 무거운 프로그램을 띄우지 말 것.

### ⚠️ 기존 bench 브랜치를 그대로 쓰지 말 것
`bench/01-naive` 등 예전 브랜치는 **아트 에셋 5.3GB가 들어오기 전 스냅샷**이라 지금 main과 씬·에셋이 다르다.
그 상태로 재면 "알고리즘 차이"가 아니라 **씬 차이**를 재게 된다.

→ 변형 브랜치는 **현재 main에서 새로 떠서** 딱 한 가지만 바꾼다:

```bash
git checkout -b bench/build-parallel main
#   SpatialHashSystem의 SeparationJob을 O(n²) 전수검사로 교체(병렬 유지)
git commit -am "bench: parallel 변형(O(n²) 전수검사, 병렬)"

git checkout -b bench/build-single bench/build-parallel
#   .ScheduleParallel() → .Schedule() 로만 변경
git commit -am "bench: single 변형(1스레드)"

git checkout -b bench/build-cs-naive bench/build-single
#   [BurstCompile] 제거
git commit -am "bench: cs-naive 변형(Burst off)"
```

이렇게 하면 각 단이 **정확히 한 가지만** 다르므로 이득이 격리된다(Week 5에서 확립한 방법론).

## 5. 측정 위생 (하니스가 자동으로 함)

- `Application.targetFrameRate = -1`, `QualitySettings.vSyncCount = 0` — VSync가 켜져 있으면 `main_ms`에 대기가 섞인다
- `runInBackground = true` — 창이 비활성일 때 스로틀되지 않게
- 결과 UI(`StageUIBridge`) 비활성 — 리더를 지우면 즉시 CLEAR 판정이 떠서 오버레이·입력 정지가 측정에 섞인다
- 스윕 시 **리더 포함 전 적 제거 후 정확히 N마리 생성**(원반 분포) — Week 7부터 스폰이 편성 1회성으로 바뀌어 `SpawnConfig.Count`로는 조절되지 않는다

## 6. 실측 결과 (2026-08-17)

**측정 환경** — Ryzen 7 7800X3D (8C/16T) · RTX 4070 Ti SUPER · 32GB · Windows 11
**빌드** — Mono · Development · 1920×1080 창모드 (IL2CPP는 §7 참조) · warmup 300 / sample 120

| 적 수 | avg_ms | p95_ms | fps | main_ms | gpu_ms | 드로우콜 | tris |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 1,000 | 1.86 | 6.81 | 538.8 | 1.43 | 0.31 | 7 | 351K |
| 2,500 | 1.63 | 2.01 | 615.3 | 1.62 | 0.35 | 7 | 586K |
| 5,000 | 1.93 | 2.46 | 517.9 | 1.92 | 0.50 | 7 | 1.32M |
| 10,000 | 2.77 | 3.12 | 361.1 | 2.75 | 0.54 | 14 | 1.95M |

원본: [`build/grid.csv`](build/grid.csv)

읽는 법:
- **1천 → 1만(10배)에 프레임타임 1.86 → 2.77ms (1.5배)** — grid 공간해시가 N에 거의 평평하다는 Week 5 결론이 빌드에서도 유지된다.
- 드로우콜이 7~14개뿐인 건 BRG가 VAT 군중을 인스턴싱으로 묶기 때문. tris가 적 수보다 덜 늘어나는 건 프러스텀 컬링(적이 늘수록 스폰 원반이 넓어져 화면 밖 비중이 커짐).
- `SpatialHashSystem_ms`·`MovementSystem_ms`가 카운트와 무관하게 고정인 건 **버그가 아니다** — 그 마커는 `OnUpdate`의 잡 *스케줄링*만 재고 실연산은 워커에서 돈다. 실비용은 `main_ms`에 잡힌다(Week 5에서 확립한 해석).
- ⚠️ **에디터 수치와 직접 비교 금지.** 에디터엔 세이프티 체크·프로파일러·씬뷰 오버헤드가 얹혀 있다. Week 2~6 표(에디터)와 이 표(빌드)는 각각 자기들끼리만 비교할 것.

## 7. IL2CPP로 재려면 — 모듈 설치가 먼저다 ★

이 프로젝트 기본 백엔드는 IL2CPP지만, **Windows Build Support (IL2CPP) 모듈이 없으면 빌드가 실패한다.**
그런데 실패 메시지가 원인을 전혀 알려주지 않는다:

```
InvalidOperationException: Unable to build with the current configuration, please check the Build Settings.
  → ContentCatalogBuildUtility.BuildContentArchives failed with status 'Exception'
```

**추적 경로** (한 번 겪었으므로 기록해 둔다):

```
BuildPipeline.BuildPlayer
 └ EntitySceneBuildPlayerProcessor.PrepareForBuild      (DOTS가 SubScene을 굽는 단계)
    └ ContentCatalogBuildUtility.BuildContentArchives
       └ SBP ContentPipeline.cs:106 → CanBuildPlayer() == false
          └ WindowsStandaloneBuildWindowExtension.EnabledBuildButton() == false
             └ m_HasIl2CppPlayers == false      ← 진짜 원인
```

- `Editor/Data/il2cpp`(툴체인)가 있다고 설치된 게 **아니다**. 필요한 건 플레이어 변형:
  `Editor/Data/PlaybackEngines/windowsstandalonesupport/Variations/win64_player_*_il2cpp`
- 라이선스·`-nographics`·배치모드는 **무관**했다(로그에 `Successfully resolved entitlement details`).
- 지금은 `BenchmarkBuild.HasIl2CppPlayers()`가 빌드 전에 검사해 설치 안내를 출력한다.

설치:

```bash
"C:\Program Files\Unity Hub\Unity Hub.exe" -- --headless install-modules --version 6000.5.4f1 --module windows-il2cpp --childModules
```

**Mono로 재도 되는 이유**: ECS 핫패스는 전부 Burst 잡이고, Burst는 스크립팅 백엔드와 무관하게 동일한 네이티브 코드를 낸다. 백엔드 차이는 주로 메인스레드 매니지드 코드(브리지·UI)에 나타난다. 단 **Mono끼리만** 비교할 것.

## 8. 스크립트 함정 (같은 데 두 번 빠지지 않으려고)

- **`.ps1`은 UTF-8 BOM으로 저장할 것.** PowerShell 5.1은 BOM 없는 스크립트를 ANSI(949)로 읽어 한글이 깨진다. 깨진 글자가 정규식에 들어가면 `(성공|실패)`가 `(?깃났|...)`가 되어 *Unrecognized grouping construct*로 죽는다.
- **인자 배열은 `[string[]]`로 타입 고정.** PS 5.1은 1요소 배열을 문자열로 언랩하고, 문자열을 `@`로 스플랫하면 **글자 단위**로 펼쳐진다(`-benchMono` → `-`, `b`, `e`, `n`, …).
- **Unity는 빌드가 실패해도 배치모드 종료코드 0을 준다.** 로그의 `[BENCH-BUILD] 성공` 마커로 판정해야 한다.
- **빌드는 워킹트리를 더럽힌다** — `ProjectSettings`뿐 아니라 URP 에셋·`UnityConnectSettings`까지 재직렬화한다. 스크립트가 빌드 후 `git checkout -- .`로 되돌린다(시작 시 클린 트리를 강제하므로 안전).

## 9. 결과를 어디에 쓰나

- `docs/benchmarks/build/*.csv` — 빌드 실측 원본
- README 수치 옆에 "(에디터)" / "(빌드)"를 병기하면 신뢰도가 올라간다
