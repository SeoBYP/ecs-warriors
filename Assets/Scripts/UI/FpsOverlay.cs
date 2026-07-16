using UnityEngine;

namespace UI
{
    public class FpsOverlay : MonoBehaviour
    {
        float _smoothDt = 0.0001f;   // 0으로 두면 1프레임차에 1/0 = Infinity → 작은 값으로 초기화
        GUIStyle _style;   // 필드에 캐싱

        void Update() {
            _smoothDt = Mathf.Lerp(_smoothDt, Time.unscaledDeltaTime, 0.1f);
        }

        void OnGUI() {
            if (_style == null) {                          // 최초 1회만
                _style = new GUIStyle(GUI.skin.label);     // 스킨 기반 → 폰트 메트릭 정상
                _style.fontSize = 24;
                _style.normal.textColor = Color.white;     // 밝은 배경에도 읽히게
            }

            float fps = 1f / _smoothDt;
            float ms  = _smoothDt * 1000f;
            GUI.Label(new Rect(10, 10, 400, 40), $"{fps:0} FPS  ({ms:0.0} ms)", _style);
        }
    }
}