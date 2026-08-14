using Unity.Entities;

namespace Simulation.Components
{
    // 스폰 목표 수/반경. 무엇을 스폰할지는 SpawnPrefab 버퍼(가중 풀)가 결정.
    // 스폰은 플레이어 중심 [InnerRadius, Radius] 링(도넛) — 안쪽 안전반경으로 즉시공격 방지.
    public struct SpawnConfig : IComponentData
    {
        public int Count;
        public float Radius;        // 바깥 반경
        public float InnerRadius;   // 안쪽 안전 반경(플레이어 주변 비움)
    }
}
