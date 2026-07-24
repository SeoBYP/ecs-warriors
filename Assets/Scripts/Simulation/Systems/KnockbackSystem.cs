using Simulation.Components;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Simulation.Systems
{
    [BurstCompile]
    [UpdateAfter(typeof(DamageApplySystem))]
    public partial struct KnockbackSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new KnockbackJob { Dt = SystemAPI.Time.DeltaTime }.ScheduleParallel();
        }
    }
    
    [BurstCompile]
    partial struct KnockbackJob : IJobEntity
    {
        public float Dt;
        void Execute(ref LocalTransform tf, ref Knockback kb)
        {
            if (kb.Remaining <= 0f) return;
            tf.Position  += kb.Velocity * Dt;
            kb.Remaining -= Dt;
            if (kb.Remaining <= 0f) { kb.Remaining = 0f; kb.Velocity = float3.zero; }
        }
    }
}