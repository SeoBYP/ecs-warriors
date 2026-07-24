# Week 6 — VAT 군중 애니메이션 (1만 마리 상태머신)

> 주차별 진행 기록. 계획은 [`작업계획.md`](작업계획.md), 이전 주차는 [`Week5-벤치하니스-프로파일러격리.md`](Week5-벤치하니스-프로파일러격리.md).
> 상태: 🟢 **완료** — VAT 파이프라인 ✅ · 멀티클립 상태머신 ✅ · 사망 연출 ✅ · 넉백 ✅

**기간**: 2026-07-24 ~ 2026-07-25
**목표**: 캡슐이던 잡몹을 **실제 좀비 메시**로 바꾸고, 1만 마리가 **각자 상태에 맞는 애니메이션**(걷기·공격·피격·사망)을 재생하게 한다.

![1만 좀비가 걷고, 물어뜯고, 넉백에 밀려 쓰러진다](images/week6-vat-state-machine.gif)

---

## 문제 — 스킨드 메시를 ECS로 그리면 T포즈가 나온다

좀비 메시를 얹었더니 **전원 T포즈로 누워서** 몰려왔다. 버그가 아니라 **필연**이었다.

리깅된 메시는 정점이 **바인드 포즈(=T포즈)** 로 저장되고, 뼈 23개에 가중치가 걸려 있다. `SkinnedMeshRenderer`가 매 프레임 뼈로 정점을 변형(**스키닝**)해야 비로소 포즈가 나온다. 그런데 ECS 인스턴싱은 `MeshFilter`로 그리고 — **거기엔 스키닝이 없다.** 저장된 원본(T포즈)을 그대로 그릴 뿐이다.

```
플레이어(1명) : SkinnedMeshRenderer + Animator → 스키닝 O → 정상 포즈
좀비 1만      : MeshFilter (스키닝 X)          → 바인드 포즈 = T포즈
```

1만 마리에 `SkinnedMeshRenderer`를 달 수는 없다(오브젝트 1만 + 스키닝 1만 = 즉사). **다른 방식이 필요했다.**

## 해법 — VAT (Vertex Animation Texture)

> **매 프레임 뼈로 정점을 "계산"하는 대신, 정점의 프레임별 위치를 텍스처에 미리 "녹화"해두고 셰이더가 "재생"한다.**

| | 정점 목표 위치의 출처 | 1만 마리 |
|---|---|---|
| **SkinnedMeshRenderer** | 뼈 행렬로 실시간 계산 | 오브젝트 1만 + 스키닝 1만 = 죽음 |
| **VAT** | 텍스처에서 읽음(미리 구움) | 메시1 + 텍스처1 공유 → **1드로우콜** |

텍스처 레이아웃은 **가로=정점 인덱스, 세로=프레임**. 정점 번호는 메시 UV3에 구워둔다.

```
        정점0   정점1  ...  정점700
프레임0 [(x,y,z)(x,y,z) ...       ]     ← 701 × 프레임수 텍스처
프레임1 [ ...                      ]
```

**메모리는 인스턴스 수와 무관하다** — 1마리든 1만이든 텍스처 1장을 공유. 저폴리 좀비(701정점) 기준 클립당 약 164KB로, 캐릭터 알베도 1장(4MB)의 1/24 수준이다.

### 파이프라인 3단

```
[에디터·1회] VATBaker: SampleAnimation → BakeMesh → 정점을 텍스처에 기록
[런타임]     셰이더 vertex: 시간→프레임 행, 텍셀 Load → 정점 이동
[ECS]        MaterialProperty로 인스턴스별 "어느 클립을 언제부터"
```

셰이더 정점 로직이 사실상 전부다:
```hlsl
float local = (_Time.y - _AnimStart) * _AnimParams.z;             // 경과 프레임
float frame = (_AnimParams.w > 0.5) ? fmod(local, _AnimParams.y)  // 루프
                                    : min(local, _AnimParams.y-1);// 원샷(사망) 마지막 프레임 정지
int row = (int)(_AnimParams.x + frame);
float3 posOS = LOAD_TEXTURE2D_LOD(_PositionMap, int2(vertexId, row), 0).xyz;
```

### 노멀도 같이 굽는 이유

포즈가 바뀌면 **면이 향하는 방향도 바뀐다.** 바인드 포즈의 고정 노멀을 쓰면 팔을 휘둘러도 밝기가 그대로라 **"빛이 모델에 그려진"** 것처럼 보인다. 그래서 위치와 같은 크기로 노멀 텍스처를 함께 굽는다.

---

## 멀티클립 상태머신

클립 5개(idle·walk·attack·damage·death)를 **한 텍스처에 세로로 스택**하고, 클립마다 `{startRow, frameCount, fps, loop}` 를 기록한다.

```
행   0~29  idle    (loop)
행  30~59  walk    (loop)
행  60~89  attack  (loop)
행  90~119 damage  (원샷)
행 120~149 death   (원샷 → 마지막 프레임 정지)
```

이 표는 **Blob 에셋**(`VATClipTable`)으로 구워 전 좀비가 공유한다. `IComponentData`엔 가변 배열을 못 넣고, `DynamicBuffer`로 넣으면 1만 마리가 각자 복사본을 갖는다. Blob은 **80바이트 1벌 + 각자 핸들 8바이트**다.

`ZombieAnimSystem`(IJobEntity 병렬)이 매 프레임 상태를 판정한다:

```
want = 넉백중?      Damage      // 밀려나는 동안엔 히트 모션 (죽었어도)
     : DeadTag?     Death
     : Stun>0?      Damage
     : 이동중?      Walk
     : 사거리내?    Attack
     :              Idle

if (want != current) {                       // ★ 전환될 때만
    animParams = table[want];
    animStart  = now - phase;                // 루프는 엔티티별 고정 위상(군중 동기화 방지)
    current    = want;
}
```

### 함정 ① — enableable 컴포넌트는 쿼리에서 "켜진 것만" 걸린다

`DeadTag`는 enableable이고 산 좀비는 **꺼진** 상태다. `EnabledRefRO<DeadTag>`를 Execute에 넣으면 상태를 읽을 수 있지만, **쿼리 필터는 그대로 살아있어** 매칭 엔티티가 **0개**가 됐다. 잡은 돌지만 처리 대상이 없어 **1만 마리가 전부 미초기화**로 남았다.

```csharp
[WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]   // 필터를 꺼야 전체 순회
```

프로브로 `Enemy+DeadTag [기본]=0` vs `[IgnoreComponentEnabledState]=10000` 을 찍어 확정했다.

### 함정 ② — 초기 상태를 실제 상태로 두면 영영 초기화가 안 된다

`Current = Idle`로 시작하면, 처음부터 idle인 좀비는 `want == current`라 **전환이 없어 파라미터가 채워지지 않는다.** 컴포넌트 기본값 `(0,0,0,0)`이 머티리얼 기본값을 덮어써 프레임0에서 얼어붙는다.

→ **`None(255)` 센티널**로 시작해 첫 프레임에 **강제 전환**시킨다.

---

## 사망 연출 — 즉시 파괴에서 "쓰러지고 나서"로

기존 `DeathSystem`은 DeadTag가 켜진 그 프레임에 `DestroyEntity`했다. 사망 클립이 재생될 시간이 없다.

```
피격 → HP 0 → DeadTag
   ├ ZombieAnimSystem : Death 클립(원샷, 마지막 프레임 정지)
   ├ DeathSystem      : 타이머 = 클립 길이(1.67s) 시작 + DeathEvent 즉시 큐잉
   ├ Move/Attack      : [WithDisabled(DeadTag)] 로 제외 → 멈춰서 쓰러짐
   └ 1.67초 후        : DestroyEntity
```

**점수·VFX는 죽는 즉시** 큐잉하고 엔티티만 남긴다. 파괴 시점에 큐잉하면 1.7초 뒤에 점수가 올라 어색하다.

---

## 넉백 — "밀려나며 히트, 멈춘 뒤 사망"

기존 넉백은 **즉시 텔레포트**였고, **죽으면 아예 적용되지 않았다**(`if (health > 0)`). "넉백 공격에 죽으면 밀려난 다음 쓰러지는" 연출을 위해 둘 다 바꿨다.

1. **지속시간 있는 이동** — `Knockback { Velocity, Remaining }` + `KnockbackSystem`. 0.25초에 걸쳐 밀린다.
2. **죽어도 넉백** — 조건에서 생존 검사를 뺐다.
3. **애니 우선순위에서 넉백 > 사망** — 밀리는 동안엔 죽었어도 히트 모션, 멈춘 뒤에 사망 모션.
4. **사망 타이머는 넉백 후 시작** — 안 그러면 사망 모션이 0.25초 잘린다.
5. **이동 정지** — 넉백과 추적 이동이 서로 밀당하지 않게.

### 함정 ③ — 애니 이벤트는 전환이 끝나기 전에 발사된다

"4타에만 넉백"을 `GetCurrentAnimatorStateInfo(0).IsName("Combo4")`로 판정했더니 **항상 false**였다. 이벤트는 클립 시작 `t=0.33s`에 발사되는데 그 시점엔 Combo3→Combo4 **전환이 끝나지 않아 여전히 Combo3**으로 보인다. (우클릭은 Animator를 안 거쳐 멀쩡했다.)

→ 클립에 이미 박혀 있던 **`floatParameter`(강타 배수, Combo4만 2.0)** 로 판정. 타이밍과 무관하고, **"강타 = 넉백"** 이라는 규칙이 데이터로 표현된다.

---

## 결과

- **1만 마리가 각자 상태에 맞는 애니메이션** — 걷다가, 붙으면 물어뜯고, 맞으면 움찔하고, 넉백에 밀려 쓰러진다.
- **뼈·Animator 없이 GPU 인스턴싱** — 좀비당 엔티티 1개(메시를 루트로 합침), 텍스처 1장 공유.
- **5000마리 @ 98 FPS** (벤치 하니스 off, 에디터 기준)

## 남은 것

- 프레임 보간 — Point 샘플이라 저프레임 클립은 뚝뚝 끊긴다(좀비엔 오히려 어울리지만)
- 툰 셰이딩 — 현재는 램버트+SH만
- 좀비 종류별 클립세트 — 중첩 Blob(`BlobArray<BlobArray<float4>>`)으로 확장
- Elite/Boss 티어는 VAT가 아니라 `SkinnedMeshRenderer`(IK·블렌드·래그돌 필요)
