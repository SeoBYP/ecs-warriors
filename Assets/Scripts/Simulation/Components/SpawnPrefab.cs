using Unity.Entities;

namespace Simulation.Components
{
    /// <summary>
    /// 스폰 풀의 한 변종. SpawnConfig 엔티티에 버퍼로 붙는다.
    /// SpawnSystem이 Weight 비례로 개수를 배분해 스폰 → 군중 다양화.
    /// </summary>
    public struct SpawnPrefab : IBufferElementData
    {
        public Entity Prefab;
        public float Weight;
    }
}
