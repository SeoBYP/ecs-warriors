using UnityEngine;

namespace Controller
{
    /// <summary>
    /// 이 상태에 진입할 때 지정한 트리거를 리셋한다.
    /// Locomotion에 붙이면 콤보가 끝나 복귀할 때 남아있는 Attack 버퍼를 청소한다.
    ///
    /// 왜 필요한가: Attack은 소비될 때까지 유지되는 트리거라, Combo4(마무리, 체인 없음)
    /// 도중에 한 번 더 클릭하면 그 트리거가 소비되지 않고 남는다. 그러면 Combo4→Locomotion
    /// 복귀 순간 Locomotion→Combo1이 다시 켜져 "혼자 1타 더" 나간다. 진입 시 청소로 방지.
    /// </summary>
    public class ResetTriggerOnEnter : StateMachineBehaviour
    {
        [SerializeField] private string _trigger = "Attack";

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            animator.ResetTrigger(_trigger);
        }
    }
}
