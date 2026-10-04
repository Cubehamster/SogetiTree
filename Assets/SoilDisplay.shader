Shader "TreePlanting/Soil Display"
{
    Properties { _Tint("Tint", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            CBUFFER_END
            struct Attributes {
                float4 positionOS:POSITION; half4 color:COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings {
                float4 positionCS:SV_POSITION; half4 color:COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings vert(Attributes i) {
                Varyings o=(Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS=TransformObjectToHClip(i.positionOS.xyz);
                o.color=i.color*_Tint; return o;
            }
            half4 frag(Varyings i):SV_Target {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return i.color;
            }
            ENDHLSL
        }
    }
}
