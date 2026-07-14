//----------------------------------------------------------------------------------------------------------------------
// Lyuma Waifu2d integration for lilToon.
//
// The actual implementation lives in custom_insert.hlsl because it depends on
// Unity and lilToon transform helpers that are included separately for each pass.

#define LIL_CUSTOM_PROPERTIES \
    float _2d_coef; \
    float _facing_coef; \
    float _lock2daxis_coef; \
    float _zcorrect_coef;

#define LIL_CUSTOM_TEXTURES
#define LIL_CUSTOM_VERT_COPY

// lilToon invokes this after its own object-space deformation (outline, fur,
// AudioLink, tessellation, and so on), so those features are flattened too.
#define LIL_CUSTOM_VERTEX_WS \
    LyumaWaifu2dApply(vertexInput);
