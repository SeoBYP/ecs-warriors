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
        const float KnockDur = 0.25f; 
        
        void Execute(
            ref LocalTransform localTransform, 
            ref Health health, 
            ref Stun stun,
            ref DynamicBuffer<DamageEvent> damages, 
            ref Knockback kb,
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

            if (total > 0 && maxScale > 0f)                       // ★ 죽어도 넉백
            {
                var diff = localTransform.Position - maxSource;
                if (math.lengthsq(diff) > 0.01f)
                {
                    var dir = math.normalize(diff);
                    kb.Velocity  = dir * (maxScale / KnockDur);   // KnockDur 동안 maxScale 만큼 이동
                    kb.Remaining = KnockDur;
                }
            }
            
            damages.Clear();                          // ★ 반드시!
        }
    }
}