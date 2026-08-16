using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Simulation.Components
{
    public struct PlayerDamageQueue : IComponentData
    {
        public NativeQueue<PlayerDamageEvent> Value;
        public JobHandle WriteHandle;
    }
}