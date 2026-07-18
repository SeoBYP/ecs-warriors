using Unity.Entities;

namespace Simulation.Components
{
    /// <summary>
    /// 이 적이 마지막으로 맞은 스윙 번호.
    /// AttackResolveSystem이 "이번 SwingId에 이미 맞았으면 스킵" 판정에 쓴다.
    /// → 한 번의 스윙(칼이 여러 프레임 스쳐도)에 적당 1히트.
    /// </summary>
    public struct HitTracker : IComponentData
    {
        public int LastSwingId;
    }
}
