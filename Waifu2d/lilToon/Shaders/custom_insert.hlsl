//----------------------------------------------------------------------------------------------------------------------
// Per-pass bridge between Lyuma's original Waifu2d math and lilToon's custom
// vertex hook. This file is part of LyumaShader; lilToon itself is not modified.
// All material-facing variants depend on this bridge through their hidden passes.

#define NO_UNIFORMS
#include "../../Waifu2d.cginc"
#undef NO_UNIFORMS

#if defined(LIL_OUTLINE) && !defined(LIL_PASS_SHADOWCASTER_INCLUDED) && !defined(LIL_PASS_MOTIONVECTOR_INCLUDED) && !(defined(LIL_URP) && defined(LIL_PASS_DEPTHONLY_INCLUDED))
    #define LYUMA_OUTLINE_ZBIAS_CLIPSPACE
#endif

void LyumaWaifu2dApply(inout lilVertexPositionInputs vertexInput)
{
    // Keep the clip-space depth reference in a field that will be recomputed below.
    // positionSS.x temporarily carries an outline-only NDC depth offset; z/w carry
    // the depth reference used by Lyuma's depth-squash / z-fighting correction.
    float4 originalPositionCS = vertexInput.positionCS;
    float4 depthReferenceCS = originalPositionCS;
    float outlineZBiasNDC = 0.0;

    // lilToon can use camera-relative world coordinates (notably in HDRP), while
    // the original Waifu2d implementation operates in absolute world space.
    float3 positionWS = lilToAbsolutePositionWS(vertexInput.positionWS);
    float3 flattenedWS;

    // lilToon's outline Z Bias is applied in object space before this custom
    // world-space hook. Rebuild the pre-bias position and flatten that geometry,
    // but retain the original bias as an NDC depth-only offset. Restoring the
    // whole world-space vector would create visible thickness from side views.
    #if defined(LYUMA_OUTLINE_ZBIAS_CLIPSPACE)
        float3 biasedPositionOS = lilTransformWStoOS(vertexInput.positionWS);
        float3 outlineViewDirectionOS = lilIsPerspective()
            ? lilViewDirectionOS(biasedPositionOS)
            : mul((float3x3)LIL_MATRIX_I_M, LIL_MATRIX_V._m20_m21_m22);
        float3 preBiasPositionOS = biasedPositionOS + normalize(outlineViewDirectionOS) * _OutlineZBias;
        float3 preBiasPositionWS = lilToAbsolutePositionWS(lilTransformOStoWS(preBiasPositionOS));
        float4 preBiasPositionCS = lilGetVertexPositionInputs(preBiasPositionOS).positionCS;
        outlineZBiasNDC = originalPositionCS.z / nonzeroify(originalPositionCS.w)
            - preBiasPositionCS.z / nonzeroify(preBiasPositionCS.w);
        depthReferenceCS = preBiasPositionCS;
        flattenedWS = waifu_computeWorldFlatWorldPos(float4(preBiasPositionWS, 1.0)).xyz;
    #else
        flattenedWS = waifu_computeWorldFlatWorldPos(float4(positionWS, 1.0)).xyz;
    #endif

    vertexInput.positionWS = lilToRelativePositionWS(lerp(positionWS, flattenedWS, waifu_coef));
    vertexInput.positionSS = float4(outlineZBiasNDC, 0.0, depthReferenceCS.z, depthReferenceCS.w);
}

lilVertexPositionInputs LyumaWaifu2dReGetVertexPositionInputs(lilVertexPositionInputs vertexInput)
{
    float4 depthReferenceData = vertexInput.positionSS;
    float outlineZBiasNDC = depthReferenceData.x;
    vertexInput = lilReGetVertexPositionInputs(vertexInput);

    if(waifu_coef > 1.0e-6)
    {
        float correctedZ = sign(depthReferenceData.w * depthReferenceData.z * vertexInput.positionCS.w)
            * max(0.00001, abs(depthReferenceData.z))
            * max(0.00001, abs(vertexInput.positionCS.w))
            / max(0.00001, abs(depthReferenceData.w));
        vertexInput.positionCS.z = lerp(correctedZ, vertexInput.positionCS.z, _zcorrect_coef);

        #if defined(LYUMA_OUTLINE_ZBIAS_CLIPSPACE)
            // The remaining physical bias shrinks with 2D Amount and contributes
            // through the flattened-depth side of the blend. Add only the missing
            // portion in clip space so the depth separation stays stable without
            // restoring any world-space thickness.
            float missingOutlineBias = 1.0 - _zcorrect_coef * (1.0 - waifu_coef);
            vertexInput.positionCS.z += outlineZBiasNDC * vertexInput.positionCS.w * missingOutlineBias;
        #endif

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
