#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace LyumaShader
{
    internal static class Waifu2dStaticMeshConversion
    {
        private const string GeneratedMeshFolder = "Assets/LyumaShader/GeneratedMeshes";

        internal static bool UsesWaifu2d(MeshRenderer renderer)
        {
            if(renderer == null) return false;
            foreach(Material material in renderer.sharedMaterials)
            {
                if(material != null && material.shader != null &&
                   material.HasProperty("_2d_coef") &&
                   material.shader.name.IndexOf("LyumaShader/Waifu2d", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        internal static List<MeshRenderer> FindTargets(GameObject root)
        {
            var result = new List<MeshRenderer>();
            if(root == null) return result;
            Waifu2dAssociatedMaterialScanner.Result associated =
                Waifu2dAssociatedMaterialScanner.Collect(new Object[] { root });
            foreach(MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if(filter == null || filter.sharedMesh == null) continue;

                bool usesWaifu2d = false;
                foreach(Material material in associated.GetCandidateMaterials(renderer))
                {
                    if(material != null && material.shader != null &&
                       material.HasProperty("_2d_coef") &&
                       material.shader.name.IndexOf("LyumaShader/Waifu2d", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        usesWaifu2d = true;
                        break;
                    }
                }
                if(usesWaifu2d) result.Add(renderer);
            }
            return result;
        }

        internal static SkinnedMeshRenderer Convert(MeshRenderer source, bool persistentMesh, bool useUndo)
        {
            if(source == null) return null;
            MeshFilter filter = source.GetComponent<MeshFilter>();
            if(filter == null || filter.sharedMesh == null) return null;

            Mesh mesh = Object.Instantiate(filter.sharedMesh);
            mesh.name = filter.sharedMesh.name + "_Waifu2dSingleBone";
            var weights = new BoneWeight[mesh.vertexCount];
            for(int i = 0; i < weights.Length; i++) weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1.0f };
            mesh.boneWeights = weights;
            mesh.bindposes = new[] { Matrix4x4.identity };

            if(persistentMesh)
            {
                EnsureFolder(GeneratedMeshFolder);
                string path = AssetDatabase.GenerateUniqueAssetPath(
                    GeneratedMeshFolder + "/" + MakeSafeName(source.gameObject.name) + "_SingleBone.asset");
                AssetDatabase.CreateAsset(mesh, path);
            }

            GameObject go = source.gameObject;
            SkinnedMeshRenderer target = useUndo
                ? Undo.AddComponent<SkinnedMeshRenderer>(go)
                : go.AddComponent<SkinnedMeshRenderer>();
            target.sharedMesh = mesh;
            target.bones = new[] { go.transform };
            target.rootBone = go.transform;
            target.sharedMaterials = source.sharedMaterials;
            target.enabled = source.enabled;
            target.shadowCastingMode = source.shadowCastingMode;
            target.receiveShadows = source.receiveShadows;
            target.lightProbeUsage = source.lightProbeUsage;
            target.reflectionProbeUsage = source.reflectionProbeUsage;
            target.probeAnchor = source.probeAnchor;
            target.motionVectorGenerationMode = source.motionVectorGenerationMode;
            target.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
            target.sortingLayerID = source.sortingLayerID;
            target.sortingOrder = source.sortingOrder;
            target.lightmapIndex = source.lightmapIndex;
            target.lightmapScaleOffset = source.lightmapScaleOffset;
            target.realtimeLightmapIndex = source.realtimeLightmapIndex;
            target.realtimeLightmapScaleOffset = source.realtimeLightmapScaleOffset;
            target.localBounds = mesh.bounds;

            if(useUndo)
            {
                Undo.DestroyObjectImmediate(source);
                Undo.DestroyObjectImmediate(filter);
            }
            else
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(filter);
            }
            return target;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for(int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if(!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string MakeSafeName(string value)
        {
            foreach(char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return string.IsNullOrEmpty(value) ? "Mesh" : value;
        }
    }
}
#endif
