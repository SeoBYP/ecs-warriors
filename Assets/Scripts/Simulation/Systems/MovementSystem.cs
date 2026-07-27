using Simulation.Components;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Simulation.Systems
{
    [BurstCompile]
    [WithDisabled(typeof(DeadTag))] 
    public partial struct MoveJob : IJobEntity
    {
        public float DeltaTime;     // 잡에 넘길 데이터는 "필드"로
        public float3 Target;
        
        void Execute(ref LocalTransform transform, ref Stun stun, in MoveStats stats, in Knockback kb)
        {
            if (kb.Remaining > 0f)
            {
                return; 
            }
            if (stun.Remaining > 0)
            {
                stun.Remaining -= DeltaTime;
                return;
            }
            
            if (math.distance(transform.Position, Target) < stats.StopDistance)
            {
                return;
            }
            
            var direction = (Target - transform.Position);
            direction = math.normalize(direction);
            transform.Position += direction * stats.Speed * DeltaTime;
            
            var flat = new float3(direction.x, 0, direction.z);
            if (math.lengthsq(flat) > 1e-6f)
            {
                transform.Rotation = quaternion.LookRotation(flat, math.up());
            }
        }
    }
    
    [BurstCompile]
    public partial struct MovementSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerState>();
        }
        
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var player = SystemAPI.GetSingleton<PlayerState>();

            if (SystemAPI.TryGetSingleton<HitStop>(out var hs))
            {
                if (hs.Remaining > 0)
                {
                    return;
                }
            }
            
            new MoveJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                Target = player.Position, 
            }.ScheduleParallel();
        }
    }
}