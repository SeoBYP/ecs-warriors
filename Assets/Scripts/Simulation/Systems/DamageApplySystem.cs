using Simulation.Components;
using Unity.Burst;
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
            new DamageApplyJob().ScheduleParallel();
        }
    }
    
    [BurstCompile]
    [WithPresent(typeof(DeadTag))]
    partial struct  DamageApplyJob : IJobEntity
    {
        void Execute(ref Health health, ref DynamicBuffer<DamageEvent> damages, EnabledRefRW<DeadTag> dead)
        {
            int total = 0;
            for (int i = 0; i < damages.Length; i++) total += damages[i].Amount;
    
            if (total > 0) health.Value -= total;
            if(health.Value <= 0)
            {
                dead.ValueRW = true;  
            }
            damages.Clear();                          // ★ 반드시!
        }
    }
}