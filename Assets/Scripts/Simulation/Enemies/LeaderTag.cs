using Unity.Entities;

namespace Simulation.Components
{
    // 리더(엘리트/보스) 엔티티 마커. STEP D의 GO 비주얼 브릿지가 이 태그로 리더를 찾아 GO를 붙인다.
    public struct LeaderTag : IComponentData
    {
    }
}