#ifndef SIMPLE_UNLIT_LIGHTING_INCLUDED 
#define SIMPLE_UNLIT_LIGHTING_INCLUDED 
 
#ifndef SHADERGRAPH_PREVIEW 
 
#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl" 
 
#if (SHADERPASS != SHADERPASS_FORWARD) 
#undef REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR 
#endif 
 
#endif 
 
 
void SimpleUnlitLighting_float(
    float3 PositionWS,
    float3 NormalWS,
    float2 LightmapUV,
 
    // 0 = completely unlit 
    // 1 = fully lit 
    float LightingStrength,

    // 0 = no self-shading
    // 1 = normal self-shading
    float SelfShadingStrength,
 
    out float Lighting)
{
#ifdef SHADERGRAPH_PREVIEW 
 
    Lighting = 1.0; 
 
#else 
 
    // ============================================================
    // LIGHT PROBE / BAKED GI
    // ============================================================
 
    float3 vertexSH;
 
    OUTPUT_SH(
        NormalWS,
        vertexSH
    );
 
 
    // Lightmap UV 
    float2 lightmapUV;
 
    OUTPUT_LIGHTMAP_UV(
        LightmapUV,
        unity_LightmapST,
        lightmapUV
    );
 
 
    // Baked GI / Light Probe 
    float3 bakedGI =
        SAMPLE_GI(
            lightmapUV,
            vertexSH,
            NormalWS
        );
 
 
    // ============================================================
    // SHADOW COORDINATES
    // ============================================================
 
    float4 positionCS =
        TransformWorldToHClip(PositionWS);
 
    float4 shadowCoord;
 
#if SHADOWS_SCREEN 
 
    shadowCoord = 
        ComputeScreenPos(positionCS); 
 
#else 
 
    shadowCoord =
        TransformWorldToShadowCoord(PositionWS);
 
#endif 
 
 
    // ============================================================
    // SHADOW MASK
    // ============================================================
 
    half4 shadowMask =
        SAMPLE_SHADOWMASK(lightmapUV);
 
 
    // ============================================================
    // MAIN LIGHT
    // ============================================================
 
    Light mainLight =
        GetMainLight(
            shadowCoord,
            PositionWS,
            shadowMask
        );
 
 
    // ============================================================
    // DIRECT LIGHT / SELF SHADING
    // ============================================================
 
    float NdotL =
        saturate(
            dot(
                NormalWS,
                mainLight.direction 
            )
        );
 
 
    // Reduce the effect of the surface normal on lighting.
    //
    // 0 = no self-shading
    // 1 = normal NdotL shading
    //
    // This does NOT modify realtime shadow attenuation.
 
    float selfShading =
        lerp(
            1.0,
            NdotL,
            saturate(SelfShadingStrength)
        );
 
 
    float directLight =
        selfShading *
        mainLight.shadowAttenuation;
 
 
    // ============================================================
    // COMBINE PROBE + REALTIME LIGHT
    // ============================================================
 
    // Light Probe brightness increased by 20%.
 
    float probeLighting =
        saturate(
            dot(
                bakedGI,
                float3(0.3333, 0.3333, 0.3333)
            ) * 1.2
        );
 
 
    float directLighting =
        saturate(directLight);
 
 
    // Combine 
 
    float rawLighting =
        saturate(
            probeLighting +
            directLighting
        );
 
 
    // ============================================================
    // LIGHTING STRENGTH
    // ============================================================
 
    // 0 = unlit 
    // 1 = normal lighting 
 
    Lighting =
        lerp(
            1.0,
            rawLighting,
            saturate(LightingStrength)
        );
 
#endif 
}
 
#endif