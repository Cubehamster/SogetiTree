#ifndef MAINLIGHT_NDOTL_INCLUDED
#define MAINLIGHT_NDOTL_INCLUDED

void LightNdotL_float(float3 NormalWS, out float NdotL)
{
#ifdef SHADERGRAPH_PREVIEW

    float3 lightDirection = normalize(float3(-0.3, -0.8, 0.6));
    NdotL = saturate(dot(normalize(NormalWS), lightDirection));

#else

    Light mainLight = GetMainLight();
    float3 lightDirection = normalize(mainLight.direction);

    NdotL = saturate(dot(normalize(NormalWS), lightDirection));

#endif
}

#endif