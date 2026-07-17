using Unity.Entities;
using Unity.Mathematics;

namespace Simulation.Components
{
    public struct AttackRequest : IComponentData
    {
        public float3 Center;   // 공격 중심 (히트박스 위치)
        public float  Radius;   // 반경
        public int  Damage;
        public float KnockbackScale;
    }
}