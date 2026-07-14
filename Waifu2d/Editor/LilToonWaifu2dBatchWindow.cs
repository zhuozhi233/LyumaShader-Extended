#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
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

        [SerializeField] private GameObject modelRoot;
        [SerializeField] private DefaultAsset animationOutputFolder;
        [SerializeField] private List<Material> targetMaterials = new List<Material>();

        [SerializeField] private bool applyFacingDirection = true;
        [SerializeField] private bool applyLockAxis = true;
        [SerializeField] private bool applySquashZ = true;
        [SerializeField] private float facingDirection;
        [SerializeField] private float lockAxis = 1.0f;
        [SerializeField] private float squashZ = 0.975f;

        [SerializeField] private bool showTargetMaterials;
        private Vector2 scrollPosition;
        private string statusMessage = "请拖入模型，或在层级/项目窗口中多选对象后读取。";
        private MessageType statusType = MessageType.Info;

        [MenuItem("Tools/LyumaShader/lilToon（含 Custom）+ Poiyomi 批量 2D 工具")]
        private static void OpenWindow()
        {
            var window = GetWindow<LilToonWaifu2dBatchWindow>();
            window.titleContent = new GUIContent("Waifu2d 批量工具");
            window.minSize = new Vector2(470.0f, 590.0f);
            window.Show();
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
                "转换受支持的 lilToon、lilToon Custom 与 Poiyomi 材质；Custom Shader 会在 LyumaShader/Generated 中生成组合副本，原插件文件不会被修改。已经转换的材质会保留现有参数。",
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
        }

        private void DrawGeneralParametersSection()
        {
            EditorGUILayout.LabelField("批量参数", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("勾选需要写入的参数。未勾选的参数保持原值。", MessageType.None);

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
                new GUIContent("Z 深度修正", "Squash Z：通常推荐 0.95 到 0.975"),
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
                "以“模型根对象”为动画根节点，为其所有使用 Lyuma Waifu2d lilToon、lilToon Custom 或 Poiyomi 材质的 Renderer 建立曲线。" +
                "只处理已经转换的材质；未转换材质不会生成对应动画曲线。",
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
            if(!applyFacingDirection && !applyLockAxis && !applySquashZ)
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

            SetStatus(
                string.Format(
                    "{0}：已恢复 {1} 个基础材质，原本未转换 {2} 个，失败 {3} 个。",
                    sourceName,
                    reverted,
                    alreadyOriginal,
                    failed
                ),
                failed == 0 ? MessageType.Info : MessageType.Warning
            );
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

            int skippedUnconvertedMaterials = 0;
            foreach(Material material in materials)
            {
                if(material == null || material.shader == null || !IsWaifu2dShader(material.shader))
                {
                    skippedUnconvertedMaterials++;
                }
            }

            List<Renderer> animatedRenderers = FindAnimatedRenderers(root);
            if(animatedRenderers.Count == 0)
            {
                SetStatus("模型中没有使用已转换 Waifu2d 材质的 Renderer，未生成动画。", MessageType.Warning);
                return;
            }

            string outputFolder = ResolveAnimationOutputFolder();
            if(string.IsNullOrEmpty(outputFolder)) return;

            string safeRootName = MakeSafeFileName(root.name);
            string disabledPath = AssetDatabase.GenerateUniqueAssetPath(
                outputFolder + "/" + safeRootName + "_Lyuma2D_关闭.anim"
            );
            string enabledPath = AssetDatabase.GenerateUniqueAssetPath(
                outputFolder + "/" + safeRootName + "_Lyuma2D_开启.anim"
            );

            AnimationClip disabledClip = CreateStrengthClip(root, animatedRenderers, 0.0f);
            AnimationClip enabledClip = CreateStrengthClip(root, animatedRenderers, 0.99f);
            disabledClip.name = Path.GetFileNameWithoutExtension(disabledPath);
            enabledClip.name = Path.GetFileNameWithoutExtension(enabledPath);

            AssetDatabase.CreateAsset(disabledClip, disabledPath);
            AssetDatabase.CreateAsset(enabledClip, enabledPath);
            AssetDatabase.SaveAssets();

            Selection.objects = new UnityEngine.Object[] { disabledClip, enabledClip };
            EditorGUIUtility.PingObject(enabledClip);
            SetStatus(
                string.Format(
                    "已生成 2 个动画，共绑定 {0} 个 Renderer；跳过 {1} 个未转换材质。\n{2}\n{3}",
                    animatedRenderers.Count,
                    skippedUnconvertedMaterials,
                    disabledPath,
                    enabledPath
                ),
                MessageType.Info
            );
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
            if(renderer is MeshRenderer) return typeof(MeshRenderer);
            return typeof(Renderer);
        }

        private static List<Renderer> FindAnimatedRenderers(GameObject root)
        {
            var result = new List<Renderer>();
            foreach(Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                bool hasWaifu2dMaterial = false;
                foreach(Material material in renderer.sharedMaterials)
                {
                    if(material != null && material.shader != null &&
                        IsWaifu2dShader(material.shader) &&
                        material.HasProperty(TwoDimensionalnessProperty))
                    {
                        hasWaifu2dMaterial = true;
                        break;
                    }
                }
                if(hasWaifu2dMaterial) result.Add(renderer);
            }
            return result;
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
                    "已读取{0}：扫描 {1} 个 Renderer、{2} 个不重复材质，其中 {3} 个是受支持的 lilToon、lilToon Custom 或 Poiyomi 材质。",
                    sourceName,
                    result.rendererCount,
                    result.allMaterialCount,
                    result.supportedMaterials.Count
                ),
                result.supportedMaterials.Count > 0 ? MessageType.Info : MessageType.Warning
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
            var allMaterials = new HashSet<Material>();
            var supportedMaterials = new List<Material>();
            var visitedRenderers = new HashSet<Renderer>();

            if(objects != null)
            {
                foreach(UnityEngine.Object sourceObject in objects)
                {
                    if(sourceObject == null) continue;
                    Material directMaterial = sourceObject as Material;
                    if(directMaterial != null)
                    {
                        allMaterials.Add(directMaterial);
                        continue;
                    }

                    GameObject gameObject = sourceObject as GameObject;
                    Component component = sourceObject as Component;
                    if(gameObject == null && component != null) gameObject = component.gameObject;
                    if(gameObject == null) continue;

                    foreach(Renderer renderer in gameObject.GetComponentsInChildren<Renderer>(true))
                    {
                        if(renderer == null || !visitedRenderers.Add(renderer)) continue;
                        foreach(Material material in renderer.sharedMaterials)
                        {
                            if(material != null) allMaterials.Add(material);
                        }
                    }
                }
            }

            foreach(Material material in allMaterials)
            {
                if(material.shader != null && IsSupportedShader(material.shader))
                {
                    supportedMaterials.Add(material);
                }
            }
            supportedMaterials.Sort((left, right) => string.Compare(left.name, right.name, StringComparison.Ordinal));

            return new ScanResult
            {
                supportedMaterials = supportedMaterials,
                rendererCount = visitedRenderers.Count,
                allMaterialCount = allMaterials.Count
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
            internal int allMaterialCount;
        }
    }
}
#endif
