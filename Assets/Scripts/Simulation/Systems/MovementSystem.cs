using Simulation.Components;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Simulation.Systems
{
    [BurstCompile]
    public partial struct MoveJob : IJobEntity
    {
        public float DeltaTime;     // 잡에 넘길 데이터는 "필드"로
        public float3 Target;
        
        void Execute(ref LocalTransform transform, in MoveStats stats)
        {
            if (math.distance(transform.Position, Target) < stats.StopDistance)
            {
                return;
            }
            var direction = (Target - transform.Position);
            direction = math.normalize(direction);
            transform.Position += direction * stats.Speed * DeltaTime;
        }
    }
    
    [BurstCompile]
    public partial struct MovementSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new MoveJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                Target = float3.zero 
            }.ScheduleParallel();
        }
    }
}