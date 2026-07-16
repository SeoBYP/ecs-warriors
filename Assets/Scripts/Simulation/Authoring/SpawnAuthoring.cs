using UnityEngine;
using Unity.Entities;

namespace Simulation.Components
{
    public class SpawnAuthoring : MonoBehaviour
    {
        public GameObject Prefab;
        public int Count;
        public float Radius;
        
    }
    
    class SpawnBaker : Baker<SpawnAuthoring>
    {
        public override void Bake(SpawnAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            
            var prefabEntity = GetEntity(authoring.Prefab, TransformUsageFlags.Dynamic);
            
            AddComponent(entity, new SpawnConfig {
                Prefab = prefabEntity,
                Count  = authoring.Count,
                Radius = authoring.Radius,
            });
        }
    }
}