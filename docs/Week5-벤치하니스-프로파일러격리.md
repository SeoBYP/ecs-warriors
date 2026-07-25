# Week 5 — 프로파일링 벤치 하니스 (시스템 마커 격리)

> 주차별 진행 기록. 계획은 [`작업계획.md`](작업계획.md), 이전 주차는 [`Week4-캐릭터-콤보-루트모션.md`](Week4-캐릭터-콤보-루트모션.md).
> 상태: 🟢 **완료** — 하니스 확장 ✅ · grid 곡선 ✅ · **단계별 래더 ✅**(cs-naive·single·parallel·grid 4단, 같은 세션)

**기간**: 2026-07-18 ~
**목표**: "총 프레임타임"만 재던 하니스를, **어디서 시간이 드는지**(CPU 메인/GPU/시스템별/드로우콜)까지 재도록 확장한다. 단계별 최적화 스토리(①naive→⑤render)의 계측 인프라.

원본 데이터: [`benchmarks/week5-stages.csv`](benchmarks/week5-stages.csv)

---

## 하니스 확장 — 프로파일러 카운터를 스윕에 얹다

기존 `BenchmarkHarness`는 `Time.unscaledDeltaTime`(총 프레임타임)만 쟀다. 그걸론 *"grid가 빠르다"* 는 보여도 *"어디가 빨라졌나"* 는 못 본다. `ProfilerRecorder`로 샘플 창 동안 아래를 같이 누적한다:

| 컬럼 | 카운터 | 스토리 |
|---|---|---|
| `main_ms` | CPU Main Thread Frame Time | 병렬화 — naive는 total과 붙고, burst-job은 뚝 떨어짐 |
| `gpu_ms` | GPU Frame Time | 에디터선 0 → 빌드에서 확보 |
| `<System>_ms` | 시스템별 마커 합산 | **근접탐색 실비용을 프레임이 아니라 시스템 단위로 격리** |
| `brg_draws` | BRG Draw Calls Count | 1만 엔티티가 몇 개 드로우콜로 접히나(Entities Graphics) |
| `setpass` / `tris` | — | 보조 |

### 함정 두 개 (실측에서 드러남)

**① 실제 카운터 이름은 열거해서 확인해야 한다.** `ProfilerCategory` 상수만으론 부족 — `ProfilerRecorderHandle.GetAvailable()`로 8,062개를 훑어 `BRG Draw Calls Count`, `CPU Main Thread Frame Time`, 그리고 각 시스템의 `Default World Simulation.Systems.<Name>` 마커(카테고리 `Scripts`+`Burst Jobs` 둘 다)를 찾았다.

**② 병렬 잡 마커는 `SumAllSamplesInFrame` 없이는 고정값이 나온다.** 처음엔 `MovementSystem`이 2,000이든 10,000이든 **똑같이 0.008ms**였다. 병렬 잡은 워커 스레드마다 마커가 여러 번 발생하는데, ProfilerRecorder 기본값은 그중 하나만 잡는다. `ProfilerRecorderOptions.SumAllSamplesInFrame`으로 프레임 내 전 샘플을 합산해야 시스템 CPU 총비용이 나온다.

> 이름이 같은 모든 카테고리를 합산하게 했다(`Scripts` 스케줄 + `Burst Jobs` 연산). Burst on/off 브랜치가 마커 카테고리를 바꿔도 총비용이 일관되게 잡히도록.

---

## grid 5점 곡선 (main 브랜치 = Spatial Hash + Burst)

| 적 수 | avg_ms | main_ms | SpatialHash_ms | Movement_ms | brg_draws | tris |
|---:|---:|---:|---:|---:|---:|---:|
| 1,000 | 6.45 | 2.54 | 0.023 | 0.014 | 22 | 0.72M |
| 2,500 | 6.37 | 2.48 | 0.023 | 0.014 | 44 | 1.42M |
| 5,000 | 6.80 | 2.74 | 0.023 | 0.014 | 69 | 2.59M |
| 7,500 | 6.56 | 2.64 | 0.023 | 0.014 | 94 | 3.89M |
| 10,000 | 6.77 | 2.92 | 0.023 | 0.014 | 118 | 5.33M |

**읽는 법:**

- **프레임타임이 완전히 평탄하다** (6.4~6.8ms). 적을 10배 늘려도 안 흔들린다 — Week 2의 "grid 기울기 0"이 프레임 전체에서 재확인.
- **시스템 마커가 바닥에 붙어 N에 안 비례한다** (SpatialHash 0.023 / Movement 0.014ms 고정). grid+Burst에선 시뮬 실연산이 나노초라, 마커는 **잡 dispatch 고정 오버헤드** 수준이다. → **최적화가 너무 잘 돼서 시뮬이 프로파일에서 사라졌다.**
- **`main_ms`(2.5~2.9)가 total(6.5)의 절반이 안 된다.** 나머지는 렌더/GPU 대기. **이제 병목은 시뮬이 아니라 렌더 파이프라인**이라는 뜻.
- **`brg_draws`만 선형으로 는다** (22→118). 5,330만 삼각형이 **118 드로우콜**로 접힌다 = Entities Graphics GPU 인스턴싱.

> gpu_ms는 에디터에서 0으로 나온다(정상). 실측은 빌드에서 별도로 확보해야 한다.

---

## 단계별 래더 — 세 이득의 분리 ✅

같은 separation 문제를 **한 번에 한 기법씩** 올리며 4단으로 측정했다: `cs-naive`(비 Burst) → `single`(Burst) → `parallel`(병렬) → `grid`(공간 해시). 각 단이 정확히 **하나의** 최적화만 바꾸므로 그 단의 이득이 격리된다. 데이터: [`benchmarks/week5-ladder.csv`](benchmarks/week5-ladder.csv) — 에디터 재시작 직후 한 세션에서 4단 연속 측정, 동일 protocol(`Counts={1000,2000,3000}`, warmup 60 / sample 60). cs-naive는 비 Burst O(n²)라 매우 느려 카운트를 낮췄다.

| 단 | 알고리즘 | Burst | 스케줄 | 직전 단 대비 |
|---|---|:-:|---|---|
| **cs-naive** | O(n²) 전수검사 | ✗ | `.Schedule` | (기준선) |
| **single** | O(n²) 전수검사 | ✓ | `.Schedule` | **+Burst** |
| **parallel** | O(n²) 전수검사 | ✓ | `.ScheduleParallel` | **+병렬화** |
| **grid** | O(n) 공간 해시 | ✓ | `.ScheduleParallel` | **+알고리즘** |

**`main_ms`** (CPU 메인 스레드 프레임타임 — separation 잡이 sync point에서 메인을 막는 실비용):

| 적 수 | **cs-naive** | **single** | **parallel** | **grid** |
|---:|---:|---:|---:|---:|
| 1,000 | 83.6 ms / 12 fps | 3.97 ms / 198 fps | 4.05 ms / 196 fps | 3.98 ms / 199 fps |
| 2,000 | 330.9 ms / 3 fps | 8.37 ms / 105 fps | 3.31 ms / 226 fps | 2.30 ms / 302 fps |
| 3,000 | **737.1 ms / 1.3 fps** | 15.05 ms / 62 fps | 4.09 ms / 192 fps | **2.49 ms / 281 fps** |

> 1,000은 스윕 첫 구간이라 Burst JIT·플레이 진입 워밍업이 남아 single/parallel/grid가 ~4ms로 붙는다(`p95` 스파이크). 신호는 **2,000·3,000**에서 깨끗하다.

**3,000에서 세 이득이 한 단씩 분리돼 보인다** (`main_ms`):

| 전환 | 얻는 것 | 배수 |
|---|---|---|
| cs-naive → single | **Burst** (SIMD·인라이닝 — 잡 몸체 컴파일) | 737 → 15.1 = **49×** |
| single → parallel | **병렬화** (워커 스레드 분산) | 15.1 → 4.1 = **3.7×** |
| parallel → grid | **알고리즘** (O(n²) → O(n)) | 4.1 → 2.5 = **1.6×** |
| **cs-naive → grid** | **합산** | 737 → 2.5 = **296×** |

**기울기가 전부 다르다** (1,000→3,000):

- **cs-naive**: O(n²)가 비 Burst로 그대로 노출 — 3천에서 **737ms(1fps)**, 슬라이드쇼.
- **single**: Burst가 상수를 ~50× 깎지만 **여전히 O(n²)** — 3천 15ms로 우상향.
- **parallel**: 병렬화가 더 완화하지만 **여전히 O(n²)** — 코어 수만큼의 상수 이득일 뿐.
- **grid**: **평평**(2.3~2.5ms) — 공간 해시가 O(n²)를 O(n)으로. 유일하게 기울기가 0.

**핵심 관찰:**

- **Burst 이득이 압도적이고 N에 따라 커진다** (1천 21× → 2천 40× → 3천 49×). 같은 O(n²) 루프인데 SIMD 벡터화가 내부 루프를 접어서, 규모가 클수록 격차가 벌어진다. 한 줄 `[BurstCompile]`이 이 래더에서 단일 최대 이득.
- **병렬화만으론 부족하다.** single→parallel는 3천에서 3.7×지만 **1,000에선 이득이 없다**(4.05 vs 3.97 — 디스패치 오버헤드 > 이득). 게다가 parallel도 O(n²)라 카운트를 키우면 결국 무너진다(5,000에서 7.34ms — [csv](benchmarks/week5-ladder.csv) 확장점). "멀티스레딩하면 해결"이라는 흔한 오해를 데이터로 반박 — **알고리즘을 바꿔야** 기울기가 0이 된다.
- **알고리즘 이득의 배수(1.6×)가 이 카운트대에선 가장 작아 보이지만, 유일하게 기울기를 죽인다.** N↑에서 parallel은 발산하고 grid만 평평하게 남는다 — 커밋된 5,000 확장점(single 37ms · parallel 7.3ms · **grid 2.8ms**)이 그 갈림을 보여준다.
- **비용은 전부 `main_ms`(와 총 `avg_ms`)에 잡히고, 시스템 마커엔 안 잡힌다.** `SpatialHashSystem_ms`는 4단 모두 **0.001ms** — 그건 OnUpdate의 *잡 스케줄링*만 재고 실연산은 워커에서 돈다. `.Schedule`(cs-naive·single)이면 메인이 sync point에서 그 잡을 통째로 기다려 `main_ms`에 실리고, `.ScheduleParallel`이면 분산돼 대기가 준다.

> **worktree는 결국 필요 없었다.** 계획엔 "별도 인스턴스로 브랜치 전환"이라 적었지만, **한 세션에서 순차로**(측정 → 코드 편집 → 도메인 리로드 → 측정) 재면 cross-session 드리프트 없이 유효하다. worktree는 여러 변형을 *동시에* 띄울 때만 이득이고 순차 측정엔 과했다. 옛 `bench/01-naive`는 에셋 이전 스냅샷이라 못 쓰고, 현재 코드 위에 변형을 새로 썼다 → `bench/week5-naive-grid` 브랜치(naive/parallel/single 변형).

### ZombieAnimSystem 마커 — 상태머신도 사라진다 (Week6)

`TrackedSystems`에 `ZombieAnimSystem`을 추가해 재보니 **`ZombieAnimSystem_ms ≈ 0.001`** (1,000~10,000 전부). 1만 마리에 매프레임 상태 판정(넉백>사망>공격>이동>대기)을 돌려도 프로파일에서 사라진다 — **전환될 때만** 파라미터를 쓰고 나머진 읽기+분기라, grid separation처럼 "너무 싸서 안 보이는" 쪽. VAT + 상태머신을 얹어도 시뮬은 여전히 공짜.

### ⚠️ 측정 위생 — 긴 세션은 값을 오염시킨다

이 단계를 재는 중 **같은 grid 코드가 재시작 직후 3.9ms@5000 → 30분+ 플레이 사이클 뒤 38ms@5000** 로 10배 벌어지는 걸 관찰했다. ECS 시스템 마커는 전부 바닥인데 `main_ms`만 부푼다 = 에디터 상태 누적(네이티브 메모리·플레이/컴파일 잔재). README가 경고한 *session drift*의 극단이다.

→ **래더는 에디터 재시작 직후 신선하게, 전 단계를 연달아** 재야 유효하다. 그래서 cs-naive 단은 **에디터를 새로 켜고 첫 순서로 4단(cs-naive·single·parallel·grid)을 연달아** 재서 완성했다. 신선 세션의 single·parallel·grid는 커밋돼 있던 값과 정합(single 3천 15.05 vs 15.36 · parallel 3천 4.09 vs 4.12 · grid 3천 2.49 vs 2.82)이라 cross-session 드리프트 없음을 재확인했다. 커밋된 **5,000 확장점**은 유지한다(cs-naive는 비 Burst라 5천이 초당 1프레임 수준이라 저카운트로 격리).

> **자동 측정 메모(헤드리스).** 4단을 uloop로 무인 측정했다. 하니스는 원래 `!Application.isFocused` 프레임을 스로틀로 보고 건너뛰는데, 에디터가 포커스를 안 쥔 헤드리스에선 한 프레임도 못 재고 멈춘다. 프로젝트가 `Run In Background`=on이라 그 가드를 `!isFocused && !runInBackground`로 완화(runInBackground이면 통과)해 포커스와 무관하게 풀스피드로 재게 했다 — 커밋된 포커스 측정치와의 정합으로 값 동등성 확인.

---

## 배운 것

- **프로파일러 카운터는 이름을 열거해서 확인하라.** 문서의 카테고리 상수와 실제 등록된 이름은 다르다.
- **병렬 잡 계측엔 `SumAllSamplesInFrame`.** 안 그러면 워커 하나치만 잡혀 N에 안 비례하는 가짜 상수가 나온다.
- **grid의 진짜 결과는 "시뮬이 사라진 것".** 마커가 바닥에 붙어 지루한 게 아니라, 그게 최적화의 증거다. 극적인 대비는 naive 쪽에 있다.
- **측정 인프라 ≠ 측정 캠페인.** 하니스는 됐지만 단계별 브랜치를 동일 프로토콜로 도는 건 별개의 일이고, 브랜치 간 에셋 divergence가 그 비용을 좌우한다.

---

## 다음

- [x] 단계별 래더 — single(1스레드)·parallel(병렬)·grid, 같은 세션 순차 (worktree 불필요로 판명)
- [x] ZombieAnimSystem 마커 — 상태머신 비용 무시 수준(0.001ms) 확인
- [x] **cs-naive(Burst 없는 단)** — `[BurstCompile]` 제거로 Burst 기여 격리. 새 세션 첫 순서로 4단 연속 측정 → Burst 이득 3천 **49×** 확인
- [ ] gpu_ms 빌드 실측
- [ ] 반경 스윕 벤치(Week 4 이월) — 셀 스캔 `(2R+1)²` 폭발 지점
