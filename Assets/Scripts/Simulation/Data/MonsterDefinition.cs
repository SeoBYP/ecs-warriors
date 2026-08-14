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
        [Tooltip("이동 속도(m/s). 몬스터마다 다르게 줘도 발 속도가 자동으로 맞춰진다.")]
        public float speed = 3f;
        [Tooltip("걷기 클립이 원래 만들어내는 지면 속도(m/s). >0이면 애니 재생 배속 = speed / 이 값 → 발 미끄러짐 제거. 리더(GO Animator)용. 0이면 보정 안 함.")]
        public float walkClipGroundSpeed = 0f;
        [Tooltip("넉백 계수. 1=정상, 0=면역(밀려나지 않음). 거대 리더는 0 권장 — 밀리지 않고, 히트스톱도 벤 즉시 걸린다.")]
        public float knockbackFactor = 1f;
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
        [Tooltip("리더 Animator에 적용할 컨트롤러 — base .controller(예: hulk_idle) 또는 AnimatorOverride 둘 다 가능.")]
        public RuntimeAnimatorController animOverride;
    }
}
