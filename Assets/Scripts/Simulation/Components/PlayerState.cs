using Unity.Entities;
using Unity.Mathematics;

namespace Simulation.Components
{
    public struct PlayerState : IComponentData
    {
        public float3 Position;
    }
}