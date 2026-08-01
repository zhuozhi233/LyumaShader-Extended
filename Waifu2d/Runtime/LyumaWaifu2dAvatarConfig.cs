using System;
using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEngine;

namespace LyumaShader
{
    /// <summary>
    /// Stores the non-destructive Waifu2d build configuration on an avatar.
    /// The editor window updates this component; NDMF consumes it on the build copy.
    /// </summary>
    [AddComponentMenu("LyumaShader Extended/Waifu2d 配置")]
    [DisallowMultipleComponent]
    public sealed class LyumaWaifu2dAvatarConfig : MonoBehaviour, INDMFEditorOnly
    {
        [Serializable]
        public sealed class MaterialRule
        {
            public Material Material;
            public bool Convert = true;
            public bool MergeCustomShader = true;
            public bool EnableCustomLogicIn2D;
            public bool FlattenMaterialVariant = true;
            public bool OverrideParameters;
            public bool UseGlobalTwoDimensionalness;
            public bool UseGlobalFacingDirection;
            public bool UseGlobalLockAxis;
            public bool UseGlobalSquashZ;
            public bool OverrideOutlineIn2D;
            public bool DisableOutlineIn2D;

            [Range(0.0f, 1.0f)]
            public float TwoDimensionalness = 0.99f;

            [Range(-1.0f, 1.0f)]
            public float FacingDirection;

            [Range(0.0f, 1.0f)]
            public float LockAxis = 1.0f;

            [Range(0.0f, 1.0f)]
            public float SquashZ = 1.0f;
        }

        [Serializable]
        public sealed class CustomMenuItem
        {
            public bool Enabled = true;
            public string ParameterName;
            public string MenuName;
            public Texture2D MenuIcon;
            public GameObject MenuParent;
            public bool OverrideDirectMenuItemSettings;
            public bool DefaultEnabled;
            public bool Saved = true;
            public bool Synced = true;

            public bool ControlTwoDimensionalness = true;

            [Range(0.0f, 1.0f)]
            public float TwoDimensionalnessValue = 0.99f;

            public bool ControlFacingDirection;

            [Range(-1.0f, 1.0f)]
            public float FacingDirectionValue;

            public bool ControlLockAxis;

            [Range(0.0f, 1.0f)]
            public float LockAxisValue = 1.0f;

            public bool ControlSquashZ;

            [Range(0.0f, 1.0f)]
            public float SquashZValue = 1.0f;

            public bool ControlCameraParallel2D;
            public bool CameraParallel2DValue;
            public bool ControlOutlineIn2D;
            public bool OutlineIn2DValue = true;
        }

        public List<MaterialRule> Materials = new List<MaterialRule>();

        [Range(0.0f, 1.0f)]
        public float TwoDimensionalness = 0.99f;

        [Range(-1.0f, 1.0f)]
        public float FacingDirection;

        [Range(0.0f, 1.0f)]
        public float LockAxis = 1.0f;

        [Range(0.0f, 1.0f)]
        public float SquashZ = 1.0f;

        public bool CameraParallel2D;
        public bool DisableOutlineIn2D;

        public int CustomMenuVersion;
        public List<CustomMenuItem> CustomMenuItems =
            new List<CustomMenuItem>();

        // Kept only for migrating configurations created before custom menus.
        [HideInInspector]
        public bool GenerateToggle = true;
        [HideInInspector]
        public string ToggleMenuName;
        [HideInInspector]
        public Texture2D ToggleMenuIcon;
        [HideInInspector]
        public GameObject ToggleMenuParent;
        [HideInInspector]
        public bool OverrideDirectMenuItemSettings;
        [HideInInspector]
        public bool ToggleDefaultEnabled;
        [HideInInspector]
        public bool ToggleSaved = true;
        [HideInInspector]
        public bool ToggleSynced = true;
        public bool PreviewIn2D;
        public bool RepairRootBones = true;
        public bool ConvertStaticMeshes = true;
        public bool ProtectParticleMaterials = true;

        public bool MigrateLegacyMenu()
        {
            if(CustomMenuVersion >= 1) return false;
            CustomMenuVersion = 1;
            if(CustomMenuItems == null)
            {
                CustomMenuItems = new List<CustomMenuItem>();
            }

            if(GenerateToggle && CustomMenuItems.Count == 0)
            {
                CustomMenuItems.Add(new CustomMenuItem
                {
                    ParameterName = "zhz/Lyuma2D",
                    MenuName = ToggleMenuName,
                    MenuIcon = ToggleMenuIcon,
                    MenuParent = ToggleMenuParent,
                    OverrideDirectMenuItemSettings =
                        OverrideDirectMenuItemSettings,
                    DefaultEnabled = ToggleDefaultEnabled,
                    Saved = ToggleSaved,
                    Synced = ToggleSynced
                });
            }
            return true;
        }

        public MaterialRule FindRule(Material material)
        {
            if(material == null || Materials == null) return null;
            for(int index = 0; index < Materials.Count; index++)
            {
                MaterialRule rule = Materials[index];
                if(rule != null && rule.Material == material) return rule;
            }
            return null;
        }
    }
}
