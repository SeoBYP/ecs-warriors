using Unity.Entities;

namespace Simulation.Components
{
    public struct HitStop : IComponentData
    {
        public float Remaining; 
    }
}