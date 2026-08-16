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
        public float StunDuration;
        public float LaunchY;   // >0이면 적을 이 초속으로 띄운다(공중 콤보용). 0이면 지상 유지.
        public int SwingId;     // >0이면 "스윙당 1히트" 중복제거. 0이면 단발(항상 히트).
    }
}