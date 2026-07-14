#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LyumaShader
{
    /// <summary>
    /// Maps lilToon's material-facing shader variants to Lyuma-owned custom
    /// variants. No file under Packages/jp.lilxyzw.liltoon is changed.
    /// </summary>
    internal static class LilToonWaifu2dAdapter
    {
        internal const string ShaderPrefix = "LyumaShader/Waifu2d/lilToon";
        private const string AssetsShaderFolder = "Assets/LyumaShader/Waifu2d/lilToon/Shaders";
        private const string PackageShaderFolder = "Packages/com.zhuozhi.lyumashader-extended/Waifu2d/lilToon/Shaders";
        private static string cachedShaderAssetFolder;

        private static readonly Dictionary<string, string> OriginalToWaifu2d =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "lilToon", ShaderPrefix + "/lilToon" },
                { "Hidden/lilToonCutout", "Hidden/" + ShaderPrefix + "/Cutout" },
                { "Hidden/lilToonTransparent", "Hidden/" + ShaderPrefix + "/Transparent" },
                { "Hidden/lilToonOnePassTransparent", "Hidden/" + ShaderPrefix + "/OnePassTransparent" },
                { "Hidden/lilToonTwoPassTransparent", "Hidden/" + ShaderPrefix + "/TwoPassTransparent" },

                { "Hidden/lilToonOutline", "Hidden/" + ShaderPrefix + "/OpaqueOutline" },
                { "Hidden/lilToonCutoutOutline", "Hidden/" + ShaderPrefix + "/CutoutOutline" },
                { "Hidden/lilToonTransparentOutline", "Hidden/" + ShaderPrefix + "/TransparentOutline" },
                { "Hidden/lilToonOnePassTransparentOutline", "Hidden/" + ShaderPrefix + "/OnePassTransparentOutline" },
                { "Hidden/lilToonTwoPassTransparentOutline", "Hidden/" + ShaderPrefix + "/TwoPassTransparentOutline" },

                { "_lil/[Optional] lilToonOutlineOnly", ShaderPrefix + "/[Optional] OutlineOnly/Opaque" },
                { "_lil/[Optional] lilToonOutlineOnlyCutout", ShaderPrefix + "/[Optional] OutlineOnly/Cutout" },
                { "_lil/[Optional] lilToonOutlineOnlyTransparent", ShaderPrefix + "/[Optional] OutlineOnly/Transparent" },

                { "Hidden/lilToonTessellation", "Hidden/" + ShaderPrefix + "/Tessellation/Opaque" },
                { "Hidden/lilToonTessellationCutout", "Hidden/" + ShaderPrefix + "/Tessellation/Cutout" },
                { "Hidden/lilToonTessellationTransparent", "Hidden/" + ShaderPrefix + "/Tessellation/Transparent" },
                { "Hidden/lilToonTessellationOnePassTransparent", "Hidden/" + ShaderPrefix + "/Tessellation/OnePassTransparent" },
                { "Hidden/lilToonTessellationTwoPassTransparent", "Hidden/" + ShaderPrefix + "/Tessellation/TwoPassTransparent" },
                { "Hidden/lilToonTessellationOutline", "Hidden/" + ShaderPrefix + "/Tessellation/OpaqueOutline" },
                { "Hidden/lilToonTessellationCutoutOutline", "Hidden/" + ShaderPrefix + "/Tessellation/CutoutOutline" },
                { "Hidden/lilToonTessellationTransparentOutline", "Hidden/" + ShaderPrefix + "/Tessellation/TransparentOutline" },
                { "Hidden/lilToonTessellationOnePassTransparentOutline", "Hidden/" + ShaderPrefix + "/Tessellation/OnePassTransparentOutline" },
                { "Hidden/lilToonTessellationTwoPassTransparentOutline", "Hidden/" + ShaderPrefix + "/Tessellation/TwoPassTransparentOutline" },

                { "Hidden/lilToonLite", ShaderPrefix + "/lilToonLite" },
                { "Hidden/lilToonLiteCutout", "Hidden/" + ShaderPrefix + "/Lite/Cutout" },
                { "Hidden/lilToonLiteTransparent", "Hidden/" + ShaderPrefix + "/Lite/Transparent" },
                { "Hidden/lilToonLiteOnePassTransparent", "Hidden/" + ShaderPrefix + "/Lite/OnePassTransparent" },
                { "Hidden/lilToonLiteTwoPassTransparent", "Hidden/" + ShaderPrefix + "/Lite/TwoPassTransparent" },
                { "Hidden/lilToonLiteOutline", "Hidden/" + ShaderPrefix + "/Lite/OpaqueOutline" },
                { "Hidden/lilToonLiteCutoutOutline", "Hidden/" + ShaderPrefix + "/Lite/CutoutOutline" },
                { "Hidden/lilToonLiteTransparentOutline", "Hidden/" + ShaderPrefix + "/Lite/TransparentOutline" },
                { "Hidden/lilToonLiteOnePassTransparentOutline", "Hidden/" + ShaderPrefix + "/Lite/OnePassTransparentOutline" },
                { "Hidden/lilToonLiteTwoPassTransparentOutline", "Hidden/" + ShaderPrefix + "/Lite/TwoPassTransparentOutline" },

                { "Hidden/lilToonRefraction", "Hidden/" + ShaderPrefix + "/Refraction" },
                { "Hidden/lilToonRefractionBlur", "Hidden/" + ShaderPrefix + "/RefractionBlur" },
                { "Hidden/lilToonFur", "Hidden/" + ShaderPrefix + "/Fur" },
                { "Hidden/lilToonFurCutout", "Hidden/" + ShaderPrefix + "/FurCutout" },
                { "Hidden/lilToonFurTwoPass", "Hidden/" + ShaderPrefix + "/FurTwoPass" },
                { "_lil/[Optional] lilToonFurOnlyTransparent", ShaderPrefix + "/[Optional] FurOnly/Transparent" },
                { "_lil/[Optional] lilToonFurOnlyCutout", ShaderPrefix + "/[Optional] FurOnly/Cutout" },
                { "_lil/[Optional] lilToonFurOnlyTwoPass", ShaderPrefix + "/[Optional] FurOnly/TwoPass" },
                { "Hidden/lilToonGem", "Hidden/" + ShaderPrefix + "/Gem" },
                { "_lil/[Optional] lilToonFakeShadow", ShaderPrefix + "/[Optional] FakeShadow" },

                { "_lil/[Optional] lilToonOverlay", ShaderPrefix + "/[Optional] Overlay" },
                { "_lil/[Optional] lilToonOverlayOnePass", ShaderPrefix + "/[Optional] OverlayOnePass" },
                { "_lil/[Optional] lilToonLiteOverlay", ShaderPrefix + "/[Optional] LiteOverlay" },
                { "_lil/[Optional] lilToonLiteOverlayOnePass", ShaderPrefix + "/[Optional] LiteOverlayOnePass" },

                { "_lil/lilToonMulti", ShaderPrefix + "/lilToonMulti" },
                { "Hidden/lilToonMultiOutline", "Hidden/" + ShaderPrefix + "/MultiOutline" },
                { "Hidden/lilToonMultiRefraction", "Hidden/" + ShaderPrefix + "/MultiRefraction" },
                { "Hidden/lilToonMultiFur", "Hidden/" + ShaderPrefix + "/MultiFur" },
                { "Hidden/lilToonMultiGem", "Hidden/" + ShaderPrefix + "/MultiGem" }
            };

        private static readonly Dictionary<string, string> Waifu2dToOriginal = BuildReverseMap();

        internal static bool IsSupported(Shader shader)
        {
            return shader != null &&
                (OriginalToWaifu2d.ContainsKey(shader.name) || Waifu2dToOriginal.ContainsKey(shader.name));
        }

        internal static bool IsWaifu2dShader(Shader shader)
        {
            return shader != null && Waifu2dToOriginal.ContainsKey(shader.name);
        }

        internal static Shader GetWaifu2dShader(Shader source)
        {
            if(source == null) return null;
            if(Waifu2dToOriginal.ContainsKey(source.name)) return source;

            string targetName;
            if(!OriginalToWaifu2d.TryGetValue(source.name, out targetName)) return null;

            Shader target = Shader.Find(targetName);
            if(target != null) return target;

            string shaderAssetFolder = GetShaderAssetFolder();
            if(!string.IsNullOrEmpty(shaderAssetFolder))
            {
                AssetDatabase.ImportAsset(
                    shaderAssetFolder,
                    ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate
                );
            }
            target = Shader.Find(targetName);
            if(target == null)
            {
                EditorUtility.DisplayDialog(
                    "Lyuma Waifu2d - lilToon",
                    "The lilToon Waifu2d shader variant could not be loaded:\n" + targetName +
                    "\n\nCheck the Console for lilToon shader compilation errors.",
                    "OK"
                );
            }
            return target;
        }

        internal static string GetShaderAssetFolder()
        {
            if(!string.IsNullOrEmpty(cachedShaderAssetFolder) && AssetDatabase.IsValidFolder(cachedShaderAssetFolder))
            {
                return cachedShaderAssetFolder;
            }

            if(AssetDatabase.IsValidFolder(AssetsShaderFolder))
            {
                cachedShaderAssetFolder = AssetsShaderFolder;
                return cachedShaderAssetFolder;
            }
            if(AssetDatabase.IsValidFolder(PackageShaderFolder))
            {
                cachedShaderAssetFolder = PackageShaderFolder;
                return cachedShaderAssetFolder;
            }

            foreach(string guid in AssetDatabase.FindAssets("lilCustomShaderDatas"))
            {
                string dataPath = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if(!dataPath.EndsWith("/Waifu2d/lilToon/Shaders/lilCustomShaderDatas.lilblock", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(dataPath))
                {
                    continue;
                }

                string content = File.ReadAllText(dataPath);
                if(content.IndexOf("ShaderName \"" + ShaderPrefix + "\"", StringComparison.Ordinal) < 0) continue;
                cachedShaderAssetFolder = dataPath.Substring(0, dataPath.LastIndexOf('/'));
                return cachedShaderAssetFolder;
            }
            return null;
        }

        internal static Shader GetOriginalShader(Shader source)
        {
            if(source == null) return null;

            string targetName;
            if(!Waifu2dToOriginal.TryGetValue(source.name, out targetName)) return null;
            return Shader.Find(targetName);
        }

        internal static void InitializeMaterial(Material material)
        {
            if(material == null) return;
            material.SetFloat("_2d_coef", 0.99f);
            material.SetFloat("_facing_coef", 0.0f);
            material.SetFloat("_lock2daxis_coef", 1.0f);
            material.SetFloat("_zcorrect_coef", 0.975f);
        }

        private static Dictionary<string, string> BuildReverseMap()
        {
            var reverse = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach(KeyValuePair<string, string> pair in OriginalToWaifu2d)
            {
                reverse.Add(pair.Value, pair.Key);
            }
            return reverse;
        }
    }

    /// <summary>
    /// Keeps the full lilToon inspector and adds a small Waifu2d control panel.
    /// The shader references below point only to Lyuma-owned .lilcontainer assets.
    /// </summary>
    public sealed class LilToonWaifu2dInspector : lilToon.lilToonInspector
    {
        private MaterialProperty twoDimensionalness;
        private MaterialProperty facingDirection;
        private MaterialProperty lockAxis;
        private MaterialProperty squashZ;
        private static bool showWaifu2d = true;

        protected override void LoadCustomProperties(MaterialProperty[] props, Material material)
        {
            isCustomShader = true;
            ReplaceToCustomShaders();
            isShowRenderMode = !material.shader.name.Contains("/[Optional] ");

            twoDimensionalness = FindProperty("_2d_coef", props);
            facingDirection = FindProperty("_facing_coef", props);
            lockAxis = FindProperty("_lock2daxis_coef", props);
            squashZ = FindProperty("_zcorrect_coef", props);
        }

        protected override void DrawCustomProperties(Material material)
        {
            showWaifu2d = Foldout("Lyuma Waifu2d", "Lyuma Waifu2d", showWaifu2d);
            if(!showWaifu2d) return;

            EditorGUILayout.BeginVertical(boxOuter);
            EditorGUILayout.LabelField("Lyuma Waifu2d", customToggleFont);
            EditorGUILayout.BeginVertical(boxInnerHalf);
            m_MaterialEditor.ShaderProperty(twoDimensionalness, "2D Amount / 2D 强度");
            m_MaterialEditor.ShaderProperty(facingDirection, "Facing Direction / 朝向");
            m_MaterialEditor.ShaderProperty(lockAxis, "Lock 2D Axis / 锁定 2D 轴");
            m_MaterialEditor.ShaderProperty(squashZ, "Squash Z / Z 深度修正");
            EditorGUILayout.HelpBox("Recommended Squash Z: 0.95 - 0.975", MessageType.Info);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
        }

        protected override void ReplaceToCustomShaders()
        {
            const string shaderName = LilToonWaifu2dAdapter.ShaderPrefix;

            lts         = Shader.Find(shaderName + "/lilToon");
            ltsc        = Shader.Find("Hidden/" + shaderName + "/Cutout");
            ltst        = Shader.Find("Hidden/" + shaderName + "/Transparent");
            ltsot       = Shader.Find("Hidden/" + shaderName + "/OnePassTransparent");
            ltstt       = Shader.Find("Hidden/" + shaderName + "/TwoPassTransparent");

            ltso        = Shader.Find("Hidden/" + shaderName + "/OpaqueOutline");
            ltsco       = Shader.Find("Hidden/" + shaderName + "/CutoutOutline");
            ltsto       = Shader.Find("Hidden/" + shaderName + "/TransparentOutline");
            ltsoto      = Shader.Find("Hidden/" + shaderName + "/OnePassTransparentOutline");
            ltstto      = Shader.Find("Hidden/" + shaderName + "/TwoPassTransparentOutline");

            ltsoo       = Shader.Find(shaderName + "/[Optional] OutlineOnly/Opaque");
            ltscoo      = Shader.Find(shaderName + "/[Optional] OutlineOnly/Cutout");
            ltstoo      = Shader.Find(shaderName + "/[Optional] OutlineOnly/Transparent");

            ltstess     = Shader.Find("Hidden/" + shaderName + "/Tessellation/Opaque");
            ltstessc    = Shader.Find("Hidden/" + shaderName + "/Tessellation/Cutout");
            ltstesst    = Shader.Find("Hidden/" + shaderName + "/Tessellation/Transparent");
            ltstessot   = Shader.Find("Hidden/" + shaderName + "/Tessellation/OnePassTransparent");
            ltstesstt   = Shader.Find("Hidden/" + shaderName + "/Tessellation/TwoPassTransparent");
            ltstesso    = Shader.Find("Hidden/" + shaderName + "/Tessellation/OpaqueOutline");
            ltstessco   = Shader.Find("Hidden/" + shaderName + "/Tessellation/CutoutOutline");
            ltstessto   = Shader.Find("Hidden/" + shaderName + "/Tessellation/TransparentOutline");
            ltstessoto  = Shader.Find("Hidden/" + shaderName + "/Tessellation/OnePassTransparentOutline");
            ltstesstto  = Shader.Find("Hidden/" + shaderName + "/Tessellation/TwoPassTransparentOutline");

            ltsl        = Shader.Find(shaderName + "/lilToonLite");
            ltslc       = Shader.Find("Hidden/" + shaderName + "/Lite/Cutout");
            ltslt       = Shader.Find("Hidden/" + shaderName + "/Lite/Transparent");
            ltslot      = Shader.Find("Hidden/" + shaderName + "/Lite/OnePassTransparent");
            ltsltt      = Shader.Find("Hidden/" + shaderName + "/Lite/TwoPassTransparent");
            ltslo       = Shader.Find("Hidden/" + shaderName + "/Lite/OpaqueOutline");
            ltslco      = Shader.Find("Hidden/" + shaderName + "/Lite/CutoutOutline");
            ltslto      = Shader.Find("Hidden/" + shaderName + "/Lite/TransparentOutline");
            ltsloto     = Shader.Find("Hidden/" + shaderName + "/Lite/OnePassTransparentOutline");
            ltsltto     = Shader.Find("Hidden/" + shaderName + "/Lite/TwoPassTransparentOutline");

            ltsref      = Shader.Find("Hidden/" + shaderName + "/Refraction");
            ltsrefb     = Shader.Find("Hidden/" + shaderName + "/RefractionBlur");
            ltsfur      = Shader.Find("Hidden/" + shaderName + "/Fur");
            ltsfurc     = Shader.Find("Hidden/" + shaderName + "/FurCutout");
            ltsfurtwo   = Shader.Find("Hidden/" + shaderName + "/FurTwoPass");
            ltsfuro     = Shader.Find(shaderName + "/[Optional] FurOnly/Transparent");
            ltsfuroc    = Shader.Find(shaderName + "/[Optional] FurOnly/Cutout");
            ltsfurotwo  = Shader.Find(shaderName + "/[Optional] FurOnly/TwoPass");
            ltsgem      = Shader.Find("Hidden/" + shaderName + "/Gem");
            ltsfs       = Shader.Find(shaderName + "/[Optional] FakeShadow");

            ltsover     = Shader.Find(shaderName + "/[Optional] Overlay");
            ltsoover    = Shader.Find(shaderName + "/[Optional] OverlayOnePass");
            ltslover    = Shader.Find(shaderName + "/[Optional] LiteOverlay");
            ltsloover   = Shader.Find(shaderName + "/[Optional] LiteOverlayOnePass");

            ltsm        = Shader.Find(shaderName + "/lilToonMulti");
            ltsmo       = Shader.Find("Hidden/" + shaderName + "/MultiOutline");
            ltsmref     = Shader.Find("Hidden/" + shaderName + "/MultiRefraction");
            ltsmfur     = Shader.Find("Hidden/" + shaderName + "/MultiFur");
            ltsmgem     = Shader.Find("Hidden/" + shaderName + "/MultiGem");
        }
    }
}
#endif
