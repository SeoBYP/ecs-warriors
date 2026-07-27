using Unity.Entities;

namespace Simulation.Components
{
    // 스폰 목표 수/반경. 무엇을 스폰할지는 SpawnPrefab 버퍼(가중 풀)가 결정.
    public struct SpawnConfig : IComponentData
    {
        public int Count;
        public float Radius;
    }
}
