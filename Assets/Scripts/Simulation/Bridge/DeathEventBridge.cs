using TMPro;
using Unity.Entities;
using UnityEngine;

namespace Simulation.Components
{
    public class DeathEventBridge : MonoBehaviour
    {
        const string k_Format = "Kill: {0}";

        [SerializeField] private TextMeshProUGUI _killCountText;

        EntityManager _em; 
        EntityQuery _q;  
        bool _ready;

        private int _killCount;

        /// <summary>
        /// 누적 킬 수. 무쌍난무 게이지 등 다른 소비자는 **큐를 직접 읽지 말고** 이 값의 증가분을 본다.
        /// (NativeQueue는 소비하면 사라져서, 소비자가 둘이면 서로 이벤트를 뺏어 둘 다 틀린다.)
        /// </summary>
        public int KillCount => _killCount;
        
        void Start() {
            var world = World.DefaultGameObjectInjectionWorld;   // ✅ Start에 존재 보장 (BeforeSceneLoad)
            if (world == null) return;
            _em = world.EntityManager;
            _q  = _em.CreateEntityQuery(typeof(DeathEventQueue));    // 쿼리 캐싱
            _ready = true;

            _killCountText.SetText(k_Format, _killCount);
        }

        private void LateUpdate()
        {
            if(!_ready) return;
            if (_q.IsEmpty) return;

            var queue = _q.GetSingleton<DeathEventQueue>();
            var killed = 0;
            while (queue.Value.TryDequeue(out var ev))
            {
                killed++;
            }
            if (killed == 0) return;                     // 안 죽은 프레임엔 TMP 메시 재생성 회피

            _killCount += killed;
            _killCountText.SetText(k_Format, _killCount);   // {0} 오버로드 = 문자열 할당 없음
        }
    }
}