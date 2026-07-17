using Unity.Entities;
using UnityEngine;

namespace Simulation.Components
{
    public class MonsterAuthoring : MonoBehaviour
    {
        public float Speed = 3.0f;
        public float StopDistance = 1.5f;
        public int Hp = 100;
    }
    
    class MonsterBaker : Baker<MonsterAuthoring>
    {
        public override void Bake(MonsterAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<Enemy>(entity);
            AddComponent(entity, new MoveStats
            {
                Speed = authoring.Speed,
                StopDistance = authoring.StopDistance
            });
            AddBuffer<DamageEvent>(entity);
            AddComponent(entity, new Health { Value = authoring.Hp });
            AddComponent<DeadTag>(entity);
            SetComponentEnabled<DeadTag>(entity, false);   // ★ 붙이되 꺼둔 채 시작
        }
    }
}