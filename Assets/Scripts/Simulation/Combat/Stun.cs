using Unity.Entities;

namespace Simulation.Components
{
    public struct Stun : IComponentData
    {
        public float Remaining;
    }
}