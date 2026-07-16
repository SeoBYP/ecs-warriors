using Unity.Entities;

namespace Simulation.Components
{
    public struct SpawnConfig : IComponentData
    {
        public Entity Prefab;
        public int Count;
        public float Radius;
    }
}