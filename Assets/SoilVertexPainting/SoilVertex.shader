Shader "TreePlanting/Soil Vertex Data" {
Properties {
    [Toggle] _UseTerrainTextures("Use 27 Terrain Textures", Float) = 0
    _TerrainTextures("Terrain Texture Array (27 slices)", 2DArray) = "" {}
    _TerrainTiling("Terrain UV Tiling (XY) and Offset (ZW)", Vector) = (1,1,0,0)
    [Enum(Smooth Blend,0,Nearest Soil Type,1)] _TextureBlendMode("Texture Blending", Float) = 0
    _TerrainTint("Terrain Texture Tint", Color) = (1,1,1,1)
    _BaseMap("Fallback Soil Texture", 2D) = "white" {}
    _DryColor("Dry Soil", Color) = (.32,.19,.09,1)
    _WetColor("Wet Soil", Color) = (.10,.065,.035,1)
    _NutrientColor("Nutrient Rich Soil", Color) = (.20,.13,.06,1)
    _NutrientTintStrength("Nutrient Tint Strength", Range(0,1)) = .4
    _DrySmoothness("Dry Smoothness", Range(0,1)) = .1
    _WetSmoothness("Wet Smoothness", Range(0,1)) = .6
    [Enum(Soil,0,RGB Data,1,Roughness,2,Nutrients,3,Water,4)] _DebugMode("View", Float) = 0
}
SubShader {
    Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
    Pass {
        Name "ForwardLit"
        Tags { "LightMode"="UniversalForwardOnly" }
        HLSLPROGRAM
        #pragma target 3.5
        #pragma require 2darray
        #pragma vertex vert
        #pragma fragment frag
        #pragma multi_compile_instancing
        #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
        #pragma multi_compile_fragment _ _SHADOWS_SOFT
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D_ARRAY(_TerrainTextures); SAMPLER(sampler_TerrainTextures);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _TerrainTiling;
            half4 _TerrainTint;
            half _UseTerrainTextures, _TextureBlendMode;
            half4 _DryColor, _WetColor, _NutrientColor;
            half _NutrientTintStrength, _DrySmoothness, _WetSmoothness, _DebugMode;
        CBUFFER_END
        struct Attributes {
            float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; half4 color:COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings {
            float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0;
            half3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; half3 soil:TEXCOORD3;
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings vert(Attributes v) {
            Varyings o=(Varyings)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            VertexPositionInputs pos=GetVertexPositionInputs(v.positionOS.xyz);
            o.positionCS=pos.positionCS; o.positionWS=pos.positionWS;
            o.normalWS=TransformObjectToWorldNormal(v.normalOS);
            o.uv=v.uv; o.soil=v.color.rgb;
            return o;
        }
        // Slice index: roughness * 9 + nutrients * 3 + water.
        half3 SampleTerrain(float2 uv, float3 level, float2 dx, float2 dy) {
            float slice = level.x * 9 + level.y * 3 + level.z;
            return SAMPLE_TEXTURE2D_ARRAY_GRAD(_TerrainTextures, sampler_TerrainTextures,
                uv, slice, dx, dy).rgb;
        }
        half3 BlendTerrain(float2 uv, half3 soil, float2 dx, float2 dy) {
            float3 grid = saturate(soil) * 2;
            if (_TextureBlendMode > .5)
                return SampleTerrain(uv, floor(grid + .5), dx, dy);
            // Always use a valid cell. At value 1 the upper anchor has weight 1.
            float3 lo = min(floor(grid), float3(1,1,1));
            float3 hi = lo + 1;
            float3 t = grid - lo;
            half3 b00 = lerp(SampleTerrain(uv, float3(lo.x,lo.y,lo.z), dx,dy),
                            SampleTerrain(uv, float3(lo.x,lo.y,hi.z), dx,dy), t.z);
            half3 b01 = lerp(SampleTerrain(uv, float3(lo.x,hi.y,lo.z), dx,dy),
                            SampleTerrain(uv, float3(lo.x,hi.y,hi.z), dx,dy), t.z);
            half3 b10 = lerp(SampleTerrain(uv, float3(hi.x,lo.y,lo.z), dx,dy),
                            SampleTerrain(uv, float3(hi.x,lo.y,hi.z), dx,dy), t.z);
            half3 b11 = lerp(SampleTerrain(uv, float3(hi.x,hi.y,lo.z), dx,dy),
                            SampleTerrain(uv, float3(hi.x,hi.y,hi.z), dx,dy), t.z);
            return lerp(lerp(b00,b01,t.y), lerp(b10,b11,t.y), t.x);
        }
        half4 frag(Varyings i):SV_Target {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            half3 soil=saturate(i.soil);
            float2 terrainUV = i.uv * _TerrainTiling.xy + _TerrainTiling.zw;
            // Explicit UV gradients keep mip selection independent of the chosen slices.
            float2 uvDx = ddx(terrainUV), uvDy = ddy(terrainUV);
            if (_DebugMode > .5) {
                half3 debug=soil;
                if (_DebugMode > 1.5 && _DebugMode < 2.5) debug=soil.rrr;
                else if (_DebugMode >= 2.5 && _DebugMode < 3.5) debug=soil.ggg;
                else if (_DebugMode >= 3.5) debug=soil.bbb;
                return half4(debug,1);
            }
            half3 albedo;
            if (_UseTerrainTextures > .5) {
                albedo = BlendTerrain(terrainUV,soil,uvDx,uvDy) * _TerrainTint.rgb;
            } else {
                albedo=lerp(_DryColor.rgb,_NutrientColor.rgb,soil.g*_NutrientTintStrength);
                albedo=lerp(albedo,_WetColor.rgb,soil.b);
                float2 fallbackUV = i.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                albedo*=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,fallbackUV).rgb;
            }
            half smoothness=lerp(_DrySmoothness,_WetSmoothness,soil.b)*(1-soil.r);
            half3 n=normalize(i.normalWS), view=GetWorldSpaceNormalizeViewDir(i.positionWS);
            Light light=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
            BRDFData brdf;
            half alpha = 1;
            InitializeBRDFData(albedo,0,half3(.04,.04,.04),smoothness,alpha,brdf);
            half3 color=SampleSH(n)*albedo;
            color+=LightingPhysicallyBased(brdf,light,n,view);
            return half4(color,1);
        }
        ENDHLSL
    }
    UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    UsePass "Universal Render Pipeline/Lit/DepthOnly"
}
}
