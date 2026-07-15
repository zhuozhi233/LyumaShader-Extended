#if UNITY_EDITOR
using nadena.dev.ndmf;
using nadena.dev.ndmf.fluent;
using UnityEngine;

[assembly: ExportsPlugin(typeof(LyumaShader.Waifu2dStaticMeshConversionPlugin))]

namespace LyumaShader
{
    internal sealed class Waifu2dStaticMeshConversionPlugin : Plugin<Waifu2dStaticMeshConversionPlugin>
    {
        public override string QualifiedName => "com.zhuozhi.lyumashader-extended.static-mesh-conversion";
        public override string DisplayName => "LyumaShader Extended - Waifu2d Static Mesh Conversion";

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                .BeforePlugin("nadena.dev.modular-avatar")
                .Run("Convert Waifu2d static meshes", context =>
                {
                    foreach(LyumaWaifu2dStaticMeshConverter marker in
                            context.AvatarRootObject.GetComponentsInChildren<LyumaWaifu2dStaticMeshConverter>(true))
                    {
                        Transform hips = Waifu2dStaticMeshConversion.FindHips(marker.gameObject);
                        foreach(MeshRenderer renderer in Waifu2dStaticMeshConversion.FindTargets(marker.gameObject))
                            Waifu2dStaticMeshConversion.Convert(renderer, hips, false, false);
                        Object.DestroyImmediate(marker);
                    }
                });
        }
    }
}
#endif
