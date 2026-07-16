# Week 1 — 스폰 시스템 & FPS 오버레이

> 주차별 진행 기록. 계획은 [`작업계획.md`](작업계획.md), 이전 주차는 [`Week0-DOTS셋업.md`](Week0-DOTS셋업.md).
> 상태: 🟡 **진행 중** — 스폰 + 오버레이 완료, **카운트 슬라이더 남음**.

**기간**: 2026-07-16
**목표**: 런타임에 엔티티 N마리 스폰 + FPS/ms 오버레이. (DoD 최종: 1만 마리 + 슬라이더)

![Week 1 — 100마리 스폰 + FPS 오버레이](images/week1-spawn-overlay.png)

---

## Step 1. 몬스터 프리팹

- **한 일**: Week 0의 SubScene 캡슐을 **Prefab 에셋**(`Assets/Prefabs/Monster.prefab`)으로 승격.
- **핵심**: 런타임에 복제하려면 "정적으로 구운 엔티티 1개"가 아니라 **복제 원본 프리팹**이 필요.
- **정리**: 프리팹에서 **Capsule Collider 제거** (자작 Spatial Hash 설계라 Physics 불필요 → 베이킹 슬림).

## Step 2. `SpawnConfig` 컴포넌트

```csharp
public struct SpawnConfig : IComponentData
{
    public Entity Prefab;   // 복제할 엔티티 프리팹 핸들
    public int Count;
    public float Radius;
}
```
- unmanaged struct, public 필드. `Entity`도 blittable이라 struct에 그냥 들어감.

## Step 3. Authoring + Baker (프리팹 → 엔티티 프리팹)

- **`SpawnAuthoring`**(MonoBehaviour): 인스펙터 필드 `Prefab / Count / Radius`.
- **`SpawnBaker : Baker<SpawnAuthoring>`**: `Bake()`에서
  1. `GetEntity(TransformUsageFlags.None)` — 스포너 자신 엔티티
  2. `GetEntity(authoring.Prefab, TransformUsageFlags.Dynamic)` — 프리팹 → 엔티티 프리팹 핸들
  3. `AddComponent(entity, new SpawnConfig { ... })`
- **막힌 점 / 해결**:
  - Baker 클래스 이름을 `Baker`로 지어 **`Unity.Entities.Baker`와 충돌**(CS0308) → `SpawnBaker`로 개명.
  - `Bake()` 본문 미완성(`AddComponent<Spawn>` — 없는 타입, 프리팹 `GetEntity` 누락) → 3단계로 채움.

## Step 4. Spawner 배치 (SubScene)

- **한 일**: SubScene 안에 빈 GameObject `Spawner` → `SpawnAuthoring` 부착 → Prefab=Monster, Count=100, Radius=10.
- **막힌 점 / 해결**: 코드만 있고 **Spawner GameObject를 안 놔서** `SpawnConfig` 엔티티가 0개였음. Baker는 authoring이 붙은 GameObject가 SubScene에 있어야 실행됨.
- **검증**: `execute-dynamic-code`로 베이킹 월드 조회 → `SpawnConfig x1 Count=100 Radius=10 prefabExists=True` 확인.

## Step 5. `SpawnSystem` (ISystem) — 100마리 스폰

```csharp
public partial struct SpawnSystem : ISystem
{
    public void OnCreate(ref SystemState state)
        => state.RequireForUpdate<SpawnConfig>();

    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<SpawnConfig>();
        var entities = state.EntityManager.Instantiate(config.Prefab, config.Count, Allocator.Temp);
        var rng = Random.CreateFromIndex(1);
        for (int i = 0; i < entities.Length; i++)
        {
            var x = rng.NextFloat(-config.Radius, config.Radius);
            var z = rng.NextFloat(-config.Radius, config.Radius);
            state.EntityManager.SetComponentData(entities[i], LocalTransform.FromPosition(new float3(x, 0, z)));
        }
        state.Enabled = false;   // 한 번만 스폰
    }
}
```
- **막힌 점 / 해결**:
  - `RequireForUpdate` 누락 → SubScene 비동기 로드 전 `GetSingleton`이 예외. 추가로 해결.
  - 위치가 Y까지 무작위 + 하드코딩 `±10` → 공중 큐브로 흩어짐. **Y=0 + `config.Radius`** 로 지면 분포.
  - 네임스페이스를 `Simulation.System`으로 → `.NET System`과 충돌 위험. **`Simulation.Systems`(복수)** 로 개명.

## Step 6. `FpsOverlay` (FPS/ms 계기판)

- **한 일**: MonoBehaviour, `OnGUI`로 `{fps} FPS ({ms} ms)` 표시. `Time.unscaledDeltaTime`을 EMA로 평활.
- **폴리시**: `GUIStyle`을 **필드에 캐싱**(매 프레임 `new` 제거 = GC 0) + `normal.textColor = Color.white`. → 성능 계기판이 스스로 GC 안 만들게.

## Step 7. 검증

- `uloop`로 **focus → Play → screenshot → Stop**.
- 결과: **128 FPS (7.8 ms)**, 몬스터 100마리 지면 분포, 흰 오버레이 또렷 (위 스크린샷).

---

## 이번 주 배운 것 (지금까지)

- **베이킹으로 엔티티 프리팹 만들기** — `GetEntity(prefab, TransformUsageFlags.Dynamic)`이 프리팹을 복제 가능한 엔티티 프리팹으로 변환. `TransformUsageFlags`가 움직임/렌더 여부를 결정.
- **`ISystem` 패턴** — `partial struct` + `RequireForUpdate` + `SystemAPI.GetSingleton` + `EntityManager.Instantiate(prefab, count, alloc)` 배치 스폰 + `state.Enabled=false` 로 1회 실행.
- **컴파일 초록불 ≠ 검증** (연속) — 빈 Baker, Spawner 미배치, RequireForUpdate 누락, Y 무작위 전부 컴파일은 통과. 실행/쿼리/스크린샷으로 확인해야 함.
- **에디터 함정 2종**:
  - `SubScene MissingReferenceException` (play 전환 시) = **Unity Entities 에디터 버그**. 무해, `Error Pause` off로 무시.
  - **비포커스 시 Play 스로틀** — CLI로 조종하면 창이 뒤로 가 게임이 거의 안 돎(frameCount 정지). `uloop focus-window`로 앞에 세워야 실측됨.

## 다음 (남은 Week 1)

- [ ] **카운트 슬라이더** — SpawnSystem을 "1회 스폰" → "**목표 수에 맞춰 스폰/제거**" 로 변경 (적 식별용 `Enemy` 태그 부착 + GO 슬라이더 → ECS 전달)
- [ ] **1만 마리 스트레스 테스트** — Count를 올려 프레임타임 관찰 (벤치마크 ⑤단계 예고편)
- [ ] 오버레이에 엔티티 수 함께 표시(선택)
