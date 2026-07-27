// VAT(Vertex Animation Texture) 재생 셰이더 — URP, 멀티 클립 상태머신.
//
// 여러 애니 클립을 한 텍스처에 세로로 쌓아두고(각 클립 = [startRow, frameCount] 구간),
// 인스턴스별 _AnimParams/_AnimStart 로 "어느 클립을, 언제부터" 재생할지 결정한다.
// 뼈/스키닝 없이 GPU 인스턴싱으로 수만 마리가 각자 다른 애니를 재생.
//
// 텍스처 규약: 가로(u)=정점 인덱스(메시 UV3.x), 세로(v)=프레임(모든 클립을 스택).
// per-instance (ECS가 [MaterialProperty]로 세팅, VATClipSet.ClipEntry에서 옴):
//   _AnimParams = (startRow, frameCount, fps, loop)  loop: 1=반복 / 0=원샷(마지막 프레임 정지)
//   _AnimStart  = 애니가 시작된 절대시각(_AnimTime 기준 = 게임 통제 시계) — 상태 전환 시점. 루프 위상 분산도 겸함.
Shader "ECSWarriors/VAT_Zombie"
{
    Properties
    {
        _BaseMap      ("Base Map", 2D) = "white" {}
        _BaseColor    ("Base Color", Color) = (1,1,1,1)
        _PositionMap  ("VAT Position Map", 2D) = "black" {}
        _NormalMapVAT ("VAT Normal Map", 2D) = "white" {}
        // (startRow, frameCount, fps, loop). 기본 = 0행부터 30프레임 30fps 루프 (비-ECS 프리뷰용).
        _AnimParams   ("Anim Params (start,frames,fps,loop)", Vector) = (0, 30, 30, 1)
        _AnimStart    ("Anim Start Time", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing            // 클래식 GPU 인스턴싱(게임오브젝트 테스트용)
            #pragma multi_compile _ DOTS_INSTANCING_ON  // Entities Graphics(ECS) 인스턴싱

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);      SAMPLER(sampler_BaseMap);
            TEXTURE2D(_PositionMap);  SAMPLER(sampler_PositionMap);
            TEXTURE2D(_NormalMapVAT); SAMPLER(sampler_NormalMapVAT);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _AnimParams;   // (startRow, frameCount, fps, loop)
                float  _AnimStart;
            CBUFFER_END

            // ── 게임이 통제하는 전역 애니 시계 (Shader.SetGlobalFloat("_AnimTime")) ──
            //   VatAnimClockSystem이 매 프레임 세팅하되 히트스톱 중엔 정지 → 전 좀비가 포즈째 얼어붙음.
            //   ⚠️ per-material CBUFFER "밖"에 둬야 전역이 정상 전달됨(SRP Batcher). DOTS 인스턴싱 프로퍼티와도 별개.
            float _AnimTime;

            // ── Entities Graphics 인스턴스별 오버라이드 ────────────────────────────
            // ECS에서 [MaterialProperty("_AnimParams")]/[MaterialProperty("_AnimStart")]로 좀비마다 세팅.
            // 주의: 이 #define들은 위 CBUFFER 선언보다 "뒤"에 와야 한다(선언은 원래 이름 유지).
            #if defined(UNITY_DOTS_INSTANCING_ENABLED)
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float4, _AnimParams)
                UNITY_DOTS_INSTANCED_PROP(float,  _AnimStart)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #define _AnimParams UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _AnimParams)
            #define _AnimStart  UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float,  _AnimStart)
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 uv3        : TEXCOORD3;   // x = 정점 인덱스 (VATBaker가 구움)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float startRow = _AnimParams.x;
                float frames   = max(_AnimParams.y, 1.0);
                float fps      = _AnimParams.z;
                float loop     = _AnimParams.w;

                // 1) 애니 시작 이후 경과 프레임 (_AnimStart = 상태 전환 시각 / 루프 위상)
                //    _AnimTime = 게임 통제 전역 시계(히트스톱 때 정지). 엔진 _Time.y 대신 사용.
                float local = (_AnimTime - _AnimStart) * fps;
                //    루프=wrap, 원샷(death)=마지막 프레임에서 정지(클램프)
                float frame = (loop > 0.5) ? fmod(local, frames)
                                           : min(max(local, 0.0), frames - 1.0);
                int row = (int)(startRow + frame);
                int vx  = (int)(IN.uv3.x + 0.5);

                // 2) VAT에서 이 정점의 위치/노멀 Load (정수 텍셀좌표, 보간 없음)
                float3 posOS = LOAD_TEXTURE2D_LOD(_PositionMap,  int2(vx, row), 0).xyz;
                float3 nrmOS = LOAD_TEXTURE2D_LOD(_NormalMapVAT, int2(vx, row), 0).xyz;

                // 3) 표준 변환 (오브젝트 → 월드 → 클립)
                VertexPositionInputs pos = GetVertexPositionInputs(posOS);
                OUT.positionHCS = pos.positionCS;
                OUT.positionWS  = pos.positionWS;
                OUT.normalWS    = TransformObjectToWorldNormal(nrmOS);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                half4 baseCol = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                // 간단 라이팅: 메인 디렉셔널 램버트 + 앰비언트(SH). 형태 확인용, 툰셰이딩은 후속.
                Light  mainLight = GetMainLight();
                float3 n = normalize(IN.normalWS);
                float  ndotl = saturate(dot(n, mainLight.direction));
                float3 lighting = mainLight.color * ndotl + SampleSH(n);
                return half4(baseCol.rgb * lighting, baseCol.a);
            }
            ENDHLSL
        }
    }
}
