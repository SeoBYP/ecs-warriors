using Unity.Entities;
using Unity.Mathematics;

namespace Simulation.Components
{
    public struct Knockback : IComponentData
    {
        public float3 Velocity;    // 방향 × 속도(m/s)
        public float  Remaining;   // 남은 시간(0 = 넉백 아님)
    }
}