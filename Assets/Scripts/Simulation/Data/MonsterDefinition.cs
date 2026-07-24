using UnityEngine;

namespace Simulation.Data
{
    /// <summary>
    /// 몬스터 1종의 데이터. ECS(일반)와 GameObject(엘리트/보스)가 공유하는 authoring 소스.
    /// - 일반(Normal) : mesh/material/scale로 ECS 인스턴싱 렌더. Baker가 이 필드들을 구움.
    /// - 엘리트/보스   : prefab + animOverride로 GameObject 스폰(Animator Override).
    /// 지금 MonsterAuthoring에 흩어진 스탯 필드들이 여기로 이사한다(다음 단계 배선).
    /// </summary>
    [CreateAssetMenu(menuName = "ECSWarriors/Monster Definition", fileName = "Monster_")]
    public class MonsterDefinition : ScriptableObject
    {
        [Header("식별")]
        public string id;
        public string displayName;
        public MonsterTier tier = MonsterTier.Normal;

        [Header("스탯")]
        public int hp = 100;
        public float speed = 3f;
        public float stopDistance = 1.5f;
        public int attackDamage = 10;
        public float attackCooldown = 1f;
        public float attackRange = 2f;

        [Header("비주얼 — 일반(ECS 인스턴싱)")]
        [Tooltip("single_mesh 좀비 메시. 애니는 후속 VAT.")]
        public Mesh mesh;
        [Tooltip("URP + DOTS 인스턴싱 호환 머티리얼(URP/Lit 등).")]
        public Material material;
        public float scale = 1f;

        [Header("비주얼 — 엘리트/보스(GameObject)")]
        [Tooltip("Animator 포함 프리팹. 일반 티어는 비워둔다.")]
        public GameObject prefab;
        [Tooltip("base Animator에 이 몬스터 클립을 덮는 Override Controller.")]
        public AnimatorOverrideController animOverride;
    }
}
