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

            [Range(0.0f, 1.0f)]
            public float TwoDimensionalness = 0.99f;

            [Range(-1.0f, 1.0f)]
            public float FacingDirection;

            [Range(0.0f, 1.0f)]
            public float LockAxis = 1.0f;

            [Range(0.0f, 1.0f)]
            public float SquashZ = 1.0f;
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

        public bool GenerateToggle = true;
        public string ToggleMenuName;
        public Texture2D ToggleMenuIcon;
        public GameObject ToggleMenuParent;
        public bool ToggleDefaultEnabled;
        public bool ToggleSaved = true;
        public bool ToggleSynced = true;
        public bool PreviewIn2D;
        public bool RepairRootBones = true;
        public bool ConvertStaticMeshes = true;
        public bool ProtectParticleMaterials = true;

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
