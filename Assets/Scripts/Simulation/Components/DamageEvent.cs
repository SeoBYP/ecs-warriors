using Unity.Entities;
using Unity.Mathematics;

namespace Simulation.Components
{
    public struct DamageEvent : IBufferElementData
    {
        public int Amount;
        public float3 SourcePos;
        public float KnockbackScale;
    }
}