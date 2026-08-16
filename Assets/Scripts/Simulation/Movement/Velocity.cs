using Unity.Entities;
using Unity.Mathematics;
namespace Simulation.Components
{
    public struct Velocity : IComponentData
    {
        public float3 Value;
    }
}

