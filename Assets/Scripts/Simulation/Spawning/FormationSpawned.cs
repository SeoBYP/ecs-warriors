using Unity.Entities;

namespace Simulation.Components
{
    // SpawnConfig 엔티티에 1회 스폰 후 부착 — 편성은 한 번만 스폰(전멸해도 재스폰 안 함).
    public struct FormationSpawned : IComponentData { }
}
