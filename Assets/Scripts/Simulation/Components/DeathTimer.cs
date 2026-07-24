using Unity.Entities;

namespace Simulation.Components
{
    public struct DeathTimer : IComponentData
    {
        // -1 = 아직 시작 안 함
        public float Remaining;
    } 
}