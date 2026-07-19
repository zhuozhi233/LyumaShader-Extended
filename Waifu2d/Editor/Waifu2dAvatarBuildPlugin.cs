#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using nadena.dev.ndmf.fluent;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: ExportsPlugin(typeof(LyumaShader.Waifu2dAvatarBuildPlugin))]

namespace LyumaShader
{
    internal sealed class Waifu2dAvatarBuildPlugin : Plugin<Waifu2dAvatarBuildPlugin>
    {
        private const string ModularAvatarPlugin = "nadena.dev.modular-avatar";
        private const string ToggleParameterName = "zhz/Lyuma2D";
        private const string ToggleDisplayName =
            "<b><size=35><line-height=100%><voffset=3.8em>2D</b>";
        private const string ToggleIconPath =
            "Packages/com.zhuozhi.lyumashader-extended/Waifu2d/Resources/Waifu2dTransparent.png";
        private const string CustomLogicProperty = "_lyuma_custom_logic_2d";

        public override string QualifiedName =>
            "com.zhuozhi.lyumashader-extended.waifu2d-avatar";

        public override string DisplayName =>
            "LyumaShader Extended - Waifu2d Avatar";

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                .BeforePlugin(ModularAvatarPlugin)
                .WithRequiredExtension(typeof(AnimatorServicesContext), sequence =>
                {
                    sequence.Run("Prepare Waifu2d avatar configuration", PrepareAvatar);
                });

            InPhase(BuildPhase.Transforming)
                .AfterPlugin(ModularAvatarPlugin)
                .WithRequiredExtension(typeof(AnimatorServicesContext), sequence =>
                {
                    sequence.Run("Convert Waifu2d materials and animations", ConvertAvatar);
                });
        }

        private static void PrepareAvatar(BuildContext context)
        {
            BuildState state = context.GetState<BuildState>();
            state.Capture(context.AvatarRootObject);
            if(state.Configurations.Count == 0) return;
            AnimatorServicesContext animatorServices =
                context.Extension<AnimatorServicesContext>();

            foreach(ConfigurationSnapshot configuration in state.Configurations)
            {
                Transform hips = FindHips(context.AvatarRootObject);
                if(configuration.RepairRootBones && hips != null)
                {
                    RepairRootBones(configuration.Root, hips, state);
                }

                if(configuration.ConvertStaticMeshes)
                {
                    ConvertStaticMeshes(
                        context,
                        configuration,
                        state,
                        hips,
                        animatorServices.AnimationIndex,
                        context.AvatarRootObject
                    );
                }

                if(configuration.GenerateToggle)
                {
                    GenerateToggle(context, configuration, state);
                }
            }
        }

        private static void ConvertAvatar(BuildContext context)
        {
            BuildState state = context.GetState<BuildState>();
            if(state.Configurations.Count == 0) return;

            foreach(Renderer renderer in
                context.AvatarRootObject.GetComponentsInChildren<Renderer>(true))
            {
                if(renderer == null) continue;
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for(int index = 0; index < materials.Length; index++)
                {
                    Material replacement = GetOrCreateConvertedMaterial(
                        context,
                        state,
                        materials[index],
                        renderer
                    );
                    if(replacement == null || replacement == materials[index]) continue;
                    materials[index] = replacement;
                    changed = true;
                }
                if(changed) renderer.sharedMaterials = materials;
            }

            AnimatorServicesContext animatorServices =
                context.Extension<AnimatorServicesContext>();
            animatorServices.AnimationIndex.RewriteObjectCurves((binding, value) =>
            {
                Material material = value as Material;
                if(material == null) return value;

                Renderer renderer = null;
                if(binding.type != null && typeof(Renderer).IsAssignableFrom(binding.type))
                {
                    Transform target = string.IsNullOrEmpty(binding.path)
                        ? context.AvatarRootTransform
                        : context.AvatarRootTransform.Find(binding.path);
                    if(target != null)
                    {
                        renderer = target.GetComponent(binding.type) as Renderer;
                        if(renderer == null) renderer = target.GetComponent<Renderer>();
                    }
                }

                Material replacement = GetOrCreateConvertedMaterial(
                    context,
                    state,
                    material,
                    renderer
                );
                return replacement != null ? replacement : value;
            });

            foreach(ConfigurationSnapshot configuration in state.Configurations)
            {
                if(configuration.Component != null)
                {
                    Object.DestroyImmediate(configuration.Component);
                }
            }
        }

        private static Material GetOrCreateConvertedMaterial(
            BuildContext context,
            BuildState state,
            Material source,
            Renderer renderer
        )
        {
            if(source == null) return null;

            ConfigurationSnapshot configuration;
            RuleSnapshot rule;
            if(!state.TryFindRule(source, renderer, out configuration, out rule) ||
                rule == null ||
                !rule.Convert)
            {
                return source;
            }

            Vector3 motchiriContactOffset =
                GetMotchiriContactOffset(state, source, renderer);
            var cloneKey = new CloneKey(
                source,
                rule,
                motchiriContactOffset
            );
            CloneRecord existing;
            if(state.Clones.TryGetValue(cloneKey, out existing))
            {
                return existing.Material;
            }

            if(rule.Source != null &&
                rule.Source.isVariant &&
                !rule.FlattenMaterialVariant)
            {
                state.Clones[cloneKey] = new CloneRecord(source);
                return source;
            }

            Shader targetShader = ResolveTargetShader(source.shader, rule);
            if(targetShader == null)
            {
                Debug.LogWarning(
                    "Lyuma Waifu2d NDMF：无法为材质“" + source.name +
                    "”准备兼容着色器，已在本次构建中跳过。",
                    rule.Source
                );
                state.Clones[cloneKey] = new CloneRecord(source);
                return source;
            }

            int renderQueue = source.renderQueue;
            Material clone;
            if(source.isVariant || (rule.Source != null && rule.Source.isVariant))
            {
                clone = new Material(source.shader);
                clone.CopyPropertiesFromMaterial(source);
                clone.parent = null;
            }
            else
            {
                clone = new Material(source);
            }

            clone.name = source.name + " (Lyuma Waifu2d Build)";
            clone.shader = targetShader;
            InitializeMaterial(clone);
            clone.renderQueue = renderQueue;

            SetFloatIfPresent(clone, "_2d_coef", rule.TwoDimensionalness);
            SetFloatIfPresent(clone, "_facing_coef", rule.FacingDirection);
            SetFloatIfPresent(clone, "_lock2daxis_coef", rule.LockAxis);
            SetFloatIfPresent(clone, "_zcorrect_coef", rule.SquashZ);
            SetFloatIfPresent(
                clone,
                CustomLogicProperty,
                rule.KeepCustomLogicIn2D ? 1.0f : 0.0f
            );
            SetVectorIfPresent(
                clone,
                GenericLilCustomWaifu2dAdapter.MotchiriContactOffsetProperty,
                motchiriContactOffset
            );

            state.Clones[cloneKey] = new CloneRecord(clone);

            ObjectRegistry.TryRegisterReplacedObject(
                ObjectRegistry.GetReference(source),
                clone
            );
            context.AssetSaver.SaveAsset(clone);
            return clone;
        }

        private static Shader ResolveTargetShader(
            Shader source,
            RuleSnapshot rule
        )
        {
            if(source == null || rule == null) return null;
            if(IsWaifu2dShader(source)) return source;

            if(GenericLilCustomWaifu2dAdapter.IsSupported(source))
            {
                return rule.MergeCustomShader
                    ? GenericLilCustomWaifu2dAdapter.GetWaifu2dShader(source)
                    : null;
            }
            if(LilToonWaifu2dAdapter.IsSupported(source))
            {
                return LilToonWaifu2dAdapter.GetWaifu2dShader(source);
            }
            if(PoiyomiWaifu2dAdapter.IsSupported(source))
            {
                return PoiyomiWaifu2dAdapter.GetWaifu2dShader(source);
            }
            return null;
        }

        private static bool IsWaifu2dShader(Shader shader)
        {
            return LilToonWaifu2dAdapter.IsWaifu2dShader(shader) ||
                GenericLilCustomWaifu2dAdapter.IsWaifu2dShader(shader) ||
                PoiyomiWaifu2dAdapter.IsWaifu2dShader(shader);
        }

        private static void InitializeMaterial(Material material)
        {
            if(material == null || material.shader == null) return;
            if(LilToonWaifu2dAdapter.IsWaifu2dShader(material.shader))
            {
                LilToonWaifu2dAdapter.InitializeMaterial(material);
            }
            else if(GenericLilCustomWaifu2dAdapter.IsWaifu2dShader(material.shader))
            {
                GenericLilCustomWaifu2dAdapter.InitializeMaterial(material);
            }
            else if(PoiyomiWaifu2dAdapter.IsWaifu2dShader(material.shader))
            {
                PoiyomiWaifu2dAdapter.InitializeMaterial(material);
            }
        }

        private static void SetFloatIfPresent(
            Material material,
            string property,
            float value
        )
        {
            if(material != null && material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private static void SetVectorIfPresent(
            Material material,
            string property,
            Vector3 value
        )
        {
            if(material != null && material.HasProperty(property))
            {
                material.SetVector(
                    property,
                    new Vector4(value.x, value.y, value.z, 0.0f)
                );
            }
        }

        private static Vector3 GetMotchiriContactOffset(
            BuildState state,
            Material source,
            Renderer renderer
        )
        {
            if(state == null ||
                renderer == null ||
                !IsMotchiriMaterial(source))
            {
                return Vector3.zero;
            }

            Transform anchor;
            if(!state.MotchiriContactAnchors.TryGetValue(
                    renderer,
                    out anchor) ||
                anchor == null)
            {
                return Vector3.zero;
            }

            var skinned = renderer as SkinnedMeshRenderer;
            Transform coordinateRoot = skinned != null
                ? skinned.rootBone
                : null;
            if(coordinateRoot == null) coordinateRoot = renderer.transform;
            if(coordinateRoot == anchor) return Vector3.zero;
            return coordinateRoot.InverseTransformPoint(anchor.position);
        }

        private static bool IsMotchiriMaterial(Material material)
        {
            return material != null &&
                material.shader != null &&
                material.shader.name.IndexOf(
                    "motchiri",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0;
        }

        private static bool IsMotchiriRenderer(Renderer renderer)
        {
            if(renderer == null) return false;
            foreach(Material material in renderer.sharedMaterials)
            {
                if(IsMotchiriMaterial(material)) return true;
            }
            return false;
        }

        private static void ConvertStaticMeshes(
            BuildContext context,
            ConfigurationSnapshot configuration,
            BuildState state,
            Transform hips,
            AnimationIndex animationIndex,
            GameObject avatarRoot
        )
        {
            MeshRenderer[] renderers =
                configuration.Root.GetComponentsInChildren<MeshRenderer>(true);
            foreach(MeshRenderer renderer in renderers)
            {
                if(renderer == null ||
                    renderer.GetComponent<MeshFilter>() == null ||
                    !RendererUsesEnabledRule(renderer, state))
                {
                    continue;
                }

                SkinnedMeshRenderer converted =
                    Waifu2dStaticMeshConversion.Convert(
                        renderer,
                        hips,
                        false,
                        false,
                        animationIndex,
                        avatarRoot
                    );
                if(converted != null && converted.sharedMesh != null)
                {
                    context.AssetSaver.SaveAsset(converted.sharedMesh);
                }
            }
        }

        private static bool RendererUsesEnabledRule(
            Renderer renderer,
            BuildState state
        )
        {
            if(renderer == null) return false;
            foreach(Material material in renderer.sharedMaterials)
            {
                ConfigurationSnapshot configuration;
                RuleSnapshot rule;
                if(state.TryFindRule(
                        material,
                        renderer,
                        out configuration,
                        out rule
                    ) &&
                    rule != null &&
                    rule.Convert)
                {
                    return true;
                }
            }
            return false;
        }

        private static void GenerateToggle(
            BuildContext context,
            ConfigurationSnapshot configuration,
            BuildState state
        )
        {
            if(HasExistingToggle(configuration.Root)) return;

            List<Renderer> renderers = configuration.Root
                .GetComponentsInChildren<Renderer>(true)
                .Where(renderer => RendererUsesEnabledRule(renderer, state))
                .ToList();
            if(renderers.Count == 0) return;

            AnimationClip disabledClip = CreateStrengthClip(
                context.AvatarRootObject,
                renderers,
                renderer => 0.0f,
                renderer => RendererUsesMotchiriRule(renderer, state)
                    ? (float?)1.0f
                    : null
            );
            AnimationClip enabledClip = CreateStrengthClip(
                context.AvatarRootObject,
                renderers,
                renderer => GetRendererTwoDimensionalness(
                    renderer,
                    state,
                    configuration
                ),
                renderer => GetRendererMotchiriCustomLogicIn2D(renderer, state)
            );
            disabledClip.name = "Lyuma2D_关闭";
            enabledClip.name = "Lyuma2D_开启";

            var blendTree = new BlendTree
            {
                name = "Lyuma2D_BlendTree",
                blendType = BlendTreeType.Simple1D,
                blendParameter = ToggleParameterName,
                minThreshold = 0.0f,
                maxThreshold = 1.0f,
                useAutomaticThresholds = false
            };
            blendTree.children = new[]
            {
                new ChildMotion
                {
                    motion = disabledClip,
                    threshold = 0.0f,
                    timeScale = 1.0f,
                    directBlendParameter = "__ModularAvatarInternal/One"
                },
                new ChildMotion
                {
                    motion = enabledClip,
                    threshold = 1.0f,
                    timeScale = 1.0f,
                    directBlendParameter = "__ModularAvatarInternal/One"
                }
            };

            context.AssetSaver.SaveAsset(disabledClip);
            context.AssetSaver.SaveAsset(enabledClip);
            context.AssetSaver.SaveAsset(blendTree);

            bool installAtRoot;
            ModularAvatarMenuInstaller selectedInstaller;
            GameObject toggleParent = ResolveToggleMenuParent(
                configuration,
                out installAtRoot,
                out selectedInstaller
            );
            string toggleDisplayName =
                string.IsNullOrWhiteSpace(configuration.ToggleMenuName)
                    ? ToggleDisplayName
                    : configuration.ToggleMenuName;
            var toggleObject = new GameObject(toggleDisplayName);
            toggleObject.transform.SetParent(toggleParent.transform, false);

            ModularAvatarMenuItem menuItem =
                toggleObject.AddComponent<ModularAvatarMenuItem>();
            menuItem.label = toggleDisplayName;
            menuItem.PortableControl.Type = PortableControlType.Toggle;
            menuItem.PortableControl.Parameter = ToggleParameterName;
            menuItem.PortableControl.Value = 1.0f;
            menuItem.PortableControl.Icon =
                configuration.ToggleMenuIcon != null
                    ? configuration.ToggleMenuIcon
                    : AssetDatabase.LoadAssetAtPath<Texture2D>(ToggleIconPath);
            menuItem.isSynced = true;
            menuItem.isSaved = true;
            menuItem.isDefault = false;
            menuItem.automaticValue = true;

            if(installAtRoot)
            {
                ModularAvatarMenuInstaller installer =
                    toggleObject.AddComponent<
                        ModularAvatarMenuInstaller
                    >();
                if(selectedInstaller != null)
                {
                    installer.installTargetMenu =
                        selectedInstaller.installTargetMenu;
                }
            }
            ModularAvatarMergeBlendTree mergeBlendTree =
                toggleObject.AddComponent<ModularAvatarMergeBlendTree>();
            mergeBlendTree.Motion = blendTree;
            mergeBlendTree.PathMode = MergeAnimatorPathMode.Absolute;
        }

        private static GameObject ResolveToggleMenuParent(
            ConfigurationSnapshot configuration,
            out bool installAtRoot,
            out ModularAvatarMenuInstaller selectedInstaller
        )
        {
            installAtRoot = true;
            selectedInstaller = null;
            GameObject logicalParent = configuration.ToggleMenuParent;
            if(logicalParent == null ||
                logicalParent == configuration.Root ||
                !logicalParent.transform.IsChildOf(
                    configuration.Root.transform
                ))
            {
                return configuration.Root;
            }

            ModularAvatarMenuItem menuItem =
                logicalParent.GetComponent<ModularAvatarMenuItem>();
            if(menuItem != null &&
                menuItem.PortableControl != null &&
                menuItem.PortableControl.Type == PortableControlType.SubMenu &&
                menuItem.MenuSource == SubmenuSource.Children)
            {
                GameObject container =
                    menuItem.menuSource_otherObjectChildren != null
                        ? menuItem.menuSource_otherObjectChildren
                        : logicalParent;
                if(container != null &&
                    (container == configuration.Root ||
                        container.transform.IsChildOf(
                            configuration.Root.transform
                        )))
                {
                    installAtRoot = false;
                    return container;
                }
            }

            ModularAvatarMenuGroup menuGroup =
                logicalParent.GetComponent<ModularAvatarMenuGroup>();
            if(menuGroup != null)
            {
                GameObject container = menuGroup.targetObject != null
                    ? menuGroup.targetObject
                    : logicalParent;
                if(container != null &&
                    (container == configuration.Root ||
                        container.transform.IsChildOf(
                            configuration.Root.transform
                        )))
                {
                    installAtRoot = false;
                    return container;
                }
            }

            ModularAvatarMenuInstaller menuInstaller =
                logicalParent.GetComponent<
                    ModularAvatarMenuInstaller
                >();
            if(menuInstaller != null)
            {
                selectedInstaller = menuInstaller;
                return logicalParent;
            }

            return configuration.Root;
        }

        private static bool HasExistingToggle(GameObject root)
        {
            foreach(ModularAvatarMenuItem item in
                root.GetComponentsInChildren<ModularAvatarMenuItem>(true))
            {
                if(item != null &&
                    item.PortableControl != null &&
                    string.Equals(
                        item.PortableControl.Parameter,
                        ToggleParameterName,
                        StringComparison.Ordinal
                    ))
                {
                    return true;
                }
            }
            return false;
        }

        private static AnimationClip CreateStrengthClip(
            GameObject avatarRoot,
            IEnumerable<Renderer> renderers,
            Func<Renderer, float> valueProvider,
            Func<Renderer, float?> customLogicProvider
        )
        {
            var clip = new AnimationClip { frameRate = 60.0f };
            var bindings = new HashSet<string>(StringComparer.Ordinal);
            foreach(Renderer renderer in renderers)
            {
                if(renderer == null) continue;
                string path = AnimationUtility.CalculateTransformPath(
                    renderer.transform,
                    avatarRoot.transform
                );
                Type rendererType = renderer.GetType();
                string key = path + "\n" + rendererType.FullName;
                if(!bindings.Add(key)) continue;

                EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                    path,
                    rendererType,
                    "material._2d_coef"
                );
                AnimationUtility.SetEditorCurve(
                    clip,
                    binding,
                    AnimationCurve.Constant(
                        0.0f,
                        1.0f / 60.0f,
                        Mathf.Clamp01(valueProvider(renderer))
                    )
                );

                float? customLogic = customLogicProvider != null
                    ? customLogicProvider(renderer)
                    : null;
                if(customLogic.HasValue)
                {
                    EditorCurveBinding customLogicBinding =
                        EditorCurveBinding.FloatCurve(
                            path,
                            rendererType,
                            "material." + CustomLogicProperty
                        );
                    AnimationUtility.SetEditorCurve(
                        clip,
                        customLogicBinding,
                        AnimationCurve.Constant(
                            0.0f,
                            1.0f / 60.0f,
                            Mathf.Clamp01(customLogic.Value)
                        )
                    );
                }
            }
            return clip;
        }

        private static bool RendererUsesMotchiriRule(
            Renderer renderer,
            BuildState state
        )
        {
            return GetRendererMotchiriCustomLogicIn2D(renderer, state).HasValue;
        }

        private static float? GetRendererMotchiriCustomLogicIn2D(
            Renderer renderer,
            BuildState state
        )
        {
            if(renderer == null || state == null) return null;

            bool found = false;
            float value = 1.0f;
            foreach(Material material in renderer.sharedMaterials)
            {
                if(material == null ||
                    material.shader == null ||
                    material.shader.name.IndexOf(
                        "motchiri",
                        StringComparison.OrdinalIgnoreCase
                    ) < 0)
                {
                    continue;
                }

                ConfigurationSnapshot configuration;
                RuleSnapshot rule;
                if(!state.TryFindRule(
                        material,
                        renderer,
                        out configuration,
                        out rule
                    ) ||
                    rule == null ||
                    !rule.Convert)
                {
                    continue;
                }

                float ruleValue = rule.KeepCustomLogicIn2D ? 1.0f : 0.0f;
                value = found ? Mathf.Min(value, ruleValue) : ruleValue;
                found = true;
            }
            return found ? (float?)value : null;
        }

        private static float GetRendererTwoDimensionalness(
            Renderer renderer,
            BuildState state,
            ConfigurationSnapshot fallback
        )
        {
            float value = fallback.TwoDimensionalness;
            bool found = false;
            foreach(Material material in renderer.sharedMaterials)
            {
                ConfigurationSnapshot configuration;
                RuleSnapshot rule;
                if(!state.TryFindRule(
                        material,
                        renderer,
                        out configuration,
                        out rule
                    ) ||
                    rule == null ||
                    !rule.Convert)
                {
                    continue;
                }

                value = found
                    ? Mathf.Min(value, rule.TwoDimensionalness)
                    : rule.TwoDimensionalness;
                found = true;
            }
            return value;
        }

        private static Transform FindHips(GameObject avatarRoot)
        {
            if(avatarRoot == null) return null;
            Animator animator = avatarRoot.GetComponent<Animator>() ??
                avatarRoot.GetComponentInChildren<Animator>(true);
            if(animator == null || !animator.isHuman) return null;
            return animator.GetBoneTransform(HumanBodyBones.Hips);
        }

        private static void RepairRootBones(
            GameObject root,
            Transform hips,
            BuildState state
        )
        {
            if(root == null || hips == null) return;
            foreach(SkinnedMeshRenderer renderer in
                root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if(renderer == null || renderer.rootBone == hips) continue;
                Transform previousRoot = renderer.rootBone != null
                    ? renderer.rootBone
                    : renderer.transform;
                renderer.localBounds = TransformBounds(
                    renderer.localBounds,
                    hips.worldToLocalMatrix * previousRoot.localToWorldMatrix
                );
                renderer.rootBone = hips;
            }

            foreach(ModularAvatarMeshSettings settings in
                root.GetComponentsInChildren<ModularAvatarMeshSettings>(true))
            {
                if(settings == null) continue;
                GameObject previous = settings.RootBone != null
                    ? settings.RootBone.Get(settings)
                    : null;
                Renderer settingsRenderer = settings.GetComponent<Renderer>();
                if(state != null &&
                    previous != null &&
                    IsMotchiriRenderer(settingsRenderer))
                {
                    state.MotchiriContactAnchors[settingsRenderer] =
                        previous.transform;
                }
                if(previous != null &&
                    previous.transform != hips &&
                    settings.InheritBounds !=
                        ModularAvatarMeshSettings.InheritMode.Inherit)
                {
                    settings.Bounds = TransformBounds(
                        settings.Bounds,
                        hips.worldToLocalMatrix *
                            previous.transform.localToWorldMatrix
                    );
                }

                var reference = new AvatarObjectReference();
                reference.Set(hips.gameObject);
                settings.RootBone = reference;
            }
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

        private sealed class BuildState
        {
            internal readonly List<ConfigurationSnapshot> Configurations =
                new List<ConfigurationSnapshot>();
            internal readonly Dictionary<CloneKey, CloneRecord> Clones =
                new Dictionary<CloneKey, CloneRecord>();
            internal readonly Dictionary<Renderer, Transform>
                MotchiriContactAnchors =
                    new Dictionary<Renderer, Transform>();

            internal void Capture(GameObject avatarRoot)
            {
                if(Configurations.Count != 0 || avatarRoot == null) return;
                foreach(LyumaWaifu2dAvatarConfig component in
                    avatarRoot.GetComponentsInChildren<LyumaWaifu2dAvatarConfig>(true))
                {
                    if(component == null) continue;
                    Configurations.Add(new ConfigurationSnapshot(component));
                }
            }

            internal bool TryFindRule(
                Material material,
                Renderer renderer,
                out ConfigurationSnapshot configuration,
                out RuleSnapshot rule
            )
            {
                configuration = null;
                rule = null;
                if(material == null) return false;

                Material original = null;
                var reference = ObjectRegistry.GetReference(material);
                if(reference != null) original = reference.Object as Material;

                foreach(ConfigurationSnapshot candidate in Configurations)
                {
                    if(renderer != null &&
                        renderer.transform != candidate.Root.transform &&
                        !renderer.transform.IsChildOf(candidate.Root.transform))
                    {
                        continue;
                    }

                    if(candidate.TryFindRule(material, out rule) ||
                        (original != null &&
                            candidate.TryFindRule(original, out rule)))
                    {
                        configuration = candidate;
                        return true;
                    }
                }
                return false;
            }
        }

        private sealed class ConfigurationSnapshot
        {
            internal readonly LyumaWaifu2dAvatarConfig Component;
            internal readonly GameObject Root;
            internal readonly List<RuleSnapshot> Rules =
                new List<RuleSnapshot>();
            internal readonly Dictionary<Material, RuleSnapshot> RuleMap =
                new Dictionary<Material, RuleSnapshot>();
            internal readonly float TwoDimensionalness;
            internal readonly float FacingDirection;
            internal readonly float LockAxis;
            internal readonly float SquashZ;
            internal readonly bool GenerateToggle;
            internal readonly string ToggleMenuName;
            internal readonly Texture2D ToggleMenuIcon;
            internal readonly GameObject ToggleMenuParent;
            internal readonly bool RepairRootBones;
            internal readonly bool ConvertStaticMeshes;

            internal ConfigurationSnapshot(
                LyumaWaifu2dAvatarConfig component
            )
            {
                Component = component;
                Root = component.gameObject;
                TwoDimensionalness = component.TwoDimensionalness;
                FacingDirection = component.FacingDirection;
                LockAxis = component.LockAxis;
                SquashZ = component.SquashZ;
                GenerateToggle = component.GenerateToggle;
                ToggleMenuName = component.ToggleMenuName;
                ToggleMenuIcon = component.ToggleMenuIcon;
                ToggleMenuParent = component.ToggleMenuParent;
                RepairRootBones = component.RepairRootBones;
                ConvertStaticMeshes = component.ConvertStaticMeshes;

                if(component.Materials == null) return;
                foreach(LyumaWaifu2dAvatarConfig.MaterialRule source in
                    component.Materials)
                {
                    if(source == null || source.Material == null) continue;
                    var rule = new RuleSnapshot(source, this);
                    Rules.Add(rule);
                    if(!RuleMap.ContainsKey(rule.Source))
                    {
                        RuleMap.Add(rule.Source, rule);
                    }
                }
            }

            internal bool TryFindRule(
                Material material,
                out RuleSnapshot rule
            )
            {
                if(RuleMap.TryGetValue(material, out rule)) return true;
                var visited = new HashSet<Material>();
                Material current = material;
                while(current != null &&
                    current.parent != null &&
                    visited.Add(current))
                {
                    current = current.parent;
                    if(current != null && RuleMap.TryGetValue(current, out rule))
                    {
                        return true;
                    }
                }
                rule = null;
                return false;
            }
        }

        private sealed class RuleSnapshot
        {
            internal readonly Material Source;
            internal readonly bool Convert;
            internal readonly bool MergeCustomShader;
            internal readonly bool KeepCustomLogicIn2D;
            internal readonly bool FlattenMaterialVariant;
            internal readonly float TwoDimensionalness;
            internal readonly float FacingDirection;
            internal readonly float LockAxis;
            internal readonly float SquashZ;

            internal RuleSnapshot(
                LyumaWaifu2dAvatarConfig.MaterialRule source,
                ConfigurationSnapshot configuration
            )
            {
                Source = source.Material;
                Convert = source.Convert;
                MergeCustomShader = source.MergeCustomShader;
                KeepCustomLogicIn2D = source.EnableCustomLogicIn2D;
                FlattenMaterialVariant = source.FlattenMaterialVariant;
                TwoDimensionalness = source.OverrideParameters
                    ? source.TwoDimensionalness
                    : configuration.TwoDimensionalness;
                FacingDirection = source.OverrideParameters
                    ? source.FacingDirection
                    : configuration.FacingDirection;
                LockAxis = source.OverrideParameters
                    ? source.LockAxis
                    : configuration.LockAxis;
                SquashZ = source.OverrideParameters
                    ? source.SquashZ
                    : configuration.SquashZ;
            }
        }

        private sealed class CloneRecord
        {
            internal readonly Material Material;

            internal CloneRecord(Material material)
            {
                Material = material;
            }
        }

        private readonly struct CloneKey : IEquatable<CloneKey>
        {
            private readonly Material material;
            private readonly RuleSnapshot rule;
            private readonly Vector3 motchiriContactOffset;

            internal CloneKey(
                Material material,
                RuleSnapshot rule,
                Vector3 motchiriContactOffset
            )
            {
                this.material = material;
                this.rule = rule;
                this.motchiriContactOffset = motchiriContactOffset;
            }

            public bool Equals(CloneKey other)
            {
                return material == other.material &&
                    ReferenceEquals(rule, other.rule) &&
                    motchiriContactOffset.Equals(
                        other.motchiriContactOffset
                    );
            }

            public override bool Equals(object obj)
            {
                return obj is CloneKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = ((material != null
                        ? material.GetInstanceID()
                        : 0) * 397) ^
                        (rule != null ? rule.GetHashCode() : 0);
                    return (hash * 397) ^
                        motchiriContactOffset.GetHashCode();
                }
            }
        }
    }
}
#endif
