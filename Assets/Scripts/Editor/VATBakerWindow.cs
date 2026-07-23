#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Simulation.VAT
{
    /// <summary>
    /// VATBaker를 클릭으로 실행하는 에디터 창.
    /// 메뉴: Tools > VAT > VAT Baker
    ///   1) "기본값 채우기" 로 M_Zombie_01 + broken_walk 자동 세팅(또는 직접 드래그)
    ///   2) Bake → Assets/Data/VAT 에 _PositionMap/_NormalMap/_VATMesh 생성
    /// </summary>
    public class VATBakerWindow : EditorWindow
    {
        [SerializeField] GameObject _model;
        [SerializeField] AnimationClip _clip;
        [SerializeField] int _frames = 30;
        [SerializeField] string _outputDir = "Assets/Data/VAT";
        [SerializeField] string _outName = "Zombie_M01_BrokenWalk";
        [SerializeField] Vector3 _vertexEuler = Vector3.zero;

        const string DefaultModel =
            "Assets/ArtResource/Toon_Zombies_extended/models/single_mesh/male_civilians/M_Zombie_01.FBX";
        const string DefaultClip =
            "Assets/ArtResource/Toon_Zombies_extended/animation/Broken/TZ_broken_walk.FBX";

        [MenuItem("Tools/VAT/VAT Baker")]
        static void Open() => GetWindow<VATBakerWindow>("VAT Baker");

        void OnGUI()
        {
            EditorGUILayout.LabelField("VAT Baker", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "스킨드 메시 FBX + 애니 클립 → VAT 텍스처/메시로 굽습니다.\n" +
                "누워서 구워지면 Vertex Rotation에 (90,0,0) 등을 넣고 다시 Bake.",
                MessageType.Info);

            _model = (GameObject)EditorGUILayout.ObjectField("Model (FBX)", _model, typeof(GameObject), false);
            _clip = (AnimationClip)EditorGUILayout.ObjectField("Anim Clip", _clip, typeof(AnimationClip), false);
            _frames = EditorGUILayout.IntSlider("Frames", _frames, 4, 120);
            _outputDir = EditorGUILayout.TextField("Output Dir", _outputDir);
            _outName = EditorGUILayout.TextField("Out Name", _outName);
            _vertexEuler = EditorGUILayout.Vector3Field("Vertex Rotation (보정)", _vertexEuler);

            EditorGUILayout.Space();
            if (GUILayout.Button("기본값 채우기 (M_Zombie_01 + broken_walk)"))
                LoadDefaults();

            using (new EditorGUI.DisabledScope(_model == null || _clip == null))
            {
                GUI.backgroundColor = new Color(0.6f, 0.9f, 0.6f);
                if (GUILayout.Button("Bake", GUILayout.Height(34)))
                    DoBake();
                GUI.backgroundColor = Color.white;
            }
        }

        void LoadDefaults()
        {
            _model = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultModel);
            _clip = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(DefaultClip))
                if (o is AnimationClip c && !c.name.StartsWith("__preview")) { _clip = c; break; }

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
                    $"정점 {r.vertexCount} × 프레임 {r.frames}\n" +
                    $"bounds(size) = {r.bounds.size}\n" +
                    $"→ {_outputDir}/ 에 3개 에셋 생성\n\n" +
                    $"※ bounds에서 Y가 가장 크면 서 있는 것. 아니면 Vertex Rotation 보정.",
                    "확인");
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
