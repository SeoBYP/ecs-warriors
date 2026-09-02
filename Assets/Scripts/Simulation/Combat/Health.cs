using Unity.Entities;

namespace Simulation.Components
{
    public struct Health : IComponentData
    {
        public int Value;
        public int Max;      // 체력바 비율용 — 베이크 시점 최대치
    }
}