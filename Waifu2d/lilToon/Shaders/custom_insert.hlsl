//----------------------------------------------------------------------------------------------------------------------
// Per-pass bridge between Lyuma's original Waifu2d math and lilToon's custom
// vertex hook. This file is part of LyumaShader; lilToon itself is not modified.
// All material-facing variants depend on this bridge through their hidden passes.

#define NO_UNIFORMS
#include "../../Waifu2d.cginc"
#undef NO_UNIFORMS

void LyumaWaifu2dApply(inout lilVertexPositionInputs vertexInput)
{
    // Keep the original clip position in a field that will be recomputed below.
    // It is used to retain Lyuma's depth-squash / z-fighting correction.
    float4 originalPositionCS = vertexInput.positionCS;

    // lilToon can use camera-relative world coordinates (notably in HDRP), while
    // the original Waifu2d implementation operates in absolute world space.
    float3 positionWS = lilToAbsolutePositionWS(vertexInput.positionWS);
    float3 flattenedWS = waifu_computeWorldFlatWorldPos(float4(positionWS, 1.0)).xyz;
    vertexInput.positionWS = lilToRelativePositionWS(lerp(positionWS, flattenedWS, waifu_coef));
    vertexInput.positionSS = originalPositionCS;
}

lilVertexPositionInputs LyumaWaifu2dReGetVertexPositionInputs(lilVertexPositionInputs vertexInput)
{
    float4 originalPositionCS = vertexInput.positionSS;
    vertexInput = lilReGetVertexPositionInputs(vertexInput);

    if(waifu_coef > 1.0e-6)
    {
        float correctedZ = sign(originalPositionCS.w * originalPositionCS.z * vertexInput.positionCS.w)
            * max(0.00001, abs(originalPositionCS.z))
            * max(0.00001, abs(vertexInput.positionCS.w))
            / max(0.00001, abs(originalPositionCS.w));
        vertexInput.positionCS.z = lerp(correctedZ, vertexInput.positionCS.z, _zcorrect_coef);
        vertexInput.positionSS = lilTransformCStoSS(vertexInput.positionCS);
    }

    return vertexInput;
}

// lilToon deliberately exposes this recompute macro to custom shaders. Replacing
// it here preserves Lyuma's clip-space depth correction after changing positionWS.
#undef LIL_RE_VERTEX_POSITION_INPUTS
#define LIL_RE_VERTEX_POSITION_INPUTS(o) o = LyumaWaifu2dReGetVertexPositionInputs(o)

// FakeShadow has a dedicated vertex shader and does not call lilCustomVertexWS.
// Hook its only position-input macro so it receives the same Waifu2d world-space
// flattening and clip-space depth correction as the regular lilToon passes.
#if defined(LIL_FAKESHADOW)
    #undef LIL_VERTEX_POSITION_INPUTS
    #define LIL_VERTEX_POSITION_INPUTS(positionOS,o) \
        lilVertexPositionInputs o = lilGetVertexPositionInputs(positionOS.xyz); \
        LyumaWaifu2dApply(o); \
        LIL_RE_VERTEX_POSITION_INPUTS(o)
#endif
