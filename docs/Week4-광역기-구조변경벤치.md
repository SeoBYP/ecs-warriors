# Week 4 — 광역기 · 구조 변경 A/B 벤치마크

> 주차별 진행 기록. 계획은 [`작업계획.md`](작업계획.md), 이전 주차는 [`Week3-전투-양방향브릿지.md`](Week3-전투-양방향브릿지.md).
> 상태: 🟡 **진행 중** — ① 광역기 ✅ · ② 구조 변경 A/B ✅ · ③ 플레이어 사망 ⬜ · ④ 넉백/경직 ⬜

**기간**: 2026-07-17 ~
**목표**: 광역기(범위 수천 동시 피격) · **"Enableable이 진짜 빠른가" 수치 증명**

---

## ① 광역기 — 시스템 코드를 **한 줄도 안 고쳤다**

Week 3에서 공격을 **"요청 엔티티 + 파라미터"** 로 만들어둔 덕에, 광역기는 **숫자만 바꾸는 일**이 됐다.

```csharp
// AttackResolveSystem — 반경이 하드코딩이 아니다
int range = (int)math.ceil(r.Radius / SpatialHash.CellSize);
```

`Radius = 25, Damage = 100`을 던지면 그게 광역기다. **별도 `AoeRequest` 타입도, 시스템 분기도 없다.** 판정을 `PlayerAttackBridge` 안에 박아뒀다면 지금 갈아엎어야 했다.

- `PlayerAttackBridge`: 평타(좌클릭) / 광역기(우클릭), **타이머 2개 독립**, 파라미터 전부 `[SerializeField]`
- **검증**: 광역기 1회 → 킬 카운터 **정확히 +10,000** · 큐 잔여 0 · `AttackRequest` 잔여 0

![Week 4 — 3천 마리 포위 중 HP 5570 (124fps)](images/week3-enemy-attack.png)

### 예측 실패 기록

작업 전 예상: *"반경 5 평타가 129마리였으니, 반경 25면 면적 25배 → **수천 마리**"*.
실제: **10,000 전부.** 수렴한 군중이 반경 25 안에 통째로 들어갔다.

밀도를 역산하면 `10000 / (π×25²) ≈ 5.1마리/unit²`인데 어림값은 1.6이었다. **3배 이상 조밀하다.**
→ **Separation은 딱딱한 제약이 아니라 무른 힘**이다. "셀 크기 1 = 1칸 간격"이라고 생각한 게 틀렸다. 1만 마리가 안쪽으로 밀면 그냥 압축된다.

---

## 🔬 ② 구조 변경 A/B — Enableable은 진짜 빠른가

Week 3에서 **원리로만** 주장했던 것을 측정했다:

> *"1만 마리 동시 사망 시 `AddComponent`면 1만 번의 이사가 일어난다. 그래서 `IEnableableComponent`를 쓴다."*

원본 데이터: [`benchmarks/week4-deadtag-ab.csv`](benchmarks/week4-deadtag-ab.csv) · 비교 브랜치: `bench/05-deadtag-structural`

### 결과 (각 n=20, 9,600마리 동시 사망)

| | **A: Enableable** | **B: AddComponent** | 차이 |
|---|---:|---:|---:|
| 죽은 수 | 9,614 | 9,624 | 0.1% (조건 동일 ✅) |
| **스파이크 최대** | **31.71 ms** (sd 6.96) | **42.26 ms** (sd 4.03) | **+10.55 ms (+33%)** |
| 범위 | 24.26 ~ 44.38 | 34.20 ~ 51.63 | **t = 5.87 ★유의★** |

**`t = 5.87`.** n=5일 땐 `t = 1.27`이라 아무 말도 못 했는데, 표본을 20으로 늘리니 뚫렸다.

```
A:  24.26 ────────── 44.38
B:                34.20 ────────── 51.63
                    ↑ B의 최솟값이 A의 평균(31.71)보다 높다
```

**B의 20라운드 중 최고 성적이 A의 평균보다 나쁘다.** 우연으로 설명되지 않는다.

### 무엇이 다른가 — "싼 연산"이 아니라 **"병렬 가능 여부"**

공식 문서가 메커니즘을 명확히 말한다:

| 주장 | 공식 문서 |
|---|---|
| 구조 변경은 잡에서 못 한다 | *"You can only perform them on the main thread; **not from jobs**"* ([structural changes](https://docs.unity3d.com/Packages/com.unity.entities@6.5/manual/concepts-structural-changes.html)) |
| Enableable은 아키타입/데이터 이동 없음 | *"the target entity **doesn't change its archetype**, ECS **doesn't move any data**"* ([enableable](https://docs.unity3d.com/Packages/com.unity.entities@6.5/manual/components-enableable-use.html)) |
| Enableable은 워커 잡에서 토글 가능 | *"**without using an entity command buffer or creating a sync point**"* |
| ECB는 잡에서 예약 → 메인 스레드 실행 | *"perform changes **on the main thread after the jobs complete**"* ([ECB](https://docs.unity3d.com/Packages/com.unity.entities@6.5/manual/systems-entity-command-buffers.html)) |

```csharp
// A: 워커 스레드가 자기 자리에서 비트만 켠다 — 1만 명이 동시에
if (health.Value <= 0) dead.ValueRW = true;

// B: 잡 안에선 못 한다 → ECB에 1만 건 예약
if (health.Value <= 0) Ecb.AddComponent<DeadTag>(sortKey, entity);
// 그리고 시스템에서:
handle.Complete();                    // 메인 스레드가 잡을 기다림
ecb.Playback(state.EntityManager);    // 1만 건을 혼자 하나씩 — 1명이 순서대로
```

> **"비트라서 싸다"가 절반, "비트라서 병렬로 할 수 있다"가 나머지 절반이고 이쪽이 더 크다.**

### ⚠️ 그런데 — Enableable도 공짜가 아니다

문서를 파다 알게 된 것:

> *"to prevent race conditions, **jobs with write access to enableable components might cause main-thread operations to block until the job completes**, even if the job doesn't enable or disable the component on any entities."*

`EnabledRefRW<DeadTag>`를 쓰는 것만으로 — **한 마리도 안 죽여도** — 메인 스레드가 블록될 수 있다. `DamageApplyJob`이 정확히 그 경우다.
→ A의 비용은 **0이 아니라 "동기화 대기"**, B는 **"구조 변경 1만 회"**. **둘 다 비용이 있고 종류가 다르다.**

### ⚠️ 버린 지표 — `초과분 합`

처음엔 `초과분 = (발사 후 15프레임 합) − (기준선 × 15)`을 주 지표로 삼았다. **잘못된 설계였다.**

| | 평균 | sd | 범위 |
|---|---:|---:|---|
| A | 11.84 | **17.48** | **−20.78** ~ 60.17 |
| B | 16.46 | 10.01 | 2.08 ~ 39.82 |

**A에 음수가 나온다.** "9,600마리 죽였는데 평상시보다 빨랐다"는 말이 안 된다. 기준선을 재는 60프레임 동안 에디터 히칭이 한 번만 끼어도 기준선이 부풀고, 초과분이 음수로 튄다. → **노이즈 2개를 빼서 노이즈를 2배로 만든 지표**였다(`t = 1.03`, 결론 불가).

**스파이크 최대는 그 문제가 없다** — 발사 프레임 하나만 본다. 40라운드 **전부 `@frame+0`** 에서 최대가 나온 것도 이 지표가 옳다는 증거다.

> **교훈: 두 노이즈 값을 빼서 만든 지표는 노이즈가 더해진다.**

---

## 측정 도구 — `AoeSpikeHarness`

`BenchmarkHarness`는 **정상 상태 120프레임 평균**을 낸다. 동시 사망은 **1회성 폭발**이라 평균에 녹으면 안 보인다. 그래서 별도 하니스를 만들었다.

**라운드 절차**: 스폰 → **수렴 대기(95%)** → 안정화(기준선 60프레임) → **`SpawnSystem` 정지** → 발사 → 15프레임 개별 기록

- ★ **발사 전 `SpawnSystem`을 끈다.** 안 끄면 `프레임 N: 1만 파괴` → `프레임 N+1: 1만 생성`이 겹쳐 **사망 비용만 분리할 수 없다.**
- **수렴 게이트(95%)** 덕에 40라운드가 전부 9,592~9,651마리(**0.6% 안**)로 일치 → **A/B 비교의 전제 성립**.
- 라벨은 손으로 안 정한다 — `TypeManager.IsEnableable(GetTypeIndex<DeadTag>())`로 **타입이 스스로 말하게** 해서 오라벨을 원천 차단.
- 비포커스 프레임 제외는 `BenchmarkHarness`와 동일 정책(기존 측정과의 비교 가능성).

## Run In Background 활성화

포커스가 없으면 게임 루프가 멈춰서(`t=0.02`) 폴링·수렴 대기가 불가능했다. → `PlayerSettings.runInBackground = true`.

**단 벤치마크 가드는 유지한다** — `Application.isFocused`는 이 설정과 무관하게 실제 포커스를 반영하고, Week 2/3을 포커스 상태에서 쟀으므로 **비교 가능성**을 지켜야 한다. Run In Background가 벌어주는 건 측정이 아니라 **워크플로우**다.

---

## 이번 주 배운 것 (①②)

- **설계선이 값을 하는 순간** — 광역기가 "숫자만 바꾸는 일"이 된 건 Week 3에 공격을 **데이터로** 모델링해뒀기 때문이다. 좋은 경계는 **나중에 공짜를 만든다.**
- **Enableable의 본질은 "병렬 가능"** — 청크 이사가 싸서가 아니라, **잡 안에서 할 수 있어서** 빠르다. 공식 문서의 강조점도 sync point / ECB 회피다.
- **표본이 결론을 만든다** — 같은 실험이 n=5에선 `t=1.27`(결론 불가), n=20에선 `t=5.87`(유의). **노이즈가 크면 표본으로 이긴다.** Week 3의 전투 A/B는 신호 1ms에 노이즈 ±0.5라 가망이 없었지만, 이번엔 신호 10ms에 이길 수 있었다.
- **지표 설계가 결론을 좌우한다** — 같은 40라운드 데이터로 `초과분`은 "모름", `스파이크`는 "+33% 유의". **뺄셈으로 만든 지표를 조심할 것.**
- **밀도 직관은 틀린다** — separation은 무른 힘이라 어림값의 3배까지 압축된다.
- **폴링 버그** — `get-logs --search-text "AOE-CSV"`의 응답 JSON에 `"SearchText": "AOE-CSV"`가 들어가서 **검색어 자체를 매칭**, 1회차에 "완료"로 오판했다. **자기 질의를 자기가 매칭하는 함정.**

## 다음 (Week 4 잔여)

- [ ] **③ 플레이어 사망 처리** — `PlayerState`에 **`IsDead` 한 비트**만 (`Health` 전체가 아니라 **적이 필요한 것만**)
- [ ] **④ 넉백/경직** — `DamageApplyJob`의 합산값을 세기로 재활용. **`DamageEvent`에 `SourcePos` 추가 필요**(현재 방향 정보 없음) — 버퍼 스키마를 바꾸는 첫 사례
- [ ] 오버숄더 카메라 · 콤보 (W3에서 이월)
- [ ] **반경 스윕 벤치** — 셀 스캔이 `(2R+1)²`로 폭발. R=25는 2,601셀, R=50은 10,201셀인데 **결과는 똑같이 1만 킬**. "그리드가 손해로 뒤집히는" 지점을 찾는 새 축
- [ ] 포위 밀도 밸런싱 — 수렴 시 초당 ~590 데미지로 플레이어가 17초에 사망
