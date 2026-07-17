using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Simulation.Components
{
    public struct SpatialHashMap : IComponentData
    {
        public NativeParallelMultiHashMap<int, HashedEnemy> Value;
        public JobHandle BuildHandle;
    }
}