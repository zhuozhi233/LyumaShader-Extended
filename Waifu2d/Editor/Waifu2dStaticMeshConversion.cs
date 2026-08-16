#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
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
                if(IsWaifu2dMaterial(material))
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
                    if(IsWaifu2dMaterial(material))
                    {
                        usesWaifu2d = true;
                        break;
                    }
                }
                if(usesWaifu2d) result.Add(renderer);
            }
            return result;
        }

        private static bool IsWaifu2dMaterial(Material material)
        {
            if(material == null) return false;
            var visited = new HashSet<Material>();
            Material current = material;
            while(current != null && visited.Add(current))
            {
                if(current.shader != null && current.HasProperty("_2d_coef") &&
                   current.shader.name.IndexOf(
                       "LyumaShader/Waifu2d",
                       StringComparison.OrdinalIgnoreCase
                   ) >= 0)
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }

        internal static List<MeshRenderer> FindAllTargets(GameObject root)
        {
            var result = new List<MeshRenderer>();
            if(root == null) return result;
            foreach(MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if(renderer == null) continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if(filter != null && filter.sharedMesh != null) result.Add(renderer);
            }
            return result;
        }

        internal static Transform FindHips(GameObject root)
        {
            if(root == null) return null;
            Animator animator = root.GetComponent<Animator>();
            if(animator == null) animator = root.GetComponentInChildren<Animator>(true);
            if(animator != null && animator.isHuman)
            {
                Transform humanoidHips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if(humanoidHips != null) return humanoidHips;
            }

            foreach(Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if(string.Equals(child.name, "Hips", StringComparison.OrdinalIgnoreCase)) return child;
            }
            return null;
        }

        internal static SkinnedMeshRenderer Convert(
            MeshRenderer source,
            Transform targetRootBone,
            bool persistentMesh,
            bool useUndo,
            AnimationIndex animationIndex = null,
            GameObject avatarRoot = null
        )
        {
            if(source == null) return null;
            MeshFilter filter = source.GetComponent<MeshFilter>();
            if(filter == null || filter.sharedMesh == null) return null;

            // Adding a SkinnedMeshRenderer can immediately destroy the MeshRenderer because
            // Unity only permits one Renderer on a GameObject. Cache everything first.
            GameObject go = source.gameObject;
            ObjectReference sourceReference = ObjectRegistry.GetReference(source);
            string animationPath = avatarRoot != null
                ? AnimationUtility.CalculateTransformPath(
                    source.transform,
                    avatarRoot.transform
                )
                : null;
            Material[] sharedMaterials = source.sharedMaterials;
            bool rendererEnabled = source.enabled;
            ShadowCastingMode shadowCastingMode = source.shadowCastingMode;
            bool receiveShadows = source.receiveShadows;
            LightProbeUsage lightProbeUsage = source.lightProbeUsage;
            ReflectionProbeUsage reflectionProbeUsage = source.reflectionProbeUsage;
            Transform probeAnchor = source.probeAnchor;
            MotionVectorGenerationMode motionVectorMode = source.motionVectorGenerationMode;
            bool allowOcclusion = source.allowOcclusionWhenDynamic;
            int sortingLayerId = source.sortingLayerID;
            int sortingOrder = source.sortingOrder;
            int lightmapIndex = source.lightmapIndex;
            Vector4 lightmapScaleOffset = source.lightmapScaleOffset;
            int realtimeLightmapIndex = source.realtimeLightmapIndex;
            Vector4 realtimeLightmapScaleOffset = source.realtimeLightmapScaleOffset;

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

            SkinnedMeshRenderer target = useUndo
                ? Undo.AddComponent<SkinnedMeshRenderer>(go)
                : go.AddComponent<SkinnedMeshRenderer>();
            target.sharedMesh = mesh;
            target.bones = new[] { go.transform };
            Transform rootBone = targetRootBone != null
                ? targetRootBone
                : go.transform;
            target.rootBone = rootBone;
            target.sharedMaterials = sharedMaterials;
            target.enabled = rendererEnabled;
            target.shadowCastingMode = shadowCastingMode;
            target.receiveShadows = receiveShadows;
            target.lightProbeUsage = lightProbeUsage;
            target.reflectionProbeUsage = reflectionProbeUsage;
            target.probeAnchor = probeAnchor;
            target.motionVectorGenerationMode = motionVectorMode;
            target.allowOcclusionWhenDynamic = allowOcclusion;
            target.sortingLayerID = sortingLayerId;
            target.sortingOrder = sortingOrder;
            target.lightmapIndex = lightmapIndex;
            target.lightmapScaleOffset = lightmapScaleOffset;
            target.realtimeLightmapIndex = realtimeLightmapIndex;
            target.realtimeLightmapScaleOffset = realtimeLightmapScaleOffset;
            target.localBounds = mesh.bounds.size == Vector3.zero
                ? default(Bounds)
                : TransformBounds(
                    mesh.bounds,
                    rootBone.worldToLocalMatrix * go.transform.localToWorldMatrix
                );

            ObjectRegistry.TryRegisterReplacedObject(sourceReference, target);
            if(animationIndex != null && animationPath != null)
            {
                RewriteRendererBindings(
                    animationIndex,
                    animationPath,
                    typeof(MeshRenderer),
                    typeof(SkinnedMeshRenderer)
                );
            }

            if(useUndo)
            {
                if(source != null) Undo.DestroyObjectImmediate(source);
                if(filter != null) Undo.DestroyObjectImmediate(filter);
            }
            else
            {
                if(source != null) Object.DestroyImmediate(source);
                if(filter != null) Object.DestroyImmediate(filter);
            }
            return target;
        }

        private static Bounds TransformBounds(
            Bounds source,
            Matrix4x4 transform
        )
        {
            Vector3 center = transform.MultiplyPoint3x4(source.center);
            Vector3 extents = source.extents;
            Vector3 axisX = transform.MultiplyVector(
                new Vector3(extents.x, 0.0f, 0.0f)
            );
            Vector3 axisY = transform.MultiplyVector(
                new Vector3(0.0f, extents.y, 0.0f)
            );
            Vector3 axisZ = transform.MultiplyVector(
                new Vector3(0.0f, 0.0f, extents.z)
            );
            Vector3 convertedExtents = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z)
            );
            return new Bounds(center, convertedExtents * 2.0f);
        }

        private static void RewriteRendererBindings(
            AnimationIndex animationIndex,
            string path,
            Type sourceType,
            Type targetType
        )
        {
            if(animationIndex == null ||
                string.IsNullOrEmpty(path) ||
                sourceType == null ||
                targetType == null)
            {
                return;
            }

            foreach(VirtualClip clip in
                animationIndex.GetClipsForObjectPath(path).ToList())
            {
                foreach(EditorCurveBinding binding in clip
                    .GetFloatCurveBindings()
                    .Where(binding =>
                        binding.type == sourceType &&
                        string.Equals(
                            binding.path,
                            path,
                            StringComparison.Ordinal
                        ))
                    .ToList())
                {
                    AnimationCurve curve = clip.GetFloatCurve(binding);
                    if(curve == null) continue;
                    EditorCurveBinding replacement = binding;
                    replacement.type = targetType;
                    clip.SetFloatCurve(binding, null);
                    clip.SetFloatCurve(replacement, curve);
                }

                foreach(EditorCurveBinding binding in clip
                    .GetObjectCurveBindings()
                    .Where(binding =>
                        binding.type == sourceType &&
                        string.Equals(
                            binding.path,
                            path,
                            StringComparison.Ordinal
                        ))
                    .ToList())
                {
                    ObjectReferenceKeyframe[] curve =
                        clip.GetObjectCurve(binding);
                    if(curve == null) continue;
                    EditorCurveBinding replacement = binding;
                    replacement.type = targetType;
                    clip.SetObjectCurve(binding, null);
                    clip.SetObjectCurve(replacement, curve);
                }
            }
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
