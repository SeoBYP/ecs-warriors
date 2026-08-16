using Unity.Entities;

namespace Simulation.Components
{
    /// <summary>
    /// 넉백을 얼마나 받는지의 계수. 1 = 정상, 0 = 면역(밀려나지 않음).
    /// 거대 리더(보스·엘리트)는 0 — 밀리지 않아야 무게감이 살고,
    /// 넉백이 끝날 때까지 사망 타이머가 미뤄지지 않아 **히트스톱이 벤 즉시** 걸린다.
    /// </summary>
    public struct KnockbackFactor : IComponentData
    {
        public float Value;
    }
}
