using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using UnityEngine;

namespace LyumaShader
{
    /// <summary>
    /// Stores the MA Mesh Settings state that existed before the Waifu2d root-bone fix.
    /// NDMF removes this helper component from the built avatar.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class LyumaWaifu2dMeshSettingsRestoreState : MonoBehaviour, INDMFEditorOnly
    {
        [SerializeField] private bool createdMeshSettings;
        [SerializeField] private ModularAvatarMeshSettings trackedMeshSettings;
        [SerializeField] private ModularAvatarMeshSettings.InheritMode previousInheritBounds;
        [SerializeField] private AvatarObjectReference previousRootBone;
        [SerializeField] private Bounds previousBounds;

        public bool CreatedMeshSettings => createdMeshSettings;
        public ModularAvatarMeshSettings TrackedMeshSettings => trackedMeshSettings;

        public void Capture(ModularAvatarMeshSettings settings)
        {
            createdMeshSettings = settings == null;
            trackedMeshSettings = settings;
            if(settings == null) return;

            previousInheritBounds = settings.InheritBounds;
            previousRootBone = settings.RootBone != null ? settings.RootBone.Clone() : null;
            previousBounds = settings.Bounds;
        }

        public void TrackCreatedSettings(ModularAvatarMeshSettings settings)
        {
            if(createdMeshSettings) trackedMeshSettings = settings;
        }

        public void Restore(ModularAvatarMeshSettings settings)
        {
            if(settings == null || createdMeshSettings) return;

            settings.InheritBounds = previousInheritBounds;
            settings.RootBone = previousRootBone != null ? previousRootBone.Clone() : null;
            settings.Bounds = previousBounds;
        }
    }
}
