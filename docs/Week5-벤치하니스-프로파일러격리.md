# Week 5 — 프로파일링 벤치 하니스 (시스템 마커 격리)

> 주차별 진행 기록. 계획은 [`작업계획.md`](작업계획.md), 이전 주차는 [`Week4-캐릭터-콤보-루트모션.md`](Week4-캐릭터-콤보-루트모션.md).
> 상태: 🟡 **진행 중** — 하니스 확장 ✅ · grid 곡선 ✅ / 단계별(naive·ecs-single·burst) 측정 ⬜

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

## 아직 못 한 것 — 단계별 대비

이 하니스의 목적은 **naive(O(n²)) 대비 grid**를 시스템 마커로 격리해 보여주는 것이다. 그런데 naive 측정 브랜치(`bench/01-naive`)는 **아트 에셋(5.3GB) 이전의 옛 스냅샷**이라, 라이브 에디터를 그 브랜치로 전환하면 대용량 리임포트가 발생한다.

→ **라이브 전환은 하지 않는다.** naive 측정이 필요하면 `git worktree add ../ecs-naive bench/01-naive`로 **별도 Unity 인스턴스**에서 같은 하니스를 돌려야 한다. (메인 프로젝트를 건드리지 않음.)

---

## 배운 것

- **프로파일러 카운터는 이름을 열거해서 확인하라.** 문서의 카테고리 상수와 실제 등록된 이름은 다르다.
- **병렬 잡 계측엔 `SumAllSamplesInFrame`.** 안 그러면 워커 하나치만 잡혀 N에 안 비례하는 가짜 상수가 나온다.
- **grid의 진짜 결과는 "시뮬이 사라진 것".** 마커가 바닥에 붙어 지루한 게 아니라, 그게 최적화의 증거다. 극적인 대비는 naive 쪽에 있다.
- **측정 인프라 ≠ 측정 캠페인.** 하니스는 됐지만 단계별 브랜치를 동일 프로토콜로 도는 건 별개의 일이고, 브랜치 간 에셋 divergence가 그 비용을 좌우한다.

---

## 다음

- [ ] naive 대비 — `git worktree` 별도 인스턴스에서 (라이브 전환 금지)
- [ ] gpu_ms 빌드 실측
- [ ] 반경 스윕 벤치(Week 4 이월) — 셀 스캔 `(2R+1)²` 폭발 지점
