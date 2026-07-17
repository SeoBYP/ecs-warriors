# Week 4 — 광역기 · 구조 변경 A/B 벤치마크 · 사망/넉백

> 주차별 진행 기록. 계획은 [`작업계획.md`](작업계획.md), 이전 주차는 [`Week3-전투-양방향브릿지.md`](Week3-전투-양방향브릿지.md).
> 상태: 🟡 **진행 중** — ① 광역기 ✅ · ② 구조 변경 A/B ✅ · ③ 플레이어 사망 ✅ · ④ 넉백 ✅ · 경직/카메라/콤보 ⬜

**기간**: 2026-07-17 ~
**목표**: 광역기(범위 수천 동시 피격) · **"Enableable이 진짜 빠른가" 수치 증명** · 양방향 전투의 완성도(사망·넉백)

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

## ③ 플레이어 사망 — **`bool` 한 비트로 끝났다**

Week 3에서 *"`PlayerState`에 Health를 추가해야 하나?"* 에 **아니오**라고 답하며 남겨둔 것을 회수했다. 그때 세운 원칙:

> **브릿지에 필드를 추가하는 기준은 "반대편이 그걸 읽느냐"다.**

그때는 플레이어 HP를 읽는 ECS 시스템이 **0개**였다. 이제 `EnemyAttackSystem`이 "죽었나?"를 알아야 하니 **생겼다.** 그래도 `Health`가 아니라 **`bool IsDead`** 만 넣는다 — 적은 "몇 HP 남았나"가 아니라 **때릴지 말지**만 정하면 되니까. `Health`를 넣었다면 **진실의 출처가 둘**이 되어(GO의 `Hp` + ECS 복사본) 매 프레임 동기화해야 하고, 어긋나면 "UI는 500인데 ECS는 300"이 된다.

> 공식 문서 확인: `bool`은 unmanaged 컴포넌트 필드로 **명시적으로 허용**된다. 재미있는 건 *"Blittable types"* 와 **별도로** 나열된다는 점 — C#에서 `bool`은 마샬링 크기가 불정(1/2/4바이트)이라 엄밀히는 blittable이 아니고, Unity가 예외적으로 허용해준 것이다.

### ⚠️ 핵심은 **부분 갱신**이었다

두 브릿지가 같은 싱글톤을 각자 쓴다. `PlayerStateBridge`가 매 프레임 이렇게 하면:

```csharp
_em.SetComponentData(e, new PlayerState { Position = transform.position });
//                       ↑ new = IsDead가 default(false)로 리셋됨
```

`PlayerHealthBridge`가 아무리 `IsDead = true`로 세팅해도 **다음 프레임에 지워진다.** 그리고 **에러는 한 줄도 안 난다** — 죽은 플레이어를 적이 계속 때릴 뿐이다.

```csharp
// 읽고 → 내 필드만 고치고 → 되쓰기
var s = _em.GetComponentData<PlayerState>(e);
s.Position = transform.position;
_em.SetComponentData(e, s);
```

### 잡이 아니라 **시스템**에서 반환

```csharp
var player = SystemAPI.GetSingleton<PlayerState>();
if (player.IsDead) return;          // ← 잡 스케줄 자체를 건너뜀
```

잡 안에서 조기 반환하면 1만 마리 순회는 그대로 한다. **시스템에서 나가면 스케줄 자체가 없다** → 사망 후 `EnemyAttackSystem`은 말 그대로 공짜.

### 검증

- `t=6.6`에 `Hp 0` → **`IsDead=True`** 전환
- 적 `Timer`가 6초간 4회 샘플 **전부 `1.000` 고정** = 시스템이 안 돎
  → Baker 초기값이 `0`이므로 `1.000`은 **"한 번 때리고 쿨다운으로 리셋된 직후"** 에만 나오는 값. 즉 **때리다가 딱 멈췄다.**
- `IsDead`가 수십 프레임 유지됨 = **부분 갱신이 `Position`에 안 밟혔다는 증거**

---

## ④ 넉백 — 버퍼 스키마를 **처음으로** 바꾼다

Week 3에 *"합산하면 나중에 넉백 세기 판단에 쓸 수 있다"* 고 남겨둔 `total`을 회수했다. **세기는 이미 있었는데 방향이 없었다.**

```
DamageEvent { int Amount }
           → { int Amount, float3 SourcePos, float KnockbackScale }
```

### 왜 `SourcePos`(위치)인가 — `Direction`도 `Entity`도 아니고

| 안 | 문제 |
|---|---|
| `float3 Direction` (정규화) | **거리 정보가 사라진다.** "가까울수록 세게"를 나중에 넣으려면 스키마를 또 고쳐야 함 |
| `Entity Attacker` | 넉백 계산 시점에 **그 적이 이미 죽어 사라졌을 수** 있다 |
| **`float3 SourcePos`** ✅ | **방향과 거리를 둘 다 품고**, 값이라 안전 |

`Entity`가 위험한 건 `DeathEvent`에서 **"죽은 자의 정보는 죽기 전에 스냅샷"** 한 것과 **같은 이유**다.

### ★ `Amount`는 더하면 그만이지만 **방향은 아니다**

한 프레임에 3명이 사방에서 때리면 방향이 3개다:

| 방식 | 결과 |
|---|---|
| 가중 평균 | 사방에서 맞으면 **상쇄돼 안 밀림**. 물리적으론 맞지만 무쌍답지 않다 |
| 첫 번째만 | 임의적 (병렬 쓰기라 순서가 비결정적) |
| **최대 데미지 1건** ✅ | "제일 센 놈한테 밀림". 무쌍답고 **결정론적** |

루프는 **합산 + 최대 건 기억**만 하고, **넉백은 루프 밖에서 한 번.** `DamageEvent` 버퍼를 "합산하고 한 번에 적용"한 것과 같은 발상이다.

> 처음엔 루프 **안**에서 밀어냈다. 그러면 3명한테 맞을 때 3번 밀리고, **밀린 위치에서 다음 방향을 다시 재서** 버퍼 순서에 결과가 의존한다. "최대 1건만"의 의미가 거기서 사라진다.

### `KnockbackScale`은 요청이 들고 온다

`AttackResolveSystem`에 `5f`를 박으면 평타와 광역기가 **똑같이** 밀린다. `AttackRequest`에 올리면 시스템은 `r.KnockbackScale`을 **흘려보내기만** 하고, 인스펙터에서 튜닝된다(평타 2.5 / 광역기 5.0). **광역기가 시스템 코드 없이 붙었던 것과 같은 구조.**

### 잡은 함정

- **세기를 `Amount`로 쓰면** — `direction`이 단위 벡터라 곱한 값이 **곧 미터**다. 광역기 `Damage=100` → **100미터 순간이동**(스폰 반경이 100인데!). **데미지와 넉백 거리는 별개 축.**
- **`normalize(0 벡터)` → `NaN`** → `Position`이 NaN → **렌더에서 사라지고 영구 복구 불가.** `SeparationJob`의 `d > 0.0001f`와 같은 가드가 필요하다. 가상의 위험이 아니었다 — 실측에서 **중심 0.03에 붙은 적이 실제로 있었다.**
- **`total > 0` 가드 없이 두면** 안 맞은 1만 마리도 매 프레임 `normalize`를 돌고, `maxSource`가 `float3.zero`라 **월드 원점 기준으로 밀** 뻔했다(`maxScale=0`이라 **우연히** 안 움직였을 뿐).
- **죽은 적은 넉백 스킵** — 같은 프레임에 `DeathSystem`이 파괴할 놈이다. 광역기면 9,600마리분의 헛일.

### 🔬 검증 — 정확히 `+5.00`

`MovementSystem`·`SpatialHashSystem`을 **런타임에 정지**시켜 넉백만 `Position`을 만지게 했다.

```
정지 검증:  3회 샘플이 소수점까지 동일 (8.08 / 0.03 / 15.39)
발사 전:    평균  8.08   최소 0.03   최대 15.39
발사 후:    평균 13.08   최소 0.03   최대 20.39
              ↑ +5.00           ↑ +5.00  = KnockbackScale과 정확히 일치
```

- **평균도 최대도 소수점까지 `+5.00`** — 1만 마리 전원이 자기 방향으로 정확히 5씩 밀렸다
- 발사 후 3회 샘플이 **완전 동일** = 밀린 자리에 정지
- **`최소`가 `0.03`에서 불변** = **NaN 가드가 실제로 일했다** (`NaN=0`)

**시스템을 켜둔 채로는 `+1.24`밖에 안 보였다** — 넉백과 이동이 같은 `Position`을 두고 싸우기 때문. 끄니까 **오차 0**.

> 방법론 실패도 기록: 처음엔 `Damage=0`으로 쏴서 "넉백만 분리"하려 했는데, **`total > 0` 가드가 그걸 막았다.** 가드는 정상 동작한 것("안 맞았으면 안 민다")이고 **테스트 설계가 틀렸다.** `Damage=1`(안 죽고 밀리기만)로 해결.

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

## 이번 주 배운 것

### 설계

- **설계선이 값을 하는 순간** — 광역기가 "숫자만 바꾸는 일"이 된 건 Week 3에 공격을 **데이터로** 모델링해뒀기 때문이다. `KnockbackScale`도 같은 자리(요청)에 얹혀 시스템 수정 없이 붙었다. **좋은 경계는 나중에 공짜를 만든다.**
- **Enableable의 본질은 "병렬 가능"** — 청크 이사가 싸서가 아니라, **잡 안에서 할 수 있어서** 빠르다. 공식 문서의 강조점도 sync point / ECB 회피다.
- **브릿지는 표면적이 좁을수록 좋다** — Week 3에 미룬 `Health`가 결국 **`bool` 한 비트**로 끝났다. 필요해질 때까지 안 넣은 게 옳았다.
- **스키마를 바꾸면 소비 로직도 바뀐다** — `Amount`는 교환법칙이 성립해 그냥 더했지만, `SourcePos`는 **여러 개일 때 무엇을 고르나**를 정해야 했다. **필드 하나가 정책 하나를 요구한다.**
- **값을 담을지 참조를 담을지** — `Entity`(참조)는 죽으면 무효, `float3`(값)는 안전. `DeathEvent`의 "죽기 전에 스냅샷"과 같은 원리가 `DamageEvent`에서 반복됐다.

### 측정 방법론 (이번 주에 네 번 데였다)

- **표본이 결론을 만든다** — 같은 실험이 n=5에선 `t=1.27`(결론 불가), n=20에선 `t=5.87`(유의). **노이즈가 크면 표본으로 이긴다.** Week 3의 전투 A/B는 신호 1ms에 노이즈 ±0.5라 가망이 없었지만, 이번엔 신호 10ms라 이길 수 있었다.
- **지표 설계가 결론을 좌우한다** — 같은 40라운드 데이터로 `초과분`은 "모름", `스파이크`는 "+33% 유의". **뺄셈으로 만든 지표는 노이즈가 더해진다.**
- **경쟁하는 시스템을 끄면 오차가 0이 된다** — 넉백을 시스템 켜둔 채 재면 `+1.24`(뭉개짐), `MovementSystem`을 끄면 **`+5.00` 정확**. **무엇이 측정을 오염시키는지 아는 게 측정의 절반이다.**
- **테스트 설계도 틀린다** — `Damage=0`으로 "넉백만 분리"하려다 `total > 0` 가드에 막혔다. **가드가 옳았고 테스트가 틀렸다.**
- **밀도 직관은 틀린다** — separation은 무른 힘이라 어림값의 3배까지 압축된다.

### 툴링

- **폴링 버그** — `get-logs --search-text "AOE-CSV"`의 응답 JSON에 `"SearchText": "AOE-CSV"`가 들어가서 **검색어 자체를 매칭**, 1회차에 "완료"로 오판했다. **자기 질의를 자기가 매칭하는 함정.**
- **`SystemHandle.Null` 체크를 빼먹으면** *"belongs to a different world"* 라는 엉뚱한 메시지가 나온다. 실제 원인은 **플레이 모드가 꺼져 있어 `Editor World`를 잡은 것.**
- **Run In Background**는 워크플로우를 벌어주지 벤치마크를 벌어주지 않는다 — `Application.isFocused`는 이 설정과 무관하다.

## 다음 (Week 4 잔여)

- [ ] **경직/히트스톱** — 넉백과 달리 **상태와 타이머**가 필요(`StunTimer` + `MovementSystem`이 존중). 넉백이 굴러가는 걸 보고 얹는다
- [ ] 오버숄더 카메라 · 콤보 (W3에서 이월)
- [ ] **반경 스윕 벤치** — 셀 스캔이 `(2R+1)²`로 폭발. R=25는 2,601셀, R=50은 10,201셀인데 **결과는 똑같이 1만 킬**. "그리드가 손해로 뒤집히는" 지점을 찾는 새 축
- [ ] 포위 밀도 밸런싱 — 수렴 시 초당 ~590 데미지로 플레이어가 17초에 사망
- [ ] 플레이어 넉백 — `PlayerDamageEvent`에도 `SourcePos` 추가 필요(스키마 두 번째 변경)
- [ ] 사망 연출 — 지금은 `IsDead`만 서고 화면엔 아무 일도 안 일어난다
