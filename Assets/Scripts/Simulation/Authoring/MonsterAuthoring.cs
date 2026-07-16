using Unity.Entities;
using UnityEngine;

namespace Simulation.Components
{
    public class MonsterAuthoring : MonoBehaviour
    {
        
    }
    
    class MonsterBaker : Baker<MonsterAuthoring>
    {
        public override void Bake(MonsterAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<Enemy>(entity);
        }
    }
}