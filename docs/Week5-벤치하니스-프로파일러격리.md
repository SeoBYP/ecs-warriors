# Week 5 — 프로파일링 벤치 하니스 (시스템 마커 격리)

> 주차별 진행 기록. 계획은 [`작업계획.md`](작업계획.md), 이전 주차는 [`Week4-캐릭터-콤보-루트모션.md`](Week4-캐릭터-콤보-루트모션.md).
> 상태: 🟢 **완료** — 하니스 확장 ✅ · grid 곡선 ✅ · **단계별 래더 ✅**(single·parallel·grid, 같은 세션)

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

## 단계별 래더 — 두 이득의 분리 ✅

같은 O(n²) separation을 **단일 스레드 → 병렬 → grid(공간 해시)** 로 한 단씩 올리며 측정했다. 데이터: [`benchmarks/week5-ladder.csv`](benchmarks/week5-ladder.csv) — 한 Unity 세션 순차 측정, 동일 protocol(`Counts={1000,3000,5000}`, warmup 60 / sample 60).

| 적 수 | **single** (O(n²)·1스레드) | **parallel** (O(n²)·병렬) | **grid** (해시·병렬) |
|---:|---:|---:|---:|
| 1,000 | 5.58 ms / 179 fps | 4.06 ms / 247 fps | 3.85 ms / 260 fps |
| 3,000 | 16.53 ms / 60 fps | 5.26 ms / 190 fps | 4.00 ms / 250 fps |
| 5,000 | **38.30 ms / 26 fps** | 8.49 ms / 118 fps | **3.90 ms / 256 fps** |

**세 곡선의 기울기가 전부 다르다** (1,000→5,000, ms/1천마리):

- **single**: +8.2 (O(n²)가 그대로 노출 — 5천에서 38ms, 26fps로 무너짐)
- **parallel**: +1.1 (병렬화가 O(n²)를 완화하지만 여전히 우상향)
- **grid**: +0.01 (**평평** — 공간 해시가 O(n²)를 O(n)으로)

**5,000에서 두 이득이 분리돼 보인다:**

| 전환 | 얻는 것 | 배수 |
|---|---|---|
| single → parallel | **병렬화** (워커 스레드 분산) | 38.30 → 8.49 = **4.5×** |
| parallel → grid | **알고리즘** (O(n²) → O(n)) | 8.49 → 3.90 = **2.2×** |
| **single → grid** | **합산** | 38.30 → 3.90 = **9.8×** |

**핵심 관찰:**

- **1,000에선 셋이 거의 붙어 있다** (5.58 / 4.06 / 3.85). *"작을 땐 최적화가 안 보인다"* — 최적화의 가치는 **규모(기울기)** 에서 나온다.
- **병렬화만으론 부족하다.** parallel도 O(n²)라 5천에서 8.49ms로 우상향한다. 흔한 오해("멀티스레딩하면 해결")를 데이터로 반박 — **알고리즘을 바꿔야** 평평해진다.
- **비용이 `main_ms`에 나타난다** (single 4.44→37.01, parallel 2.96→7.34, grid 2.70→2.77 평평). 시스템 마커(SpatialHash_ms=0.001)엔 안 잡힌다 — separation 실연산은 워커라, 총 프레임타임(`avg_ms`)과 `main_ms`가 진짜 지표다.

> **worktree는 결국 필요 없었다.** 계획엔 "별도 인스턴스로 브랜치 전환"이라 적었지만, **한 세션에서 순차로**(측정 → 코드 편집 → 도메인 리로드 → 측정) 재면 cross-session 드리프트 없이 유효하다. worktree는 여러 변형을 *동시에* 띄울 때만 이득이고 순차 측정엔 과했다. 옛 `bench/01-naive`는 에셋 이전 스냅샷이라 못 쓰고, 현재 코드 위에 변형을 새로 썼다 → `bench/week5-naive-grid` 브랜치(naive/parallel/single 변형).

### ZombieAnimSystem 마커 — 상태머신도 사라진다 (Week6)

`TrackedSystems`에 `ZombieAnimSystem`을 추가해 재보니 **`ZombieAnimSystem_ms ≈ 0.001`** (1,000~10,000 전부). 1만 마리에 매프레임 상태 판정(넉백>사망>공격>이동>대기)을 돌려도 프로파일에서 사라진다 — **전환될 때만** 파라미터를 쓰고 나머진 읽기+분기라, grid separation처럼 "너무 싸서 안 보이는" 쪽. VAT + 상태머신을 얹어도 시뮬은 여전히 공짜.

### ⚠️ 측정 위생 — 긴 세션은 값을 오염시킨다

이 단계를 재는 중 **같은 grid 코드가 재시작 직후 3.9ms@5000 → 30분+ 플레이 사이클 뒤 38ms@5000** 로 10배 벌어지는 걸 관찰했다. ECS 시스템 마커는 전부 바닥인데 `main_ms`만 부푼다 = 에디터 상태 누적(네이티브 메모리·플레이/컴파일 잔재). README가 경고한 *session drift*의 극단이다.

→ **래더는 에디터 재시작 직후 신선하게, 전 단계를 연달아** 재야 유효하다. 이미 커밋된 single·parallel·grid 래더는 재시작 직후 신선하게 잰 값(기존 데이터와 정합)이라 유지하고, **cs-naive(Burst 없는 단)는 오염된 상태에서 재면 비교 무효라 다음 신선한 세션 첫 순서로** 미룬다.

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
- [ ] **cs-naive(Burst 없는 단)** — `[BurstCompile]` 제거로 Burst 기여 격리. **⚠️ 새 세션 첫 순서로**(오염 전) — cs-naive+single+grid 연달아
- [ ] gpu_ms 빌드 실측
- [ ] 반경 스윕 벤치(Week 4 이월) — 셀 스캔 `(2R+1)²` 폭발 지점
