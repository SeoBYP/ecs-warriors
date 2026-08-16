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
    // ★ Airborne(enableable)을 Execute에 받으면 "꺼진" 지상 적이 쿼리에서 제외된다 →
    //   지상 적에게 데미지가 아예 안 들어간다. DeadTag와 같은 이유로 Present로 열어둬야 한다.
    [WithPresent(typeof(Airborne))]
    partial struct  DamageApplyJob : IJobEntity
    {
        const float KnockDur = 0.25f; 
        
        void Execute(
            ref LocalTransform localTransform,
            ref Health health,
            ref Stun stun,
            ref DynamicBuffer<DamageEvent> damages,
            ref Knockback kb,
            in KnockbackFactor kbFactor,
            ref Airborne air,
            EnabledRefRW<Airborne> flying,
            EnabledRefRW<DeadTag> dead)
        {
            int total = 0;
            int maxAmount = 0;
            float3 maxSource = float3.zero;
            float maxScale = 0;
            float maxStun = 0;
            float maxLaunch = 0;
            for (int i = 0; i < damages.Length; i++)
            {
                total += damages[i].Amount;
                if (damages[i].LaunchY > maxLaunch) maxLaunch = damages[i].LaunchY;   // 띄우기는 가장 센 것
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

            maxScale  *= kbFactor.Value;                         // ★ 넉백 계수(리더=0 → 면역)
            maxLaunch *= kbFactor.Value;                         // 띄우기도 같은 계수 — 거대 리더는 안 뜬다

            if (total > 0 && maxLaunch > 0f)                      // ★ 띄우기: 수직 초속 부여
            {
                air.VelocityY = maxLaunch;
                flying.ValueRW = true;
            }

            if (total > 0 && maxScale > 0f)                       // ★ 죽어도 넉백
            {
                var diff = localTransform.Position - maxSource;
                diff.y = 0f;                                     // ★ 넉백은 수평만(수직은 LaunchY 담당)
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