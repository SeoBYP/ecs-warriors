using Simulation.Data;
using Unity.Entities;
using UnityEngine;

namespace Simulation.Components
{
    public class LeaderAuthoring : MonoBehaviour
    {
        [Tooltip("리더(엘리트/보스) 데이터 — 스탯·티어를 여기서 굽는다.")]
        public MonsterDefinition Definition;
    }

    class LeaderBaker : Baker<LeaderAuthoring>
    {
        public override void Bake(LeaderAuthoring authoring)
        {
            var def = authoring.Definition;
            DependsOn(def);
            if (def == null) return;   // 리더는 Definition 필수
            
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<Enemy>(entity);
            AddComponent(entity, new MoveStats { Speed = def.speed, StopDistance = def.stopDistance });
            AddBuffer<DamageEvent>(entity);
            AddComponent(entity, new Knockback());

            AddComponent(entity, new Health { Value = def.hp });
            AddComponent<DeadTag>(entity);
            SetComponentEnabled<DeadTag>(entity, false);
            AddComponent(entity, new DeathTimer { Remaining = -1f });

            AddComponent(entity, new EnemyAttack
            {
                Range = def.attackRange,
                Cooldown = def.attackCooldown,
                Timer = def.attackCooldown,
                Damage = def.attackDamage
            });

            AddComponent(entity, new Stun { Remaining = 0 });
            AddComponent(entity, new HitTracker { LastSwingId = -1 });

            AddComponent(entity, new TierTag { Value = def.tier });
            AddComponent<LeaderTag>(entity);
        }
    }
}