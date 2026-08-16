using Unity.Collections;
using Unity.Entities;

namespace Simulation.Components
{
    public struct DeathEventQueue : IComponentData
    {
        public NativeQueue<DeathEvent> Value;
    }
}