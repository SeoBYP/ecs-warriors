# Week 0 — DOTS 셋업 & 첫 엔티티 렌더

> 주차별 진행 기록. 계획은 [`작업계획.md`](작업계획.md), 작업 방식은 [`협업방식.md`](협업방식.md), 개인 개발일지는 Obsidian `개발로그/`.
> 다음 주차: `Week1-스폰.md` (예정)

**기간**: 2026-07-16
**목표**: 빈 URP 프로젝트를 DOTS 착수 가능 상태로 만들고, 엔티티 1개를 화면에 렌더.
**결과**: ✅ DoD 달성 (커밋 `fdd4c09`, `8f645a6`).

---

## Step 1. 현황 파악 & 버전 확정

- **한 일**: 레포가 **Unity 6000.5.4f1(6.5) + URP 17.5** 순수 프로젝트임을 확인. 기획서는 "Week 1 착수 예정"이었지만 실제론 **DOTS 미설치 = Week 0 이전** 상태였음.
- **핵심**: Unity 6.5부터 DOTS 패키지가 **에디터 버전과 정렬** → Entities/Entities Graphics = **6.5.x** (과거 `1.x` 표기 아님).
- **검증**: `Packages/manifest.json` · `packages-lock.json` 확인.

## Step 2. DOTS 패키지 설치

- **한 일**: `com.unity.feature.ecs` 설치.
- **해결된 버전**: Entities / Entities Graphics / Physics / Collections **6.5.0**, Burst 1.8.29, Mathematics 1.4.0.
- **검증**: `uloop compile` → 에러 0 (feature 패키지들이 6.5와 충돌 없이 임포트됨).

## Step 3. 툴체인 연동 — Unity CLI Loop (uLoopMCP)

- **한 일**: 에디터의 "MCP 연동 deprecated" 경고를 확인하고 **CLI 모드로 전환**. `npm i -g uloop-cli`(v2.2.0) + `uloop skills install --claude`.
- **핵심**: 이제 `uloop compile / get-logs / screenshot / run-tests`로 에디터를 CLI에서 검증하는 루프 확보.
- **막힌 점 / 해결**: DOTS feature 최초 임포트 직후 `compile`이 **3분 타임아웃** → 임포트+Burst AOT+소스 제너레이터로 에디터가 바빴던 것. 임포트 완료 후 정상.

## Step 4. 문서화 & 설계 보강

- **한 일**: `README.md`(포폴 골격), [`작업계획.md`](작업계획.md)(실행 계획), [`협업방식.md`](협업방식.md)(작업 합의) 작성.
- **설계 변경**: "몬스터가 플레이어를 공격"(**양방향 전투**) 반영 — ECS→플레이어 두 번째 이벤트 스트림(`PlayerDamageQueue`) + `EnemyAttack`/`AttackCooldown` 컴포넌트 + `EnemyAttackSystem`.
- **검증**: 레포 문서 + Obsidian 기획·인덱스까지 4종 동기화.

## Step 5. 어셈블리 생성 — `ECSWarriors.Simulation`

- **한 일**: `Assets/Scripts/Simulation/`에 Assembly Definition 생성.
- **참조 (딱 5개)**: `Unity.Entities`, `Unity.Entities.Graphics`, `Unity.Burst`, `Unity.Collections`, `Unity.Mathematics`.
- **막힌 점 / 해결**:
  - asmdef `name`이 `NewAssembly`로 남음 → **파일명 아닌 Inspector Name 필드**가 진짜 어셈블리 이름. Name 칸에서 바꿔 Apply.
  - 참조 목록에 `Unity.Rendering`/`Unity.Transforms`가 없음 → 이 둘은 **어셈블리가 아니라 네임스페이스**. 각각 `Unity.Entities.Graphics`/`Unity.Entities`가 제공하므로 5개면 충분. (**네임스페이스 ≠ 어셈블리**)
- **검증**: `uloop compile` ✅. 단, 이때는 코드 0개라 "빈 어셈블리 통과"일 뿐 참조는 아직 미검증.

## Step 6. 첫 unmanaged 컴포넌트 — `Enemy`, `Velocity`

- **한 일**: `Components/`에 컴포넌트 2개 작성.

```csharp
public struct Enemy : IComponentData { }              // 태그

public struct Velocity : IComponentData
{
    public float3 Value;                               // blittable, public 필드
}
```

- **막힌 점 / 해결**:
  - 처음에 `class`로 작성 → 컴파일은 되지만 **managed component**(GC·Burst 불가·캐시 미스)로 빠짐 → `struct`로 수정.
  - `Velocity.Value`를 `private`로 둠 → 시스템이 접근 불가로 무용지물 → `public`으로 수정.
  - 학습노트로 정리: [[DOTS 컴포넌트 - Managed vs Unmanaged]].
- **검증**: `uloop compile` ✅. 이제 `float3`/`IComponentData`가 컴파일됨 = **Mathematics/Entities 참조가 실제로 작동함이 증명**.

## Step 7. SubScene 베이킹 → 캡슐 엔티티 렌더 (DoD)

- **한 일**: Main 씬에 `EnemySubScene`(SubScene) 생성 → 안에 Capsule(GameObject) 배치.
- **핵심**: 베이킹이 자동으로 `LocalTransform`/`LocalToWorld`/`RenderMeshArray`/`MaterialMeshInfo` 등을 붙인 **엔티티**를 생성 → Entities Graphics가 GPU 인스턴싱으로 렌더. **커스텀 코드 0줄.**
- **검증**: Entities Hierarchy에 "Capsule (Live converted)" 확인 + `uloop screenshot`으로 Game 뷰 렌더 확인.

![Week 0 — 첫 ECS 엔티티 렌더](images/week0-first-entity-render.png)

## Step 8. 커밋 & 정리

- **한 일**: README 개발 진행 섹션 + 스크린샷, 이 진행 기록, Obsidian 개발일지 작성. `feat/week0-dots-setup` 브랜치에 커밋.
- **gitignore**: `Assets/SceneDependencyCache/`(베이킹 생성 캐시), `.idea/`. `.uloop/`·`.claude/`는 미커밋(추후 결정).

---

## 이번 주 배운 것

- **네임스페이스 ≠ 어셈블리** — `Unity.Transforms`/`Unity.Rendering`은 각각 `Unity.Entities`/`Unity.Entities.Graphics`가 제공.
- **Managed(class) vs Unmanaged(struct)** — 항상 struct. class는 GC·Burst 불가·캐시 미스에 6.5 deprecated. → [[DOTS 컴포넌트 - Managed vs Unmanaged]]
- **베이킹/SubScene** — GameObject authoring → 엔티티 변환. 렌더 컴포넌트는 베이킹이 자동 부여.
- **컴파일 초록불 ≠ 검증** — 빈 어셈블리·private 필드·class 컴포넌트가 전부 통과했다. 설계는 실행/구조로 확인해야 함.

## 다음 (Week 1)

- [ ] 캡슐을 엔티티 **프리팹**으로 등록 → `SpawnSystem`(ISystem) + `EntityCommandBuffer`로 N개 스폰
- [ ] 적 프리팹에서 **Capsule Collider 제거** (자작 Spatial Hash 설계라 Physics 컴포넌트 불필요)
- [ ] 카운트 슬라이더 + FPS/ms 오버레이 → "1만 마리 정적 렌더" DoD
