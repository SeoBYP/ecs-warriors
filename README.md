# ecs-warriors

> **Unity DOTS(ECS) 기반 3D 백뷰 무쌍 액션** — 화면에 적 수천 마리, 광역기 한 방에 날려도 프레임을 유지하는 데이터 지향 전투 구현.

![Unity](https://img.shields.io/badge/Unity-6000.5.4f1-000000?logo=unity)
![Entities](https://img.shields.io/badge/Entities-6.5.0-blue)
![URP](https://img.shields.io/badge/URP-17.5.0-green)
![Status](https://img.shields.io/badge/status-WIP%20·%20Week%206-orange)

## TL;DR

> **적 10,000마리가 서로 회피하며 몰려들고, 플레이어와 서로 주고받는 양방향 전투 @ 107 FPS (9.37 ms)** — `IJobEntity` + Burst 병렬 잡 + Spatial Hash 근접 탐색.
>
> 이웃 탐색을 O(n²) 전수 검사에서 **Spatial Hash Grid로 바꿔 1만 마리에서 19.60 ms → 6.26 ms (3.13×)**. 더 중요한 건 **기울기** — 그리드 버전은 적이 1천이든 1만이든 프레임타임이 **평평하다**.
> 그리고 그 위에 **전투 시스템(1만 마리 거리 판정 + 데미지 적용)을 얹어도 기울기는 여전히 0**이다. 기울기를 가진 건 O(n²) naive뿐(**+13.25**).
> 그 최적화가 **어디서** 오는지 4단으로 쪼갰다 — 같은 O(n²)에 한 기법씩 얹어 **Burst 49× · 병렬화 3.7× · 알고리즘 1.6×**로 이득을 분리 측정(Week 5).
> → 측정 데이터: [Week2](docs/benchmarks/week2-separation.csv) · [Week3 A/B](docs/benchmarks/week3-combat-ab.csv) · [Week4 A/B](docs/benchmarks/week4-deadtag-ab.csv) · [Week5 래더](docs/benchmarks/week5-ladder.csv) · 분석: [Week2](docs/Week2-이동-SpatialHash.md) · [Week3](docs/Week3-전투-양방향브릿지.md) · [Week4](docs/Week4-광역기-구조변경벤치.md) · [Week5](docs/Week5-벤치하니스-프로파일러격리.md)

![1만 마리 이웃 회피 군집 @ 160fps](docs/images/week2-crowd-separation.png)

---

## 프로젝트 소개

삼국무쌍 오리진 스타일의 **3D 오버숄더 액션**을 세로 슬라이스로 구현한 포트폴리오 프로젝트입니다.
플레이어 1명이 수천 마리의 잡몹을 상대하며, **광역기 한 방에 화면이 정리되는 손맛**을 목표로 합니다.

이 프로젝트가 증명하려는 것은 "ECS 문법을 안다"가 아니라 두 가지입니다.

1. **병목을 찾고 개선할 줄 안다** — 프로파일링 → 원인 진단 → 최적화 → 수치 개선의 사이클.
2. **기술을 적재적소에 쓸 줄 안다** — 전부 ECS로 밀어붙이지 않고, 플레이어의 액션감은 GameObject로, 대량 군중만 ECS로 처리하는 판단.

그래서 이 저장소의 **본체는 게임이 아니라 "최적화 케이스 스터디"** 입니다. 같은 로직을 단계별로 개선하며 프레임타임을 측정·기록하고, before/after 데이터로 실력을 증명합니다. (아래 [최적화 케이스 스터디](#최적화-케이스-스터디-) 참고.)

---

## 기술 스택

| 영역 | 스택 | 버전 |
|---|---|---|
| 에디터 | Unity | **6000.5.4f1** (Unity 6.5) |
| 시뮬레이션 | Entities (ECS) | **6.5.0** |
| 렌더링 | Entities Graphics (GPU 인스턴싱) | **6.5.0** |
| 컴파일 | Burst (SIMD / 네이티브) | 1.8.29 |
| 컨테이너 | Collections | 6.5.0 |
| 수학 | Mathematics (SIMD) | 1.4.0 |
| 물리 (보조) | Unity Physics | 6.5.0 |
| 렌더 파이프라인 | Universal RP | 17.5.0 |
| 입력 | Input System | 1.19.0 |
| 플레이어/연출 | GameObject · MonoBehaviour | — |
| 프로파일링 | Unity Profiler · Burst Inspector | — |

> DOTS 패키지는 `com.unity.feature.ecs`로 설치되며 Unity 6.5부터 **에디터 버전과 정렬된 6.5.x**로 배포됩니다.

---

## 아키텍처 — 하이브리드 설계

전부 ECS가 정답은 아닙니다. **경계를 어디에 긋느냐**가 이 프로젝트의 핵심 설계 결정입니다.

```mermaid
flowchart TD
    subgraph GO["GameObject / MonoBehaviour — 반응성·연출"]
        P[플레이어: 콤보·타격감·카메라]
        UI[UI / 킬 카운터]
        VFX[VFX / 카메라 셰이크]
    end
    subgraph ECS["ECS / DOTS — 대량 시뮬레이션"]
        SP[스폰 시스템]
        MV[이동·타겟팅 Job]
        SH[Spatial Hash Grid]
        EA[적 공격 Job]
        DMG[데미지 적용 · 사망]
        RND[Entities Graphics 렌더]
    end
    P -->|공격 히트박스 → AttackRequest 엔티티| DMG
    MV --> SH
    SH --> DMG
    MV --> EA
    DMG -->|사망 이벤트 큐 · 프레임당 1회 소비| VFX
    EA -->|플레이어 피격 데미지 큐 · 프레임당 합산| P
```

- **플레이어 → ECS (공격)**: 공격 시 `EntityCommandBuffer`로 **AttackRequest 엔티티**를 스폰 → ECS 시스템이 Spatial Grid로 범위 내 적을 조회해 데미지 버퍼에 기록. (순수 데이터 흐름, Job 친화적.)
- **ECS → 플레이어 (적 사망)**: 사망 이벤트를 네이티브 큐에 모아 **메인 스레드에서 프레임당 1회 소비** → VFX·카메라 셰이크·킬 카운트.
- **ECS → 플레이어 (적의 공격, 양방향 전투)**: 적이 사거리 안이면 쿨다운마다 **플레이어 피격 데미지**를 큐에 push → 플레이어 GO가 프레임당 합산해 **HP 차감·피격 연출·패배 판정**. 적에게 둘러싸이면 플레이어도 죽을 수 있다. (HP의 소유자는 GO, ECS는 스냅샷만 읽는 단방향 데이터 흐름.)

> **왜 플레이어는 ECS가 아닌가?** 애니메이션 캔슬·입력 버퍼·타격 프레임 튜닝은 반복 이터레이션이 잦은데 ECS는 그 비용이 높습니다. 객체 1개를 위해 ECS 오버헤드를 지불할 이유가 없습니다.

---

## 최적화 케이스 스터디 ❤️

이 프로젝트가 파는 상품입니다. 같은 적 수·같은 씬 기준으로 단계별 프레임타임을 측정합니다.

| 단계 | 구현 | 기대 병목 | 측정 지표 | 상태 |
|---|---|---|---|---|
| ① 순진 | MonoBehaviour + O(n²) 근접탐색 | 메인스레드 CPU 포화 | 적 N마리별 프레임타임(ms) | 🔬 래더 `cs-naive` 단(비Burst O(n²)) |
| ② ECS 전환 | Entities, 싱글스레드 | 캐시 미스↓ | 동일 | 🔬 래더 `single` 단(Burst 1스레드) |
| ③ Burst + Job | 병렬 잡 + SIMD | 워커스레드 활용 | 코어별 부하 분산 | ✅ **래더: Burst 49× · 병렬 3.7×** |
| ④ Spatial Hashing | 그리드 근접탐색 | 알고리즘 개선 | 근접탐색 잡 시간 | ✅ **1만 @ 19.60ms → 6.26ms (3.13×)** |
| ⑤ 렌더 최적화 | Entities Graphics 인스턴싱 (VAT) | 드로우콜↓ | 드로우콜 수, GPU 타임 | ✅ **VAT/SMR A/B: 5천 55× · 1만 @ 198fps** |
| ⑥ **API 선택** | Enableable vs AddComponent | **구조 변경 / 동기화** | 동시 사망 프레임 스파이크 | ✅ **1만 동시 사망 42.26 → 31.71ms (+33%, t=5.87)** |

> ⑥은 앞의 단계들과 **결이 다르다.** ①~⑤가 "느린 걸 빠르게"라면, ⑥은 **결과가 똑같은 두 API 중 무엇을 고르나**의 문제다. 코드도 4파일 몇 줄 차이인데 프레임 스파이크가 33% 갈린다.

> 순진한 버전은 별도 브랜치(`bench/01-naive`)로 유지 → 벤치마크의 출발점.

### ④ Spatial Hash 실측 (동일 조건, 이웃 탐색 방법만 교체)

| 적 수 | naive O(n²) | **grid** Spatial Hash | 개선 |
|---:|---:|---:|---:|
| 1,000 | 6.35 ms | 6.51 ms | 0.98× (손익분기점 미만) |
| 2,500 | 7.60 ms | 6.14 ms | 1.24× |
| 5,000 | 9.25 ms | 6.21 ms | 1.49× |
| **10,000** | **19.60 ms (51 fps)** | **6.26 ms (160 fps)** | **3.13×** |

**핵심은 기울기다** — grid는 1천이든 1만이든 **~6.2ms로 평평**(이웃 탐색 비용 ≈ 0), naive는 5천을 넘기며 무너진다. 원본: [`docs/benchmarks/week2-separation.csv`](docs/benchmarks/week2-separation.csv) · 분석: [`docs/Week2-이동-SpatialHash.md`](docs/Week2-이동-SpatialHash.md)

> 정직한 관찰: naive도 **51 fps로 돈다**. 양쪽 다 Burst+SIMD+멀티코어를 쓰기 때문 — 즉 이건 "느린 O(n²) vs 빠른 알고리즘"이 아니라 **"Burst로 최적화된 O(n²) vs Burst + 알고리즘"** 의 비교다. 그리고 1,000마리에선 그리드가 **오히려 손해**다(해시맵 재구축 오버헤드). 최적화엔 손익분기점이 있다.

### 단계별 래더 — 한 기법씩 격리 (cs-naive→single→parallel→grid) ✅

④가 "naive vs grid" 2단 비교라면, Week 5엔 같은 O(n²) 이웃 탐색을 **한 번에 한 기법씩** 올려 4단으로 쪼갰다. 각 단이 정확히 하나만 바꾸므로 그 단의 이득이 격리된다. **에디터 재시작 직후 한 세션에서 4단 연속 측정**(`Counts={1000,2000,3000}`, warmup60/sample60).

| 단 | 알고리즘 | Burst | 스케줄 | `main_ms` @3,000 |
|---|---|:-:|---|---:|
| **cs-naive** | O(n²) 전수검사 | ✗ | 1스레드 | **737.1** (1.3 fps) |
| **single** | O(n²) 전수검사 | ✓ | 1스레드 | 15.05 (62 fps) |
| **parallel** | O(n²) 전수검사 | ✓ | 병렬 | 4.09 (192 fps) |
| **grid** | O(n) 공간 해시 | ✓ | 병렬 | **2.49** (281 fps) |

| 전환 | 격리되는 이득 | 배수 |
|---|---|---:|
| cs-naive → single | **Burst** (SIMD·인라이닝) | **49×** |
| single → parallel | **병렬화** (워커 분산) | **3.7×** |
| parallel → grid | **알고리즘** (O(n²)→O(n)) | **1.6×** |
| **cs-naive → grid** | 합산 | **296×** |

**Burst 이득이 압도적이고 N에 비례해 커진다**(1천 21× → 3천 49×) — 한 줄 `[BurstCompile]`이 이 래더의 단일 최대 이득. 반면 **병렬화만으론 부족**하다: parallel도 O(n²)라 카운트를 키우면 결국 무너지고(5천 7.34ms), **알고리즘을 바꿔야 기울기가 0**이 된다(grid만 평평). 실비용은 `main_ms`에만 잡힌다 — `SpatialHashSystem_ms`는 4단 모두 **0.001ms**(OnUpdate 스케줄링만 계측), 실연산은 워커에서 돌아 sync point에서 메인을 막는다. 원본: [`docs/benchmarks/week5-ladder.csv`](docs/benchmarks/week5-ladder.csv) · 분석: [`docs/Week5-벤치하니스-프로파일러격리.md`](docs/Week5-벤치하니스-프로파일러격리.md)

### ⑥ 구조 변경 실측 — Enableable vs AddComponent (각 n=20)

광역기 한 방으로 **9,600마리 동시 사망**을 만들고 그 프레임의 스파이크를 측정. `DeadTag`를 표시하는 방법만 바꿨다.

| | **Enableable** (main) | **AddComponent** (`bench/05-deadtag-structural`) |
|---|---:|---:|
| 스파이크 최대 | **31.71 ms** (sd 6.96) | **42.26 ms** (sd 4.03) |
| 범위 | 24.26 ~ 44.38 | **34.20** ~ 51.63 |
| | | **+10.55 ms (+33%) · t = 5.87** |

**AddComponent 20라운드 중 최고 성적(34.20ms)이 Enableable의 평균(31.71ms)보다 나쁘다.** 원본: [`docs/benchmarks/week4-deadtag-ab.csv`](docs/benchmarks/week4-deadtag-ab.csv) · 분석: [`docs/Week4-광역기-구조변경벤치.md`](docs/Week4-광역기-구조변경벤치.md)

**왜 갈리나** — "비트라서 싸다"가 절반, **"비트라서 병렬로 할 수 있다"** 가 나머지 절반이고 이쪽이 더 크다. 구조 변경은 [공식 문서](https://docs.unity3d.com/Packages/com.unity.entities@6.5/manual/concepts-structural-changes.html)상 *"only on the main thread; not from jobs"* 라, `AddComponent`는 ECB에 1만 건을 예약한 뒤 **메인 스레드가 혼자 하나씩** 재생해야 한다. **1만 명이 동시에 vs 1명이 순서대로.**

> 방법론 관찰: **같은 40라운드 데이터로 결론이 갈렸다.** 처음 쓴 `초과분 합` 지표는 `t=1.03`(결론 불가)이었고 **음수까지 나왔다**(기준선을 빼는 구조라 노이즈가 2배). `스파이크 최대`로 바꾸니 `t=5.87`. 그리고 n=5에선 `t=1.27`이라 아무 말도 못 했다 — **지표 설계와 표본 수가 결론을 만든다.**

**적 수 목표**: 화면 내 활성 1,500~3,000 + 총 5,000~10,000을 슬라이더로 조절, FPS 오버레이로 노출.

---

## 개발 로드맵 (6~8주)

| 주차 | 내용 | 산출물 | 상태 |
|---|---|---|---|
| **W0** | DOTS 셋업 · asmdef · 첫 컴포넌트 · SubScene 렌더 파이프라인 | 캡슐 엔티티 인스턴싱 렌더 | ✅ 완료 |
| **W1** | 스폰 시스템 · 카운트 슬라이더 · FPS 오버레이 | 1만 마리 @ ~156 FPS + 슬라이더 | ✅ 완료 |
| **W2** | 군중 이동 · Spatial Grid (+ `bench/01-naive` 분기) | 1만 군집 @ 160fps + **④단계 벤치 3.13×** | ✅ 완료 |
| **W3** | GO 플레이어 · 공격↔ECS 브릿지 · 데미지/사망 · 적→플레이어 공격 | 핵심 게임루프 성립 + **전투 A/B 벤치** | ✅ 완료 |
| **W4** | 캐릭터·4타 콤보·무기 영역 판정·루트모션 · 광역기·넉백·경직 · 플레이어 사망 | 무쌍 손맛 + **⑥ 구조 변경 A/B (+33%)** | ✅ 완료 |
| **W5** | 🔬 벤치 하니스 확장(시스템 마커 격리) · 단계별 래더 | 프로파일러 격리 + 4단 래더 CSV | ✅ 완료 |
| **W6** | 애니메이션 · 폴리시 · 승리조건 | "완성"처럼 보이는 세로 슬라이스 | ⬜ |
| **W7~8** | 문서화 · 데모 영상 · 배포 (버퍼) | 제출 가능한 포폴 | ⬜ |

상세 태스크 분해는 [`docs/작업계획.md`](docs/작업계획.md) 참고. 주차별 진행 기록은 `docs/Week*.md` (예: [`Week0-DOTS셋업.md`](docs/Week0-DOTS셋업.md)).

---

## 개발 진행

### Week 0 — DOTS 셋업 & 첫 엔티티 렌더 ✅

빈 URP 프로젝트에서 시작해 **SubScene 베이킹으로 만든 엔티티를 Entities Graphics로 렌더**하는 데까지. 커스텀 코드 없이 캡슐 GameObject가 엔티티(`LocalTransform` + `RenderMeshArray` + `MaterialMeshInfo`)로 변환되어 GPU 인스턴싱으로 그려진다.

![Week 0 — 첫 ECS 엔티티 렌더](docs/images/week0-first-entity-render.png)

- DOTS 6.5.0 설치 + 버전 확정 · Unity CLI Loop(CLI 모드) 검증 파이프라인 구축
- `ECSWarriors.Simulation` asmdef (참조 5개) · 첫 unmanaged 컴포넌트 `Enemy`/`Velocity`
- `EnemySubScene` 베이킹 → 캡슐 엔티티 렌더 확인

### Week 1 — 스폰 시스템 · FPS 오버레이 · 카운트 슬라이더 ✅

몬스터 프리팹을 **엔티티 프리팹으로 베이킹**하고, `SpawnSystem`(`ISystem`)이 런타임에 `SpawnConfig.Count`를 목표치로 삼아 **부족하면 스폰 / 넘치면 디스폰**한다. uGUI 슬라이더로 목표 수를 실시간 조절하고, Text로 현재 적 수를 표시.

![Week 1 — 카운트 슬라이더 + FPS 오버레이 (5000마리 @ 121 FPS)](docs/images/week1-count-slider.png)

- `Monster` 프리팹 + `SpawnAuthoring`/`SpawnBaker` → `SpawnConfig` 베이킹, `MonsterAuthoring`으로 `Enemy` 태그 부착
- `SpawnSystem`(목표 수 유지: spawn/despawn diff) · `FpsOverlay`(GC-free)
- `SpawnCountSlider`: 슬라이더 → `SpawnConfig.Count`(GO→ECS 쓰기) + Update 폴링으로 적 수 Text 표시(ECS→GO 읽기)
- **🔬 1만 마리 @ ~120–156 FPS** (GPU 인스턴싱) → 상세 [`docs/Week1-스폰.md`](docs/Week1-스폰.md)

### Week 2 — 군중 이동 · Spatial Hash · 벤치마크 ④ ✅

`IJobEntity` + Burst로 1만 마리를 병렬 이동시키고, **Spatial Hash Grid**(셀 크기 = 회피 반경)로 이웃을 3×3 셀만 조회해 회피시킨다. 한 점에 뭉치던 덩어리가 **균일한 원반 군집**으로 펴진다.

![Week 2 — 1만 마리 이웃 회피 군집](docs/images/week2-crowd-separation.png)

- `MovementSystem`(IJobEntity+Burst+ScheduleParallel) — 등속 추적 + StopDistance 정지
- `SpatialHashSystem` — `NativeParallelMultiHashMap` 매 프레임 재구축(ParallelWriter) → `SeparationJob`이 3×3 셀만 조회
- **④단계 벤치마크 확보**: 1만에서 **19.60ms → 6.26ms (3.13×)**, grid는 적 수와 무관하게 평평 → 상세 [`docs/Week2-이동-SpatialHash.md`](docs/Week2-이동-SpatialHash.md)

### Week 3 — 양방향 전투 · GO↔ECS 브릿지 · 전투 A/B 벤치 ✅

플레이어(GameObject)와 적 1만(ECS)이 **서로 주고받는** 전투를 완성했다. 핵심은 두 세계를 잇는 **브릿지 3종**이고, 각각 **방향과 모양이 다르다**.

![Week 3 — 3천 마리가 플레이어를 포위해 HP를 갈아먹는 중 (124fps)](docs/images/week3-enemy-attack.png)

| 브릿지 | 방향 | 방식 |
|---|---|---|
| `PlayerStateBridge` | GO → ECS | 싱글톤 **덮어쓰기** (최신값 1개) |
| `PlayerAttackBridge` | GO → ECS | **요청 엔티티** 던지기 (사건 1건) |
| `DeathEventBridge` · `PlayerHealthBridge` | **ECS → GO** | **`NativeQueue` 싱글톤** (사건 N건) |

- `AttackResolveSystem` — **Week2의 Spatial Hash 재사용**해 반경 내 적 조회 → `DamageEvent` 버퍼에 누적
- `DamageApplySystem` / `DeathSystem` — 버퍼 합산 → HP 차감 → `DeadTag`(**`IEnableableComponent`**, 1만 동시 사망 시 구조 변경 회피) → 사망 이벤트 큐
- `EnemyAttackSystem` — 1만 마리 병렬 쿨다운·거리 판정 → `AsParallelWriter()`로 플레이어 데미지 큐에 push
- **🔬 전투 A/B**: 전투 전부 켬 vs 끔을 **같은 세션에서** 측정 → **기울기 +0.84로 여전히 평평** → 상세 [`docs/Week3-전투-양방향브릿지.md`](docs/Week3-전투-양방향브릿지.md)

> 정직한 관찰: 처음엔 "Week3(9.37ms) − Week2 grid(6.26ms) = 전투 비용 3ms"라고 결론 낼 뻔했다. **틀렸다** — 전투를 **전부 끈** 조건도 7.76ms라, 차이의 절반 이상이 코드와 무관한 **세션 간 베이스라인 드리프트**였다. **cross-session 절대값 비교는 무효**이고, 유효한 건 같은 세션 A/B와 기울기뿐이다.

### Week 4 — 캐릭터·4타 콤보 · 광역기·넉백·경직 · 구조 변경 벤치 ⑥ ✅

캡슐 플레이어를 실제 캐릭터로 바꾸고 **무쌍류 4타 콤보**를 붙였다. 그리고 **광역기 한 방에 화면이 정리되는 손맛**(넉백 + 경직)까지 완성했다. 이번 주의 헤드라인은 **Week 3에서 공격을 "요청 엔티티 + 파라미터"로 설계해둔 복리** — 광역기도 근접 콤보도 **새 ECS 시스템 0개**로 얹혔다.

![Week 4 — HornedKnight가 1만 군중 속에서 콤보를 휘두르는 중 (157fps)](docs/images/week4-character-animation.png)

**액션 · 근접 콤보** — 상세 [`docs/Week4-캐릭터-콤보-루트모션.md`](docs/Week4-캐릭터-콤보-루트모션.md)

- **근접 판정 = 가상 영역 쿼리**: DOTS엔 콜라이더가 없다(적은 인스턴싱 캡슐 수천 개). 무기(손) 위치로 **Week 2의 `SpatialHash` 반경 쿼리를 그대로 재사용** — 회피·광역기가 쓰던 그 쿼리다. `OnAttackStart`~`End` 사이 **매 프레임** 손 위치로 `AttackRequest`를 발사해 손이 아크를 그리며 훑는다.
- **스윙당 1히트 (`SwingId`)**: 한 적이 여러 프레임 맞지 않게 스윙마다 ID를 증가시켜 중복 제거. `SwingId=0`이면 단발이라 **기존 광역기가 안 깨진다** — 새 시스템 0개, 필드 하나로 끝. 검증: 한 스윙에 1,258마리 **전부 정확히 1히트**, 중복 0.
- **콤보를 코드가 모른다**: 4타 체이닝은 **순수 Animator 상태 기계**이고 코드 변경 0. "각 콤보 타 = 새 SwingId"만으로 코드는 콤보 인덱스에 무지한 채 정확하다. 판정 기점(왼손/오른손)·강타 배수(Combo4 = 2배)는 **Animation Event 파라미터**로 클립마다 지정.
- **루트모션**: 콤보 클립에 이미 baked된 전진(0.77~1.42 m/s)이 `applyRootMotion=false`로 버려지고 있었다 — **켜기만 하면 됐다.** `clip.averageSpeed`를 먼저 찍어 "이미 들어있음"을 확인한 덕에 클립 재작업 헛수고를 피했다.

**손맛 · 대량 사망 · 벤치** — 상세 [`docs/Week4-광역기-구조변경벤치.md`](docs/Week4-광역기-구조변경벤치.md)

- **광역기 = 숫자만 바꾸기**: `Radius=25, Damage=100`을 던지면 그게 광역기다. 별도 `AoeRequest` 타입도 시스템 분기도 없다. 검증: 1회에 킬 카운터 **정확히 +10,000**.
- **넉백**: `DamageEvent` 버퍼 스키마를 처음 확장(`+SourcePos, +KnockbackScale`). `Amount`는 더하면 그만이지만 **방향은 아니다** — 사방에서 맞으면 상쇄되므로 **"최대 데미지 1건" 기준**의 결정론적 넉백. 검증: `MovementSystem` 정지 후 1만 전원 정확히 `+5.00`, `normalize(0)`→NaN 가드 확인.
- **경직 (`Stun`)**: `DamageApplySystem`(쓰기) ↔ `MovementSystem`(읽기)이 처음으로 **계약**을 맺는다. 계약은 *무엇을 공유하나*뿐 아니라 **"언제 확인하나"**(거리 체크 앞)까지 맞아야 한다.
- **플레이어 사망**: 브릿지에 `Health`가 아니라 **`bool IsDead` 한 비트**만 — 적은 "몇 HP 남았나"가 아니라 때릴지 말지만 알면 된다. `EnemyAttackSystem`은 시스템에서 조기 반환해 사망 후 **1만 순회 자체를 건너뛴다.**
- **🔬 ⑥ 구조 변경 A/B**: `DeadTag`를 Enableable vs AddComponent로 측정 → **1만 동시 사망 스파이크 +33% (t=5.87)**. 상세는 위 [최적화 케이스 스터디](#최적화-케이스-스터디-)의 ⑥.

> 정직한 관찰: 경직 가드를 거리 체크 **뒤**에 뒀더니 플레이어에 붙어 있는 적들의 경직 타이머가 얼어붙었다 — 그런데 **넉백이 이 버그를 가려줬다.** 넉백이 적을 사거리 밖으로 밀어내면 다음 프레임엔 정상 동작해서, 광역기 데모는 "밀렸다 → 잠깐 멈췄다 → 온다"로 **정확히 보인다.** 로직은 틀렸는데 화면은 맞다. 넉백을 `0`으로 꺼 격리하고서야 드러났다.

### Week 5 — 벤치 하니스 확장 · 단계별 래더 ✅

"총 프레임타임"만 재던 `BenchmarkHarness`를 **어디서 시간이 드는지**(CPU 메인 · GPU · 시스템별 마커 · 드로우콜)까지 `ProfilerRecorder`로 확장하고, 같은 O(n²) 이웃 탐색을 한 기법씩 올려 **cs-naive → single → parallel → grid** 4단으로 격리 측정했다.

- **함정 둘 (실측에서 드러남)**: ① 프로파일러 카운터는 상수명이 아니라 `GetAvailable()`로 **이름을 열거**해 찾아야 하고(8,062개 중), ② 병렬 잡 마커는 `SumAllSamplesInFrame` 없이는 워커 하나치만 잡혀 **N에 안 비례하는 가짜 상수**가 나온다.
- **🔬 단계별 래더**: 3천 마리 기준 Burst **49×** → 병렬화 **3.7×** → 알고리즘 **1.6×**(합산 296×). 각 단이 기법 하나씩 격리 — 상세는 위 [최적화 케이스 스터디](#최적화-케이스-스터디-)의 **단계별 래더**.
- **🔬 상태머신 무비용 재확인**: `TrackedSystems`에 `ZombieAnimSystem`을 넣어 재보니 1만 마리에도 **0.001ms** — 전환되는 순간에만 파라미터를 쓰고 나머진 읽기+분기라 grid separation처럼 프로파일에서 사라진다.
- 상세 [`docs/Week5-벤치하니스-프로파일러격리.md`](docs/Week5-벤치하니스-프로파일러격리.md)

> 정직한 관찰: 같은 grid 코드가 재시작 직후 3.9ms@5000 → 30분+ 플레이 뒤 38ms@5000으로 **10배** 벌어졌다(ECS 마커는 바닥, `main_ms`만 폭발 = **세션 드리프트**). 래더는 **에디터 재시작 직후 4단을 연달아** 재야 유효 — 그래서 cs-naive 단은 새 세션 첫 순서로 측정했다.

### Week 6 — VAT 군중 애니메이션 (1만 마리 상태머신) ✅

캡슐이던 잡몹을 **실제 좀비 메시**로 바꾸자마자 벽에 부딪혔다 — **전원 T포즈로 누워서** 몰려온 것이다. 버그가 아니라 필연이었다: 리깅된 메시는 정점이 **바인드 포즈(=T포즈)** 로 저장되고, `SkinnedMeshRenderer`가 매 프레임 뼈로 변형(**스키닝**)해야 포즈가 나온다. 그런데 ECS 인스턴싱은 `MeshFilter`로 그리고 **거기엔 스키닝이 없다.** 1만 마리에 `SkinnedMeshRenderer`를 달 수도 없다.

![Week 6 — 1만 좀비가 걷고, 물어뜯고, 넉백에 밀려 쓰러진다](docs/images/week6-vat-state-machine.gif)

**VAT (Vertex Animation Texture)** — 상세 [`docs/Week6-VAT-군중애니메이션.md`](docs/Week6-VAT-군중애니메이션.md)

- **계산에서 재생으로**: 매 프레임 뼈로 정점을 *계산*하는 대신, 정점의 프레임별 위치를 **텍스처에 미리 굽고**(가로=정점, 세로=프레임) 셰이더가 **읽어서 재생**한다. 뼈·Animator 없이 GPU 인스턴싱 1드로우콜. **메모리는 인스턴스 수와 무관** — 1만 마리가 텍스처 1장(클립당 164KB)을 공유한다.
- **멀티클립 상태머신**: 5클립(idle·walk·attack·damage·death)을 한 텍스처에 **세로로 스택**하고, `{startRow, frameCount, fps, loop}` 표를 **Blob 에셋**으로 구워 전 좀비가 공유(80바이트 1벌 + 핸들 8바이트). `ZombieAnimSystem`이 게임플레이 상태를 읽어 **전환되는 순간에만** 클립 파라미터를 갈아끼운다.
- **넉백 → 사망 순서 연출**: 넉백을 즉시 텔레포트에서 **0.25초 지속 이동**으로 바꾸고, 애니 우선순위에서 **넉백 > 사망**으로 둬 *"밀려나며 히트 모션 → 멈춘 뒤 쓰러지는 모션"* 을 만들었다. 사망 타이머는 넉백이 끝나야 시작한다(안 그러면 사망 모션이 잘림).
- **강타 = 넉백**: 1~3타는 제자리 히트, **4타와 우클릭만** 밀어낸다. 판정은 클립에 이미 박혀 있던 **`floatParameter`(강타 배수)** 로 — 코드가 콤보 인덱스를 모르는 Week 4의 설계를 그대로 잇는다.
- **🔬 VAT/SMR A/B 벤치**: 같은 메시(701정점)로 렌더+애니만 비교 → SMR은 N에 선형(5천 158ms·6fps)인데 VAT는 평평(**1만 @ 3.34ms·198fps**), **5천에서 55×**. 원본: [`docs/benchmarks/week6-vat-ab.csv`](docs/benchmarks/week6-vat-ab.csv) · 분석: 위 [최적화 케이스 스터디](#최적화-케이스-스터디-)의 ⑤ / [`docs/Week6-VAT-군중애니메이션.md`](docs/Week6-VAT-군중애니메이션.md)

> 정직한 관찰 셋. **①** `EnabledRefRO<DeadTag>`를 쿼리에 넣으면 상태를 읽을 수는 있지만 **"켜진 것만" 필터가 그대로 살아있어** 매칭 엔티티가 **0개**가 됐다 — 잡은 도는데 1만 마리가 전부 미초기화. `IgnoreComponentEnabledState`가 필요했고, 프로브로 `[기본]=0 vs [Ignore]=10000`을 찍어 확정했다. **②** 초기 상태를 `Idle`로 두면 처음부터 idle인 좀비는 전환이 없어 **영영 초기화가 안 된다** — `None(255)` 센티널로 첫 프레임을 강제했다. **③** "4타만 넉백"을 `IsName("Combo4")`로 판정했더니 항상 false였다. 애니 이벤트는 **전환이 끝나기 전(t=0.33s)에 발사**되어 아직 Combo3으로 보인다. 우클릭은 Animator를 안 거쳐 멀쩡했던 탓에 "4타만 안 되는" 증상으로 나타났다.

---

## 프로젝트 구조

```
Assets/
  Scripts/
    Simulation/                       # ECS 코어 (ECSWarriors.Simulation asmdef)
      Components/                     #   Enemy · Velocity · MoveStats · SpawnConfig · HashedEnemy · SpatialHashMap
                                      #   PlayerState(+IsDead) · AttackRequest · DamageEvent(버퍼, +넉백/경직) · Health · DeadTag(enableable)
                                      #   DeathEvent/DeathEventQueue · EnemyAttack · PlayerDamageEvent/PlayerDamageQueue · HitTracker · Stun
      SpatialHash.cs                  #   셀 좌표·해시 유틸 (빌드/조회 공유)
      Authoring/                      #   SpawnAuthoring/Baker · MonsterAuthoring/Baker
      Bridge/                         #   PlayerStateBridge · PlayerAttackBridge (GO→ECS)
                                      #   DeathEventBridge · PlayerHealthBridge (ECS→GO, NativeQueue 소비)
      Systems/                        #   SpawnSystem · MovementSystem · SpatialHashSystem(+SeparationJob)
                                      #   PlayerStateSystem · AttackResolveSystem · DamageApplySystem
                                      #   DeathSystem(사망 연출 지연) · EnemyAttackSystem
                                      #   ZombieAnimSystem(VAT 상태머신) · KnockbackSystem
      VAT/                            #   VATClipSet(클립 메타 SO) · VATClipTable(Blob 싱글톤+authoring)
                                      #   VATAnimParams/VATAnimStart(MaterialProperty) · ZombieAnim · Knockback · DeathTimer
    Editor/                           # VATBaker(멀티클립 스택 베이커) · VATBakerWindow
  Shaders/
    VAT_Zombie.shader                 # URP. 정점단계 텍스처 lookup으로 애니 재생 + DOTS 인스턴싱
    Controller/                       # PlayerController · PlayerAnimationEventListener · ResetTriggerOnEnter (GO 플레이어·4타 콤보)
    UI/                               # FpsOverlay · SpawnCountSlider (MonoBehaviour)
    Benchmark/                        # BenchmarkHarness (적 수 자동 스윕 + ProfilerRecorder 시스템 마커 격리) · AoeSpikeHarness (동시 사망 스파이크)
  Prefabs/
    Monster.prefab                    # 스폰 원본(콜라이더 제거)
  Scenes/
    Main.unity                        # EnemySubScene(SubScene) — Spawner 포함
docs/
  작업계획.md              # 실행 계획 (기획 → 작업 분해)
  협업방식.md              # 작업 합의 (짝 프로그래밍 + 검수)
  Week0-DOTS셋업.md        # 주차별 진행 기록
  Week1-스폰.md
  Week2-이동-SpatialHash.md
  Week3-전투-양방향브릿지.md
  Week4-광역기-구조변경벤치.md
  Week4-캐릭터-콤보-루트모션.md
  Week5-벤치하니스-프로파일러격리.md
  Week6-VAT-군중애니메이션.md
  benchmarks/             # 측정 원본 CSV (week2-separation · week3-combat-ab · week4-deadtag-ab · week5-ladder)
  images/                 # 진행 스크린샷
```

**브랜치**: `main`(최신 통합) · `bench/01-naive`(벤치마크 O(n²) 기준선 — 머지하지 않고 비교용 보존) · `bench/05-deadtag-structural`(⑥ DeadTag Enableable vs AddComponent A/B용) · `bench/week5-naive-grid`(cs-naive/single/parallel 변형 — 단계별 래더 측정용)

---

## 빌드 / 실행

**요구 사항**: Unity **6000.5.4f1** (Unity Hub에서 동일 버전 설치 권장 — DOTS 패키지는 버전에 민감)

```bash
git clone https://github.com/SeoBYP/ecs-warriors.git
```

1. Unity Hub에서 프로젝트를 연다 (패키지는 최초 실행 시 자동 복원).
2. `Assets/Scenes/Main.unity`(현재는 `SampleScene.unity`)를 연다.
3. Play.

---

## 문서

- [`docs/작업계획.md`](docs/작업계획.md) — 개발 작업 계획서 (현재 상태 · 아키텍처 확정 · 주차별 실행 태스크 · 벤치마크 하니스)
- 기획·설계 원문 및 개발 일지는 별도 Obsidian 볼트에서 관리.

---

*이 프로젝트는 개발 중(WIP)입니다. 데모 GIF·벤치마크 그래프·기술 결정 Q&A는 개발 진행에 따라 이 문서에 채워집니다.*
