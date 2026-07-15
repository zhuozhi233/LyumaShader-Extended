#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace LyumaShader
{
    /// <summary>
    /// Finds materials associated with an avatar, including materials that are only referenced by
    /// animation material-swap curves or serialized components such as Modular Avatar reactions.
    /// The scanner intentionally uses serialized data for optional integrations so newer component
    /// types do not create an additional compile-time version requirement.
    /// </summary>
    internal static class Waifu2dAssociatedMaterialScanner
    {
        private const string MaterialArrayPropertyPrefix = "m_Materials.Array.data[";
        private const string AvatarRootReferencePath = "$$$AVATAR_ROOT$$$";

        internal sealed class Result
        {
            internal readonly HashSet<Material> AllMaterials = new HashSet<Material>();
            internal readonly HashSet<Material> RendererReferencedMaterials = new HashSet<Material>();
            internal readonly HashSet<Material> ComponentReferencedMaterials = new HashSet<Material>();
            internal readonly HashSet<Material> AnimationReferencedMaterials = new HashSet<Material>();
            internal readonly HashSet<Renderer> Renderers = new HashSet<Renderer>();
            internal readonly HashSet<RuntimeAnimatorController> Controllers =
                new HashSet<RuntimeAnimatorController>();
            internal readonly HashSet<AnimationClip> AnimationClips = new HashSet<AnimationClip>();
            internal readonly Dictionary<Renderer, HashSet<Material>> RendererMaterialCandidates =
                new Dictionary<Renderer, HashSet<Material>>();

            internal int SerializationFailureCount;

            internal int RendererCount
            {
                get { return Renderers.Count; }
            }

            internal int ControllerCount
            {
                get { return Controllers.Count; }
            }

            internal int AnimationClipCount
            {
                get { return AnimationClips.Count; }
            }

            internal ICollection<Material> GetCandidateMaterials(Renderer renderer)
            {
                HashSet<Material> materials;
                if(renderer != null && RendererMaterialCandidates.TryGetValue(renderer, out materials))
                {
                    return materials;
                }
                return Array.Empty<Material>();
            }
        }

        private sealed class Scanner
        {
            private readonly Result result = new Result();
            private readonly HashSet<Component> visitedComponents = new HashSet<Component>();
            private readonly Queue<ControllerContext> pendingControllers = new Queue<ControllerContext>();
            private readonly Queue<MotionContext> pendingMotions = new Queue<MotionContext>();
            private readonly Queue<ClipContext> pendingClips = new Queue<ClipContext>();
            private readonly HashSet<string> visitedControllerContexts = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> visitedMotionContexts = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> visitedClipContexts = new HashSet<string>(StringComparer.Ordinal);
            private readonly List<MaterialSwapUsage> materialSwaps = new List<MaterialSwapUsage>();

            internal Result Collect(IEnumerable<UnityEngine.Object> objects)
            {
                if(objects != null)
                {
                    foreach(UnityEngine.Object sourceObject in objects)
                    {
                        CollectSource(sourceObject);
                    }
                }

                ProcessControllerQueue();
                ProcessMotionQueue();
                ProcessClipQueue();
                ApplyMaterialSwaps();
                return result;
            }

            private void CollectSource(UnityEngine.Object sourceObject)
            {
                if(sourceObject == null) return;

                Material material = sourceObject as Material;
                if(material != null)
                {
                    AddMaterial(material, null);
                    return;
                }

                RuntimeAnimatorController controller = sourceObject as RuntimeAnimatorController;
                if(controller != null)
                {
                    QueueController(controller, null);
                    return;
                }

                AnimationClip clip = sourceObject as AnimationClip;
                if(clip != null)
                {
                    QueueClip(clip, null);
                    return;
                }

                Motion motion = sourceObject as Motion;
                if(motion != null)
                {
                    QueueMotion(motion, null);
                    return;
                }

                GameObject gameObject = sourceObject as GameObject;
                Component component = sourceObject as Component;
                if(gameObject == null && component != null) gameObject = component.gameObject;
                if(gameObject == null) return;

                CollectGameObject(gameObject);
            }

            private void CollectGameObject(GameObject scanRoot)
            {
                if(scanRoot == null) return;

                foreach(Renderer renderer in scanRoot.GetComponentsInChildren<Renderer>(true))
                {
                    AddRenderer(renderer);
                }

                foreach(Component component in scanRoot.GetComponentsInChildren<Component>(true))
                {
                    if(component == null || !visitedComponents.Add(component)) continue;
                    CollectComponent(component, scanRoot);
                }
            }

            private void CollectComponent(Component component, GameObject scanRoot)
            {
                // Renderer materials are collected directly, and Transforms cannot reference any
                // material/controller/motion assets relevant to this scan.
                if(component is Renderer || component is Transform) return;

                SerializedObject serializedObject;
                try
                {
                    serializedObject = new SerializedObject(component);
                }
                catch(Exception)
                {
                    result.SerializationFailureCount++;
                    return;
                }

                Transform bindingRoot = ResolveAnimationBindingRoot(component, serializedObject, scanRoot);
                try
                {
                    SerializedProperty iterator = serializedObject.GetIterator();
                    while(iterator.Next(true))
                    {
                        if(iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                        if(iterator.propertyPath == "m_Script") continue;

                        UnityEngine.Object reference = iterator.objectReferenceValue;
                        if(reference == null) continue;

                        Material material = reference as Material;
                        if(material != null)
                        {
                            if(!(component is Renderer)) AddMaterial(material, result.ComponentReferencedMaterials);
                            continue;
                        }

                        RuntimeAnimatorController controller = reference as RuntimeAnimatorController;
                        if(controller != null)
                        {
                            QueueController(controller, bindingRoot);
                            continue;
                        }

                        AnimationClip clip = reference as AnimationClip;
                        if(clip != null)
                        {
                            QueueClip(clip, bindingRoot);
                            continue;
                        }

                        Motion motion = reference as Motion;
                        if(motion != null)
                        {
                            QueueMotion(motion, bindingRoot);
                        }
                    }

                    CollectKnownMaterialRelationships(component, serializedObject, scanRoot);
                }
                catch(Exception)
                {
                    result.SerializationFailureCount++;
                }
            }

            private void CollectKnownMaterialRelationships(
                Component component,
                SerializedObject serializedObject,
                GameObject scanRoot
            )
            {
                string typeName = component.GetType().Name;
                if(string.Equals(typeName, "ModularAvatarMaterialSetter", StringComparison.Ordinal))
                {
                    CollectMaterialSetter(serializedObject, scanRoot);
                }
                else if(string.Equals(typeName, "ModularAvatarMaterialSwap", StringComparison.Ordinal))
                {
                    CollectMaterialSwap(serializedObject, scanRoot);
                }
            }

            private void CollectMaterialSetter(SerializedObject serializedObject, GameObject scanRoot)
            {
                SerializedProperty objects = serializedObject.FindProperty("m_objects");
                if(objects == null || !objects.isArray) return;

                for(int i = 0; i < objects.arraySize; i++)
                {
                    SerializedProperty entry = objects.GetArrayElementAtIndex(i);
                    SerializedProperty materialProperty = entry.FindPropertyRelative("Material");
                    Material material = materialProperty != null
                        ? materialProperty.objectReferenceValue as Material
                        : null;
                    if(material == null) continue;

                    AddMaterial(material, result.ComponentReferencedMaterials);
                    GameObject target = ResolveAvatarObjectReference(
                        entry.FindPropertyRelative("Object"),
                        scanRoot
                    );
                    Renderer renderer = target != null ? target.GetComponent<Renderer>() : null;
                    if(renderer != null) AddRendererCandidate(renderer, material);
                }
            }

            private void CollectMaterialSwap(SerializedObject serializedObject, GameObject scanRoot)
            {
                GameObject swapRoot = ResolveAvatarObjectReference(
                    serializedObject.FindProperty("m_root"),
                    scanRoot
                );
                SerializedProperty swaps = serializedObject.FindProperty("m_swaps");
                if(swaps == null || !swaps.isArray) return;

                for(int i = 0; i < swaps.arraySize; i++)
                {
                    SerializedProperty entry = swaps.GetArrayElementAtIndex(i);
                    SerializedProperty fromProperty = entry.FindPropertyRelative("From");
                    SerializedProperty toProperty = entry.FindPropertyRelative("To");
                    Material from = fromProperty != null ? fromProperty.objectReferenceValue as Material : null;
                    Material to = toProperty != null ? toProperty.objectReferenceValue as Material : null;

                    AddMaterial(from, result.ComponentReferencedMaterials);
                    AddMaterial(to, result.ComponentReferencedMaterials);
                    if(from != null && to != null)
                    {
                        materialSwaps.Add(new MaterialSwapUsage
                        {
                            Root = swapRoot,
                            From = from,
                            To = to
                        });
                    }
                }
            }

            private void ProcessControllerQueue()
            {
                while(pendingControllers.Count > 0)
                {
                    ControllerContext context = pendingControllers.Dequeue();
                    RuntimeAnimatorController controller = context.Controller;
                    if(controller == null) continue;
                    result.Controllers.Add(controller);

                    AnimationClip[] clips;
                    try
                    {
                        clips = controller.animationClips;
                    }
                    catch(Exception)
                    {
                        result.SerializationFailureCount++;
                        continue;
                    }

                    if(clips == null) continue;
                    foreach(AnimationClip clip in clips)
                    {
                        QueueClip(clip, context.BindingRoot);
                    }
                }
            }

            private void ProcessMotionQueue()
            {
                while(pendingMotions.Count > 0)
                {
                    MotionContext context = pendingMotions.Dequeue();
                    Motion motion = context.Motion;
                    if(motion == null) continue;

                    AnimationClip clip = motion as AnimationClip;
                    if(clip != null)
                    {
                        QueueClip(clip, context.BindingRoot);
                        continue;
                    }

                    BlendTree blendTree = motion as BlendTree;
                    if(blendTree == null) continue;
                    ChildMotion[] children = blendTree.children;
                    if(children == null) continue;
                    foreach(ChildMotion child in children)
                    {
                        QueueMotion(child.motion, context.BindingRoot);
                    }
                }
            }

            private void ProcessClipQueue()
            {
                while(pendingClips.Count > 0)
                {
                    ClipContext context = pendingClips.Dequeue();
                    AnimationClip clip = context.Clip;
                    if(clip == null) continue;
                    result.AnimationClips.Add(clip);

                    EditorCurveBinding[] bindings;
                    try
                    {
                        bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                    }
                    catch(Exception)
                    {
                        result.SerializationFailureCount++;
                        continue;
                    }

                    foreach(EditorCurveBinding binding in bindings)
                    {
                        if(binding.type == null || !typeof(Renderer).IsAssignableFrom(binding.type)) continue;
                        if(string.IsNullOrEmpty(binding.propertyName) ||
                            !binding.propertyName.StartsWith(MaterialArrayPropertyPrefix, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        Renderer renderer = ResolveRenderer(context.BindingRoot, binding);
                        ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                        if(keyframes == null) continue;
                        foreach(ObjectReferenceKeyframe keyframe in keyframes)
                        {
                            Material material = keyframe.value as Material;
                            if(material == null) continue;
                            AddMaterial(material, result.AnimationReferencedMaterials);
                            if(renderer != null) AddRendererCandidate(renderer, material);
                        }
                    }
                }
            }

            private void ApplyMaterialSwaps()
            {
                if(materialSwaps.Count == 0 || result.Renderers.Count == 0) return;

                int maximumPasses = materialSwaps.Count + 1;
                for(int pass = 0; pass < maximumPasses; pass++)
                {
                    bool changed = false;
                    foreach(MaterialSwapUsage swap in materialSwaps)
                    {
                        foreach(Renderer renderer in result.Renderers)
                        {
                            if(renderer == null) continue;
                            if(swap.Root != null && !renderer.transform.IsChildOf(swap.Root.transform)) continue;

                            HashSet<Material> candidates;
                            if(!result.RendererMaterialCandidates.TryGetValue(renderer, out candidates) ||
                                !candidates.Contains(swap.From))
                            {
                                continue;
                            }

                            if(candidates.Add(swap.To)) changed = true;
                        }
                    }
                    if(!changed) break;
                }
            }

            private void AddRenderer(Renderer renderer)
            {
                if(renderer == null) return;
                bool isNew = result.Renderers.Add(renderer);
                HashSet<Material> candidates;
                if(!result.RendererMaterialCandidates.TryGetValue(renderer, out candidates))
                {
                    candidates = new HashSet<Material>();
                    result.RendererMaterialCandidates.Add(renderer, candidates);
                }
                if(!isNew) return;

                foreach(Material material in renderer.sharedMaterials)
                {
                    if(material == null) continue;
                    candidates.Add(material);
                    AddMaterial(material, result.RendererReferencedMaterials);
                }
            }

            private void AddRendererCandidate(Renderer renderer, Material material)
            {
                if(renderer == null || material == null) return;
                AddRenderer(renderer);
                result.RendererMaterialCandidates[renderer].Add(material);
            }

            private void AddMaterial(Material material, HashSet<Material> sourceSet)
            {
                if(material == null) return;
                result.AllMaterials.Add(material);
                if(sourceSet != null) sourceSet.Add(material);
            }

            private void QueueController(RuntimeAnimatorController controller, Transform bindingRoot)
            {
                if(controller == null) return;
                string key = MakeContextKey(controller, bindingRoot);
                if(!visitedControllerContexts.Add(key)) return;
                pendingControllers.Enqueue(new ControllerContext
                {
                    Controller = controller,
                    BindingRoot = bindingRoot
                });
            }

            private void QueueMotion(Motion motion, Transform bindingRoot)
            {
                if(motion == null) return;
                string key = MakeContextKey(motion, bindingRoot);
                if(!visitedMotionContexts.Add(key)) return;
                pendingMotions.Enqueue(new MotionContext
                {
                    Motion = motion,
                    BindingRoot = bindingRoot
                });
            }

            private void QueueClip(AnimationClip clip, Transform bindingRoot)
            {
                if(clip == null) return;
                string key = MakeContextKey(clip, bindingRoot);
                if(!visitedClipContexts.Add(key)) return;
                pendingClips.Enqueue(new ClipContext
                {
                    Clip = clip,
                    BindingRoot = bindingRoot
                });
            }

            private static string MakeContextKey(UnityEngine.Object asset, Transform bindingRoot)
            {
                int rootId = bindingRoot != null ? bindingRoot.GetInstanceID() : 0;
                return asset.GetInstanceID() + ":" + rootId;
            }

            private static Transform ResolveAnimationBindingRoot(
                Component component,
                SerializedObject serializedObject,
                GameObject scanRoot
            )
            {
                Animator animator = component as Animator;
                if(animator != null) return animator.transform;
                if(scanRoot == null) return component.transform;

                string typeName = component.GetType().Name;
                if(string.Equals(typeName, "ModularAvatarMergeAnimator", StringComparison.Ordinal) ||
                    string.Equals(typeName, "ModularAvatarMergeBlendTree", StringComparison.Ordinal))
                {
                    SerializedProperty pathMode = serializedObject.FindProperty("pathMode") ??
                        serializedObject.FindProperty("PathMode");
                    if(pathMode != null && pathMode.intValue == 1) return scanRoot.transform;

                    SerializedProperty relativeRoot = serializedObject.FindProperty("relativePathRoot") ??
                        serializedObject.FindProperty("RelativePathRoot");
                    GameObject resolved = ResolveAvatarObjectReference(relativeRoot, scanRoot);
                    return resolved != null ? resolved.transform : component.transform;
                }

                if(string.Equals(typeName, "VRCAvatarDescriptor", StringComparison.Ordinal))
                {
                    return scanRoot.transform;
                }

                return scanRoot.transform;
            }

            private static GameObject ResolveAvatarObjectReference(
                SerializedProperty referenceProperty,
                GameObject scanRoot
            )
            {
                if(referenceProperty == null || scanRoot == null) return null;

                SerializedProperty targetProperty = referenceProperty.FindPropertyRelative("targetObject");
                GameObject target = targetProperty != null
                    ? targetProperty.objectReferenceValue as GameObject
                    : null;
                if(target != null && target.transform.IsChildOf(scanRoot.transform)) return target;

                SerializedProperty pathProperty = referenceProperty.FindPropertyRelative("referencePath");
                string path = pathProperty != null ? pathProperty.stringValue : null;
                if(string.Equals(path, AvatarRootReferencePath, StringComparison.Ordinal)) return scanRoot;
                if(string.IsNullOrEmpty(path)) return null;

                Transform resolved = scanRoot.transform.Find(path);
                return resolved != null ? resolved.gameObject : null;
            }

            private static Renderer ResolveRenderer(Transform bindingRoot, EditorCurveBinding binding)
            {
                if(bindingRoot == null) return null;
                Transform target = string.IsNullOrEmpty(binding.path)
                    ? bindingRoot
                    : bindingRoot.Find(binding.path);
                if(target == null) return null;

                Renderer renderer = target.GetComponent(binding.type) as Renderer;
                return renderer != null ? renderer : target.GetComponent<Renderer>();
            }
        }

        private struct ControllerContext
        {
            internal RuntimeAnimatorController Controller;
            internal Transform BindingRoot;
        }

        private struct MotionContext
        {
            internal Motion Motion;
            internal Transform BindingRoot;
        }

        private struct ClipContext
        {
            internal AnimationClip Clip;
            internal Transform BindingRoot;
        }

        private struct MaterialSwapUsage
        {
            internal GameObject Root;
            internal Material From;
            internal Material To;
        }

        internal static Result Collect(IEnumerable<UnityEngine.Object> objects)
        {
            return new Scanner().Collect(objects);
        }
    }
}
#endif
