using Simulation.Components;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Simulation.Systems
{
    [BurstCompile]
    [UpdateAfter(typeof(DamageApplySystem))]
    public partial struct DeathSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DeadTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (_, e) in SystemAPI.Query<RefRO<Health>>().WithAll<DeadTag>().WithEntityAccess())
                ecb.DestroyEntity(e);
            
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}