using Simulation.Data;
using Unity.Entities;

namespace Simulation.Components
{
    /// <summary>
    /// 이 적의 티어(Normal/Elite/Boss). 사망 시 히트스톱 강도·리더 판정에 사용.
    /// 모든 Enemy 엔티티에 부착 — 병사=Normal, 리더=Elite/Boss.
    /// </summary>
    public struct TierTag : IComponentData
    {
        public MonsterTier Value;
    }
}
