using Simulation.Components;
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

namespace Simulation.Systems
{
    /// <summary>
    /// 떠 있는 적에 중력을 먹여 포물선으로 떨어뜨리고, 바닥(y=0)에 닿으면 착지시킨다.
    /// 히트스톱 중에는 멈춘다(프리즈 중 공중의 적만 떨어지면 프리즈가 깨진다).
    /// </summary>
    [BurstCompile]
    [UpdateAfter(typeof(KnockbackSystem))]
    public partial struct AirborneSystem : ISystem
    {
        public const float Gravity = 22f;   // m/s² — 실제 중력보다 세게(공중콤보가 늘어지지 않게)

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton<HitStop>(out var hs) && hs.Remaining > 0f) return;

            new AirborneJob { Dt = SystemAPI.Time.DeltaTime }.ScheduleParallel();
        }
    }

    [BurstCompile]
    partial struct AirborneJob : IJobEntity
    {
        public float Dt;

        void Execute(ref LocalTransform tf, ref Airborne air, EnabledRefRW<Airborne> flying)
        {
            air.VelocityY -= AirborneSystem.Gravity * Dt;

            var p = tf.Position;
            p.y += air.VelocityY * Dt;

            if (p.y <= 0f)          // 착지
            {
                p.y = 0f;
                air.VelocityY = 0f;
                flying.ValueRW = false;
            }
            tf.Position = p;
        }
    }
}
