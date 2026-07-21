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

void LyumaWaifu2dApply(inout lilVertexPositionInputs vertexInput, float3 positionOS)
{
    // Keep the depth data in a field that will be recomputed below. Outline
    // passes store the original unbiased NDC depth, the original NDC Z Bias,
    // the actual view-space Z displacement, and a shared safety switch.
    // Other passes retain Lyuma's original clip-space depth reference in z/w.
    float4 originalPositionCS = vertexInput.positionCS;
    float4 depthReferenceCS = originalPositionCS;
    float originalDepthNDC = 0.0;
    float outlineZBiasNDC = 0.0;
    float outlineZBiasVSZ = 0.0;
    float outlineZBiasSafeFactor = 1.0;

    float3 stableWorldOffset;
#if defined(LYUMA_OUTLINE_ZBIAS_CLIPSPACE)
    float3 remainingOutlineBiasWS = 0.0;
    float3 fullOutlineBiasWS = 0.0;
#endif

    // lilToon's outline Z Bias is applied in object space before this custom
    // world-space hook. Rebuild the pre-bias position and flatten that geometry,
    // but retain the original bias as an NDC depth-only offset. Restoring the
    // whole world-space vector would create visible thickness from side views.
    #if defined(LYUMA_OUTLINE_ZBIAS_CLIPSPACE)
        // The custom vertex hook now passes lilToon's already-deformed object
        // position directly, so no large WS -> OS round-trip is required.
        float3 biasedPositionOS = positionOS;
        float3 outlineViewDirectionOS = lilIsPerspective()
            ? lilViewDirectionOS(biasedPositionOS)
            : mul((float3x3)LIL_MATRIX_I_M, LIL_MATRIX_V._m20_m21_m22);
        float3 preBiasPositionOS = biasedPositionOS + normalize(outlineViewDirectionOS) * _OutlineZBias;
        float4 preBiasPositionCS = lilGetVertexPositionInputs(preBiasPositionOS).positionCS;
        originalDepthNDC = preBiasPositionCS.z / nonzeroify(preBiasPositionCS.w);
        outlineZBiasNDC = originalPositionCS.z / nonzeroify(originalPositionCS.w)
            - originalDepthNDC;
        depthReferenceCS = preBiasPositionCS;
        stableWorldOffset = waifu_computeVertexWorldOffset(float4(preBiasPositionOS, 1.0));

        // Keep the physical part of lilToon's outline Z Bias only while the
        // mesh is not fully flattened. Delay applying it until the near-clip
        // attenuation below has been calculated.
        fullOutlineBiasWS = mul(
            (float3x3)unity_ObjectToWorld,
            positionOS - preBiasPositionOS);
        outlineZBiasVSZ = mul((float3x3)LIL_MATRIX_V, fullOutlineBiasWS).z;
        remainingOutlineBiasWS = fullOutlineBiasWS * (1.0 - waifu_coef);
    #else
        stableWorldOffset = waifu_computeVertexWorldOffset(float4(positionOS, 1.0));
    #endif

    // Preserve a high-precision view-space position for the recompute hook.
    // lilReGetVertexPositionInputs would otherwise transform the large absolute
    // positionWS again and reintroduce the precision loss fixed above.
    #if defined(LIL_BRP)
        float3 objectOriginVS = mul(UNITY_MATRIX_MV, float4(0.0, 0.0, 0.0, 1.0)).xyz;
    #else
        // SRP object matrices are camera-relative where required by the pipeline.
        float3 objectOriginVS = lilTransformWStoVS(
            lilTransformOStoWS(float3(0.0, 0.0, 0.0)));
    #endif
    #if defined(LYUMA_OUTLINE_ZBIAS_CLIPSPACE)
        // Very close to the camera, even a partially retained Z Bias can move a
        // large outline triangle back into the visible frustum. The cutoff must
        // be uniform for the whole renderer: a per-vertex cutoff is interpolated
        // across the triangle and can stretch its back-facing side into a large
        // solid patch.
        if(lilIsPerspective() && waifu_coef > 1.0e-6)
        {
            // abs() makes approaching the flattened plane from its front or back
            // use the same safety rule. Object-space scale is also evaluated
            // uniformly instead of deriving the threshold from each vertex's
            // view direction.
            float viewDepth = abs(objectOriginVS.z);
            float nearClip = max(0.00001, _ProjectionParams.y);
            float maxObjectScale = max(
                length(mul((float3x3)unity_ObjectToWorld, float3(1.0, 0.0, 0.0))),
                max(
                    length(mul((float3x3)unity_ObjectToWorld, float3(0.0, 1.0, 0.0))),
                    length(mul((float3x3)unity_ObjectToWorld, float3(0.0, 0.0, 1.0)))));
            float biasWorldDistance = abs(_OutlineZBias) * maxObjectScale;
            float safeDepth = nearClip + max(0.15, biasWorldDistance * 4.0);
            outlineZBiasSafeFactor = step(safeDepth, viewDepth);
        }

        // A two-sided main surface paired with lilToon's front-culled outline
        // reverses which outline faces are exposed when a flattened model is
        // viewed from behind. Any non-zero depth separation can then reveal a
        // large interior outline patch. In 2D, make rear views behave exactly
        // like Outline Z Bias = 0 while preserving the front-view setting.
        if(waifu_coef > 0.1 && _Cull == 0)
        {
            // Switch a little before the exact side-on boundary. At zero, a
            // camera or mirror moving across the boundary can leave one frame
            // where an unstable rear shell is still visible. Normalize in the
            // horizontal plane so the margin is angular and distance-independent.
            float cameraHorizontalDistance = max(
                length(cameraPosInObjectSpace.xz),
                1.0e-5);
            float cameraFacingSide = dot(
                cameraPosInObjectSpace,
                targetCameraPosFacingVec) / cameraHorizontalDistance;
            outlineZBiasSafeFactor *= step(0.1, cameraFacingSide);
        }

        stableWorldOffset += remainingOutlineBiasWS * outlineZBiasSafeFactor;
    #endif

    vertexInput.positionWS = lilToRelativePositionWS(objectPos + stableWorldOffset);
    vertexInput.positionVS = objectOriginVS + mul((float3x3)LIL_MATRIX_V, stableWorldOffset);
    #if defined(LYUMA_OUTLINE_ZBIAS_CLIPSPACE)
        vertexInput.positionSS = float4(
            originalDepthNDC,
            outlineZBiasNDC,
            outlineZBiasVSZ,
            outlineZBiasSafeFactor);
    #else
        vertexInput.positionSS = float4(
            0.0,
            1.0,
            depthReferenceCS.z,
            depthReferenceCS.w);
    #endif
}

// Compatibility overload for custom shader families generated by older package
// versions. Regenerating them upgrades the hook to pass positionOS directly and
// receives the full large-coordinate precision improvement.
void LyumaWaifu2dApply(inout lilVertexPositionInputs vertexInput)
{
    LyumaWaifu2dApply(vertexInput, lilTransformWStoOS(vertexInput.positionWS));
}

// Motchiri deforms vertices before the world-space custom hook. Pass its
// deformed position through Waifu2d so the contact response, modified normals,
// and fragment effect are preserved, but its displacement is flattened with the
// rest of the mesh. Restoring the removed depth after flattening would give a
// 2D renderer real front/back thickness again; at close range that perspective
// difference exposes mouth and tongue geometry through the face.
void LyumaWaifu2dApplyPreservingCustomOffset(
    inout lilVertexPositionInputs vertexInput,
    float3 deformedPositionOS,
    float3 customDeltaOS)
{
    // At the 3D endpoint lilToon has already built vertexInput from Motchiri's
    // deformed input.positionOS. Leaving it completely untouched guarantees the
    // original custom shader result instead of reconstructing an equivalent
    // value through Waifu2d's relative-position path.
    if(waifu_coef <= 1.0e-6)
    {
        return;
    }

    float3 basePositionOS = deformedPositionOS - customDeltaOS;
    float keepCustomLogic = lerp(
        1.0,
        saturate(_lyuma_custom_logic_2d),
        waifu_coef);
    customDeltaOS *= keepCustomLogic;
    deformedPositionOS = basePositionOS + customDeltaOS;
    LyumaWaifu2dApply(vertexInput, deformedPositionOS);
}

lilVertexPositionInputs LyumaWaifu2dReGetVertexPositionInputs(lilVertexPositionInputs vertexInput)
{
    float4 depthReferenceData = vertexInput.positionSS;
    float3 stablePositionVS = vertexInput.positionVS;
    vertexInput.positionVS = stablePositionVS;
    vertexInput.positionCS = lilTransformVStoCS(stablePositionVS);
    vertexInput.positionSS = lilTransformCStoSS(vertexInput.positionCS);

    if(waifu_coef > 1.0e-6)
    {
        float safeNearDepth = max(
            0.005,
            max(0.00001, _ProjectionParams.y) * 1.05);
        bool applyWaifuDepthCorrection = !lilIsPerspective()
            || stablePositionVS.z <= -safeNearDepth;

        if(applyWaifuDepthCorrection)
        {
            #if defined(LYUMA_OUTLINE_ZBIAS_CLIPSPACE)
                float originalUnbiasedNDC = depthReferenceData.x;
                float originalBiasNDC = depthReferenceData.y;
                float outlineBiasVSZ = depthReferenceData.z;
                float outlineBiasSafeFactor = depthReferenceData.w;

                // stablePositionVS already retains the physical part of Z Bias that
                // remains at the current 2D Amount. Project only the removed part at
                // the new flattened depth, then use that projected NDC depth without
                // restoring any view-space X/Y movement or visible thickness.
                float3 reprojectedBiasPositionVS = stablePositionVS;
                reprojectedBiasPositionVS.z +=
                    outlineBiasVSZ * waifu_coef * outlineBiasSafeFactor;
                float4 reprojectedBiasPositionCS =
                    lilTransformVStoCS(reprojectedBiasPositionVS);
                float reprojectedBiasedNDC =
                    reprojectedBiasPositionCS.z
                    / nonzeroify(reprojectedBiasPositionCS.w);

                // zcorrect = 0 keeps lilToon's original biased NDC depth.
                // zcorrect = 1 uses the flattened position with Z Bias reprojected
                // at that position.
                float originalBiasedNDC = originalUnbiasedNDC
                    + originalBiasNDC * outlineBiasSafeFactor;
                float correctedBiasedNDC = lerp(
                    originalBiasedNDC,
                    reprojectedBiasedNDC,
                    _zcorrect_coef);
                vertexInput.positionCS.z =
                    correctedBiasedNDC * vertexInput.positionCS.w;
            #else
                float correctedZ = sign(depthReferenceData.w * depthReferenceData.z * vertexInput.positionCS.w)
                    * max(0.00001, abs(depthReferenceData.z))
                    * max(0.00001, abs(vertexInput.positionCS.w))
                    / max(0.00001, abs(depthReferenceData.w));
                vertexInput.positionCS.z = lerp(correctedZ, vertexInput.positionCS.z, _zcorrect_coef);
            #endif
        }

        #if defined(LIL_FAKESHADOW) && !defined(LIL_HDRP)
            // FakeShadow applies its light-direction offset after this custom
            // position hook. If that full 3D offset is left unchanged while the
            // hair and face depth are flattened, its depth component can become
            // larger than their remaining separation and expose the whole hair
            // mesh through the face stencil. Pre-compensate the original shift so
            // the pass ultimately uses the same flattened offset as the geometry.
            float3 fakeShadowLightDirection = normalize(
                lilGetLightDirection()
                + length(_FakeShadowVector.xyz)
                    * normalize(mul((float3x3)LIL_MATRIX_M, _FakeShadowVector.xyz)));
            float3 originalFakeShadowOffsetWS =
                fakeShadowLightDirection * _FakeShadowVector.w;
            float3 flattenedFakeShadowOffsetWS = originalFakeShadowOffsetWS
                - flattenNormal * dot(originalFakeShadowOffsetWS, flattenNormal);
            float3 adjustedFakeShadowOffsetWS = lerp(
                originalFakeShadowOffsetWS,
                flattenedFakeShadowOffsetWS,
                waifu_coef);
            float4 originalFakeShadowShiftCS = mul(
                LIL_MATRIX_VP,
                float4(originalFakeShadowOffsetWS, 0.0));
            float4 adjustedFakeShadowShiftCS = mul(
                LIL_MATRIX_VP,
                float4(adjustedFakeShadowOffsetWS, 0.0));

            // lil_pass_forward_fakeshadow subtracts originalFakeShadowShiftCS
            // immediately after this hook. Adding the difference here makes the
            // final result subtract adjustedFakeShadowShiftCS instead.
            if(applyWaifuDepthCorrection)
            {
                vertexInput.positionCS +=
                    originalFakeShadowShiftCS - adjustedFakeShadowShiftCS;
            }
        #endif

        #if defined(LYUMA_OUTLINE_ZBIAS_CLIPSPACE)
            // Flattening a two-sided surface makes its front and rear shells
            // coplanar. From the model's rear, lilToon's front-culled outline can
            // then expose interior triangles; width masks, fixed width, lighting,
            // ZTest, ZWrite, and Z Bias only change the shape or intensity of the
            // resulting flashing patches. Cull the rear outline as one uniform
            // renderer-level decision while leaving the main two-sided surface
            // untouched.
            if(waifu_coef > 0.1
                && _Cull == 0
                && dot(cameraPosInObjectSpace, targetCameraPosFacingVec)
                    < length(cameraPosInObjectSpace.xz) * 0.1)
            {
                // Outside both D3D's [0,w] and OpenGL's [-w,w] clip ranges.
                vertexInput.positionCS = float4(0.0, 0.0, -2.0, 1.0);
            }
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
        LyumaWaifu2dApply(o, positionOS.xyz); \
        LIL_RE_VERTEX_POSITION_INPUTS(o)
#endif
