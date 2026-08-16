using Simulation.Components;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using Unity.Transforms;

namespace Simulation.Systems
{
    /// <summary>
    /// submesh가 2개 이상인 좀비(여성 모델 전부)는 Entities Graphics가 렌더를 **자식 엔티티**로 분리한다.
    /// 그 자식들은 Baker가 루트에만 붙인 VATAnimParams/VATAnimStart를 못 받아
    /// 머티리얼 기본값으로 굳어버린다(= 애니 정지). 여기서 자식에 컴포넌트를 달고 부모 값을 복사한다.
    ///
    /// 단일 submesh(남성 모델)는 루트가 곧 렌더 엔티티라 이 시스템과 무관하다.
    /// 복사는 메인스레드에서 한다 — 대상이 수백 개뿐이고, 잡으로 돌리면 같은 컴포넌트를
    /// 부모에서 읽고 자식에 쓰는 앨리어싱이라 안전 시스템과 충돌한다.
    /// </summary>
    [UpdateAfter(typeof(ZombieAnimSystem))]
    public partial struct VATSubmeshSyncSystem : ISystem
    {
        EntityQuery _needAdd;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<VATClipTable>();
            _needAdd = SystemAPI.QueryBuilder()
                .WithAll<MaterialMeshInfo, Parent>()
                .WithNone<VATAnimParams>()
                .Build();
        }

        public void OnUpdate(ref SystemState state)
        {
            // ZombieAnimJob이 부모 값을 다 쓴 뒤에 읽는다
            state.CompleteDependency();

            var animParams = SystemAPI.GetComponentLookup<VATAnimParams>(true);
            var animStart  = SystemAPI.GetComponentLookup<VATAnimStart>(true);

            // 1) 아직 애니 컴포넌트가 없는 자식 렌더 엔티티에 부착 (초기 몇 프레임만)
            if (!_needAdd.IsEmpty)
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                foreach (var (parent, entity) in
                         SystemAPI.Query<RefRO<Parent>>()
                             .WithAll<MaterialMeshInfo>()
                             .WithNone<VATAnimParams>()
                             .WithEntityAccess())
                {
                    if (!animParams.HasComponent(parent.ValueRO.Value)) continue;   // 좀비 자식만
                    ecb.AddComponent<VATAnimParams>(entity);
                    ecb.AddComponent<VATAnimStart>(entity);
                }
                ecb.Playback(state.EntityManager);
                ecb.Dispose();

                animParams = SystemAPI.GetComponentLookup<VATAnimParams>(true);   // 구조 변경 후 갱신
                animStart  = SystemAPI.GetComponentLookup<VATAnimStart>(true);
            }

            // 2) 부모(루트)의 애니 값을 자식 렌더 엔티티로 복사
            foreach (var (parent, p, s) in
                     SystemAPI.Query<RefRO<Parent>, RefRW<VATAnimParams>, RefRW<VATAnimStart>>()
                         .WithAll<MaterialMeshInfo>())
            {
                var e = parent.ValueRO.Value;
                if (!animParams.HasComponent(e)) continue;
                p.ValueRW.Value = animParams[e].Value;
                s.ValueRW.Value = animStart[e].Value;
            }
        }
    }
}
