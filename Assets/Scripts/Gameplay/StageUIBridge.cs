using Simulation.Components;
using Simulation.Stage;
using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Gameplay
{
    /// <summary>
    /// 스테이지 진행 UI + 리트라이. ECS가 소유한 <see cref="StageState"/>를 **읽어서 그리기만** 한다(Week9 §3.1).
    ///
    /// ⚠️ 리트라이가 단순 씬 리로드로 끝나지 않는 이유:
    ///   ECS World는 씬 로드와 무관하게 살아남는다. 그래서
    ///   ① 시스템이 만든 싱글톤(StageState·HitStop·PlayerState)은 이전 판의 값을 그대로 들고 있고,
    ///   ② SpawnSystem이 런타임에 만든 병사 엔티티도 SubScene 소속이 아니라 살아남는다.
    ///   → 리셋 없이 리로드하면 "시작하자마자 클리어" 화면이 다시 뜨고 병사가 두 배가 된다.
    ///   여기서 적 엔티티를 정리하고 싱글톤을 초기화한 뒤 씬을 다시 올린다.
    /// </summary>
    public class StageUIBridge : MonoBehaviour
    {
        [Header("HUD")]
        [SerializeField] private TextMeshProUGUI _leaderText;   // 남은 장수 N / 11
        [SerializeField] private TextMeshProUGUI _timeText;     // 경과 시간

        [Header("결과")]
        [SerializeField] private GameObject _resultPanel;
        [SerializeField] private TextMeshProUGUI _resultTitle;
        [SerializeField] private TextMeshProUGUI _resultDetail;
        [SerializeField] private Key _retryKey = Key.Enter;

        [Header("결과 중 정지시킬 것 (비우면 자동 탐색)")]
        [SerializeField] private MonoBehaviour[] _disableOnResult;

        EntityManager _em;
        EntityQuery _stageQuery;
        bool _ready;
        bool _resultShown;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            _em = world.EntityManager;
            _stageQuery = _em.CreateEntityQuery(typeof(StageState));

            if (_disableOnResult == null || _disableOnResult.Length == 0)
            {
                var pc = FindAnyObjectByType<Controller.PlayerController>();
                var musou = FindAnyObjectByType<Controller.MusouGaugeBridge>();
                _disableOnResult = new MonoBehaviour[] { pc, musou };
            }

            if (_resultPanel != null) _resultPanel.SetActive(false);
            _ready = true;
        }

        void Update()
        {
            if (!_ready || _stageQuery.IsEmpty) return;
            var s = _stageQuery.GetSingleton<StageState>();

            // ※ TMP 기본 폰트에 한글 글리프가 없어 UI 문자열은 영문으로 통일한다(기존 Kill/HP 표기와 동일).
            //   한글로 가려면 한글 TTF로 TMP Font Asset을 만들어야 하는데 폰트 라이선스가 걸린다.
            if (_leaderText != null)
                _leaderText.SetText("GENERALS  {0} / {1}", s.LeadersRemaining, s.LeadersTotal);
            if (_timeText != null)
                _timeText.SetText("{0}:{1:00}", (int)(s.Elapsed / 60f), (int)(s.Elapsed % 60f));

            if (!_resultShown && s.Phase != StagePhase.Playing) ShowResult(s);

            if (_resultShown)
            {
                var kb = Keyboard.current;
                if (kb != null && kb[_retryKey].wasPressedThisFrame) Retry();
            }
        }

        void ShowResult(StageState s)
        {
            _resultShown = true;

            foreach (var mb in _disableOnResult)
                if (mb != null) mb.enabled = false;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (_resultPanel != null) _resultPanel.SetActive(true);
            bool cleared = s.Phase == StagePhase.Cleared;

            if (_resultTitle != null)
            {
                _resultTitle.text = cleared ? "MISSION CLEAR" : "DEFEATED";
                _resultTitle.color = cleared ? new Color(1f, 0.85f, 0.25f) : new Color(0.9f, 0.25f, 0.25f);
            }
            if (_resultDetail != null)
            {
                int m = (int)(s.Elapsed / 60f), sec = (int)(s.Elapsed % 60f);
                _resultDetail.text = cleared
                    ? $"ALL {s.LeadersTotal} GENERALS SLAIN   |   TIME {m}:{sec:00}\n\nPress  ENTER  to retry"
                    : $"GENERALS LEFT  {s.LeadersRemaining} / {s.LeadersTotal}\n\nPress  ENTER  to retry";
            }
        }

        void Retry()
        {
            // ① 진행 중인 잡을 끝내고 이전 판의 적 엔티티를 모두 정리
            //    (런타임 스폰 병사는 SubScene 소속이 아니라 씬 리로드로 안 사라진다)
            //    ⚠️ DestroyEntity(EntityQuery)는 쓰면 안 된다 — 여성 좀비는 submesh가 2개라
            //       LinkedEntityGroup(자식 렌더 엔티티)을 갖는데 그 자식이 쿼리에 없어서 예외가 난다.
            //       엔티티 배열 오버로드는 링크된 자식까지 함께 파괴한다.
            _em.CompleteAllTrackedJobs();
            var enemies = _em.CreateEntityQuery(ComponentType.ReadOnly<Enemy>());
            using (var arr = enemies.ToEntityArray(Unity.Collections.Allocator.Temp))
                _em.DestroyEntity(arr);

            // ② 시스템이 들고 있는 싱글톤 초기화 — 안 하면 리로드 직후 결과 화면이 다시 뜬다
            if (!_stageQuery.IsEmpty)
                _em.SetComponentData(_stageQuery.GetSingletonEntity(), new StageState
                {
                    Phase = StagePhase.Playing,
                    LeadersRemaining = 0,
                    LeadersTotal = 0,
                    Elapsed = 0f,
                });

            var playerQ = _em.CreateEntityQuery(typeof(PlayerState));
            if (!playerQ.IsEmpty)
            {
                var e = playerQ.GetSingletonEntity();
                var ps = _em.GetComponentData<PlayerState>(e);
                ps.IsDead = false;
                _em.SetComponentData(e, ps);
            }

            var hitStopQ = _em.CreateEntityQuery(typeof(HitStop));
            if (!hitStopQ.IsEmpty)
                _em.SetComponentData(hitStopQ.GetSingletonEntity(), new HitStop { Remaining = 0f });

            // ③ 연출 상태 복구 후 씬 재적재
            Time.timeScale = 1f;                       // 보스 슬로모 중 죽었을 수 있다
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
