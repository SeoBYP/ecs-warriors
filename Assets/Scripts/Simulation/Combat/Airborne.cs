using Unity.Entities;

namespace Simulation.Components
{
    /// <summary>
    /// 공중에 떠 있는 상태. 활성 = 체공 중.
    /// 지금까지 모든 시스템이 "적은 바닥(y=0)"을 전제했기 때문에, 이 컴포넌트가 켜진 동안에는
    /// 지상 행동(추적 이동·공격)을 하지 않도록 각 시스템이 존중해야 한다.
    ///
    /// Enableable로 둔 이유: 1만 마리 중 소수만 떠 있으므로, 컴포넌트를 붙였다 뗐다 하는
    /// 구조 변경(청크 이동) 없이 비트만 켜고 끈다 — Week4에서 DeadTag로 검증한 그 패턴.
    /// </summary>
    public struct Airborne : IComponentData, IEnableableComponent
    {
        public float VelocityY;   // 현재 수직 속도(m/s). 중력으로 매 프레임 감소.
    }
}
