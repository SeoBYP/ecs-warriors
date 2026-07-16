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
            
            var entities = state.EntityManager
                .Instantiate(config.Prefab, config.Count, Allocator.Temp);
            
            var random = Random.CreateFromIndex(1);
            for (var i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                var x = random.NextFloat(-config.Radius, config.Radius);
                var z = random.NextFloat(-config.Radius, config.Radius);
                var position = new float3(x, 0 , z);
                state.EntityManager.SetComponentData(entity, LocalTransform.FromPosition(position));
            }
            
            state.Enabled = false;
        }

        public void OnDestroy(ref SystemState state)
        {
        }
    }
}