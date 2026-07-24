#if UNITY_EDITOR
using System.Text;
using Simulation.Components;   // VATClipSet
using UnityEditor;
using UnityEngine;

namespace Simulation.VAT
{
    /// <summary>
    /// VATBaker를 클릭으로 실행하는 에디터 창.  메뉴: Tools > VAT > VAT Baker
    ///   - 단일 클립: 모델+클립 넣고 Bake
    ///   - 멀티 클립: "Bake Aggressive Set" 버튼 → 5클립 스택 + VATClipSet 생성
    /// </summary>
    public class VATBakerWindow : EditorWindow
    {
        [SerializeField] GameObject _model;
        [SerializeField] AnimationClip _clip;
        [SerializeField] int _frames = 30;
        [SerializeField] string _outputDir = "Assets/Data/VAT";
        [SerializeField] string _outName = "Zombie_M01_BrokenWalk";
        [SerializeField] Vector3 _vertexEuler = new Vector3(-90f, 0f, 0f);

        const string DefaultModel =
            "Assets/ArtResource/Toon_Zombies_extended/models/single_mesh/male_civilians/M_Zombie_01.FBX";
        const string DefaultClip =
            "Assets/ArtResource/Toon_Zombies_extended/animation/Broken/TZ_broken_walk.FBX";
        const string AnimDir =
            "Assets/ArtResource/Toon_Zombies_extended/animation";

        // 상태머신 5클립 (순서 = ZAnim enum: Idle, Walk, Attack, Damage, Death)
        static readonly (string path, bool loop)[] AggroSet =
        {
            (AnimDir + "/Aggresive/TZ_aggresive_idle.FBX",     true),
            (AnimDir + "/Aggresive/TZ_aggresive_walk.FBX",     true),
            (AnimDir + "/Aggresive/TZ_aggresive_attack_A.FBX", true),
            (AnimDir + "/Aggresive/TZ_aggresive_damage_A.FBX", false),  // 원샷
            (AnimDir + "/Generic/TZ_death_A.FBX",              false),  // 원샷
        };

        [MenuItem("Tools/VAT/VAT Baker")]
        static void Open() => GetWindow<VATBakerWindow>("VAT Baker");

        void OnGUI()
        {
            EditorGUILayout.LabelField("VAT Baker", EditorStyles.boldLabel);

            // ── 멀티클립 세트 (상태머신) ──
            EditorGUILayout.HelpBox("상태머신용 5클립(Idle/Walk/Attack/Damage/Death)을 한 텍스처에 스택 → VATClipSet 생성.",
                MessageType.Info);
            GUI.backgroundColor = new Color(0.6f, 0.8f, 0.95f);
            if (GUILayout.Button("Bake Zombie Aggressive Set (5클립 스택)", GUILayout.Height(32)))
                DoBakeSet();
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("단일 클립", EditorStyles.boldLabel);

            _model = (GameObject)EditorGUILayout.ObjectField("Model (FBX)", _model, typeof(GameObject), false);
            _clip = (AnimationClip)EditorGUILayout.ObjectField("Anim Clip", _clip, typeof(AnimationClip), false);
            _frames = EditorGUILayout.IntSlider("Frames", _frames, 4, 120);
            _outputDir = EditorGUILayout.TextField("Output Dir", _outputDir);
            _outName = EditorGUILayout.TextField("Out Name", _outName);
            _vertexEuler = EditorGUILayout.Vector3Field("Vertex Rotation (보정)", _vertexEuler);

            if (GUILayout.Button("기본값 채우기 (M_Zombie_01 + broken_walk)"))
                LoadDefaults();

            using (new EditorGUI.DisabledScope(_model == null || _clip == null))
            {
                GUI.backgroundColor = new Color(0.6f, 0.9f, 0.6f);
                if (GUILayout.Button("Bake (단일)", GUILayout.Height(28)))
                    DoBake();
                GUI.backgroundColor = Color.white;
            }
        }

        void DoBakeSet()
        {
            try
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultModel);
                if (model == null) { EditorUtility.DisplayDialog("실패", "모델 없음:\n" + DefaultModel, "확인"); return; }

                var clips = new (AnimationClip, bool)[AggroSet.Length];
                for (int i = 0; i < AggroSet.Length; i++)
                {
                    var c = VATBaker.LoadFirstClip(AggroSet[i].path);
                    if (c == null) { EditorUtility.DisplayDialog("실패", "클립 없음:\n" + AggroSet[i].path, "확인"); return; }
                    clips[i] = (c, AggroSet[i].loop);
                }

                var set = VATBaker.BakeMulti(model, clips, _frames, _outputDir, "Zombie_M01_Aggro",
                                             Quaternion.Euler(_vertexEuler));
                EditorGUIUtility.PingObject(set);

                var sb = new StringBuilder();
                sb.AppendLine($"verts={set.vertexCount}  totalFrames={set.totalFrames}");
                foreach (var e in set.clips)
                    sb.AppendLine($"  {e.name}: startRow={e.startRow} frames={e.frameCount} fps={e.fps:F1} loop={e.loop}");
                Debug.Log("[VAT] Multi bake:\n" + sb);
                EditorUtility.DisplayDialog("VAT Set Bake 완료", sb.ToString(), "확인");
            }
            catch (System.Exception e)
            {
                Debug.LogError("VAT Set Bake 실패: " + e);
                EditorUtility.DisplayDialog("VAT Set Bake 실패", e.Message, "확인");
            }
        }

        void LoadDefaults()
        {
            _model = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultModel);
            _clip = VATBaker.LoadFirstClip(DefaultClip);
            if (_model == null) Debug.LogWarning("기본 모델을 못 찾음: " + DefaultModel);
            if (_clip == null) Debug.LogWarning("기본 클립을 못 찾음: " + DefaultClip);
        }

        void DoBake()
        {
            try
            {
                var r = VATBaker.Bake(_model, _clip, _frames, _outputDir, _outName, Quaternion.Euler(_vertexEuler));
                Debug.Log(r.ToString());
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(r.mesh);
                if (mesh != null) EditorGUIUtility.PingObject(mesh);
                EditorUtility.DisplayDialog("VAT Bake 완료",
                    $"정점 {r.vertexCount} × 프레임 {r.frames}\nbounds(size) = {r.bounds.size}\n→ {_outputDir}/ 에 3개 에셋", "확인");
            }
            catch (System.Exception e)
            {
                Debug.LogError("VAT Bake 실패: " + e);
                EditorUtility.DisplayDialog("VAT Bake 실패", e.Message, "확인");
            }
        }
    }
}
#endif
