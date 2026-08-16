using Unity.Entities;
using Unity.Mathematics;

namespace Simulation.Components
{
    public struct DamageEvent : IBufferElementData
    {
        public int Amount;
        public float3 SourcePos;
        public float KnockbackScale;
        public float StunDuration;
        public float LaunchY;        // >0이면 이 초속으로 띄운다(공중 콤보)
    }
}