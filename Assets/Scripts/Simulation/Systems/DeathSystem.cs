using Simulation.Components;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace Simulation.Systems
{
    [BurstCompile]
    [UpdateAfter(typeof(DamageApplySystem))]
    public partial struct DeathSystem : ISystem
    {
        NativeQueue<DeathEvent> _queue;
        
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DeadTag>();
            _queue = new NativeQueue<DeathEvent>(Allocator.Persistent);
            state.EntityManager.CreateSingleton(new DeathEventQueue { Value = _queue });
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (_,transform,e ) in SystemAPI.Query<RefRO<Health>,RefRO<LocalTransform>>().WithAll<DeadTag>().WithEntityAccess())
            {
                ecb.DestroyEntity(e);
                _queue.Enqueue(new DeathEvent { Position = transform.ValueRO.Position });
            }
            
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_queue.IsCreated)
            {
                _queue.Dispose();
            }
        }
    }
}