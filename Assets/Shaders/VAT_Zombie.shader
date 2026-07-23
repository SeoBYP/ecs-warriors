// VAT(Vertex Animation Texture) 재생 셰이더 — URP.
//
// 정점 단계에서 _PositionMap/_NormalMap 텍스처를 읽어 정점을 "그 프레임의 위치"로 옮긴다.
// 뼈/스키닝 없이 애니메이션이 재생되며, GPU 인스턴싱으로 수만 마리를 1드로우콜에 그릴 수 있다.
//
// 텍스처 규약(VATBaker와 일치): 가로(u)=정점 인덱스, 세로(v)=프레임.
//   - 정점 인덱스는 메시 UV3.x 에 구워져 있음.
//   - 프레임 행은 (_Time.y + _AnimOffset) * _Fps 를 _Frames 로 나눈 나머지.
//   - _AnimOffset 은 인스턴스별로 다르게 줘서(ECS MaterialProperty) 좀비마다 위상을 분산.
Shader "ECSWarriors/VAT_Zombie"
{
    Properties
    {
        _BaseMap      ("Base Map", 2D) = "white" {}
        _BaseColor    ("Base Color", Color) = (1,1,1,1)
        _PositionMap  ("VAT Position Map", 2D) = "black" {}
        _NormalMapVAT ("VAT Normal Map", 2D) = "white" {}
        _Frames       ("Frame Count", Float) = 30
        _Fps          ("Playback FPS", Float) = 30
        _AnimOffset   ("Anim Time Offset", Float) = 0
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
                float  _Frames;
                float  _Fps;
                float  _AnimOffset;
            CBUFFER_END

            // ── Entities Graphics 인스턴스별 오버라이드 ────────────────────────────
            // ECS에서 [MaterialProperty("_AnimOffset")] 컴포넌트로 좀비마다 값을 다르게 준다.
            // 주의: 이 #define은 위 CBUFFER 선언보다 "뒤"에 와야 한다(선언은 원래 이름 유지).
            #if defined(UNITY_DOTS_INSTANCING_ENABLED)
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float, _AnimOffset)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #define _AnimOffset UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _AnimOffset)
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

                // 1) 시간 → 프레임 행(row). _Time.y = 초. 인스턴스별 오프셋으로 위상 분산.
                float frame = fmod((_Time.y + _AnimOffset) * _Fps, _Frames);
                int   fy = (int)floor(frame);
                int   vx = (int)(IN.uv3.x + 0.5);   // 내 정점 번호(열)

                // 2) VAT에서 이 정점의 위치/노멀을 Load (정수 텍셀좌표, 보간 없음)
                float3 posOS = LOAD_TEXTURE2D_LOD(_PositionMap,  int2(vx, fy), 0).xyz;
                float3 nrmOS = LOAD_TEXTURE2D_LOD(_NormalMapVAT, int2(vx, fy), 0).xyz;

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
