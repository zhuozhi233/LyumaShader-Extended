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
using VRC.SDK3.Avatars.ScriptableObjects;
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
                    RepairRootBones(context, configuration.Root, hips, state);
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

                if(configuration.CustomMenuItems.Count > 0)
                {
                    GenerateCustomMenus(context, configuration, state);
                }
            }
        }

        private static void ConvertAvatar(BuildContext context)
        {
            BuildState state = context.GetState<BuildState>();
            if(state.Configurations.Count == 0) return;
            AnimatorServicesContext animatorServices =
                context.Extension<AnimatorServicesContext>();
            state.CaptureMaterialUsage(
                context.AvatarRootObject,
                animatorServices.AnimationIndex
            );

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

            animatorServices.AnimationIndex.RewriteObjectCurves((binding, value) =>
            {
                Material material = value as Material;
                if(material == null) return value;

                Renderer renderer = ResolveRenderer(
                    context.AvatarRootTransform,
                    binding
                );

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

            if(configuration.ProtectParticleMaterials &&
                renderer is ParticleSystemRenderer)
            {
                return state.IsUsedByNonParticleRenderer(source)
                    ? GetOrCreateParticleMaterialClone(
                        context,
                        state,
                        source
                    )
                    : source;
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

            Shader conversionSourceShader =
                PoiyomiWaifu2dAdapter.GetUnlockedSourceShader(source);
            Shader targetShader = ResolveTargetShader(
                conversionSourceShader,
                rule
            );
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

            AaoShaderInformationBridge.RegisterOfficialLilToonShader(
                targetShader
            );

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

            SetFloatIfPresent(
                clone,
                "_2d_coef",
                configuration.PreviewIn2D
                    ? rule.TwoDimensionalness
                    : 0.0f
            );
            SetFloatIfPresent(clone, "_facing_coef", rule.FacingDirection);
            SetFloatIfPresent(clone, "_lock2daxis_coef", rule.LockAxis);
            SetFloatIfPresent(clone, "_zcorrect_coef", rule.SquashZ);
            SetFloatIfPresent(
                clone,
                "_lyuma_camera_parallel_2d",
                configuration.CameraParallel2D ? 1.0f : 0.0f
            );
            SetFloatIfPresent(
                clone,
                "_lyuma_outline_2d",
                rule.OutlineIn2D ? 1.0f : 0.0f
            );
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

        private static Material GetOrCreateParticleMaterialClone(
            BuildContext context,
            BuildState state,
            Material source
        )
        {
            Material existing;
            if(state.ParticleClones.TryGetValue(source, out existing))
            {
                return existing;
            }

            int renderQueue = source.renderQueue;
            Material clone;
            if(source.isVariant)
            {
                clone = new Material(source.shader);
                clone.CopyPropertiesFromMaterial(source);
                clone.parent = null;
            }
            else
            {
                clone = new Material(source);
            }

            Shader unlockedSourceShader =
                PoiyomiWaifu2dAdapter.GetUnlockedSourceShader(source);
            Shader originalShader = ResolveOriginalShader(unlockedSourceShader);
            if(originalShader == null &&
                unlockedSourceShader != null &&
                unlockedSourceShader != source.shader)
            {
                originalShader = unlockedSourceShader;
            }
            if(originalShader != null) clone.shader = originalShader;
            clone.name = source.name + " (Lyuma Particle Build)";
            clone.renderQueue = renderQueue;
            state.ParticleClones[source] = clone;
            context.AssetSaver.SaveAsset(clone);
            return clone;
        }

        private static Shader ResolveTargetShader(
            Shader source,
            RuleSnapshot rule
        )
        {
            if(source == null || rule == null) return null;
            if(LilToonWaifu2dAdapter.IsWaifu2dShader(source))
            {
                return source;
            }
            if(GenericLilCustomWaifu2dAdapter.IsWaifu2dShader(source))
            {
                return GenericLilCustomWaifu2dAdapter.GetWaifu2dShader(
                    source
                );
            }
            if(PoiyomiWaifu2dAdapter.IsWaifu2dShader(source))
            {
                return PoiyomiWaifu2dAdapter.GetWaifu2dShader(source);
            }

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

        private static Shader ResolveOriginalShader(Shader shader)
        {
            if(shader == null) return null;
            Shader original = LilToonWaifu2dAdapter.GetOriginalShader(shader);
            if(original != null) return original;
            original =
                GenericLilCustomWaifu2dAdapter.GetOriginalShader(shader);
            return original != null
                ? original
                : PoiyomiWaifu2dAdapter.GetOriginalShader(shader);
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

        private static void GenerateCustomMenus(
            BuildContext context,
            ConfigurationSnapshot configuration,
            BuildState state
        )
        {
            List<Renderer> renderers = configuration.Root
                .GetComponentsInChildren<Renderer>(true)
                .Where(renderer =>
                    !(configuration.ProtectParticleMaterials &&
                        renderer is ParticleSystemRenderer) &&
                    RendererUsesEnabledRule(renderer, state))
                .ToList();
            if(renderers.Count == 0) return;

            var menuItems = new List<MenuItemSnapshot>();
            var parameters = new HashSet<string>(StringComparer.Ordinal);
            foreach(MenuItemSnapshot item in configuration.CustomMenuItems)
            {
                if(item == null ||
                    !item.Enabled ||
                    string.IsNullOrWhiteSpace(item.ParameterName) ||
                    !parameters.Add(item.ParameterName))
                {
                    continue;
                }
                menuItems.Add(item);
            }
            if(menuItems.Count == 0) return;

            AnimationClip baselineClip = CreateCustomMenuClip(
                context.AvatarRootObject,
                renderers,
                configuration,
                state,
                null
            );
            baselineClip.name = "Lyuma2D_菜单基础值";
            context.AssetSaver.SaveAsset(baselineClip);

            Motion selectedMotion = baselineClip;

            for(int index = 0; index < menuItems.Count; index++)
            {
                MenuItemSnapshot item = menuItems[index];
                AnimationClip itemClip = CreateCustomMenuClip(
                    context.AvatarRootObject,
                    renderers,
                    configuration,
                    state,
                    item
                );
                itemClip.name = "Lyuma2D_菜单_" + (index + 1);
                context.AssetSaver.SaveAsset(itemClip);

                var priorityLayer = new BlendTree
                {
                    name = "Lyuma2D_优先级_" + (index + 1),
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = item.ParameterName,
                    blendParameterY = item.ParameterName,
                    minThreshold = 0.0f,
                    maxThreshold = 1.0f,
                    useAutomaticThresholds = false,
                    children = new[]
                    {
                        new ChildMotion
                        {
                            motion = selectedMotion,
                            threshold = 0.0f,
                            timeScale = 1.0f
                        },
                        new ChildMotion
                        {
                            motion = itemClip,
                            threshold = 1.0f,
                            timeScale = 1.0f
                        }
                    }
                };
                context.AssetSaver.SaveAsset(priorityLayer);
                selectedMotion = priorityLayer;
            }

            for(int index = 0; index < menuItems.Count; index++)
            {
                CreateCustomMenuItem(
                    configuration,
                    menuItems[index],
                    index
                );
            }

            var controllerObject = new GameObject("Lyuma2D 自定义菜单控制");
            controllerObject.transform.SetParent(
                configuration.Root.transform,
                false
            );
            ModularAvatarMergeBlendTree mergeBlendTree =
                controllerObject.AddComponent<ModularAvatarMergeBlendTree>();
            mergeBlendTree.Motion = selectedMotion;
            mergeBlendTree.PathMode = MergeAnimatorPathMode.Absolute;
        }

        private static string GetCustomMenuFallbackDisplayName(
            string parameterName,
            int fallbackIndex
        )
        {
            int parameterIndex = GetCustomMenuParameterIndex(parameterName);
            if(parameterName == ToggleParameterName || parameterIndex == 1)
                return ToggleDisplayName;
            return "2D 菜单 " +
                (parameterIndex > 1 ? parameterIndex : fallbackIndex + 1);
        }

        private static int GetCustomMenuParameterIndex(string parameterName)
        {
            if(string.IsNullOrWhiteSpace(parameterName)) return 0;
            string prefix = ToggleParameterName + "/";
            if(!parameterName.StartsWith(prefix, StringComparison.Ordinal))
                return 0;
            int value;
            return int.TryParse(
                    parameterName.Substring(prefix.Length),
                    out value
                ) && value > 0
                    ? value
                    : 0;
        }

        private static void CreateCustomMenuItem(
            ConfigurationSnapshot configuration,
            MenuItemSnapshot settings,
            int index
        )
        {
            bool installAtRoot;
            ModularAvatarMenuInstaller selectedInstaller;
            ModularAvatarMenuItem selectedMenuItem;
            VRCExpressionsMenu selectedTargetMenu;
            GameObject toggleParent = ResolveToggleMenuParent(
                configuration.Root,
                settings,
                out installAtRoot,
                out selectedInstaller,
                out selectedMenuItem,
                out selectedTargetMenu
            );
            string toggleDisplayName =
                string.IsNullOrWhiteSpace(settings.MenuName)
                    ? GetCustomMenuFallbackDisplayName(
                        settings.ParameterName,
                        index
                    )
                    : settings.MenuName;
            GameObject toggleObject;
            ModularAvatarMenuItem menuItem;
            if(selectedMenuItem != null)
            {
                toggleObject = selectedMenuItem.gameObject;
                menuItem = selectedMenuItem;
            }
            else
            {
                toggleObject = new GameObject(toggleDisplayName);
                toggleObject.transform.SetParent(
                    toggleParent.transform,
                    false
                );
                menuItem =
                    toggleObject.AddComponent<ModularAvatarMenuItem>();
            }
            menuItem.PortableControl.Parameter = settings.ParameterName;
            menuItem.PortableControl.Value = 1.0f;
            menuItem.automaticValue = true;
            if(selectedMenuItem == null ||
                settings.OverrideDirectMenuItemSettings)
            {
                menuItem.label = toggleDisplayName;
                menuItem.PortableControl.Type = PortableControlType.Toggle;
                menuItem.PortableControl.Icon =
                    settings.MenuIcon != null
                        ? settings.MenuIcon
                        : AssetDatabase.LoadAssetAtPath<Texture2D>(
                            ToggleIconPath
                        );
                menuItem.isSynced = settings.Synced;
                menuItem.isSaved = settings.Saved;
                menuItem.isDefault = settings.DefaultEnabled;
            }

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
                else if(selectedTargetMenu != null)
                {
                    installer.installTargetMenu = selectedTargetMenu;
                }
            }
        }

        private static GameObject ResolveToggleMenuParent(
            GameObject root,
            MenuItemSnapshot settings,
            out bool installAtRoot,
            out ModularAvatarMenuInstaller selectedInstaller,
            out ModularAvatarMenuItem selectedMenuItem,
            out VRCExpressionsMenu selectedTargetMenu
        )
        {
            installAtRoot = true;
            selectedInstaller = null;
            selectedMenuItem = null;
            selectedTargetMenu = null;
            GameObject logicalParent = settings.MenuParent;
            if(logicalParent == null ||
                logicalParent == root ||
                !logicalParent.transform.IsChildOf(
                    root.transform
                ))
            {
                return root;
            }

            ModularAvatarMenuItem menuItem =
                logicalParent.GetComponent<ModularAvatarMenuItem>();
            if(menuItem != null)
            {
                if(menuItem.PortableControl != null &&
                    menuItem.PortableControl.Type ==
                        PortableControlType.SubMenu)
                {
                    if(menuItem.MenuSource == SubmenuSource.MenuAsset)
                    {
                        selectedTargetMenu =
                            menuItem.PortableControl.VRChatSubMenu as
                                VRCExpressionsMenu;
                        if(selectedTargetMenu != null)
                        {
                            return logicalParent;
                        }
                        menuItem.MenuSource = SubmenuSource.Children;
                        menuItem.menuSource_otherObjectChildren =
                            logicalParent;
                    }

                    GameObject container =
                        menuItem.menuSource_otherObjectChildren != null
                            ? menuItem.menuSource_otherObjectChildren
                            : logicalParent;
                    if(container != null &&
                        (container == root ||
                            container.transform.IsChildOf(
                                root.transform
                            )))
                    {
                        installAtRoot = false;
                        return container;
                    }
                }

                selectedMenuItem = menuItem;
                ModularAvatarMenuInstaller existingInstaller =
                    logicalParent.GetComponent<
                        ModularAvatarMenuInstaller
                    >();
                if(existingInstaller != null ||
                    IsIncludedByMenuSource(
                        root,
                        logicalParent
                    ))
                {
                    installAtRoot = false;
                }
                return logicalParent;
            }

            ModularAvatarMenuGroup menuGroup =
                logicalParent.GetComponent<ModularAvatarMenuGroup>();
            if(menuGroup != null)
            {
                GameObject container = menuGroup.targetObject != null
                    ? menuGroup.targetObject
                    : logicalParent;
                if(container != null &&
                    (container == root ||
                        container.transform.IsChildOf(
                            root.transform
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

            return root;
        }

        private static bool IsIncludedByMenuSource(
            GameObject root,
            GameObject menuItemObject
        )
        {
            if(root == null ||
                menuItemObject == null ||
                menuItemObject.transform.parent == null)
            {
                return false;
            }

            Transform parent = menuItemObject.transform.parent;
            foreach(ModularAvatarMenuItem item in
                root.GetComponentsInChildren<ModularAvatarMenuItem>(true))
            {
                if(item == null ||
                    item == menuItemObject.GetComponent<
                        ModularAvatarMenuItem
                    >() ||
                    item.PortableControl == null ||
                    item.PortableControl.Type != PortableControlType.SubMenu ||
                    item.MenuSource != SubmenuSource.Children)
                {
                    continue;
                }

                GameObject container =
                    item.menuSource_otherObjectChildren != null
                        ? item.menuSource_otherObjectChildren
                        : item.gameObject;
                if(container != null && container.transform == parent)
                {
                    return true;
                }
            }

            foreach(ModularAvatarMenuGroup group in
                root.GetComponentsInChildren<ModularAvatarMenuGroup>(true))
            {
                if(group == null) continue;
                GameObject container = group.targetObject != null
                    ? group.targetObject
                    : group.gameObject;
                if(container != null && container.transform == parent)
                {
                    return true;
                }
            }
            return false;
        }

        private static AnimationClip CreateCustomMenuClip(
            GameObject avatarRoot,
            IEnumerable<Renderer> renderers,
            ConfigurationSnapshot configuration,
            BuildState state,
            MenuItemSnapshot activeItem
        )
        {
            var clip = new AnimationClip { frameRate = 60.0f };
            var bindings = new HashSet<string>(StringComparer.Ordinal);
            bool baseline = activeItem == null;

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

                float baseStrength = 0.0f;
                float baseFacing = GetRendererFacingDirection(
                    renderer,
                    state,
                    configuration
                );
                float baseLockAxis = GetRendererLockAxis(
                    renderer,
                    state,
                    configuration
                );
                float baseSquashZ = GetRendererSquashZ(
                    renderer,
                    state,
                    configuration
                );
                float baseCameraParallel =
                    configuration.CameraParallel2D ? 1.0f : 0.0f;
                float baseOutline = GetRendererOutlineIn2D(
                    renderer,
                    state,
                    configuration
                );
                bool hasIndependentStrength =
                    RendererUsesIndependentRule(
                        renderer,
                        state,
                        rule => rule.IndependentTwoDimensionalness
                    );
                bool hasIndependentFacing =
                    RendererUsesIndependentRule(
                        renderer,
                        state,
                        rule => rule.IndependentFacingDirection
                    );
                bool hasIndependentLockAxis =
                    RendererUsesIndependentRule(
                        renderer,
                        state,
                        rule => rule.IndependentLockAxis
                    );
                bool hasIndependentSquashZ =
                    RendererUsesIndependentRule(
                        renderer,
                        state,
                        rule => rule.IndependentSquashZ
                    );
                bool hasIndependentOutline =
                    RendererUsesIndependentRule(
                        renderer,
                        state,
                        rule => rule.IndependentOutlineIn2D
                    );

                if(!hasIndependentStrength)
                {
                    float value = baseline
                        ? baseStrength
                        : GetRendererTwoDimensionalness(
                            renderer,
                            state,
                            configuration
                        );
                    if(!baseline && activeItem.ControlTwoDimensionalness)
                    {
                        value = Mathf.Clamp01(
                            activeItem.TwoDimensionalnessValue
                        );
                    }
                    SetMaterialCurve(
                        clip,
                        path,
                        rendererType,
                        "_2d_coef",
                        value
                    );

                    float? customLogic =
                        GetRendererMotchiriCustomLogicIn2D(renderer, state);
                    if(customLogic.HasValue)
                    {
                        SetMaterialCurve(
                            clip,
                            path,
                            rendererType,
                            CustomLogicProperty,
                            !baseline
                                ? Mathf.Clamp01(customLogic.Value)
                                : 1.0f
                        );
                    }
                }

                if(!hasIndependentFacing)
                {
                    float value = baseFacing;
                    if(!baseline && activeItem.ControlFacingDirection)
                    {
                        value = Mathf.Clamp(
                            activeItem.FacingDirectionValue,
                            -1.0f,
                            1.0f
                        );
                    }
                    SetMaterialCurve(
                        clip,
                        path,
                        rendererType,
                        "_facing_coef",
                        value
                    );
                }

                if(!hasIndependentLockAxis)
                {
                    float value = baseLockAxis;
                    if(!baseline && activeItem.ControlLockAxis)
                    {
                        value = Mathf.Clamp01(
                            activeItem.LockAxisValue
                        );
                    }
                    SetMaterialCurve(
                        clip,
                        path,
                        rendererType,
                        "_lock2daxis_coef",
                        value
                    );
                }

                if(!hasIndependentSquashZ)
                {
                    float value = baseSquashZ;
                    if(!baseline && activeItem.ControlSquashZ)
                    {
                        value = Mathf.Clamp01(
                            activeItem.SquashZValue
                        );
                    }
                    SetMaterialCurve(
                        clip,
                        path,
                        rendererType,
                        "_zcorrect_coef",
                        value
                    );
                }

                SetMaterialCurve(
                    clip,
                    path,
                    rendererType,
                    "_lyuma_camera_parallel_2d",
                    !baseline && activeItem.ControlCameraParallel2D
                        ? (activeItem.CameraParallel2DValue ? 1.0f : 0.0f)
                        : baseCameraParallel
                );

                if(!hasIndependentOutline)
                {
                    SetMaterialCurve(
                        clip,
                        path,
                        rendererType,
                        "_lyuma_outline_2d",
                        !baseline && activeItem.ControlOutlineIn2D
                            ? (activeItem.OutlineIn2DValue
                                ? 1.0f
                                : 0.0f)
                            : baseOutline
                    );
                }
            }
            return clip;
        }

        private static void SetMaterialCurve(
            AnimationClip clip,
            string path,
            Type rendererType,
            string propertyName,
            float value
        )
        {
            EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                path,
                rendererType,
                "material." + propertyName
            );
            AnimationUtility.SetEditorCurve(
                clip,
                binding,
                AnimationCurve.Constant(0.0f, 1.0f / 60.0f, value)
            );
        }

        private static Renderer ResolveRenderer(
            Transform avatarRoot,
            EditorCurveBinding binding
        )
        {
            if(avatarRoot == null ||
                binding.type == null ||
                !typeof(Renderer).IsAssignableFrom(binding.type))
            {
                return null;
            }

            Transform target = string.IsNullOrEmpty(binding.path)
                ? avatarRoot
                : avatarRoot.Find(binding.path);
            if(target == null) return null;
            Renderer renderer =
                target.GetComponent(binding.type) as Renderer;
            return renderer != null
                ? renderer
                : target.GetComponent<Renderer>();
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

        private static float GetRendererFacingDirection(
            Renderer renderer,
            BuildState state,
            ConfigurationSnapshot fallback
        )
        {
            return GetRendererRuleValue(
                renderer,
                state,
                fallback.FacingDirection,
                rule => rule.FacingDirection,
                false
            );
        }

        private static float GetRendererLockAxis(
            Renderer renderer,
            BuildState state,
            ConfigurationSnapshot fallback
        )
        {
            return GetRendererRuleValue(
                renderer,
                state,
                fallback.LockAxis,
                rule => rule.LockAxis,
                false
            );
        }

        private static float GetRendererSquashZ(
            Renderer renderer,
            BuildState state,
            ConfigurationSnapshot fallback
        )
        {
            return GetRendererRuleValue(
                renderer,
                state,
                fallback.SquashZ,
                rule => rule.SquashZ,
                false
            );
        }

        private static float GetRendererOutlineIn2D(
            Renderer renderer,
            BuildState state,
            ConfigurationSnapshot fallback
        )
        {
            return GetRendererRuleValue(
                renderer,
                state,
                fallback.OutlineIn2D ? 1.0f : 0.0f,
                rule => rule.OutlineIn2D ? 1.0f : 0.0f,
                true
            );
        }

        private static float GetRendererRuleValue(
            Renderer renderer,
            BuildState state,
            float fallback,
            Func<RuleSnapshot, float> selector,
            bool useMinimum
        )
        {
            if(renderer == null || state == null || selector == null)
                return fallback;

            float value = fallback;
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

                float candidate = selector(rule);
                if(!found)
                {
                    value = candidate;
                    found = true;
                }
                else if(useMinimum)
                {
                    value = Mathf.Min(value, candidate);
                }
            }
            return value;
        }

        private static bool RendererUsesIndependentRule(
            Renderer renderer,
            BuildState state,
            Func<RuleSnapshot, bool> selector
        )
        {
            if(renderer == null || state == null || selector == null)
                return false;

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
                    rule.Convert &&
                    selector(rule))
                {
                    return true;
                }
            }
            return false;
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
            BuildContext context,
            GameObject root,
            Transform hips,
            BuildState state
        )
        {
            if(context == null || root == null || hips == null) return;
            foreach(SkinnedMeshRenderer renderer in
                root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if(renderer == null || renderer.rootBone == hips) continue;
                Transform previousRoot = renderer.rootBone != null
                    ? renderer.rootBone
                    : renderer.transform;
                if(renderer.rootBone == null &&
                    !HasUsableBones(renderer) &&
                    renderer.sharedMesh != null)
                {
                    Mesh mesh = Object.Instantiate(renderer.sharedMesh);
                    mesh.name =
                        renderer.sharedMesh.name + "_Waifu2dSingleBone";
                    var weights = new BoneWeight[mesh.vertexCount];
                    for(int i = 0; i < weights.Length; i++)
                    {
                        weights[i] = new BoneWeight
                        {
                            boneIndex0 = 0,
                            weight0 = 1.0f
                        };
                    }
                    mesh.boneWeights = weights;
                    mesh.bindposes = new[] { Matrix4x4.identity };
                    renderer.sharedMesh = mesh;
                    renderer.bones = new[] { renderer.transform };
                    context.AssetSaver.SaveAsset(mesh);
                }
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

        private static bool HasUsableBones(SkinnedMeshRenderer renderer)
        {
            if(renderer == null || renderer.bones == null) return false;
            foreach(Transform bone in renderer.bones)
            {
                if(bone != null) return true;
            }
            return false;
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
            internal readonly Dictionary<Material, Material> ParticleClones =
                new Dictionary<Material, Material>();
            internal readonly Dictionary<Renderer, Transform>
                MotchiriContactAnchors =
                    new Dictionary<Renderer, Transform>();
            private readonly HashSet<Material> materialsUsedByNonParticles =
                new HashSet<Material>();

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

            internal void CaptureMaterialUsage(
                GameObject avatarRoot,
                AnimationIndex animationIndex
            )
            {
                materialsUsedByNonParticles.Clear();
                if(avatarRoot == null) return;

                foreach(Renderer renderer in
                    avatarRoot.GetComponentsInChildren<Renderer>(true))
                {
                    if(renderer == null) continue;
                    foreach(Material material in renderer.sharedMaterials)
                    {
                        CaptureMaterialUsage(material, renderer);
                    }
                }

                if(animationIndex == null) return;
                animationIndex.RewriteObjectCurves((binding, value) =>
                {
                    Material material = value as Material;
                    if(material == null) return value;
                    Renderer renderer = ResolveRenderer(
                        avatarRoot.transform,
                        binding
                    );
                    CaptureMaterialUsage(material, renderer);
                    return value;
                });
            }

            internal bool IsUsedByNonParticleRenderer(Material material)
            {
                return materialsUsedByNonParticles.Contains(
                    ResolveOriginalMaterial(material)
                );
            }

            private void CaptureMaterialUsage(
                Material material,
                Renderer renderer
            )
            {
                ConfigurationSnapshot configuration;
                RuleSnapshot rule;
                if(!TryFindRule(
                        material,
                        renderer,
                        out configuration,
                        out rule
                    ) ||
                    configuration == null ||
                    rule == null ||
                    !rule.Convert)
                {
                    return;
                }

                if(!(renderer is ParticleSystemRenderer))
                {
                    materialsUsedByNonParticles.Add(
                        ResolveOriginalMaterial(material)
                    );
                }
            }

            private static Material ResolveOriginalMaterial(
                Material material
            )
            {
                if(material == null) return null;
                var reference = ObjectRegistry.GetReference(material);
                Material original = reference != null
                    ? reference.Object as Material
                    : null;
                return original != null ? original : material;
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
            internal readonly bool CameraParallel2D;
            internal readonly bool OutlineIn2D;
            internal readonly List<MenuItemSnapshot> CustomMenuItems =
                new List<MenuItemSnapshot>();
            internal readonly bool PreviewIn2D;
            internal readonly bool RepairRootBones;
            internal readonly bool ConvertStaticMeshes;
            internal readonly bool ProtectParticleMaterials;

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
                CameraParallel2D = component.CameraParallel2D;
                OutlineIn2D = !component.DisableOutlineIn2D;
                PreviewIn2D = component.PreviewIn2D;
                RepairRootBones = component.RepairRootBones;
                ConvertStaticMeshes = component.ConvertStaticMeshes;
                ProtectParticleMaterials =
                    component.ProtectParticleMaterials;

                if(component.CustomMenuVersion >= 1)
                {
                    if(component.CustomMenuItems != null)
                    {
                        foreach(LyumaWaifu2dAvatarConfig.CustomMenuItem item in
                            component.CustomMenuItems)
                        {
                            if(item != null)
                                CustomMenuItems.Add(
                                    new MenuItemSnapshot(item)
                                );
                        }
                    }
                }
                else if(component.GenerateToggle)
                {
                    CustomMenuItems.Add(new MenuItemSnapshot(component));
                }

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

        private sealed class MenuItemSnapshot
        {
            internal readonly bool Enabled;
            internal readonly string ParameterName;
            internal readonly string MenuName;
            internal readonly Texture2D MenuIcon;
            internal readonly GameObject MenuParent;
            internal readonly bool OverrideDirectMenuItemSettings;
            internal readonly bool DefaultEnabled;
            internal readonly bool Saved;
            internal readonly bool Synced;
            internal readonly bool ControlTwoDimensionalness;
            internal readonly float TwoDimensionalnessValue;
            internal readonly bool ControlFacingDirection;
            internal readonly float FacingDirectionValue;
            internal readonly bool ControlLockAxis;
            internal readonly float LockAxisValue;
            internal readonly bool ControlSquashZ;
            internal readonly float SquashZValue;
            internal readonly bool ControlCameraParallel2D;
            internal readonly bool CameraParallel2DValue;
            internal readonly bool ControlOutlineIn2D;
            internal readonly bool OutlineIn2DValue;

            internal MenuItemSnapshot(
                LyumaWaifu2dAvatarConfig.CustomMenuItem source
            )
            {
                Enabled = source.Enabled;
                ParameterName = source.ParameterName;
                MenuName = source.MenuName;
                MenuIcon = source.MenuIcon;
                MenuParent = source.MenuParent;
                OverrideDirectMenuItemSettings =
                    source.OverrideDirectMenuItemSettings;
                DefaultEnabled = source.DefaultEnabled;
                Saved = source.Saved;
                Synced = source.Synced;
                ControlTwoDimensionalness =
                    source.ControlTwoDimensionalness;
                TwoDimensionalnessValue =
                    source.TwoDimensionalnessValue;
                ControlFacingDirection = source.ControlFacingDirection;
                FacingDirectionValue = source.FacingDirectionValue;
                ControlLockAxis = source.ControlLockAxis;
                LockAxisValue = source.LockAxisValue;
                ControlSquashZ = source.ControlSquashZ;
                SquashZValue = source.SquashZValue;
                ControlCameraParallel2D =
                    source.ControlCameraParallel2D;
                CameraParallel2DValue = source.CameraParallel2DValue;
                ControlOutlineIn2D = source.ControlOutlineIn2D;
                OutlineIn2DValue = source.OutlineIn2DValue;
            }

            internal MenuItemSnapshot(LyumaWaifu2dAvatarConfig legacy)
            {
                Enabled = true;
                ParameterName = ToggleParameterName;
                MenuName = legacy.ToggleMenuName;
                MenuIcon = legacy.ToggleMenuIcon;
                MenuParent = legacy.ToggleMenuParent;
                OverrideDirectMenuItemSettings =
                    legacy.OverrideDirectMenuItemSettings;
                DefaultEnabled = legacy.ToggleDefaultEnabled;
                Saved = legacy.ToggleSaved;
                Synced = legacy.ToggleSynced;
                ControlTwoDimensionalness = true;
                TwoDimensionalnessValue = 0.99f;
                OutlineIn2DValue = true;
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
            internal readonly bool OutlineIn2D;
            internal readonly bool IndependentTwoDimensionalness;
            internal readonly bool IndependentFacingDirection;
            internal readonly bool IndependentLockAxis;
            internal readonly bool IndependentSquashZ;
            internal readonly bool IndependentOutlineIn2D;

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
                IndependentTwoDimensionalness =
                    source.OverrideParameters &&
                    !source.UseGlobalTwoDimensionalness;
                IndependentFacingDirection = source.OverrideParameters &&
                    !source.UseGlobalFacingDirection;
                IndependentLockAxis = source.OverrideParameters &&
                    !source.UseGlobalLockAxis;
                IndependentSquashZ = source.OverrideParameters &&
                    !source.UseGlobalSquashZ;
                IndependentOutlineIn2D = source.OverrideParameters &&
                    source.OverrideOutlineIn2D;
                TwoDimensionalness = IndependentTwoDimensionalness
                    ? source.TwoDimensionalness
                    : configuration.TwoDimensionalness;
                FacingDirection = IndependentFacingDirection
                    ? source.FacingDirection
                    : configuration.FacingDirection;
                LockAxis = IndependentLockAxis
                    ? source.LockAxis
                    : configuration.LockAxis;
                SquashZ = IndependentSquashZ
                    ? source.SquashZ
                    : configuration.SquashZ;
                OutlineIn2D = IndependentOutlineIn2D
                    ? !source.DisableOutlineIn2D
                    : configuration.OutlineIn2D;
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
