using Simulation.Components;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Simulation.Systems
{
    [BurstCompile]
    [UpdateBefore(typeof(MovementSystem))]  
    public partial struct SpatialHashSystem : ISystem
    {
        NativeParallelMultiHashMap<int, float3> _map; 

        public void OnCreate(ref SystemState state)
        {
            _map = new NativeParallelMultiHashMap<int, float3>(10000, Allocator.Persistent);
            state.RequireForUpdate<Enemy>(); // 적이 없으면 놀기
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var query = SystemAPI.QueryBuilder().WithAll<Enemy, LocalTransform>().Build();
            int count = query.CalculateEntityCount();
            
            _map.Clear();                                // 적이 움직이니 매 프레임 재구축
            if (_map.Capacity < count) _map.Capacity = count;
            
            new BuildHashJob {
                Writer = _map.AsParallelWriter(),        // ★ 병렬 등록
            }.ScheduleParallel();
            
            new SeparationJob
            {
                Map = _map,
                Radius = SpatialHash.CellSize,
                Strength = 5,
                DeltaTime = SystemAPI.Time.DeltaTime,
            }.ScheduleParallel();
        }

        public void OnDestroy(ref SystemState state)    // ★ 반드시!
        {
            if (_map.IsCreated) _map.Dispose();          // Persistent라 안 하면 릭
        }
    }
    
    [BurstCompile]
    partial struct BuildHashJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int, float3>.ParallelWriter Writer;
        
        void Execute(in LocalTransform transform, in Enemy _)   // in = 읽기 전용
        {
            Writer.Add(SpatialHash.Key(transform.Position), transform.Position);
        }
    }
    
    [BurstCompile]
    partial struct SeparationJob : IJobEntity
    {
        [ReadOnly] public NativeParallelMultiHashMap<int, float3> Map;
        public float Radius;      // 1.0 (= CellSize)
        public float Strength;    // 밀어내는 세기 (예: 5)
        public float DeltaTime;

        void Execute(ref LocalTransform transform)
        {
            float3 me = transform.Position;
            int2 myCell = SpatialHash.ToCell(me);
            
            float3 push = float3.zero;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    int key = SpatialHash.Key(myCell + new int2(dx, dz));   // ★ 빌드와 같은 해시!
                    if (Map.TryGetFirstValue(key, out float3 other, out var it))
                    {
                        do {
                            float3 diff = me - other;
                            float d = math.length(diff);
                            if (d > 0.0001f && d < Radius)          // 자기 자신(d≈0) 제외 + 반경 내만
                                push += diff / d * (1f - d / Radius);  // 가까울수록 세게
                        } while (Map.TryGetNextValue(out other, ref it));
                    }
                }
            }
            transform.Position += push * Strength * DeltaTime;
        }
    }
}