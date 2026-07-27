using UnityEngine;

namespace Simulation.Data
{
    /// <summary>
    /// 편성(Squad) = 리더 1 + 병사 N. 무쌍식 "부장/장수 + 호위 병사".
    /// - leader  : Elite/Boss (GameObject 티어)
    /// - soldier : Normal (ECS/VAT 티어)
    /// 엘리트 편성 = 병사 ~100, 보스 편성 = 병사 ~300.
    /// </summary>
    [CreateAssetMenu(menuName = "ECSWarriors/Squad Definition", fileName = "Squad_")]
    public class SquadDefinition : ScriptableObject
    {
        public MonsterDefinition leader;    // Elite/Boss
        public MonsterDefinition soldier;   // Normal(병사)
        public int   soldierCount = 100;    // 호위 병사 수 (엘리트 100 / 보스 300)
        public float spawnRadius  = 8f;     // 리더 중심 병사 스폰 반경
    }
}
