using Unity.Entities;

namespace Simulation.Components
{
    public struct DamageEvent : IBufferElementData
    {
        public int Amount;
    }
}