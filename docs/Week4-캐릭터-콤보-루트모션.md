# Week 4 — 캐릭터 · 4타 콤보 · 무기 영역 판정 · 루트모션

> 주차별 진행 기록. 계획은 [`작업계획.md`](작업계획.md), 같은 주차 전반부는 [`Week4-광역기-구조변경벤치.md`](Week4-광역기-구조변경벤치.md).
> 상태: ✅ **완료** — 캐릭터 ✅ · 카메라 ✅ · 애니 ✅ · 4타 콤보 ✅ · 무기 영역 판정 ✅ · 루트모션 ✅

**기간**: 2026-07-18
**목표**: 캡슐 플레이어를 실제 캐릭터로 교체하고, **무쌍류 4타 콤보**를 붙인다. 핵심은 *"DOTS엔 콜라이더가 없는데 근접 공격 판정을 어떻게 하나"*.

---

## 캐릭터 스택 — 이동 · 카메라 · 애니

- **이동**: 3인칭 **스트레이프**(조준형). 마우스 X → 루트 yaw, 마우스 Y → CameraTarget pitch, WASD는 캐릭터 로컬 기준. 이동 방향으로 몸을 돌리지 않으므로 WASD가 그대로 방향별 이동 애니가 된다. 평소 달리기 / Shift 걷기.
- **카메라**: Cinemachine v3 (`CinemachineCamera` + `ThirdPersonFollow`). 진삼국무쌍 오리진처럼 CameraTarget Y=0 · Distance=3.
- **애니**: 중첩 블렌드트리 — 1D(Speed) → Idle(0) / Walk 2D(0.5) / Run 2D(1.0), 방향은 FreeformDirectional2D(MoveX/MoveY).
- **리타게팅**: IdaFaber HornedKnight 모델(Humanoid) + ARPG Warrior 애니.

이 부분은 정석대로라 특별할 게 없다. **진짜 이야기는 공격 판정부터.**

---

## 공격 판정 — Collider가 아니라 "가상 영역 쿼리"

보통 근접 공격은 무기에 콜라이더를 달고 `OnTriggerEnter`로 잡는다. **DOTS에선 안 된다** — 적은 콜라이더 없는 인스턴싱 캡슐 수천 개고, 물리 트리거를 쓰면 그 이점이 통째로 날아간다.

대신 이미 있는 걸 재사용했다: **`SpatialHash` 공간 쿼리**. 무기(손) 위치를 중심으로 반경 안의 적을 훑는다.

```
콜라이더 접촉  ✗   →   무기 위치로 SpatialHash 반경 쿼리  ✓
                       (= 이미 회피·광역기가 쓰던 그 쿼리)
```

이건 우회가 아니라 **DOTS의 정공법**이다. 그리고 이 "무기에 가상 영역을 그린다"는 발상은 그대로 **몬스터·보스에게 재사용**된다 — 칼 든 보스가 이 파이프라인을 그대로 쓴다.

### 스윙 동안 훑기 (Start/End 윈도우) + 스윙당 1히트

단발 판정이면 무쌍이 안 된다. **애니가 칼을 휘두르는 내내** 지나가는 적이 차례로 맞아야 한다.

- `OnAttackStart` ~ `OnAttackEnd` 사이 **매 프레임** 손 위치로 `AttackRequest` 발사 → 손이 아크를 그리며 훑는다.
- 그러면 한 적이 여러 프레임 맞는다. 그래서 `SwingId`:

```csharp
// AttackResolveSystem — 스윙당 1히트 중복제거
if (r.SwingId > 0 && hitTracker.HasComponent(hit.Entity)) {
    var ht = hitTracker[hit.Entity];
    if (ht.LastSwingId == r.SwingId) continue;   // 이번 스윙에 이미 맞음 → 스킵
    ht.LastSwingId = r.SwingId;
    hitTracker[hit.Entity] = ht;
}
```

`SwingId>0`이면 스윙당 1히트, `SwingId=0`이면 단발(항상 히트) — **기존 광역기가 안 깨진다.** 새 ECS 시스템 **0개**. `AttackRequest`에 필드 하나, 적에 `HitTracker` 하나, AttackResolve에 블록 하나가 전부.

**검증 (한 스윙)**: 공격 전 풀피 10,000 → 공격 1회 → 다친 적 **1,258마리, 전부 HP 60**(=100−40, 정확히 1히트) · **중복히트 0** · 죽음 0. 매 프레임(~18) 발사됐는데도 각 적 1번. 한 스윙에 1,258마리 = 손이 조밀한 무리를 훑었다.

---

## 판정 기점을 Animation Event로 클립마다 지정

콤보 클립이 **왼손·오른손을 번갈아** 친다. 히트박스를 `hand_r`에 고정하면 왼손 공격 때 **엉뚱한 손에서 판정**이 나가 빗나간다.

해결: `OnAttackStart`를 `(AnimationEvent)` 시그니처로 받아 **이벤트의 파라미터**를 읽는다. 이벤트 하나에 String·int·float이 다 실린다:

- **String** = 기점 본 이름 (`hand_r` / `hand_l`). 클립마다 에디터에서 지정. 비면 기본값.
- **float** = 위력 배수(강타). Combo4에 `2.0` → 데미지·넉백·경직 2배. 비면 1x.

```csharp
public void OnAttackStart(AnimationEvent evt) {
    string bone = string.IsNullOrEmpty(evt.stringParameter) ? _defaultAnchorBone : evt.stringParameter;
    _activeAnchor = ResolveBone(bone);                         // 이 스윙의 기점
    _activePower  = evt.floatParameter > 0f ? evt.floatParameter : 1f;   // 강타 배수
    IsSwinging = true; SwingId++;
}
```

> `(AnimationEvent)` 오버로드는 파라미터 유무와 무관하게 **항상 바인딩**된다. 그래서 파라미터 없이 만들어둔 기존 Combo1 이벤트도 그대로 기본 손으로 동작한다.

**검증 (강타)**: Combo4 재생 → 1,341마리 **전부 HP 20**(=100−80, 2배) · 평타(HP 60) 0 · 그 이하 0. 강타 2배 + 스윙당 1히트 둘 다 유지.

---

## 4타 콤보 — 코드는 콤보를 **모른다**

이 설계의 핵심. 리스너는 *"지금 몇 타째냐"* 를 전혀 몰라도 된다.

- 좌클릭 → `Attack` 트리거. **어느 콤보 클립이든** 그 안의 `OnAttackStart`가 뜨면 새 스윙(`SwingId++`).
- 각 콤보 타 = 새 SwingId = 새 스윙 → 적은 **타마다** 다시 1히트 가능.

즉 **콤보 체이닝은 순수 Animator 작업**이다. 코드 변경 0.

```
Combo1 →(Attack, ExitTime 0.60)→ Combo2 →(Attack, 0.60)→ Combo3 →(Attack, 0.70)→ Combo4
   └(ExitTime 0.90)→ Locomotion  (각 상태에서 체인 안 하면 복귀)
```

`Attack`은 소비될 때까지 남는 **트리거(입력 버퍼)**. 재생 중 클릭하면 트리거가 켜지고, ExitTime을 넘는 순간 다음 콤보로 전이. 안 누르면 복귀. → **[ExitTime, 복귀시점] 구간이 자연스러운 콤보 윈도우.** 체인 지점이 스윙 끝 직전이라 **캔슬 감**이 붙는다(무쌍 손맛).

---

## 잡은 것 세 가지

### ① 연타하면 1타 더 나가는 "유령타"

Combo4(마무리, 체인 없음) 중에 한 번 더 클릭하면 그 `Attack` 트리거가 **소비될 데가 없어 남는다**. Combo4→Locomotion 복귀 순간 Locomotion→Combo1을 **재발동** → 혼자 1타 더.

**수정**: `ResetTriggerOnEnter`(StateMachineBehaviour)를 Locomotion에 붙여 **진입 시 트리거 청소**. 폴링이 아니라 `OnStateEnter`라 프레임 순서가 보장된다.

> 검증: Combo4 + 여분 트리거 심음 → 마무리 후 **Loco 유지**(Combo1 아님), 트리거 대기 False.

### ② "모션은 전진인데 몸은 제자리" — 루트모션

콤보 클립엔 전진(z **0.77~1.42 m/s**)이 이미 baked돼 있었다. 그런데 `applyRootMotion=False`라 **버려지고** 있었다. **새 클립도 재작업도 불필요 — 켜기만 하면 됐다.**

함정: **Animator가 자식**(SK_HornedKnight)에 있다. 그냥 켜면 루트모션이 **자식만** 밀어 루트(카메라·ECS 위치)와 어긋난다.

```
Player (루트) ← PlayerController · PlayerStateBridge   ← 여기가 움직여야 함
 └ SK_HornedKnight (자식) ← Animator                   ← 기본 루트모션은 여길 민다 ✗
```

→ `OnAnimatorMove`에서 **공격 중에만** `deltaPosition`을 **루트**로 옮긴다(회전은 마우스가 쥐고 있으니 위치만). 로코모션은 in-place라 델타≈0이고 어차피 스크립트가 이동.

> 검증: Combo1 재생당 루트 **0.67 m 전진**(WASD 입력 0인데도).

### ③ 공격 중 이동 잠금

`PlayerController`가 리스너 `IsAttacking`(현재 Combo1~4 상태)을 확인해 공격 중 **WASD 병진 차단**. 마우스 시점·루트모션 러시는 유지 → **러시 방향을 마우스로 조준**할 수 있다.

---

## 배운 것

- **Week 3에서 공격을 "요청 엔티티"로 만들어둔 복리(複利)가 여기서 또 터졌다.** 광역기에 이어 근접 콤보까지 새 시스템 0개. 파이프라인 설계 한 번이 몇 주를 먹여 살린다.
- **콤보를 코드로 짜지 않았다.** 상태 기계는 Animator가 잘하는 일이다. 코드가 콤보 인덱스를 들고 있으려 했으면 복잡해졌을 것. "각 스윙 = 새 SwingId"만으로 코드는 콤보에 무지한 채 정확하다.
- **문제를 데이터로 먼저 봤다.** "루트모션 해야 하나?"에 답하기 전에 `clip.averageSpeed`를 찍었더니 *이미 들어있는데 꺼져 있음*이 드러났다. 안 찍었으면 클립 재작업이라는 헛수고를 했을 것.
- **Animation Event는 컴포넌트가 Animator와 같은 GameObject에 있어야 전달된다.** 그래서 리스너를 자식(캐릭터)에 뒀고, 그 때문에 루트모션 함정(②)이 생겼다. 한 제약이 다른 곳에 파장을 남긴다.

---

## 다음

- 손 String 클립별 지정(에디터) 후 최종 플레이감 확인
- 적 모델 교체(캡슐 → 실제 몬스터) 시 이 공격 파이프라인을 몬스터가 재사용
