using System;
using System.Collections.Generic;
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
        [Serializable]
        public sealed class MeshSettingsSnapshot
        {
            [SerializeField] private bool createdByTool;
            [SerializeField] private ModularAvatarMeshSettings meshSettings;
            [SerializeField] private ModularAvatarMeshSettings.InheritMode previousInheritBounds;
            [SerializeField] private AvatarObjectReference previousRootBone;
            [SerializeField] private Bounds previousBounds;

            public bool CreatedByTool => createdByTool;
            public ModularAvatarMeshSettings MeshSettings => meshSettings;

            public MeshSettingsSnapshot(ModularAvatarMeshSettings settings, bool created)
            {
                createdByTool = created;
                meshSettings = settings;
                if(settings == null || created) return;

                previousInheritBounds = settings.InheritBounds;
                previousRootBone = settings.RootBone != null ? settings.RootBone.Clone() : null;
                previousBounds = settings.Bounds;
            }

            internal MeshSettingsSnapshot(
                ModularAvatarMeshSettings settings,
                bool created,
                ModularAvatarMeshSettings.InheritMode inheritBounds,
                AvatarObjectReference rootBone,
                Bounds bounds
            )
            {
                createdByTool = created;
                meshSettings = settings;
                previousInheritBounds = inheritBounds;
                previousRootBone = rootBone != null ? rootBone.Clone() : null;
                previousBounds = bounds;
            }

            public void Restore()
            {
                if(meshSettings == null || createdByTool) return;
                meshSettings.InheritBounds = previousInheritBounds;
                meshSettings.RootBone = previousRootBone != null ? previousRootBone.Clone() : null;
                meshSettings.Bounds = previousBounds;
            }
        }

        // These fields are retained so restore records created by 1.1.5 and earlier remain usable.
        [SerializeField] private bool createdMeshSettings;
        [SerializeField] private ModularAvatarMeshSettings trackedMeshSettings;
        [SerializeField] private ModularAvatarMeshSettings.InheritMode previousInheritBounds;
        [SerializeField] private AvatarObjectReference previousRootBone;
        [SerializeField] private Bounds previousBounds;
        [SerializeField] private List<MeshSettingsSnapshot> meshSettingsSnapshots =
            new List<MeshSettingsSnapshot>();

        public bool CreatedMeshSettings => createdMeshSettings;
        public ModularAvatarMeshSettings TrackedMeshSettings => trackedMeshSettings;
        public IReadOnlyList<MeshSettingsSnapshot> MeshSettingsSnapshots => meshSettingsSnapshots;

        public void MigrateLegacySnapshot()
        {
            if(meshSettingsSnapshots == null)
            {
                meshSettingsSnapshots = new List<MeshSettingsSnapshot>();
            }
            if(trackedMeshSettings == null || Contains(trackedMeshSettings)) return;

            meshSettingsSnapshots.Add(new MeshSettingsSnapshot(
                trackedMeshSettings,
                createdMeshSettings,
                previousInheritBounds,
                previousRootBone,
                previousBounds
            ));
        }

        public void CaptureExisting(ModularAvatarMeshSettings settings)
        {
            MigrateLegacySnapshot();
            if(settings == null || Contains(settings)) return;
            meshSettingsSnapshots.Add(new MeshSettingsSnapshot(settings, false));
        }

        public void CaptureCreated(ModularAvatarMeshSettings settings)
        {
            MigrateLegacySnapshot();
            if(settings == null || Contains(settings)) return;
            meshSettingsSnapshots.Add(new MeshSettingsSnapshot(settings, true));
        }

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

        private bool Contains(ModularAvatarMeshSettings settings)
        {
            if(meshSettingsSnapshots == null || settings == null) return false;
            foreach(MeshSettingsSnapshot snapshot in meshSettingsSnapshots)
            {
                if(snapshot != null && snapshot.MeshSettings == settings) return true;
            }
            return false;
        }
    }
}
