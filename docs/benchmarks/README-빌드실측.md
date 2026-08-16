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

## 6. 결과를 어디에 쓰나

- `docs/benchmarks/build/*.csv` — 빌드 실측 원본
- README 수치 옆에 "(에디터)" / "(빌드)"를 병기하면 신뢰도가 올라간다
- **측정 하드웨어(CPU/GPU 모델)를 반드시 함께 적을 것** — 지금 문서에 빠져 있다
