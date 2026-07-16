using Simulation.Components;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Simulation.Systems
{
    public partial struct SpawnSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SpawnConfig>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<SpawnConfig>();
            
            int target  = config.Count;
            var enemyQuery = SystemAPI.QueryBuilder().WithAll<Enemy>().Build();
            int current = enemyQuery.CalculateEntityCount();

            if (current < target)
            {
                var count = target - current;
                var entities = state.EntityManager
                    .Instantiate(config.Prefab, count, Allocator.Temp);

                var random = Random.CreateFromIndex(1);
                
                for (int i = 0; i < count; i++)
                {
                    var x = random.NextFloat(-config.Radius, config.Radius);
                    var z = random.NextFloat(-config.Radius, config.Radius);
                    var position = new float3(x, 0 , z);
                    state.EntityManager.SetComponentData(entities[i], LocalTransform.FromPosition(position));
                }
            }
            else if (current > target)
            {
                var count = current - target;
                var removes = enemyQuery.ToEntityArray(Allocator.Temp);
                state.EntityManager.DestroyEntity(removes.GetSubArray(0,count));
            }
            
        }

        public void OnDestroy(ref SystemState state)
        {
        }
    }
}