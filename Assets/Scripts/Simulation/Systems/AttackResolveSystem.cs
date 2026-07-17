using Simulation.Components;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Simulation.Systems
{
    [UpdateAfter(typeof(SpatialHashSystem))]
    public partial struct AttackResolveSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SpatialHashMap>();
            state.RequireForUpdate<AttackRequest>();
        }
        
        public void OnUpdate(ref SystemState state)
        {
            var hash = SystemAPI.GetSingleton<SpatialHashMap>();
            hash.BuildHandle.Complete();
            var map = hash.Value;
            var damage = SystemAPI.GetBufferLookup<DamageEvent>();   // ★ 엔티티로 버퍼 접근
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (req, reqEntity) in 
                     SystemAPI.Query<RefRO<AttackRequest>>().WithEntityAccess())
            {
                var r = req.ValueRO;
                
                // 반경 R을 덮는 셀 범위 (회피는 3x3 고정이었지만 공격 반경은 크다)
                int range = (int)math.ceil(r.Radius / SpatialHash.CellSize);
                int2 center = SpatialHash.ToCell(r.Center);

                for (int dx = -range; dx <= range; dx++)
                {
                    for (int dz = -range; dz <= range; dz++)
                    {
                        int key = SpatialHash.Key(center + new int2(dx, dz));
                        if (map.TryGetFirstValue(key, out var hit, out var it))
                        {
                            do
                            {
                                // 셀은 사각, 반경은 원 → 실제 거리로 한 번 더 걸러야 함
                                if (math.distance(hit.Position, r.Center) <= r.Radius
                                    && damage.HasBuffer(hit.Entity))
                                {
                                    damage[hit.Entity].Add(new DamageEvent
                                    {
                                        Amount = r.Damage,
                                        SourcePos = r.Center,
                                        KnockbackScale = r.KnockbackScale,
                                        StunDuration = r.StunDuration,
                                    });
                                }
                            } while (map.TryGetNextValue(out hit, ref it));
                        }
                    }
                }
                ecb.DestroyEntity(reqEntity); // 1프레임 수명

            }
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}