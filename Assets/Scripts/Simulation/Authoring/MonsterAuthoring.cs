using Unity.Entities;
using UnityEngine;

namespace Simulation.Components
{
    public class MonsterAuthoring : MonoBehaviour
    {
        public float Speed = 3.0f;
        public float StopDistance = 1.5f;
        public int Hp = 100;
        
        public int AttackDamage = 10;
        public float AttackCooldown = 1.0f;
        public float AttackRange = 2.0f;
        
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
            
            AddComponent(entity, new EnemyAttack
            {
                Range = authoring.AttackRange,
                Cooldown = authoring.AttackCooldown,
                Timer = authoring.AttackCooldown,
                Damage = authoring.AttackDamage
            });
            
            AddComponent(entity, new Stun { Remaining = 0 });
            AddComponent(entity, new HitTracker { LastSwingId = -1 });   // 아직 어떤 스윙에도 안 맞음
        }
    }
}