#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;

namespace LyumaShader
{
    /// <summary>
    /// Batch workflow for applying Lyuma Waifu2d to lilToon and Poiyomi materials.
    /// This editor-only tool changes material assets and creates AnimationClips;
    /// it does not modify lilToon's package files.
    /// </summary>
    public sealed class LilToonWaifu2dBatchWindow : EditorWindow
    {
        private const string DefaultAnimationFolder = "Assets/LyumaShader/GeneratedAnimations";
        private const string TwoDimensionalnessProperty = "_2d_coef";
        private const string FacingDirectionProperty = "_facing_coef";
        private const string LockAxisProperty = "_lock2daxis_coef";
        private const string SquashZProperty = "_zcorrect_coef";
        private const int CurrentSettingsVersion = 1;

        [SerializeField] private GameObject modelRoot;
        [SerializeField] private DefaultAsset animationOutputFolder;
        [SerializeField] private List<Material> targetMaterials = new List<Material>();

        [SerializeField] private bool applyTwoDimensionalness = true;
        [SerializeField] private bool applyFacingDirection = true;
        [SerializeField] private bool applyLockAxis = true;
        [SerializeField] private bool applySquashZ = true;
        [SerializeField] private float twoDimensionalness = 0.99f;
        [SerializeField] private float facingDirection;
        [SerializeField] private float lockAxis = 1.0f;
        [SerializeField] private float squashZ = 0.8f;
        [SerializeField] private int settingsVersion;

        [SerializeField] private bool showTargetMaterials;
        private Vector2 scrollPosition;
        private string statusMessage = "请拖入模型，或在层级/项目窗口中多选对象后读取。";
        private MessageType statusType = MessageType.Info;

        [MenuItem("Tools/LyumaShader Extended/Waifu2d 批量工具")]
        private static void OpenWindow()
        {
            var window = GetWindow<LilToonWaifu2dBatchWindow>();
            window.titleContent = new GUIContent("Waifu2d 批量工具 by 浊鸷");
            window.minSize = new Vector2(470.0f, 590.0f);
            window.Show();
        }

        private void OnEnable()
        {
            if(settingsVersion >= CurrentSettingsVersion) return;
            applyTwoDimensionalness = true;
            if(twoDimensionalness <= 0.0f) twoDimensionalness = 0.99f;
            if(Mathf.Approximately(squashZ, 0.975f)) squashZ = 0.8f;
            settingsVersion = CurrentSettingsVersion;
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawTargetSection();
            EditorGUILayout.Space(8.0f);
            DrawConversionSection();
            EditorGUILayout.Space(8.0f);
            DrawGeneralParametersSection();
            EditorGUILayout.Space(8.0f);
            DrawAnimationSection();
            EditorGUILayout.Space(8.0f);
            EditorGUILayout.HelpBox(statusMessage, statusType);
            EditorGUILayout.EndScrollView();
        }

        private void DrawTargetSection()
        {
            EditorGUILayout.LabelField("目标", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            GameObject newRoot = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("模型根对象", "可使用场景对象、Prefab 或模型资源。生成动画时以它为动画根节点。"),
                modelRoot,
                typeof(GameObject),
                true
            );
            if(EditorGUI.EndChangeCheck())
            {
                modelRoot = newRoot;
            }

            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button("扫描模型中的材质"))
            {
                if(modelRoot == null)
                {
                    SetStatus("请先拖入模型根对象。", MessageType.Warning);
                }
                else
                {
                    SetTargets(CollectMaterials(new UnityEngine.Object[] { modelRoot }), "模型");
                }
            }
            if(GUILayout.Button("读取当前多选"))
            {
                SetTargets(CollectMaterials(Selection.objects), "当前多选");
            }
            if(GUILayout.Button("清空", GUILayout.Width(60.0f)))
            {
                targetMaterials.Clear();
                SetStatus("已清空目标材质。", MessageType.Info);
            }
            EditorGUILayout.EndHorizontal();

            RemoveInvalidAndDuplicateTargets();
            int lilToonConvertedCount = 0;
            int lilToonOriginalCount = 0;
            int lilCustomConvertedCount = 0;
            int lilCustomOriginalCount = 0;
            int poiyomiConvertedCount = 0;
            int poiyomiOriginalCount = 0;
            foreach(Material material in targetMaterials)
            {
                if(material == null || material.shader == null) continue;
                if(LilToonWaifu2dAdapter.IsSupported(material.shader))
                {
                    if(LilToonWaifu2dAdapter.IsWaifu2dShader(material.shader)) lilToonConvertedCount++;
                    else lilToonOriginalCount++;
                }
                else if(GenericLilCustomWaifu2dAdapter.IsSupported(material.shader))
                {
                    if(GenericLilCustomWaifu2dAdapter.IsWaifu2dShader(material.shader)) lilCustomConvertedCount++;
                    else lilCustomOriginalCount++;
                }
                else if(PoiyomiWaifu2dAdapter.IsSupported(material.shader))
                {
                    if(PoiyomiWaifu2dAdapter.IsWaifu2dShader(material.shader)) poiyomiConvertedCount++;
                    else poiyomiOriginalCount++;
                }
            }

            EditorGUILayout.LabelField(
                "已扫描的支持材质",
                targetMaterials.Count + " 个"
            );
            EditorGUILayout.LabelField(
                "lilToon",
                string.Format("待转换 {0} / 已转换 {1}", lilToonOriginalCount, lilToonConvertedCount)
            );
            EditorGUILayout.LabelField(
                "lilToon Custom",
                string.Format("待转换 {0} / 已转换 {1}", lilCustomOriginalCount, lilCustomConvertedCount)
            );
            EditorGUILayout.LabelField(
                "Poiyomi",
                string.Format("待转换 {0} / 已转换 {1}", poiyomiOriginalCount, poiyomiConvertedCount)
            );

            showTargetMaterials = EditorGUILayout.Foldout(showTargetMaterials, "查看目标材质", true);
            if(showTargetMaterials)
            {
                EditorGUI.indentLevel++;
                using(new EditorGUI.DisabledScope(true))
                {
                    foreach(Material material in targetMaterials)
                    {
                        EditorGUILayout.ObjectField(material, typeof(Material), false);
                    }
                }
                EditorGUI.indentLevel--;
            }
        }

        private void DrawConversionSection()
        {
            EditorGUILayout.LabelField("应用 Lyuma Waifu2d", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "转换受支持的 lilToon、lilToon Custom 与 Poiyomi 材质；扫描会包含控制器动画和组件中引用的备用材质。" +
                "Custom Shader 会在 LyumaShader/Generated 中生成组合副本，原插件文件不会被修改。已经转换的材质会保留现有参数。",
                MessageType.Info
            );

            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button("转换已扫描材质", GUILayout.Height(28.0f)))
            {
                ConvertMaterials(GetUsableTargets(), "已扫描材质");
            }
            if(GUILayout.Button("一键转换当前多选", GUILayout.Height(28.0f)))
            {
                ScanSelectionAndRun(ConvertMaterials, "当前多选");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button("从已扫描材质移除 Waifu2d", GUILayout.Height(26.0f)))
            {
                RevertMaterials(GetUsableTargets(), "已扫描材质");
            }
            if(GUILayout.Button("一键从当前多选移除", GUILayout.Height(26.0f)))
            {
                ScanSelectionAndRun(RevertMaterials, "当前多选");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8.0f);
            EditorGUILayout.LabelField("Root Bone 修复", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "在模型 Root 上添加或更新一个 MA Mesh Settings，将 Bounds 模式设为“指定”，Root Bone 指向模型的 Hips。" +
                "还原只撤销本工具保存的修改，不会误删用户原本的 MA Mesh Settings。",
                MessageType.Info
            );

            bool hasRootBoneRepair = modelRoot != null &&
                modelRoot.GetComponent<LyumaWaifu2dMeshSettingsRestoreState>() != null;
            string rootBoneButton = hasRootBoneRepair ? "还原 Root Bone 修复" : "修复 Root Bone";
            if(GUILayout.Button(rootBoneButton, GUILayout.Height(28.0f)))
            {
                RunRootBoneRepair(hasRootBoneRepair);
            }

            EditorGUILayout.Space(8.0f);
            EditorGUILayout.LabelField("普通 MeshRenderer 修复", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "只处理模型中使用 Waifu2d 材质的 MeshRenderer + MeshFilter。构建期方式仅在 NDMF/MA 的构建副本中转换。直接转换会立即改为单骨骼 SkinnedMeshRenderer，工具无法还原，只能立即使用 Unity Undo 或自行恢复原组件。",
                MessageType.Info
            );
            EditorGUILayout.BeginHorizontal();
            bool hasBuildConverter = modelRoot != null &&
                modelRoot.GetComponent<LyumaWaifu2dStaticMeshConverter>() != null;
            string buildConverterButton = hasBuildConverter
                ? "移除 NDMF 构建期转换"
                : "添加 NDMF 构建期转换";
            if(GUILayout.Button(buildConverterButton, GUILayout.Height(28.0f)))
            {
                if(hasBuildConverter) RemoveStaticMeshBuildConverter();
                else AddStaticMeshBuildConverter();
            }
            if(GUILayout.Button("直接转换为单骨骼", GUILayout.Height(28.0f)))
            {
                ConvertStaticMeshesDirectly();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void AddStaticMeshBuildConverter()
        {
            if(modelRoot == null)
            {
                SetStatus("请先指定模型根对象。", MessageType.Warning);
                return;
            }
            int count = Waifu2dStaticMeshConversion.FindTargets(modelRoot).Count;
            if(count == 0)
            {
                SetStatus("模型中没有找到使用 Waifu2d 材质的 MeshRenderer + MeshFilter。", MessageType.Warning);
                return;
            }
            LyumaWaifu2dStaticMeshConverter marker = modelRoot.GetComponent<LyumaWaifu2dStaticMeshConverter>();
            if(marker == null) marker = Undo.AddComponent<LyumaWaifu2dStaticMeshConverter>(modelRoot);
            EditorUtility.SetDirty(modelRoot);
            SetStatus(string.Format("已添加 NDMF 构建期转换，构建时将临时转换 {0} 个普通网格。", count), MessageType.Info);
        }

        private void RemoveStaticMeshBuildConverter()
        {
            if(modelRoot == null)
            {
                SetStatus("请先指定模型根对象。", MessageType.Warning);
                return;
            }
            LyumaWaifu2dStaticMeshConverter marker = modelRoot.GetComponent<LyumaWaifu2dStaticMeshConverter>();
            if(marker == null)
            {
                SetStatus("模型根对象上没有 NDMF 构建期转换。", MessageType.Info);
                return;
            }
            Undo.DestroyObjectImmediate(marker);
            EditorUtility.SetDirty(modelRoot);
            SetStatus("已移除 NDMF 构建期转换。", MessageType.Info);
        }

        private void ConvertStaticMeshesDirectly()
        {
            if(modelRoot == null)
            {
                SetStatus("请先指定模型根对象。", MessageType.Warning);
                return;
            }
            List<MeshRenderer> targets = Waifu2dStaticMeshConversion.FindTargets(modelRoot);
            if(targets.Count == 0)
            {
                SetStatus("模型中没有找到使用 Waifu2d 材质的 MeshRenderer + MeshFilter。", MessageType.Warning);
                return;
            }
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Waifu2d 普通网格转单骨骼");
            int converted = 0;
            Transform hips = Waifu2dStaticMeshConversion.FindHips(modelRoot);
            foreach(MeshRenderer renderer in targets)
                if(Waifu2dStaticMeshConversion.Convert(renderer, hips, true, true) != null) converted++;
            Undo.CollapseUndoOperations(group);
            AssetDatabase.SaveAssets();
            SetStatus(string.Format("已将 {0} 个普通网格直接转换为单骨骼 SkinnedMeshRenderer。", converted), MessageType.Info);
        }

        private void DrawGeneralParametersSection()
        {
            EditorGUILayout.LabelField("批量参数", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("勾选需要写入的参数。未勾选的参数保持原值。", MessageType.None);

            DrawOptionalSlider(
                ref applyTwoDimensionalness,
                ref twoDimensionalness,
                new GUIContent("2D 强度", "2D Amount：0 为 3D，0.99 为推荐的 2D 值"),
                0.0f,
                1.0f
            );
            DrawOptionalSlider(
                ref applyFacingDirection,
                ref facingDirection,
                new GUIContent("朝向", "Facing Direction：-1 到 1"),
                -1.0f,
                1.0f
            );
            DrawOptionalSlider(
                ref applyLockAxis,
                ref lockAxis,
                new GUIContent("锁定 2D 轴", "Lock 2D Axis：0 到 1"),
                0.0f,
                1.0f
            );
            DrawOptionalSlider(
                ref applySquashZ,
                ref squashZ,
                new GUIContent("Z 深度修正", "Squash Z：推荐 0.8；数值越低越保留原始 3D 深度"),
                0.0f,
                1.0f
            );

            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button("应用到已扫描材质", GUILayout.Height(26.0f)))
            {
                ApplyGeneralParameters(GetUsableTargets(), "已扫描材质");
            }
            if(GUILayout.Button("一键应用到当前多选", GUILayout.Height(26.0f)))
            {
                ScanSelectionAndRun(ApplyGeneralParameters, "当前多选");
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAnimationSection()
        {
            EditorGUILayout.LabelField("生成 2D 开关动画", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "以“模型根对象”为动画根节点，同时检查当前材质、控制器换材质和 MA 换材质所关联的 Renderer。" +
                "只有相关受支持材质均已转换时才建立曲线；未转换或无法确定目标的关联材质会被跳过并提示。",
                MessageType.Info
            );

            animationOutputFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                new GUIContent("动画输出文件夹", "留空时使用 " + DefaultAnimationFolder),
                animationOutputFolder,
                typeof(DefaultAsset),
                false
            );

            if(GUILayout.Button("生成两个动画（2D 强度 0 / 0.99）", GUILayout.Height(32.0f)))
            {
                GenerateStrengthAnimations();
            }
        }

        private static void DrawOptionalSlider(
            ref bool shouldApply,
            ref float value,
            GUIContent label,
            float minimum,
            float maximum
        )
        {
            EditorGUILayout.BeginHorizontal();
            shouldApply = EditorGUILayout.Toggle(shouldApply, GUILayout.Width(18.0f));
            using(new EditorGUI.DisabledScope(!shouldApply))
            {
                value = EditorGUILayout.Slider(label, value, minimum, maximum);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void ScanSelectionAndRun(Action<List<Material>, string> operation, string sourceName)
        {
            ScanResult result = CollectMaterials(Selection.objects);
            SetTargets(result, sourceName);
            operation(GetUsableTargets(), sourceName);
        }

        private void ConvertMaterials(List<Material> materials, string sourceName)
        {
            if(materials.Count == 0)
            {
                SetStatus(sourceName + "中没有找到受支持的 lilToon、lilToon Custom 或 Poiyomi 材质。", MessageType.Warning);
                return;
            }

            int converted = 0;
            int alreadyConverted = 0;
            int failed = 0;
            var processedShaderOwners = new HashSet<Material>();

            BeginMaterialUndo("批量应用 Lyuma Waifu2d", out int undoGroup);
            foreach(Material material in materials)
            {
                if(material == null || material.shader == null) continue;
                if(IsWaifu2dShader(material.shader))
                {
                    alreadyConverted++;
                    continue;
                }

                Material shaderOwner = GetShaderOwner(material);
                if(!processedShaderOwners.Add(shaderOwner))
                {
                    alreadyConverted++;
                    continue;
                }
                if(IsWaifu2dShader(shaderOwner.shader))
                {
                    alreadyConverted++;
                    continue;
                }

                Shader targetShader = GetWaifu2dShader(shaderOwner.shader);
                if(targetShader == null)
                {
                    failed++;
                    continue;
                }

                int renderQueue = shaderOwner.renderQueue;
                Undo.RecordObject(shaderOwner, "应用 Lyuma Waifu2d");
                shaderOwner.shader = targetShader;
                InitializeWaifu2dMaterial(shaderOwner);
                shaderOwner.renderQueue = renderQueue;
                EditorUtility.SetDirty(shaderOwner);
                converted++;
            }
            FinishMaterialUndo(undoGroup);

            SetStatus(
                string.Format(
                    "{0}：已转换 {1} 个，原本已转换 {2} 个，失败 {3} 个。",
                    sourceName,
                    converted,
                    alreadyConverted,
                    failed
                ),
                failed == 0 ? MessageType.Info : MessageType.Warning
            );
        }

        private void ApplyGeneralParameters(List<Material> materials, string sourceName)
        {
            if(!applyTwoDimensionalness && !applyFacingDirection && !applyLockAxis && !applySquashZ)
            {
                SetStatus("请至少勾选一个要批量修改的参数。", MessageType.Warning);
                return;
            }
            if(materials.Count == 0)
            {
                SetStatus(sourceName + "中没有找到受支持的 lilToon、lilToon Custom 或 Poiyomi 材质。", MessageType.Warning);
                return;
            }

            int changed = 0;
            int converted = 0;
            int failed = 0;
            BeginMaterialUndo("批量修改 Lyuma Waifu2d 参数", out int undoGroup);
            foreach(Material material in materials)
            {
                if(!PrepareMaterialForEditing(material, ref converted))
                {
                    failed++;
                    continue;
                }

                Undo.RecordObject(material, "修改 Lyuma Waifu2d 参数");
                if(applyTwoDimensionalness) material.SetFloat(TwoDimensionalnessProperty, twoDimensionalness);
                if(applyFacingDirection) material.SetFloat(FacingDirectionProperty, facingDirection);
                if(applyLockAxis) material.SetFloat(LockAxisProperty, lockAxis);
                if(applySquashZ) material.SetFloat(SquashZProperty, squashZ);
                EditorUtility.SetDirty(material);
                changed++;
            }
            FinishMaterialUndo(undoGroup);

            SetStatus(
                string.Format(
                    "{0}：已修改 {1} 个材质（其中自动转换 {2} 个），失败 {3} 个。",
                    sourceName,
                    changed,
                    converted,
                    failed
                ),
                failed == 0 ? MessageType.Info : MessageType.Warning
            );
        }

        private void RevertMaterials(List<Material> materials, string sourceName)
        {
            if(materials.Count == 0)
            {
                SetStatus(sourceName + "中没有找到受支持的 lilToon、lilToon Custom 或 Poiyomi 材质。", MessageType.Warning);
                return;
            }

            int reverted = 0;
            int alreadyOriginal = 0;
            int failed = 0;
            var processedShaderOwners = new HashSet<Material>();

            BeginMaterialUndo("批量移除 Lyuma Waifu2d", out int undoGroup);
            foreach(Material material in materials)
            {
                if(material == null || material.shader == null) continue;
                Material shaderOwner = GetShaderOwner(material);
                if(!processedShaderOwners.Add(shaderOwner)) continue;
                if(!IsWaifu2dShader(shaderOwner.shader))
                {
                    alreadyOriginal++;
                    continue;
                }

                Shader originalShader = GetOriginalShader(shaderOwner.shader);
                if(originalShader == null)
                {
                    failed++;
                    continue;
                }

                int renderQueue = shaderOwner.renderQueue;
                Undo.RecordObject(shaderOwner, "移除 Lyuma Waifu2d");
                shaderOwner.shader = originalShader;
                shaderOwner.renderQueue = renderQueue;
                EditorUtility.SetDirty(shaderOwner);
                reverted++;
            }
            FinishMaterialUndo(undoGroup);

            string rootBoneRestoreStatus = TryAutoRestoreRootBoneRepair();

            SetStatus(
                string.Format(
                    "{0}：已恢复 {1} 个基础材质，原本未转换 {2} 个，失败 {3} 个。{4}",
                    sourceName,
                    reverted,
                    alreadyOriginal,
                    failed,
                    rootBoneRestoreStatus
                ),
                failed == 0 ? MessageType.Info : MessageType.Warning
            );
        }

        private void RunRootBoneRepair(bool restore)
        {
            GameObject requestedRoot = modelRoot != null ? modelRoot : Selection.activeGameObject;
            if(requestedRoot == null)
            {
                SetStatus("请先指定模型根对象，或在层级窗口选中模型根对象。", MessageType.Warning);
                return;
            }

            RootBoneRepairResult result = ProcessRootBoneRepairTarget(requestedRoot, restore, false);
            if(!EditorUtility.IsPersistent(requestedRoot) && result.resolvedRoot != null)
            {
                modelRoot = result.resolvedRoot;
            }
            SetStatus(result.message, result.messageType);
        }

        private string TryAutoRestoreRootBoneRepair()
        {
            GameObject requestedRoot = modelRoot != null ? modelRoot : Selection.activeGameObject;
            if(requestedRoot == null) return string.Empty;

            RootBoneRepairResult result = ProcessRootBoneRepairTarget(requestedRoot, true, true);
            if(result.changed) return "\n已同步还原本工具添加的 Root Bone 修复。";
            if(result.skippedBecauseWaifu2d) return "\n模型中仍有已转换的 Waifu2d 材质，已保留 Root Bone 修复。";
            return string.Empty;
        }

        private static RootBoneRepairResult ProcessRootBoneRepairTarget(
            GameObject requestedRoot,
            bool restore,
            bool onlyWhenNoWaifu2d
        )
        {
            if(!EditorUtility.IsPersistent(requestedRoot))
            {
                if(restore)
                {
                    GameObject restoreRoot = FindRootBoneRestoreRoot(requestedRoot);
                    if(restoreRoot == null)
                    {
                        return RootBoneRepairResult.Unchanged(
                            "模型上没有本工具保存的 Root Bone 修复记录。",
                            MessageType.Info
                        );
                    }
                    if(onlyWhenNoWaifu2d && HasWaifu2dMaterial(restoreRoot))
                    {
                        return RootBoneRepairResult.SkippedForWaifu2d(restoreRoot);
                    }
                    return RestoreRootBoneRepair(restoreRoot, true);
                }

                return ApplyRootBoneRepair(requestedRoot, true);
            }

            string assetPath = AssetDatabase.GetAssetPath(requestedRoot);
            if(string.IsNullOrEmpty(assetPath) ||
                !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return RootBoneRepairResult.Unchanged(
                    "Root Bone 修复不能直接写入 FBX/模型资源。请将模型放入场景，或使用可编辑的 Prefab。",
                    MessageType.Warning
                );
            }

            GameObject prefabRoot = null;
            try
            {
                prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);
                RootBoneRepairResult result;
                if(restore)
                {
                    GameObject restoreRoot = FindRootBoneRestoreRoot(prefabRoot);
                    if(restoreRoot == null)
                    {
                        return RootBoneRepairResult.Unchanged(
                            "Prefab 上没有本工具保存的 Root Bone 修复记录。",
                            MessageType.Info
                        );
                    }
                    if(onlyWhenNoWaifu2d && HasWaifu2dMaterial(restoreRoot))
                    {
                        return RootBoneRepairResult.SkippedForWaifu2d(null);
                    }
                    result = RestoreRootBoneRepair(restoreRoot, false);
                }
                else
                {
                    result = ApplyRootBoneRepair(prefabRoot, false);
                }

                if(result.changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, assetPath);
                    AssetDatabase.SaveAssets();
                }
                result.resolvedRoot = null;
                return result;
            }
            catch(Exception exception)
            {
                return RootBoneRepairResult.Unchanged(
                    "处理 Prefab 的 Root Bone 修复失败：" + exception.Message,
                    MessageType.Error
                );
            }
            finally
            {
                if(prefabRoot != null) PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static RootBoneRepairResult ApplyRootBoneRepair(GameObject requestedRoot, bool useUndo)
        {
            if(!TryResolveHumanoidRoot(requestedRoot, out GameObject avatarRoot, out Transform hips))
            {
                return RootBoneRepairResult.Unchanged(
                    "没有找到有效的 Humanoid Animator 或 Hips，无法应用 Root Bone 修复。",
                    MessageType.Warning
                );
            }

            if(!TryCalculateCommonBounds(avatarRoot, hips, out Bounds commonBounds))
            {
                return RootBoneRepairResult.Unchanged(
                    "模型中没有可用于计算 Bounds 的 Skinned Mesh Renderer。",
                    MessageType.Warning
                );
            }

            int undoGroup = BeginObjectUndo(useUndo, "修复 Waifu2d Root Bone");
            try
            {
                ModularAvatarMeshSettings settings = avatarRoot.GetComponent<ModularAvatarMeshSettings>();
                LyumaWaifu2dMeshSettingsRestoreState restoreState =
                    avatarRoot.GetComponent<LyumaWaifu2dMeshSettingsRestoreState>();

                if(restoreState != null && restoreState.TrackedMeshSettings == null)
                {
                    DestroyObject(restoreState, useUndo);
                    restoreState = null;
                }

                if(restoreState == null)
                {
                    restoreState = AddComponent<LyumaWaifu2dMeshSettingsRestoreState>(avatarRoot, useUndo);
                    RecordObject(restoreState, useUndo, "保存 MA Mesh Settings 原始状态");
                    restoreState.hideFlags |= HideFlags.HideInInspector;
                    restoreState.Capture(settings);
                }

                if(settings == null)
                {
                    settings = AddComponent<ModularAvatarMeshSettings>(avatarRoot, useUndo);
                    RecordObject(restoreState, useUndo, "记录新建的 MA Mesh Settings");
                    restoreState.TrackCreatedSettings(settings);
                }

                RecordObject(settings, useUndo, "设置 MA Mesh Settings Root Bone");
                settings.InheritBounds = ModularAvatarMeshSettings.InheritMode.Set;
                settings.RootBone = new AvatarObjectReference(hips.gameObject);
                if(string.IsNullOrEmpty(settings.RootBone.referencePath))
                {
                    settings.RootBone.referencePath = AnimationUtility.CalculateTransformPath(
                        hips,
                        avatarRoot.transform
                    );
                }
                settings.Bounds = commonBounds;

                MarkObjectDirty(settings);
                MarkObjectDirty(restoreState);

                return RootBoneRepairResult.Changed(
                    string.Format(
                        "已在 {0} 上应用 Root Bone 修复：模式为“指定”，Root Bone 为 {1}，并统一了模型 Bounds。",
                        avatarRoot.name,
                        hips.name
                    ),
                    avatarRoot
                );
            }
            finally
            {
                FinishObjectUndo(useUndo, undoGroup);
            }
        }

        private static RootBoneRepairResult RestoreRootBoneRepair(GameObject avatarRoot, bool useUndo)
        {
            LyumaWaifu2dMeshSettingsRestoreState restoreState =
                avatarRoot.GetComponent<LyumaWaifu2dMeshSettingsRestoreState>();
            if(restoreState == null)
            {
                return RootBoneRepairResult.Unchanged(
                    "模型上没有本工具保存的 Root Bone 修复记录。",
                    MessageType.Info
                );
            }

            int undoGroup = BeginObjectUndo(useUndo, "还原 Waifu2d Root Bone 修复");
            try
            {
                ModularAvatarMeshSettings trackedSettings = restoreState.TrackedMeshSettings;
                if(restoreState.CreatedMeshSettings)
                {
                    if(trackedSettings != null) DestroyObject(trackedSettings, useUndo);
                }
                else
                {
                    if(trackedSettings == null)
                    {
                        ModularAvatarMeshSettings currentSettings =
                            avatarRoot.GetComponent<ModularAvatarMeshSettings>();
                        if(currentSettings == null)
                        {
                            trackedSettings = AddComponent<ModularAvatarMeshSettings>(avatarRoot, useUndo);
                        }
                    }

                    if(trackedSettings != null)
                    {
                        RecordObject(trackedSettings, useUndo, "恢复 MA Mesh Settings 原始状态");
                        restoreState.Restore(trackedSettings);
                        MarkObjectDirty(trackedSettings);
                    }
                }

                DestroyObject(restoreState, useUndo);
                return RootBoneRepairResult.Changed(
                    "已还原本工具对 Root 上 MA Mesh Settings 所做的修改。",
                    avatarRoot
                );
            }
            finally
            {
                FinishObjectUndo(useUndo, undoGroup);
            }
        }

        private static bool TryResolveHumanoidRoot(
            GameObject requestedRoot,
            out GameObject avatarRoot,
            out Transform hips
        )
        {
            avatarRoot = null;
            hips = null;

            Transform current = requestedRoot.transform;
            while(current != null)
            {
                Animator animator = current.GetComponent<Animator>();
                if(TryGetHips(animator, out hips))
                {
                    avatarRoot = animator.gameObject;
                    return true;
                }
                current = current.parent;
            }

            foreach(Animator animator in requestedRoot.GetComponentsInChildren<Animator>(true))
            {
                if(!TryGetHips(animator, out hips)) continue;
                avatarRoot = requestedRoot;
                return true;
            }

            return false;
        }

        private static bool TryGetHips(Animator animator, out Transform hips)
        {
            hips = null;
            if(animator == null || !animator.isHuman) return false;
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            return hips != null;
        }

        private static GameObject FindRootBoneRestoreRoot(GameObject requestedRoot)
        {
            Transform current = requestedRoot.transform;
            while(current != null)
            {
                if(current.GetComponent<LyumaWaifu2dMeshSettingsRestoreState>() != null)
                {
                    return current.gameObject;
                }
                current = current.parent;
            }

            LyumaWaifu2dMeshSettingsRestoreState childState =
                requestedRoot.GetComponentInChildren<LyumaWaifu2dMeshSettingsRestoreState>(true);
            return childState != null ? childState.gameObject : null;
        }

        private static bool TryCalculateCommonBounds(
            GameObject avatarRoot,
            Transform targetRootBone,
            out Bounds commonBounds
        )
        {
            commonBounds = new Bounds();
            bool hasBounds = false;

            foreach(SkinnedMeshRenderer renderer in
                avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if(renderer == null || renderer.sharedMesh == null) continue;

                Transform sourceRootBone = renderer.rootBone != null
                    ? renderer.rootBone
                    : renderer.transform;
                Matrix4x4 sourceToTarget =
                    targetRootBone.worldToLocalMatrix * sourceRootBone.localToWorldMatrix;
                EncapsulateTransformedBounds(
                    ref commonBounds,
                    ref hasBounds,
                    renderer.localBounds,
                    sourceToTarget
                );
            }

            return hasBounds;
        }

        private static void EncapsulateTransformedBounds(
            ref Bounds destination,
            ref bool hasDestination,
            Bounds source,
            Matrix4x4 transform
        )
        {
            Vector3 center = source.center;
            Vector3 extents = source.extents;
            for(int x = -1; x <= 1; x += 2)
            {
                for(int y = -1; y <= 1; y += 2)
                {
                    for(int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = center + Vector3.Scale(
                            extents,
                            new Vector3(x, y, z)
                        );
                        Vector3 transformedCorner = transform.MultiplyPoint3x4(corner);
                        if(!hasDestination)
                        {
                            destination = new Bounds(transformedCorner, Vector3.zero);
                            hasDestination = true;
                        }
                        else
                        {
                            destination.Encapsulate(transformedCorner);
                        }
                    }
                }
            }
        }

        private static bool HasWaifu2dMaterial(GameObject root)
        {
            if(root == null) return false;
            Waifu2dAssociatedMaterialScanner.Result scan =
                Waifu2dAssociatedMaterialScanner.Collect(new UnityEngine.Object[] { root });
            foreach(Material material in scan.AllMaterials)
            {
                if(material != null && material.shader != null &&
                    IsWaifu2dShader(material.shader))
                {
                    return true;
                }
            }
            return false;
        }

        private static int BeginObjectUndo(bool useUndo, string groupName)
        {
            if(!useUndo) return -1;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(groupName);
            return group;
        }

        private static void FinishObjectUndo(bool useUndo, int group)
        {
            if(useUndo && group >= 0) Undo.CollapseUndoOperations(group);
        }

        private static T AddComponent<T>(GameObject gameObject, bool useUndo) where T : Component
        {
            return useUndo ? Undo.AddComponent<T>(gameObject) : gameObject.AddComponent<T>();
        }

        private static void DestroyObject(UnityEngine.Object target, bool useUndo)
        {
            if(target == null) return;
            if(useUndo) Undo.DestroyObjectImmediate(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }

        private static void RecordObject(UnityEngine.Object target, bool useUndo, string actionName)
        {
            if(useUndo && target != null) Undo.RecordObject(target, actionName);
        }

        private static void MarkObjectDirty(UnityEngine.Object target)
        {
            if(target == null) return;
            EditorUtility.SetDirty(target);
            if(PrefabUtility.IsPartOfPrefabInstance(target))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            }
        }

        private struct RootBoneRepairResult
        {
            internal bool changed;
            internal bool skippedBecauseWaifu2d;
            internal string message;
            internal MessageType messageType;
            internal GameObject resolvedRoot;

            internal static RootBoneRepairResult Changed(string message, GameObject resolvedRoot)
            {
                return new RootBoneRepairResult
                {
                    changed = true,
                    message = message,
                    messageType = MessageType.Info,
                    resolvedRoot = resolvedRoot
                };
            }

            internal static RootBoneRepairResult Unchanged(string message, MessageType messageType)
            {
                return new RootBoneRepairResult
                {
                    message = message,
                    messageType = messageType
                };
            }

            internal static RootBoneRepairResult SkippedForWaifu2d(GameObject resolvedRoot)
            {
                return new RootBoneRepairResult
                {
                    skippedBecauseWaifu2d = true,
                    message = "模型中仍有已转换的 Waifu2d 材质，已保留 Root Bone 修复。",
                    messageType = MessageType.Info,
                    resolvedRoot = resolvedRoot
                };
            }
        }

        private static bool PrepareMaterialForEditing(Material material, ref int converted)
        {
            if(material == null || material.shader == null || !IsSupportedShader(material.shader))
            {
                return false;
            }

            if(!IsWaifu2dShader(material.shader))
            {
                Material shaderOwner = GetShaderOwner(material);
                if(!IsWaifu2dShader(shaderOwner.shader))
                {
                    Shader targetShader = GetWaifu2dShader(shaderOwner.shader);
                    if(targetShader == null) return false;

                    int renderQueue = shaderOwner.renderQueue;
                    Undo.RecordObject(shaderOwner, "应用 Lyuma Waifu2d");
                    shaderOwner.shader = targetShader;
                    InitializeWaifu2dMaterial(shaderOwner);
                    shaderOwner.renderQueue = renderQueue;
                    EditorUtility.SetDirty(shaderOwner);
                    converted++;
                }
            }

            return material.HasProperty(TwoDimensionalnessProperty) &&
                material.HasProperty(FacingDirectionProperty) &&
                material.HasProperty(LockAxisProperty) &&
                material.HasProperty(SquashZProperty);
        }

        private static Material GetShaderOwner(Material material)
        {
            Material shaderOwner = material;
            var visited = new HashSet<Material>();
            while(shaderOwner != null && shaderOwner.parent != null && visited.Add(shaderOwner))
            {
                shaderOwner = shaderOwner.parent;
            }
            return shaderOwner != null ? shaderOwner : material;
        }

        private void GenerateStrengthAnimations()
        {
            GameObject root = modelRoot != null ? modelRoot : Selection.activeGameObject;
            if(root == null)
            {
                SetStatus("生成动画需要一个模型根对象。请拖入模型，或在层级窗口选中模型根对象。", MessageType.Warning);
                return;
            }

            modelRoot = root;
            ScanResult rootScan = CollectMaterials(new UnityEngine.Object[] { root });
            SetTargets(rootScan, "模型");
            List<Material> materials = GetUsableTargets();
            if(materials.Count == 0)
            {
                SetStatus("模型中没有找到受支持的 lilToon、lilToon Custom 或 Poiyomi 材质，未生成动画。", MessageType.Warning);
                return;
            }

            AnimationRendererScanResult animationScan = FindAnimatedRenderers(
                root,
                rootScan.associatedScan
            );
            if(animationScan.renderers.Count == 0)
            {
                SetStatus(
                    string.Format(
                        "模型中没有可以安全生成曲线的 Renderer，未生成动画。" +
                        "\n未转换关联材质 {0} 个：{1}" +
                        "\n因混用未转换材质跳过 Renderer {2} 个：{3}" +
                        "\n已转换但无法确定目标 Renderer 的关联材质 {4} 个：{5}",
                        animationScan.unconvertedMaterials.Count,
                        FormatObjectNames(animationScan.unconvertedMaterials),
                        animationScan.skippedRenderers.Count,
                        FormatObjectNames(animationScan.skippedRenderers),
                        animationScan.unresolvedConvertedMaterials.Count,
                        FormatObjectNames(animationScan.unresolvedConvertedMaterials)
                    ),
                    MessageType.Warning
                );
                return;
            }

            string outputFolder = ResolveAnimationOutputFolder();
            if(string.IsNullOrEmpty(outputFolder)) return;

            string safeRootName = MakeSafeFileName(root.name);
            string disabledPath = outputFolder + "/" + safeRootName + "_Lyuma2D_关闭.anim";
            string enabledPath = outputFolder + "/" + safeRootName + "_Lyuma2D_开启.anim";

            AnimationClip disabledClip = CreateStrengthClip(root, animationScan.renderers, 0.0f);
            AnimationClip enabledClip = CreateStrengthClip(root, animationScan.renderers, 0.99f);
            disabledClip.name = Path.GetFileNameWithoutExtension(disabledPath);
            enabledClip.name = Path.GetFileNameWithoutExtension(enabledPath);

            disabledClip = SaveOrOverwriteClip(disabledClip, disabledPath);
            enabledClip = SaveOrOverwriteClip(enabledClip, enabledPath);
            AssetDatabase.SaveAssets();

            Selection.objects = new UnityEngine.Object[] { disabledClip, enabledClip };
            EditorGUIUtility.PingObject(enabledClip);
            SetStatus(
                string.Format(
                    "已生成 2 个动画，共绑定 {0} 个 Renderer。" +
                    "\n未转换关联材质 {1} 个：{2}" +
                    "\n因混用未转换材质跳过 Renderer {3} 个：{4}" +
                    "\n已转换但无法确定目标 Renderer 的关联材质 {5} 个：{6}" +
                    "\n{7}\n{8}",
                    animationScan.renderers.Count,
                    animationScan.unconvertedMaterials.Count,
                    FormatObjectNames(animationScan.unconvertedMaterials),
                    animationScan.skippedRenderers.Count,
                    FormatObjectNames(animationScan.skippedRenderers),
                    animationScan.unresolvedConvertedMaterials.Count,
                    FormatObjectNames(animationScan.unresolvedConvertedMaterials),
                    disabledPath,
                    enabledPath
                ),
                animationScan.HasWarnings ? MessageType.Warning : MessageType.Info
            );
        }

        private static AnimationClip SaveOrOverwriteClip(AnimationClip generatedClip, string assetPath)
        {
            AnimationClip existingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if(existingClip == null)
            {
                AssetDatabase.CreateAsset(generatedClip, assetPath);
                return generatedClip;
            }

            // Copy into the existing asset instead of deleting it so its GUID and all references remain valid.
            Undo.RecordObject(existingClip, "更新 Lyuma Waifu2d 动画");
            EditorUtility.CopySerialized(generatedClip, existingClip);
            existingClip.name = Path.GetFileNameWithoutExtension(assetPath);
            EditorUtility.SetDirty(existingClip);
            UnityEngine.Object.DestroyImmediate(generatedClip);
            return existingClip;
        }

        private static AnimationClip CreateStrengthClip(
            GameObject root,
            List<Renderer> renderers,
            float value
        )
        {
            var clip = new AnimationClip { frameRate = 60.0f };
            var bindings = new HashSet<string>(StringComparer.Ordinal);

            foreach(Renderer renderer in renderers)
            {
                if(renderer == null) continue;
                string path = AnimationUtility.CalculateTransformPath(renderer.transform, root.transform);
                Type rendererType = GetAnimationRendererType(renderer);
                string bindingKey = path + "\n" + rendererType.FullName;
                if(!bindings.Add(bindingKey)) continue;

                EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                    path,
                    rendererType,
                    "material." + TwoDimensionalnessProperty
                );
                AnimationUtility.SetEditorCurve(
                    clip,
                    binding,
                    AnimationCurve.Constant(0.0f, 1.0f / 60.0f, value)
                );
            }
            return clip;
        }

        private static Type GetAnimationRendererType(Renderer renderer)
        {
            if(renderer is SkinnedMeshRenderer) return typeof(SkinnedMeshRenderer);
            if(renderer is MeshRenderer)
            {
                // A build-time converter changes this component type on the NDMF avatar copy.
                // Generate the final binding type now so the curve still works after conversion.
                if(renderer.GetComponentInParent<LyumaWaifu2dStaticMeshConverter>() != null)
                    return typeof(SkinnedMeshRenderer);
                return typeof(MeshRenderer);
            }
            return typeof(Renderer);
        }

        private static AnimationRendererScanResult FindAnimatedRenderers(
            GameObject root,
            Waifu2dAssociatedMaterialScanner.Result associatedScan
        )
        {
            var result = new AnimationRendererScanResult();
            var mappedMaterials = new HashSet<Material>();
            foreach(Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                bool hasConvertedMaterial = false;
                bool hasUnconvertedMaterial = false;
                ICollection<Material> candidates = associatedScan != null
                    ? associatedScan.GetCandidateMaterials(renderer)
                    : renderer.sharedMaterials;

                foreach(Material material in candidates)
                {
                    if(material == null || material.shader == null ||
                        !IsSupportedShader(material.shader))
                    {
                        continue;
                    }

                    mappedMaterials.Add(material);
                    if(IsWaifu2dShader(material.shader) &&
                        material.HasProperty(TwoDimensionalnessProperty))
                    {
                        hasConvertedMaterial = true;
                    }
                    else
                    {
                        hasUnconvertedMaterial = true;
                        result.unconvertedMaterials.Add(material);
                    }
                }

                if(hasConvertedMaterial && !hasUnconvertedMaterial)
                {
                    result.renderers.Add(renderer);
                }
                else if(hasUnconvertedMaterial)
                {
                    result.skippedRenderers.Add(renderer);
                }
            }

            if(associatedScan != null)
            {
                foreach(Material material in associatedScan.AllMaterials)
                {
                    if(material == null || material.shader == null ||
                        !IsSupportedShader(material.shader))
                    {
                        continue;
                    }

                    if(!IsWaifu2dShader(material.shader))
                    {
                        result.unconvertedMaterials.Add(material);
                    }
                    else if(material.HasProperty(TwoDimensionalnessProperty) &&
                        !mappedMaterials.Contains(material))
                    {
                        result.unresolvedConvertedMaterials.Add(material);
                    }
                }
            }

            return result;
        }

        private static string FormatObjectNames<T>(IEnumerable<T> objects) where T : UnityEngine.Object
        {
            const int maximumNames = 6;
            var names = new List<string>();
            if(objects != null)
            {
                foreach(T target in objects)
                {
                    if(target != null) names.Add(target.name);
                }
            }

            if(names.Count == 0) return "无";
            names.Sort(StringComparer.Ordinal);
            int remaining = names.Count - maximumNames;
            if(remaining > 0) names.RemoveRange(maximumNames, remaining);
            string formatted = string.Join("、", names.ToArray());
            return remaining > 0 ? formatted + " 等另外 " + remaining + " 个" : formatted;
        }

        private string ResolveAnimationOutputFolder()
        {
            if(animationOutputFolder != null)
            {
                string selectedPath = AssetDatabase.GetAssetPath(animationOutputFolder);
                if(AssetDatabase.IsValidFolder(selectedPath)) return selectedPath;

                SetStatus("“动画输出文件夹”必须是 Assets 下的文件夹。", MessageType.Warning);
                return null;
            }

            EnsureAssetFolder(DefaultAnimationFolder);
            return DefaultAnimationFolder;
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string currentPath = parts[0];
            for(int i = 1; i < parts.Length; i++)
            {
                string nextPath = currentPath + "/" + parts[i];
                if(!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, parts[i]);
                }
                currentPath = nextPath;
            }
        }

        private static string MakeSafeFileName(string fileName)
        {
            if(string.IsNullOrEmpty(fileName)) return "Model";
            foreach(char invalidCharacter in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalidCharacter, '_');
            }
            return fileName;
        }

        private static void BeginMaterialUndo(string groupName, out int group)
        {
            Undo.IncrementCurrentGroup();
            group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(groupName);
        }

        private static void FinishMaterialUndo(int group)
        {
            Undo.CollapseUndoOperations(group);
            AssetDatabase.SaveAssets();
        }

        private void SetTargets(ScanResult result, string sourceName)
        {
            targetMaterials.Clear();
            targetMaterials.AddRange(result.supportedMaterials);
            SetStatus(
                string.Format(
                    "已读取{0}：扫描 {1} 个 Renderer、{2} 个控制器、{3} 个动画，找到 {4} 个不重复材质" +
                    "（Renderer 当前引用 {5}、组件引用 {6}、动画换材质引用 {7}），其中 {8} 个受支持。" +
                    "序列化读取失败 {9} 个组件。",
                    sourceName,
                    result.rendererCount,
                    result.controllerCount,
                    result.animationClipCount,
                    result.allMaterialCount,
                    result.rendererMaterialCount,
                    result.componentMaterialCount,
                    result.animationMaterialCount,
                    result.supportedMaterials.Count,
                    result.serializationFailureCount
                ),
                result.supportedMaterials.Count > 0 && result.serializationFailureCount == 0
                    ? MessageType.Info
                    : MessageType.Warning
            );
            Repaint();
        }

        private List<Material> GetUsableTargets()
        {
            RemoveInvalidAndDuplicateTargets();
            return new List<Material>(targetMaterials);
        }

        private void RemoveInvalidAndDuplicateTargets()
        {
            var unique = new HashSet<Material>();
            for(int i = 0; i < targetMaterials.Count; i++)
            {
                Material material = targetMaterials[i];
                if(material == null || material.shader == null ||
                    !IsSupportedShader(material.shader) || !unique.Add(material))
                {
                    targetMaterials.RemoveAt(i);
                    i--;
                }
            }
        }

        private static ScanResult CollectMaterials(IEnumerable<UnityEngine.Object> objects)
        {
            Waifu2dAssociatedMaterialScanner.Result associatedScan =
                Waifu2dAssociatedMaterialScanner.Collect(objects);
            var supportedMaterials = new List<Material>();

            foreach(Material material in associatedScan.AllMaterials)
            {
                if(material != null && material.shader != null && IsSupportedShader(material.shader))
                {
                    supportedMaterials.Add(material);
                }
            }
            supportedMaterials.Sort((left, right) => string.Compare(left.name, right.name, StringComparison.Ordinal));

            return new ScanResult
            {
                supportedMaterials = supportedMaterials,
                rendererCount = associatedScan.RendererCount,
                controllerCount = associatedScan.ControllerCount,
                animationClipCount = associatedScan.AnimationClipCount,
                allMaterialCount = associatedScan.AllMaterials.Count,
                rendererMaterialCount = associatedScan.RendererReferencedMaterials.Count,
                componentMaterialCount = associatedScan.ComponentReferencedMaterials.Count,
                animationMaterialCount = associatedScan.AnimationReferencedMaterials.Count,
                serializationFailureCount = associatedScan.SerializationFailureCount,
                associatedScan = associatedScan
            };
        }

        private static bool IsSupportedShader(Shader shader)
        {
            return LilToonWaifu2dAdapter.IsSupported(shader) ||
                GenericLilCustomWaifu2dAdapter.IsSupported(shader) ||
                PoiyomiWaifu2dAdapter.IsSupported(shader);
        }

        private static bool IsWaifu2dShader(Shader shader)
        {
            return LilToonWaifu2dAdapter.IsWaifu2dShader(shader) ||
                GenericLilCustomWaifu2dAdapter.IsWaifu2dShader(shader) ||
                PoiyomiWaifu2dAdapter.IsWaifu2dShader(shader);
        }

        private static Shader GetWaifu2dShader(Shader source)
        {
            if(LilToonWaifu2dAdapter.IsSupported(source))
            {
                return LilToonWaifu2dAdapter.GetWaifu2dShader(source);
            }
            if(GenericLilCustomWaifu2dAdapter.IsSupported(source))
            {
                return GenericLilCustomWaifu2dAdapter.GetWaifu2dShader(source);
            }
            return PoiyomiWaifu2dAdapter.GetWaifu2dShader(source);
        }

        private static Shader GetOriginalShader(Shader source)
        {
            Shader lilToonOriginal = LilToonWaifu2dAdapter.GetOriginalShader(source);
            if(lilToonOriginal != null) return lilToonOriginal;
            Shader lilCustomOriginal = GenericLilCustomWaifu2dAdapter.GetOriginalShader(source);
            return lilCustomOriginal != null ? lilCustomOriginal : PoiyomiWaifu2dAdapter.GetOriginalShader(source);
        }

        private static void InitializeWaifu2dMaterial(Material material)
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
            else
            {
                PoiyomiWaifu2dAdapter.InitializeMaterial(material);
            }
        }

        private void SetStatus(string message, MessageType messageType)
        {
            statusMessage = message;
            statusType = messageType;
            Repaint();
        }

        private struct ScanResult
        {
            internal List<Material> supportedMaterials;
            internal int rendererCount;
            internal int controllerCount;
            internal int animationClipCount;
            internal int allMaterialCount;
            internal int rendererMaterialCount;
            internal int componentMaterialCount;
            internal int animationMaterialCount;
            internal int serializationFailureCount;
            internal Waifu2dAssociatedMaterialScanner.Result associatedScan;
        }

        private sealed class AnimationRendererScanResult
        {
            internal readonly List<Renderer> renderers = new List<Renderer>();
            internal readonly HashSet<Material> unconvertedMaterials = new HashSet<Material>();
            internal readonly HashSet<Material> unresolvedConvertedMaterials = new HashSet<Material>();
            internal readonly HashSet<Renderer> skippedRenderers = new HashSet<Renderer>();

            internal bool HasWarnings
            {
                get
                {
                    return unconvertedMaterials.Count > 0 ||
                        unresolvedConvertedMaterials.Count > 0 ||
                        skippedRenderers.Count > 0;
                }
            }
        }
    }
}
#endif
