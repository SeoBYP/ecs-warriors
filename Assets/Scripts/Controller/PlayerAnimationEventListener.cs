using UnityEngine;
using UnityEngine.InputSystem;

namespace Controller
{
    /// <summary>
    /// 캐릭터(Animator와 같은 GameObject)에 붙어 Animation Event를 받는다.
    /// ★ Animation Event는 Animator가 있는 GO의 컴포넌트로만 전달되므로 여기(자식)에 둔다.
    ///
    /// 좌클릭 → Attack 트리거 → ComboB1 재생
    ///   → [Event] OnAttackStart : 스윙 판정 윈도우 시작 (SwingId++)
    ///   → (윈도우 동안 매 프레임) 칼 위치로 AttackRequest 발사  ← 다음 단계
    ///   → [Event] OnAttackEnd   : 윈도우 종료
    ///
    /// 지금은 플러밍 검증: 이벤트가 실제로 불리는지 로그로 확인.
    /// </summary>
    public class PlayerAnimationEventListener : MonoBehaviour
    {
        private Animator _animator;

        // 스윙 판정 윈도우 (다음 단계에서 AttackRequest 발사에 사용)
        public bool IsSwinging { get; private set; }
        public int SwingId { get; private set; }

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                _animator.SetTrigger("Attack");
            }
        }

        // ── Animation Event 콜백 (ComboB1 클립의 스윙 시작/끝 프레임에 심는다) ──

        public void OnAttackStart()
        {
            IsSwinging = true;
            SwingId++;
            Debug.Log($"[Attack] OnAttackStart  SwingId={SwingId}  t={Time.time:F2}");
        }

        public void OnAttackEnd()
        {
            IsSwinging = false;
            Debug.Log($"[Attack] OnAttackEnd    SwingId={SwingId}  t={Time.time:F2}");
        }
    }
}
