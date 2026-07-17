using System.Collections.Generic;
using System.Text;
using Simulation.Components;
using Simulation.Systems;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Benchmark
{
    /// <summary>
    /// 광역기 한 방으로 "대량 동시 사망"을 만들고 그 프레임의 스파이크를 측정하는 하니스.
    ///
    /// 왜 BenchmarkHarness와 따로인가:
    ///   BenchmarkHarness는 "정상 상태 120프레임 평균"을 낸다. 동시 사망은 1회성 폭발이라
    ///   평균에 녹으면 안 보인다. 여기선 발사 직전 기준선과 발사 직후 프레임들을 개별로 기록한다.
    ///
    /// 무엇을 A/B 하나:
    ///   DeadTag = IEnableableComponent (main)  vs  DeadTag = 일반 IComponentData (bench 브랜치).
    ///   전자는 워커 스레드에서 비트만 토글, 후자는 ECB에 1만 건 예약 → 메인 스레드 직렬 재생.
    ///   → 공식 문서: "structural changes ... only on the main thread; not from jobs"
    ///
    /// 라운드 절차:
    ///   스폰 → 수렴 대기 → 안정화(기준선 수집) → SpawnSystem 정지 → 발사 → N프레임 기록 → 리포트
    ///
    /// 주의:
    ///   - SpawnSystem을 끄고 발사한다. 안 끄면 같은 프레임대에 1만 파괴 + 1만 생성이 겹쳐
    ///     사망 비용만 분리할 수 없다.
    ///   - 비포커스 프레임은 제외한다(BenchmarkHarness와 동일 정책). Run In Background가 켜져
    ///     있어도 Application.isFocused는 실제 포커스를 반영하므로, 기존 측정과의 비교 가능성을
    ///     위해 가드를 유지한다.
    /// </summary>
    public class AoeSpikeHarness : MonoBehaviour
    {
        [Tooltip("측정 구분 라벨 (enableable / structural)")]
        public string Label = "enableable";

        [Tooltip("측정할 적 수")]
        public int TargetEnemies = 10000;

        [Tooltip("광역기 반경 — 수렴한 군중을 덮을 만큼")]
        public float AoeRadius = 25f;

        [Tooltip("광역기 데미지 — 적 HP 이상이어야 '한 방 사망'")]
        public int AoeDamage = 100;

        [Tooltip("반복 횟수 (노이즈 축소용)")]
        public int Repeats = 5;

        [Tooltip("이 비율만큼 반경 안에 들어오면 수렴으로 간주")]
        [Range(0.5f, 1f)] public float ConvergeRatio = 0.95f;

        [Tooltip("측정 중 스폰 반경 — 작을수록 수렴이 빠르다. 수렴 밀도는 separation이 정하므로 결과에 영향 없음")]
        public float SpawnRadiusForTest = 30f;

        [Tooltip("수렴 후 안정화 + 기준선 수집 프레임")]
        public int BaselineFrames = 60;

        [Tooltip("발사 후 기록할 프레임 수")]
        public int RecordFrames = 15;

        enum Phase { Prepare, WaitSpawn, WaitConverge, Baseline, Record, Done }

        Phase _phase = Phase.Prepare;
        int _round;
        int _framesLeft;
        int _killedBefore;

        readonly List<float> _baseline = new List<float>();
        readonly List<float> _spike = new List<float>();
        readonly List<string> _csv = new List<string>();

        EntityManager _em;
        EntityQuery _enemyQuery;
        EntityQuery _cfgQuery;
        World _world;
        bool _ready;
        int _convergeCheckCooldown;

        void Start()
        {
            _world = World.DefaultGameObjectInjectionWorld;
            if (_world == null) return;
            _em = _world.EntityManager;
            _enemyQuery = _em.CreateEntityQuery(ComponentType.ReadOnly<Enemy>(),
                                               ComponentType.ReadOnly<LocalTransform>());
            _cfgQuery = _em.CreateEntityQuery(ComponentType.ReadWrite<SpawnConfig>());

            // 적 수를 흔드는 다른 하니스가 있으면 측정이 망가진다
            var sweep = FindObjectOfType<BenchmarkHarness>(true);
            if (sweep != null) sweep.enabled = false;

            _csv.Add("label,round,killed,baseline_ms,spike_max_ms,spike_frame,excess_ms");
            _ready = true;
        }

        void Update()
        {
            if (!_ready || _phase == Phase.Done) return;
            if (!Application.isFocused) return;              // 스로틀 프레임 제외
            if (_cfgQuery.IsEmpty) return;                   // SubScene 비동기 로드 대기

            switch (_phase)
            {
                case Phase.Prepare:      Prepare();      break;
                case Phase.WaitSpawn:    WaitSpawn();    break;
                case Phase.WaitConverge: WaitConverge(); break;
                case Phase.Baseline:     Baseline_();    break;
                case Phase.Record:       Record();       break;
            }
        }

        void Prepare()
        {
            SetSpawnSystemEnabled(true);

            var e = _cfgQuery.GetSingletonEntity();
            var cfg = _em.GetComponentData<SpawnConfig>(e);
            cfg.Count = TargetEnemies;
            cfg.Radius = SpawnRadiusForTest;
            _em.SetComponentData(e, cfg);

            _baseline.Clear();
            _spike.Clear();
            _convergeCheckCooldown = 0;
            _phase = Phase.WaitSpawn;
        }

        void WaitSpawn()
        {
            if (_enemyQuery.CalculateEntityCount() != TargetEnemies) return;
            _phase = Phase.WaitConverge;
        }

        void WaitConverge()
        {
            // 매 프레임 1만 개를 도는 건 그 자체가 부하 → 15프레임마다만 확인
            if (--_convergeCheckCooldown > 0) return;
            _convergeCheckCooldown = 15;

            float3 center = transform.position;
            var xforms = _enemyQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            int inRadius = 0;
            for (int i = 0; i < xforms.Length; i++)
                if (math.distance(xforms[i].Position, center) <= AoeRadius) inRadius++;
            int total = xforms.Length;
            xforms.Dispose();

            if (total == 0 || inRadius < total * ConvergeRatio) return;

            _framesLeft = BaselineFrames;
            _phase = Phase.Baseline;
        }

        void Baseline_()
        {
            _baseline.Add(Time.unscaledDeltaTime * 1000f);
            if (--_framesLeft > 0) return;

            Fire();
        }

        void Fire()
        {
            // ★ 리스폰이 사망 비용에 섞이지 않도록 스포너 정지
            SetSpawnSystemEnabled(false);

            _killedBefore = _enemyQuery.CalculateEntityCount();

            var req = _em.CreateEntity();
            _em.AddComponentData(req, new AttackRequest
            {
                Center = transform.position,
                Radius = AoeRadius,
                Damage = AoeDamage,
            });

            _framesLeft = RecordFrames;
            _phase = Phase.Record;
        }

        void Record()
        {
            _spike.Add(Time.unscaledDeltaTime * 1000f);
            if (--_framesLeft > 0) return;

            Report();

            _round++;
            _phase = _round < Repeats ? Phase.Prepare : Phase.Done;
            if (_phase == Phase.Done) { SetSpawnSystemEnabled(true); DumpCsv(); }
        }

        void Report()
        {
            int killed = _killedBefore - _enemyQuery.CalculateEntityCount();

            float baseSum = 0f;
            foreach (var s in _baseline) baseSum += s;
            float baseAvg = _baseline.Count > 0 ? baseSum / _baseline.Count : 0f;

            float max = 0f; int maxAt = -1; float excess = 0f;
            for (int i = 0; i < _spike.Count; i++)
            {
                if (_spike[i] > max) { max = _spike[i]; maxAt = i; }
                excess += _spike[i] - baseAvg;          // 기준선 대비 초과분 총합
            }

            var frames = new StringBuilder();
            for (int i = 0; i < _spike.Count; i++)
            {
                if (i > 0) frames.Append(' ');
                frames.Append(_spike[i].ToString("0.0"));
            }

            Debug.Log($"[AOE] label={Label} round={_round + 1}/{Repeats} killed={killed} " +
                      $"baseline={baseAvg:0.00}ms spikeMax={max:0.00}ms @frame+{maxAt} " +
                      $"excess={excess:0.00}ms | frames=[{frames}]");

            _csv.Add($"{Label},{_round + 1},{killed},{baseAvg:0.00},{max:0.00},{maxAt},{excess:0.00}");
        }

        void DumpCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[AOE-CSV]");
            foreach (var line in _csv) sb.AppendLine(line);
            Debug.Log(sb.ToString());
        }

        void SetSpawnSystemEnabled(bool enabled)
        {
            var h = _world.GetExistingSystem<SpawnSystem>();
            if (h != SystemHandle.Null)
                _world.Unmanaged.ResolveSystemStateRef(h).Enabled = enabled;
        }
    }
}
