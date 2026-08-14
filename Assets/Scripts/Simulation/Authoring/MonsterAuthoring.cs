using Simulation.Data;
using Unity.Entities;
using UnityEngine;

namespace Simulation.Components
{
    public class MonsterAuthoring : MonoBehaviour
    {
        [Tooltip("설정 시 스탯·티어를 이 SO에서 굽는다. 비우면 아래 폴백 값을 사용.")]
        public MonsterDefinition Definition;

        [Header("폴백 스탯 (Definition 미설정 시)")]
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
            var def = authoring.Definition;
            DependsOn(def);   // SO 변경 시 재베이크

            float speed       = def ? def.speed          : authoring.Speed;
            float stopDist    = def ? def.stopDistance   : authoring.StopDistance;
            int   hp          = def ? def.hp             : authoring.Hp;
            int   atkDamage   = def ? def.attackDamage   : authoring.AttackDamage;
            float atkCooldown = def ? def.attackCooldown : authoring.AttackCooldown;
            float atkRange    = def ? def.attackRange    : authoring.AttackRange;
            var   tier        = def ? def.tier           : MonsterTier.Normal;

            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<Enemy>(entity);
            AddComponent(entity, new MoveStats
            {
                Speed = speed,
                StopDistance = stopDist
            });
            AddBuffer<DamageEvent>(entity);
            AddComponent(entity, new Knockback());   // 기본값 0 = 넉백 아님
            AddComponent(entity, new KnockbackFactor { Value = def ? def.knockbackFactor : 1f });

            AddComponent(entity, new Health { Value = hp });
            AddComponent<DeadTag>(entity);
            SetComponentEnabled<DeadTag>(entity, false);   // ★ 붙이되 꺼둔 채 시작
            AddComponent(entity, new DeathTimer { Remaining = -1f });

            AddComponent(entity, new EnemyAttack
            {
                Range = atkRange,
                Cooldown = atkCooldown,
                Timer = atkCooldown,
                Damage = atkDamage
            });

            AddComponent(entity, new Stun { Remaining = 0 });
            AddComponent(entity, new HitTracker { LastSwingId = -1 });   // 아직 어떤 스윙에도 안 맞음

            AddComponent(entity, new VATAnimParams());
            AddComponent(entity, new VATAnimStart());
            AddComponent(entity, new ZombieAnim { Current = (byte)ZAnim.None });

            AddComponent(entity, new TierTag { Value = tier });   // ★ 티어 부착 (사망→히트스톱 훅)
        }
    }
}
