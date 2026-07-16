using Unity.Entities;
using UnityEngine;

namespace Simulation.Components
{
    public class MonsterAuthoring : MonoBehaviour
    {
        public float Speed = 3.0f;
        public float StopDistance = 1.5f;
    }
    
    class MonsterBaker : Baker<MonsterAuthoring>
    {
        public override void Bake(MonsterAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<Enemy>(entity);
            AddComponent<MoveStats>(entity, new MoveStats
            {
                Speed = authoring.Speed,
                StopDistance = authoring.StopDistance
            });
        }
    }
}