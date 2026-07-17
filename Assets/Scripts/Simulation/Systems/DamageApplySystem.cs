using Simulation.Components;
using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

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
        void Execute(
            ref LocalTransform localTransform, 
            ref Health health, 
            ref Stun stun,
            ref DynamicBuffer<DamageEvent> damages, 
            EnabledRefRW<DeadTag> dead)
        {
            int total = 0;
            int maxAmount = 0;
            float3 maxSource = float3.zero;
            float maxScale = 0;
            float maxStun = 0;
            for (int i = 0; i < damages.Length; i++)
            {
                total += damages[i].Amount;
                if (damages[i].Amount > maxAmount)
                {
                    maxAmount = damages[i].Amount;
                    maxSource = damages[i].SourcePos;
                    maxScale = damages[i].KnockbackScale;
                    maxStun = damages[i].StunDuration;
                }
            }

            if (total > 0)
            {
                health.Value -= total;
                stun.Remaining = maxStun;
            }
            if(health.Value <= 0)
            {
                dead.ValueRW = true;  
            }

            if (health.Value > 0 && total > 0)
            {
                var diff = localTransform.Position - maxSource;
                if (math.lengthsq(diff) > 0.01f)
                {
                    var direction = math.normalize(diff);
                    localTransform.Position += direction * maxScale;
                };
            }
            
            damages.Clear();                          // ★ 반드시!
        }
    }
}