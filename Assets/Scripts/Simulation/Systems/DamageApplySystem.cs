using Simulation.Components;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Simulation.Systems
{
    [BurstCompile]
    [UpdateAfter(typeof(AttackResolveSystem))]
    public partial struct DamageApplySystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var handle = new DamageApplyJob { Ecb = ecb.AsParallelWriter() }
                .ScheduleParallel(state.Dependency);

            state.Dependency = handle;
            
            handle.Complete();                    // ★ 잡 끝날 때까지 메인 스레드가 기다림
            ecb.Playback(state.EntityManager);    // ★ 1만 건을 메인 스레드가 하나씩 실행
            ecb.Dispose();
        }
    }
    
    [BurstCompile]
    partial struct  DamageApplyJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter Ecb;
        
        void Execute([ChunkIndexInQuery] int sortKey,       // ← 추가
            Entity entity,                          // ← 추가
            ref Health health,
            ref DynamicBuffer<DamageEvent> damages)
        {
            int total = 0;
            for (int i = 0; i < damages.Length; i++) total += damages[i].Amount;
    
            if (total > 0) health.Value -= total;
            if (health.Value <= 0)
                Ecb.AddComponent<DeadTag>(sortKey, entity);
            damages.Clear();                          // ★ 반드시!
        }
    }
}