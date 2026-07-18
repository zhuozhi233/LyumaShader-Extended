#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace LyumaShader
{
    /// <summary>
    /// Editor front-end for the non-destructive Waifu2d NDMF configuration.
    /// Shader caches may be prepared in edit mode, but source materials and
    /// controllers are only cloned and converted on NDMF's build copy.
    /// </summary>
    public sealed class LilToonWaifu2dBatchWindow : EditorWindow
    {
        private const string DefaultAnimationFolder = "Assets/LyumaShader/GeneratedAnimations";
        private const string ToggleIconPath =
            "Packages/com.zhuozhi.lyumashader-extended/Waifu2d/Resources/Waifu2dTransparent.png";
        private const string ToggleParameterName = "zhz/Lyuma2D";
        private const string ToggleDisplayName =
            "<b><size=35><line-height=100%><voffset=3.8em>2D</b>";
        private const string TogglePrefabFileName = "切换2D开关";
        private const string WindowTitle = "Waifu2d 配置工具 by 浊鸷";
        private const string TwoDimensionalnessProperty = "_2d_coef";
        private const string FacingDirectionProperty = "_facing_coef";
        private const string LockAxisProperty = "_lock2daxis_coef";
        private const string SquashZProperty = "_zcorrect_coef";
        private const int CurrentSettingsVersion = 4;
        private static readonly string[] MainPageNames =
        {
            "材质规则",
            "2D 参数",
            "构建设置"
        };

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
        [SerializeField] private float squashZ = 1.0f;
        [SerializeField] private int settingsVersion;

        [SerializeField] private int selectedMainPage;
        [SerializeField] private bool showDirectToolsSection;
        [SerializeField] private bool thirdPartyShaderVariantRiskAccepted;
        private string materialSearch = string.Empty;
        private readonly Dictionary<int, bool> materialDetailFoldouts =
            new Dictionary<int, bool>();
        private Vector2 scrollPosition;
        private string statusMessage = "请选择目标模型。";
        private MessageType statusType = MessageType.Info;

        [MenuItem("Tools/LyumaShader Extended/Waifu2d 配置工具")]
        private static void OpenWindow()
        {
            ShowWindow();
        }

        internal static void OpenForConfiguration(
            LyumaWaifu2dAvatarConfig configuration
        )
        {
            LilToonWaifu2dBatchWindow window = ShowWindow();
            if(configuration == null) return;

            window.modelRoot = configuration.gameObject;
            window.targetMaterials.Clear();
            window.thirdPartyShaderVariantRiskAccepted = false;
            window.LoadWindowParametersFromConfiguration();
            ScanResult result = CollectMaterials(
                new UnityEngine.Object[] { configuration.gameObject }
            );
            window.SetTargets(result, "当前模型");
            window.selectedMainPage = 0;
            window.Repaint();
        }

        private static LilToonWaifu2dBatchWindow ShowWindow()
        {
            var window = GetWindow<LilToonWaifu2dBatchWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(430.0f, 460.0f);
            window.Show();
            return window;
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            if(settingsVersion >= CurrentSettingsVersion) return;
            applyTwoDimensionalness = true;
            if(twoDimensionalness <= 0.0f) twoDimensionalness = 0.99f;
            if(Mathf.Approximately(squashZ, 0.975f) || Mathf.Approximately(squashZ, 0.8f))
                squashZ = 1.0f;
            selectedMainPage = 0;
            showDirectToolsSection = false;
            settingsVersion = CurrentSettingsVersion;
        }

        private void OnGUI()
        {
            if(titleContent.text != WindowTitle)
                titleContent = new GUIContent(WindowTitle);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawWindowHeader();
            EditorGUILayout.Space(4.0f);
            DrawQuickActionsSection();
            EditorGUILayout.Space(6.0f);

            selectedMainPage = GUILayout.Toolbar(
                Mathf.Clamp(selectedMainPage, 0, MainPageNames.Length - 1),
                MainPageNames,
                GUILayout.Height(27.0f)
            );
            EditorGUILayout.Space(5.0f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            switch(selectedMainPage)
            {
                case 1:
                    DrawGeneralParametersSection();
                    break;
                case 2:
                    DrawBuildOptionsSection();
                    break;
                default:
                    DrawTargetSection();
                    break;
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4.0f);

            showDirectToolsSection = EditorGUILayout.Foldout(
                showDirectToolsSection,
                "高级：直接修改模型",
                true
            );
            if(showDirectToolsSection)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawDirectToolsSection();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.Space(4.0f);

            if(!string.IsNullOrEmpty(statusMessage))
                EditorGUILayout.HelpBox(statusMessage, statusType);
            EditorGUILayout.EndScrollView();
        }

        private static void DrawWindowHeader()
        {
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleLeft
            };
            GUIStyle watermarkStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight
            };
            EditorGUILayout.LabelField(
                "LyumaShader Extended · Waifu2d 配置工具",
                titleStyle,
                GUILayout.Height(22.0f)
            );
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                "非破坏材质转换与构建配置",
                EditorStyles.miniLabel
            );
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(
                "by 浊鸷",
                watermarkStyle,
                GUILayout.Width(52.0f),
                GUILayout.Height(EditorGUIUtility.singleLineHeight)
            );
            EditorGUILayout.EndHorizontal();
        }

        private void DrawQuickActionsSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginChangeCheck();
            GameObject newRoot = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent(
                    "目标模型",
                    "可使用场景对象或可编辑 Prefab；FBX 资源不能直接保存 NDMF 配置。"
                ),
                modelRoot,
                typeof(GameObject),
                true
            );
            if(EditorGUI.EndChangeCheck())
            {
                modelRoot = newRoot;
                targetMaterials.Clear();
                thirdPartyShaderVariantRiskAccepted = false;
                LoadWindowParametersFromConfiguration();
                SetStatus(
                    modelRoot != null
                        ? "已选择模型。点击“一键配置”或重新扫描以更新 NDMF 配置。"
                        : "请指定模型根对象。",
                    MessageType.Info
                );
            }

            DrawConfigurationStatus();
            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button("一键配置", GUILayout.Height(27.0f)))
            {
                RunCompleteWorkflow();
            }
            if(GUILayout.Button("移除配置", GUILayout.Height(27.0f)))
            {
                RunCompleteRemoval();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawConfigurationStatus()
        {
            LyumaWaifu2dAvatarConfig configuration = GetConfiguration(false);
            string status;
            if(modelRoot == null)
            {
                status = "未选择目标模型";
            }
            else if(configuration == null)
            {
                status = "尚未添加 Waifu2d NDMF 配置";
            }
            else
            {
                status = string.Format(
                    "NDMF 配置已启用 · {0} 个材质规则 · {1} 个参与转换",
                    configuration.Materials != null
                        ? configuration.Materials.Count
                        : 0,
                    CountEnabledRules(configuration)
                );
            }
            EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
        }

        private void DrawTargetSection()
        {
            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button("重新扫描", GUILayout.Height(24.0f)))
            {
                if(modelRoot == null)
                {
                    SetStatus("请先拖入模型根对象。", MessageType.Warning);
                }
                else
                {
                    ScanModelIntoConfiguration();
                }
            }
            if(GUILayout.Button("启用官方材质", GUILayout.Height(24.0f)))
            {
                ConfigureMaterials(GetUsableTargets(), true, "已扫描材质");
            }
            if(thirdPartyShaderVariantRiskAccepted &&
                GUILayout.Button("启用所有材质", GUILayout.Height(24.0f)))
            {
                ConfigureMaterials(
                    GetUsableTargets(),
                    true,
                    "已扫描材质",
                    true
                );
            }
            if(GUILayout.Button("全部停用", GUILayout.Height(24.0f)))
            {
                ConfigureMaterials(GetUsableTargets(), false, "已扫描材质");
            }
            EditorGUILayout.EndHorizontal();

            materialSearch = EditorGUILayout.TextField(
                materialSearch,
                EditorStyles.toolbarSearchField
            );

            RemoveInvalidAndDuplicateTargets();
            int lilToonCount = 0;
            int lilCustomCount = 0;
            int poiyomiCount = 0;
            int materialVariantCount = 0;
            int thirdPartyShaderVariantCount = 0;
            int visibleMaterialCount = 0;
            foreach(Material material in targetMaterials)
            {
                if(material == null) continue;
                if(MatchesMaterialSearch(material)) visibleMaterialCount++;
                if(material.isVariant) materialVariantCount++;
                if(IsThirdPartyShaderVariant(material))
                {
                    thirdPartyShaderVariantCount++;
                }
                Shader materialShader = GetMaterialShader(material);
                if(materialShader == null) continue;
                if(LilToonWaifu2dAdapter.IsSupported(materialShader))
                {
                    lilToonCount++;
                }
                else if(GenericLilCustomWaifu2dAdapter.IsSupported(materialShader))
                {
                    lilCustomCount++;
                }
                else if(PoiyomiWaifu2dAdapter.IsSupported(materialShader))
                {
                    poiyomiCount++;
                }
            }

            LyumaWaifu2dAvatarConfig configuration = GetConfiguration(false);
            EditorGUILayout.LabelField(
                string.Format(
                    "材质 {0}  ·  已启用 {1}  ·  lilToon {2}  ·  Custom {3}  ·  Poiyomi {4}  ·  变体 {5}",
                    targetMaterials.Count,
                    CountEnabledRules(configuration),
                    lilToonCount,
                    lilCustomCount,
                    poiyomiCount,
                    materialVariantCount
                ),
                EditorStyles.wordWrappedMiniLabel
            );
            if(thirdPartyShaderVariantCount > 0)
            {
                EditorGUILayout.HelpBox(
                    string.Format(
                        "检测到 {0} 个第三方变体着色器。工具默认只启用官方 lilToon 和 Poiyomi；" +
                        "标记为 Custom* 或 Motchiri* 的材质需要手动勾选启用，且不保证能够正确兼容。",
                        thirdPartyShaderVariantCount
                    ),
                    MessageType.Warning
                );
            }
            if(!string.IsNullOrWhiteSpace(materialSearch))
            {
                EditorGUILayout.LabelField(
                    string.Format(
                        "搜索结果：{0} / {1}",
                        visibleMaterialCount,
                        targetMaterials.Count
                    ),
                    EditorStyles.miniLabel
                );
            }
            EditorGUILayout.Space(3.0f);

            if(configuration == null)
            {
                EditorGUILayout.HelpBox(
                    "点击“一键配置”或“重新扫描”后即可设置各材质规则。",
                    MessageType.Info
                );
                using(new EditorGUI.DisabledScope(true))
                {
                    foreach(Material material in targetMaterials)
                    {
                        if(!MatchesMaterialSearch(material)) continue;
                        EditorGUILayout.ObjectField(
                            material,
                            typeof(Material),
                            false
                        );
                    }
                }
            }
            else
            {
                DrawMaterialRules(configuration);
            }
        }

        private void DrawMaterialRules(LyumaWaifu2dAvatarConfig configuration)
        {
            if(configuration == null) return;
            ReconcileMaterialRules(configuration, targetMaterials, false);

            foreach(Material material in targetMaterials)
            {
                if(material == null) continue;
                if(!MatchesMaterialSearch(material)) continue;
                LyumaWaifu2dAvatarConfig.MaterialRule rule =
                    configuration.FindRule(material);
                if(rule == null) continue;

                Shader shader = GetMaterialShader(material);
                bool isCustom = shader != null &&
                    GenericLilCustomWaifu2dAdapter.IsSupported(shader);
                bool isMotchiri = shader != null &&
                    shader.name.IndexOf(
                        "motchiri",
                        StringComparison.OrdinalIgnoreCase
                    ) >= 0;
                bool becomesMotchiriAtBuild =
                    IsMaterialScheduledForMotchiri(material);
                if(becomesMotchiriAtBuild)
                {
                    isCustom = true;
                    isMotchiri = true;
                }
                bool isThirdPartyShaderVariant =
                    isCustom || becomesMotchiriAtBuild;
                bool canBecomeCustom = isCustom ||
                    (shader != null &&
                        LilToonWaifu2dAdapter.IsSupported(shader));
                bool hasDetails = true;
                int materialId = material.GetInstanceID();
                bool showDetails;
                materialDetailFoldouts.TryGetValue(
                    materialId,
                    out showDetails
                );

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                bool convert = EditorGUILayout.Toggle(
                    rule.Convert,
                    GUILayout.Width(18.0f)
                );
                EditorGUILayout.ObjectField(
                    material,
                    typeof(Material),
                    false
                );
                string shaderType = isMotchiri
                    ? "Motchiri*"
                    : isCustom
                        ? "Custom*"
                        : shader != null &&
                            PoiyomiWaifu2dAdapter.IsSupported(shader)
                            ? "Poiyomi"
                            : "lilToon";
                GUILayout.Label(
                    shaderType,
                    EditorStyles.miniLabel,
                    GUILayout.Width(58.0f)
                );
                if(hasDetails)
                {
                    bool newShowDetails = GUILayout.Toggle(
                        showDetails,
                        showDetails ? "收起" : "设置",
                        EditorStyles.miniButton,
                        GUILayout.Width(42.0f)
                    );
                    if(newShowDetails != showDetails)
                    {
                        showDetails = newShowDetails;
                        materialDetailFoldouts[materialId] = showDetails;
                    }
                }
                EditorGUI.EndChangeCheck();
                if(convert != rule.Convert &&
                    convert &&
                    isThirdPartyShaderVariant &&
                    !ConfirmThirdPartyShaderVariant(material))
                {
                    convert = false;
                }
                if(convert != rule.Convert)
                {
                    RecordConfiguration(configuration, "修改 Waifu2d 材质规则");
                    rule.Convert = convert;
                    if(convert) PrepareConfiguredShader(rule);
                    SaveConfiguration(configuration);
                    if(convert && isThirdPartyShaderVariant)
                    {
                        thirdPartyShaderVariantRiskAccepted = true;
                        EditorUtility.SetDirty(this);
                        SetStatus(
                            "已手动启用第三方变体着色器。第三方着色器结构可能与官方版本不同，兼容性不作保证。",
                            MessageType.Warning
                        );
                    }
                }
                EditorGUILayout.EndHorizontal();

                if(!hasDetails || !showDetails)
                {
                    EditorGUILayout.EndVertical();
                    continue;
                }

                EditorGUI.indentLevel++;
                DrawMaterialParameterOverrides(configuration, rule);

                if(material.isVariant)
                {
                    EditorGUI.BeginChangeCheck();
                    bool flatten = EditorGUILayout.ToggleLeft(
                        "构建时展开材质变体后转换",
                        rule.FlattenMaterialVariant
                    );
                    if(EditorGUI.EndChangeCheck())
                    {
                        RecordConfiguration(configuration, "修改 Waifu2d 材质变体规则");
                        rule.FlattenMaterialVariant = flatten;
                        SaveConfiguration(configuration);
                    }
                }

                if(canBecomeCustom)
                {
                    EditorGUI.BeginChangeCheck();
                    bool merge = EditorGUILayout.ToggleLeft(
                        isMotchiri
                            ? "合并 Motchiri Shader"
                            : isCustom
                                ? "合并 lilToon Custom Shader"
                                : "构建时如变为 lilToon Custom Shader 则合并",
                        rule.MergeCustomShader
                    );
                    if(EditorGUI.EndChangeCheck())
                    {
                        RecordConfiguration(configuration, "修改 Waifu2d Custom Shader 规则");
                        rule.MergeCustomShader = merge;
                        if(merge && rule.Convert) PrepareConfiguredShader(rule);
                        SaveConfiguration(configuration);
                    }

                    using(new EditorGUI.DisabledScope(
                        !isMotchiri || !rule.MergeCustomShader))
                    {
                        EditorGUI.BeginChangeCheck();
                        GUIContent keepLogicLabel = isMotchiri
                            ? new GUIContent(
                                "2D 模式启用 Motchiri 变形",
                                "关闭后 Motchiri 仅在 3D 状态生效。"
                            )
                            : new GUIContent(
                                "2D 模式启用原着色器顶点逻辑",
                                "当前着色器使用自动兼容模式。"
                            );
                        bool keepLogic = EditorGUILayout.ToggleLeft(
                            keepLogicLabel,
                            rule.EnableCustomLogicIn2D
                        );
                        if(EditorGUI.EndChangeCheck())
                        {
                            RecordConfiguration(configuration, "修改 Waifu2d 顶点逻辑规则");
                            rule.EnableCustomLogicIn2D = keepLogic;
                            SaveConfiguration(configuration);
                        }
                    }
                }
                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();
            }
        }

        private static void DrawMaterialParameterOverrides(
            LyumaWaifu2dAvatarConfig configuration,
            LyumaWaifu2dAvatarConfig.MaterialRule rule
        )
        {
            EditorGUI.BeginChangeCheck();
            bool overrideParameters = EditorGUILayout.ToggleLeft(
                "单独设置 2D 参数",
                rule.OverrideParameters
            );
            if(EditorGUI.EndChangeCheck())
            {
                RecordConfiguration(
                    configuration,
                    "修改 Waifu2d 材质参数模式"
                );
                if(overrideParameters && !rule.OverrideParameters)
                {
                    rule.TwoDimensionalness =
                        configuration.TwoDimensionalness;
                    rule.FacingDirection = configuration.FacingDirection;
                    rule.LockAxis = configuration.LockAxis;
                    rule.SquashZ = configuration.SquashZ;
                }
                rule.OverrideParameters = overrideParameters;
                SaveConfiguration(configuration);
            }

            if(!rule.OverrideParameters) return;

            EditorGUI.BeginChangeCheck();
            float twoDimensionalness = EditorGUILayout.Slider(
                "2D 强度",
                rule.TwoDimensionalness,
                0.0f,
                1.0f
            );
            float facingDirection = EditorGUILayout.Slider(
                "朝向",
                rule.FacingDirection,
                -1.0f,
                1.0f
            );
            float lockAxis = EditorGUILayout.Slider(
                "锁定 2D 轴",
                rule.LockAxis,
                0.0f,
                1.0f
            );
            float squashZ = EditorGUILayout.Slider(
                "Z 深度修正",
                rule.SquashZ,
                0.0f,
                1.0f
            );
            if(EditorGUI.EndChangeCheck())
            {
                RecordConfiguration(
                    configuration,
                    "修改 Waifu2d 材质参数"
                );
                rule.TwoDimensionalness = twoDimensionalness;
                rule.FacingDirection = facingDirection;
                rule.LockAxis = lockAxis;
                rule.SquashZ = squashZ;
                SaveConfiguration(configuration);
            }
            EditorGUILayout.Space(2.0f);
        }

        private bool MatchesMaterialSearch(Material material)
        {
            if(material == null) return false;
            string search = materialSearch != null
                ? materialSearch.Trim()
                : string.Empty;
            if(string.IsNullOrEmpty(search)) return true;
            if(material.name.IndexOf(
                search,
                StringComparison.OrdinalIgnoreCase
            ) >= 0)
            {
                return true;
            }

            Shader shader = GetMaterialShader(material);
            return shader != null &&
                shader.name.IndexOf(
                    search,
                    StringComparison.OrdinalIgnoreCase
                ) >= 0;
        }

        private bool IsMaterialScheduledForMotchiri(Material material)
        {
            if(modelRoot == null || material == null) return false;

            foreach(Component component in
                modelRoot.GetComponentsInChildren<Component>(true))
            {
                if(component == null ||
                    !string.Equals(
                        component.GetType().Name,
                        "motchiri_shader_MA",
                        StringComparison.Ordinal
                    ))
                {
                    continue;
                }

                SerializedObject serialized;
                try
                {
                    serialized = new SerializedObject(component);
                }
                catch(Exception)
                {
                    continue;
                }

                SerializedProperty renderers =
                    serialized.FindProperty("_meshRenderer");
                SerializedProperty slots =
                    serialized.FindProperty("_meshMaterialSlot");
                if(renderers == null ||
                    slots == null ||
                    !renderers.isArray ||
                    !slots.isArray)
                {
                    continue;
                }

                int count = Mathf.Min(renderers.arraySize, slots.arraySize);
                for(int index = 0; index < count; index++)
                {
                    Renderer renderer = renderers
                        .GetArrayElementAtIndex(index)
                        .objectReferenceValue as Renderer;
                    int slot = slots
                        .GetArrayElementAtIndex(index)
                        .intValue;
                    if(renderer == null ||
                        slot < 0 ||
                        slot >= renderer.sharedMaterials.Length)
                    {
                        continue;
                    }

                    if(renderer.sharedMaterials[slot] == material) return true;
                }
            }
            return false;
        }

        private bool ConfirmThirdPartyShaderVariant(Material material)
        {
            string materialName = material != null
                ? material.name
                : "未知材质";
            return EditorUtility.DisplayDialog(
                "启用第三方变体着色器",
                "材质“" + materialName + "”使用第三方变体着色器。\n\n" +
                "第三方着色器的结构和顶点逻辑可能与官方版本不同，自动适配不保证可用，" +
                "可能出现着色器编译错误、原功能失效或渲染异常。\n\n" +
                "确定要手动启用这个材质吗？确认后界面会显示“启用所有材质”按钮。",
                "确定",
                "取消"
            );
        }

        private bool IsThirdPartyShaderVariant(Material material)
        {
            if(material == null) return false;
            Shader shader = GetMaterialShader(material);
            return (shader != null &&
                    GenericLilCustomWaifu2dAdapter.IsSupported(shader)) ||
                IsMaterialScheduledForMotchiri(material);
        }

        private LyumaWaifu2dAvatarConfig.MaterialRule
            CreateDefaultMaterialRule(Material material)
        {
            bool thirdPartyShaderVariant =
                IsThirdPartyShaderVariant(material);
            return new LyumaWaifu2dAvatarConfig.MaterialRule
            {
                Material = material,
                Convert = !thirdPartyShaderVariant,
                // Official shaders must not opt into a Custom Shader merge
                // automatically. A detected third-party rule keeps merge
                // enabled so manually checking Convert is the only opt-in
                // required from the user.
                MergeCustomShader = thirdPartyShaderVariant
            };
        }

        private void RunCompleteWorkflow()
        {
            if(modelRoot == null)
            {
                SetStatus("请先指定模型根对象。", MessageType.Warning);
                return;
            }

            LyumaWaifu2dAvatarConfig configuration = GetConfiguration(true);
            if(configuration == null) return;

            ScanResult scan = CollectMaterials(new UnityEngine.Object[] { modelRoot });
            SetTargets(scan, "模型");
            if(targetMaterials.Count == 0)
            {
                SetStatus("模型中没有找到受支持的 lilToon、lilToon Custom 或 Poiyomi 材质，已停止一键执行。", MessageType.Warning);
                return;
            }

            RecordConfiguration(configuration, "配置 Lyuma Waifu2d NDMF");
            ReconcileMaterialRules(configuration, targetMaterials, true);
            CopyWindowParametersToConfiguration(configuration);
            configuration.GenerateToggle = true;
            configuration.RepairRootBones = true;
            configuration.ConvertStaticMeshes = true;
            PrepareConfiguredShaders(configuration);
            SaveConfiguration(configuration);
            SetStatus(
                string.Format(
                    "已更新 NDMF 配置：启用 {0} 个材质，并开启 2D 开关、Root Bone 修复和普通网格修复。" +
                    "\n原材质、动画和控制器没有被修改；效果会在 NDMF 构建副本中生成。",
                    CountEnabledRules(configuration)
                ),
                MessageType.Info
            );
        }

        private void RunCompleteRemoval()
        {
            if(modelRoot == null)
            {
                SetStatus("请先指定模型根对象。", MessageType.Warning);
                return;
            }

            bool removed = RemoveConfiguration();
            SetStatus(
                removed
                    ? "已移除模型上的 Waifu2d NDMF 配置。原材质和原控制器未被修改，因此不需要还原资源。"
                    : "模型上没有 Waifu2d NDMF 配置。",
                MessageType.Info
            );
        }

        private void DrawBuildOptionsSection()
        {
            LyumaWaifu2dAvatarConfig configuration = GetConfiguration(false);
            if(configuration == null)
            {
                EditorGUILayout.HelpBox(
                    "目标模型还没有配置。修改下面任意选项时会自动创建 NDMF 配置。",
                    MessageType.Info
                );
            }

            bool generateToggle = configuration != null &&
                configuration.GenerateToggle;
            EditorGUI.BeginChangeCheck();
            bool newGenerateToggle = EditorGUILayout.ToggleLeft(
                new GUIContent(
                    "生成 MA 菜单与 2D 开关",
                    "构建时生成 zhz/Lyuma2D 参数、BlendTree 和 MA 菜单开关。"
                ),
                generateToggle
            );
            if(EditorGUI.EndChangeCheck())
            {
                SetBuildToggleEnabled(newGenerateToggle);
                configuration = GetConfiguration(false);
            }

            if(newGenerateToggle && configuration != null)
            {
                DrawToggleMenuSettings(configuration);
            }

            EditorGUILayout.Space(3.0f);
            bool repairRootBones = configuration != null &&
                configuration.RepairRootBones;
            EditorGUI.BeginChangeCheck();
            bool newRepairRootBones = EditorGUILayout.ToggleLeft(
                new GUIContent(
                    "修复蒙皮网格 Root Bone",
                    "在构建副本中将 SkinnedMeshRenderer 与全部 MA Mesh Settings 的 Root Bone 统一到 Hips。"
                ),
                repairRootBones
            );
            if(EditorGUI.EndChangeCheck())
            {
                SetRootBoneBuildRepair(newRepairRootBones);
            }

            EditorGUILayout.Space(3.0f);
            bool convertStaticMeshes = configuration != null &&
                configuration.ConvertStaticMeshes;
            EditorGUI.BeginChangeCheck();
            bool newConvertStaticMeshes = EditorGUILayout.ToggleLeft(
                new GUIContent(
                    "修复普通 MeshRenderer",
                    "把使用已启用 Waifu2d 规则的普通网格在构建期临时转换为单骨骼 SkinnedMeshRenderer。"
                ),
                convertStaticMeshes
            );
            if(EditorGUI.EndChangeCheck())
            {
                SetStaticMeshBuildRepair(newConvertStaticMeshes);
            }
        }

        private void DrawToggleMenuSettings(
            LyumaWaifu2dAvatarConfig configuration
        )
        {
            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            string displayedName = string.Equals(
                configuration.ToggleMenuName,
                ToggleDisplayName,
                StringComparison.Ordinal
            )
                ? string.Empty
                : configuration.ToggleMenuName;
            string newName = EditorGUILayout.TextField(
                new GUIContent(
                    "菜单名称",
                    "留空时使用默认的 2D 富文本名称。"
                ),
                displayedName
            );
            Texture2D newIcon = (Texture2D)EditorGUILayout.ObjectField(
                new GUIContent(
                    "菜单图标",
                    "留空时使用包内的默认透明图片。"
                ),
                configuration.ToggleMenuIcon,
                typeof(Texture2D),
                false
            );
            if(EditorGUI.EndChangeCheck())
            {
                RecordConfiguration(
                    configuration,
                    "修改 Waifu2d 菜单外观"
                );
                configuration.ToggleMenuName = newName;
                configuration.ToggleMenuIcon = newIcon;
                SaveConfiguration(configuration);
            }

            EditorGUI.BeginChangeCheck();
            GameObject newParent = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent(
                    "菜单位置",
                    "留空时安装到菜单根；也可以指定使用“子对象”作为来源的 MA 子菜单或 MA Menu Group。"
                ),
                configuration.ToggleMenuParent,
                typeof(GameObject),
                true
            );
            if(EditorGUI.EndChangeCheck())
            {
                if(IsValidToggleMenuParent(newParent))
                {
                    RecordConfiguration(
                        configuration,
                        "修改 Waifu2d 菜单位置"
                    );
                    configuration.ToggleMenuParent = newParent;
                    SaveConfiguration(configuration);
                    SetStatus(
                        newParent == null
                            ? "2D 开关将安装到模型菜单根。"
                            : "2D 开关将生成到指定的 MA 子菜单中。",
                        MessageType.Info
                    );
                }
                else
                {
                    SetStatus(
                        "菜单位置必须位于当前模型内部，并且带有使用“子对象”作为来源的 MA Menu Item，或 MA Menu Group。",
                        MessageType.Warning
                    );
                }
            }
            EditorGUI.indentLevel--;
        }

        private bool IsValidToggleMenuParent(GameObject candidate)
        {
            if(candidate == null) return true;
            if(modelRoot == null ||
                (candidate != modelRoot &&
                    !candidate.transform.IsChildOf(modelRoot.transform)))
            {
                return false;
            }
            if(candidate == modelRoot) return true;

            ModularAvatarMenuItem menuItem =
                candidate.GetComponent<ModularAvatarMenuItem>();
            if(menuItem != null &&
                menuItem.PortableControl != null &&
                menuItem.PortableControl.Type == PortableControlType.SubMenu &&
                menuItem.MenuSource == SubmenuSource.Children)
            {
                return true;
            }
            return candidate.GetComponent<ModularAvatarMenuGroup>() != null;
        }

        private void DrawDirectToolsSection()
        {
            EditorGUILayout.HelpBox(
                "以下操作会立即修改模型资源，不属于 NDMF 非破坏流程。工具无法自动还原，请只在明确需要时使用。",
                MessageType.Warning
            );
            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button(
                "转换已扫描材质",
                GUILayout.Height(28.0f)))
            {
                ConvertMaterials(GetUsableTargets(), "已扫描材质");
            }
            if(GUILayout.Button(
                "还原已扫描材质",
                GUILayout.Height(28.0f)))
            {
                RevertMaterials(GetUsableTargets(), "已扫描材质");
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button(
                "直接修复全部蒙皮网格",
                GUILayout.Height(28.0f)))
            {
                RunDirectRootBoneRepair();
            }
            if(GUILayout.Button(
                "直接转换全部普通网格",
                GUILayout.Height(28.0f)))
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
            LyumaWaifu2dStaticMeshConverter marker = modelRoot.GetComponent<LyumaWaifu2dStaticMeshConverter>();
            if(marker == null) marker = Undo.AddComponent<LyumaWaifu2dStaticMeshConverter>(modelRoot);
            EditorUtility.SetDirty(modelRoot);
            SetStatus(
                count > 0
                    ? string.Format("已添加 NDMF 构建期转换，构建时将临时转换 {0} 个普通网格。", count)
                    : "已添加 NDMF 构建期转换。当前没有可转换的普通网格；构建时如果仍无目标，将不会修改任何网格。",
                MessageType.Info
            );
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
            List<MeshRenderer> targets = Waifu2dStaticMeshConversion.FindAllTargets(modelRoot);
            if(targets.Count == 0)
            {
                SetStatus("拖入对象中没有找到可转换的 MeshRenderer + MeshFilter。", MessageType.Warning);
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
            SetStatus(string.Format("已将拖入对象下的 {0} 个普通网格转换为 SkinnedMeshRenderer。", converted), MessageType.Info);
        }

        private void DrawGeneralParametersSection()
        {
            EditorGUILayout.LabelField(
                "默认参数会用于没有启用“单独设置 2D 参数”的材质。",
                EditorStyles.wordWrappedMiniLabel
            );
            EditorGUILayout.Space(3.0f);

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
                new GUIContent("Z 深度修正", "Squash Z：推荐 1.0；使用压平后的稳定深度"),
                0.0f,
                1.0f
            );

            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button("写入当前模型配置", GUILayout.Height(27.0f)))
            {
                ApplyParametersToConfiguration("已扫描材质");
            }
            if(GUILayout.Button("从模型配置重新读取", GUILayout.Height(27.0f)))
            {
                LoadWindowParametersFromConfiguration();
                SetStatus("已从当前模型的 NDMF 配置读取参数。", MessageType.Info);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAnimationSection()
        {
            EditorGUILayout.LabelField("生成 2D 开关动画", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "NDMF 会在构建副本中临时生成关闭/开启动画、BlendTree 和 MA 菜单开关。" +
                "不会在 Assets 中生成动画缓存，也不会修改原控制器。参数名保持为 zhz/Lyuma2D。",
                MessageType.Info
            );

            LyumaWaifu2dAvatarConfig configuration = GetConfiguration(false);
            bool enabled = configuration != null && configuration.GenerateToggle;
            string buttonText = enabled
                ? "取消生成构建期 2D 开关"
                : "生成构建期 2D 开关";
            if(GUILayout.Button(buttonText, GUILayout.Height(32.0f)))
            {
                SetBuildToggleEnabled(!enabled);
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

        private void ScanModelIntoConfiguration()
        {
            if(modelRoot == null)
            {
                SetStatus("请先指定模型根对象。", MessageType.Warning);
                return;
            }

            LyumaWaifu2dAvatarConfig configuration = GetConfiguration(true);
            if(configuration == null) return;

            ScanResult result = CollectMaterials(
                new UnityEngine.Object[] { modelRoot }
            );
            SetTargets(result, "模型");
            RecordConfiguration(configuration, "扫描 Waifu2d 材质");
            ReconcileMaterialRules(configuration, targetMaterials, true);
            PrepareConfiguredShaders(configuration);
            SaveConfiguration(configuration);
        }

        private LyumaWaifu2dAvatarConfig GetConfiguration(bool create)
        {
            if(modelRoot == null) return null;
            LyumaWaifu2dAvatarConfig existing =
                modelRoot.GetComponent<LyumaWaifu2dAvatarConfig>();
            if(existing != null || !create) return existing;

            if(!EditorUtility.IsPersistent(modelRoot))
            {
                existing = Undo.AddComponent<LyumaWaifu2dAvatarConfig>(modelRoot);
                SaveConfiguration(existing);
                return existing;
            }

            string assetPath = AssetDatabase.GetAssetPath(modelRoot);
            if(string.IsNullOrEmpty(assetPath) ||
                !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus(
                    "NDMF 配置不能直接添加到 FBX/模型资源。请把模型放入场景，或制作成可编辑 Prefab。",
                    MessageType.Warning
                );
                return null;
            }

            GameObject prefabRoot = null;
            try
            {
                prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);
                if(prefabRoot.GetComponent<LyumaWaifu2dAvatarConfig>() == null)
                {
                    prefabRoot.AddComponent<LyumaWaifu2dAvatarConfig>();
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, assetPath);
                }
            }
            catch(Exception exception)
            {
                SetStatus(
                    "添加 Waifu2d NDMF 配置失败：" + exception.Message,
                    MessageType.Error
                );
                return null;
            }
            finally
            {
                if(prefabRoot != null) PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            modelRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            return modelRoot != null
                ? modelRoot.GetComponent<LyumaWaifu2dAvatarConfig>()
                : null;
        }

        private bool RemoveConfiguration()
        {
            if(modelRoot == null) return false;
            bool removed = false;

            if(!EditorUtility.IsPersistent(modelRoot))
            {
                LyumaWaifu2dAvatarConfig configuration =
                    modelRoot.GetComponent<LyumaWaifu2dAvatarConfig>();
                if(configuration != null)
                {
                    Undo.DestroyObjectImmediate(configuration);
                    removed = true;
                }
                LyumaWaifu2dStaticMeshConverter legacy =
                    modelRoot.GetComponent<LyumaWaifu2dStaticMeshConverter>();
                if(legacy != null)
                {
                    Undo.DestroyObjectImmediate(legacy);
                    removed = true;
                }
                if(removed) SaveConfigurationObject(modelRoot);
                return removed;
            }

            string assetPath = AssetDatabase.GetAssetPath(modelRoot);
            if(string.IsNullOrEmpty(assetPath) ||
                !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            GameObject prefabRoot = null;
            try
            {
                prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);
                LyumaWaifu2dAvatarConfig configuration =
                    prefabRoot.GetComponent<LyumaWaifu2dAvatarConfig>();
                if(configuration != null)
                {
                    UnityEngine.Object.DestroyImmediate(configuration);
                    removed = true;
                }
                LyumaWaifu2dStaticMeshConverter legacy =
                    prefabRoot.GetComponent<LyumaWaifu2dStaticMeshConverter>();
                if(legacy != null)
                {
                    UnityEngine.Object.DestroyImmediate(legacy);
                    removed = true;
                }
                if(removed) PrefabUtility.SaveAsPrefabAsset(prefabRoot, assetPath);
            }
            finally
            {
                if(prefabRoot != null) PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
            return removed;
        }

        private static void RecordConfiguration(
            LyumaWaifu2dAvatarConfig configuration,
            string action
        )
        {
            if(configuration != null &&
                !EditorUtility.IsPersistent(configuration))
            {
                Undo.RecordObject(configuration, action);
            }
        }

        private static void SaveConfiguration(
            LyumaWaifu2dAvatarConfig configuration
        )
        {
            if(configuration == null) return;
            SaveConfigurationObject(configuration);
        }

        private static void SaveConfigurationObject(UnityEngine.Object target)
        {
            if(target == null) return;
            EditorUtility.SetDirty(target);
            if(PrefabUtility.IsPartOfPrefabInstance(target))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            }
            if(EditorUtility.IsPersistent(target)) AssetDatabase.SaveAssets();
        }

        private void ReconcileMaterialRules(
            LyumaWaifu2dAvatarConfig configuration,
            IEnumerable<Material> materials,
            bool removeMissing
        )
        {
            if(configuration == null) return;
            if(configuration.Materials == null)
            {
                configuration.Materials =
                    new List<LyumaWaifu2dAvatarConfig.MaterialRule>();
            }

            var existing =
                new Dictionary<Material, LyumaWaifu2dAvatarConfig.MaterialRule>();
            foreach(LyumaWaifu2dAvatarConfig.MaterialRule rule in
                configuration.Materials)
            {
                if(rule != null &&
                    rule.Material != null &&
                    !existing.ContainsKey(rule.Material))
                {
                    existing.Add(rule.Material, rule);
                }
            }

            var scanned = new List<Material>();
            var unique = new HashSet<Material>();
            if(materials != null)
            {
                foreach(Material material in materials)
                {
                    if(material != null && unique.Add(material))
                    {
                        scanned.Add(material);
                    }
                }
            }

            bool changed = false;
            if(removeMissing)
            {
                var reconciled =
                    new List<LyumaWaifu2dAvatarConfig.MaterialRule>();
                foreach(Material material in scanned)
                {
                    LyumaWaifu2dAvatarConfig.MaterialRule rule;
                    if(!existing.TryGetValue(material, out rule))
                    {
                        rule = CreateDefaultMaterialRule(material);
                        changed = true;
                    }
                    reconciled.Add(rule);
                }
                if(reconciled.Count != configuration.Materials.Count)
                {
                    changed = true;
                }
                configuration.Materials = reconciled;
            }
            else
            {
                foreach(Material material in scanned)
                {
                    if(existing.ContainsKey(material)) continue;
                    configuration.Materials.Add(
                        CreateDefaultMaterialRule(material)
                    );
                    changed = true;
                }
            }

            if(changed) SaveConfiguration(configuration);
        }

        private void ConfigureMaterials(
            List<Material> materials,
            bool enabled,
            string sourceName,
            bool includeThirdParty = false
        )
        {
            if(materials == null || materials.Count == 0)
            {
                SetStatus(sourceName + "中没有受支持的材质。", MessageType.Warning);
                return;
            }

            LyumaWaifu2dAvatarConfig configuration = GetConfiguration(true);
            if(configuration == null) return;
            RecordConfiguration(configuration, "修改 Waifu2d NDMF 材质规则");
            ReconcileMaterialRules(configuration, materials, false);

            int changed = 0;
            int failed = 0;
            int skippedThirdParty = 0;
            foreach(Material material in materials)
            {
                LyumaWaifu2dAvatarConfig.MaterialRule rule =
                    configuration.FindRule(material);
                if(rule == null) continue;
                if(enabled &&
                    !includeThirdParty &&
                    IsThirdPartyShaderVariant(material))
                {
                    skippedThirdParty++;
                    continue;
                }
                if(rule.Convert != enabled)
                {
                    rule.Convert = enabled;
                    changed++;
                }
                if(enabled && !PrepareConfiguredShader(rule)) failed++;
            }
            SaveConfiguration(configuration);
            SetStatus(
                string.Format(
                    "{0}：已{1} {2} 个材质规则，着色器缓存准备失败 {3} 个。" +
                    (skippedThirdParty > 0
                        ? "\n已跳过 {4} 个第三方变体着色器；如需尝试，请在材质规则中手动启用。"
                        : string.Empty) +
                    "\n原材质没有被修改。",
                    sourceName,
                    enabled ? "启用" : "停用",
                    changed,
                    failed,
                    skippedThirdParty
                ),
                failed == 0 && skippedThirdParty == 0
                    ? MessageType.Info
                    : MessageType.Warning
            );
        }

        private void ApplyParametersToConfiguration(string sourceName)
        {
            if(!applyTwoDimensionalness &&
                !applyFacingDirection &&
                !applyLockAxis &&
                !applySquashZ)
            {
                SetStatus("请至少勾选一个要写入配置的参数。", MessageType.Warning);
                return;
            }

            LyumaWaifu2dAvatarConfig configuration = GetConfiguration(true);
            if(configuration == null) return;
            RecordConfiguration(configuration, "修改 Waifu2d NDMF 参数");
            if(applyTwoDimensionalness)
                configuration.TwoDimensionalness = twoDimensionalness;
            if(applyFacingDirection)
                configuration.FacingDirection = facingDirection;
            if(applyLockAxis)
                configuration.LockAxis = lockAxis;
            if(applySquashZ)
                configuration.SquashZ = squashZ;
            SaveConfiguration(configuration);
            SetStatus(
                sourceName + "：已更新 NDMF 参数，原材质没有被修改。",
                MessageType.Info
            );
        }

        private void CopyWindowParametersToConfiguration(
            LyumaWaifu2dAvatarConfig configuration
        )
        {
            if(configuration == null) return;
            if(applyTwoDimensionalness)
                configuration.TwoDimensionalness = twoDimensionalness;
            if(applyFacingDirection)
                configuration.FacingDirection = facingDirection;
            if(applyLockAxis)
                configuration.LockAxis = lockAxis;
            if(applySquashZ)
                configuration.SquashZ = squashZ;
        }

        private void LoadWindowParametersFromConfiguration()
        {
            LyumaWaifu2dAvatarConfig configuration = GetConfiguration(false);
            if(configuration == null) return;
            twoDimensionalness = configuration.TwoDimensionalness;
            facingDirection = configuration.FacingDirection;
            lockAxis = configuration.LockAxis;
            squashZ = configuration.SquashZ;
        }

        private void SetRootBoneBuildRepair(bool enabled)
        {
            LyumaWaifu2dAvatarConfig configuration = GetConfiguration(true);
            if(configuration == null) return;
            RecordConfiguration(configuration, "修改 Waifu2d Root Bone 构建修复");
            configuration.RepairRootBones = enabled;
            SaveConfiguration(configuration);
            SetStatus(
                enabled
                    ? "已启用 NDMF Root Bone 修复；只会修改构建副本。"
                    : "已停用 NDMF Root Bone 修复。",
                MessageType.Info
            );
        }

        private void SetStaticMeshBuildRepair(bool enabled)
        {
            LyumaWaifu2dAvatarConfig configuration = GetConfiguration(true);
            if(configuration == null) return;
            RecordConfiguration(configuration, "修改 Waifu2d 普通网格构建修复");
            configuration.ConvertStaticMeshes = enabled;
            SaveConfiguration(configuration);
            SetStatus(
                enabled
                    ? "已启用 NDMF 普通网格修复；只转换使用已启用材质规则的构建副本网格。"
                    : "已停用 NDMF 普通网格修复。",
                MessageType.Info
            );
        }

        private void SetBuildToggleEnabled(bool enabled)
        {
            LyumaWaifu2dAvatarConfig configuration = GetConfiguration(true);
            if(configuration == null) return;
            RecordConfiguration(configuration, "修改 Waifu2d 构建期菜单");
            configuration.GenerateToggle = enabled;
            SaveConfiguration(configuration);
            SetStatus(
                enabled
                    ? "已启用构建期 2D 开关。动画、BlendTree 和菜单会由 NDMF 临时生成。"
                    : "已停用构建期 2D 开关；构建材质会一直使用配置的 2D 强度。",
                MessageType.Info
            );
        }

        private static int CountEnabledRules(
            LyumaWaifu2dAvatarConfig configuration
        )
        {
            if(configuration == null || configuration.Materials == null) return 0;
            int count = 0;
            foreach(LyumaWaifu2dAvatarConfig.MaterialRule rule in
                configuration.Materials)
            {
                if(rule != null && rule.Material != null && rule.Convert) count++;
            }
            return count;
        }

        private static void PrepareConfiguredShaders(
            LyumaWaifu2dAvatarConfig configuration
        )
        {
            if(configuration == null || configuration.Materials == null) return;
            foreach(LyumaWaifu2dAvatarConfig.MaterialRule rule in
                configuration.Materials)
            {
                if(rule != null && rule.Convert) PrepareConfiguredShader(rule);
            }
        }

        private static bool PrepareConfiguredShader(
            LyumaWaifu2dAvatarConfig.MaterialRule rule
        )
        {
            if(rule == null || rule.Material == null) return false;
            Shader shader = GetMaterialShader(rule.Material);
            if(shader == null) return false;
            if(IsWaifu2dShader(shader)) return true;
            if(GenericLilCustomWaifu2dAdapter.IsSupported(shader) &&
                !rule.MergeCustomShader)
            {
                return true;
            }
            return GetWaifu2dShader(shader) != null;
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
                if(!IsSupportedMaterial(material)) continue;
                if(IsWaifu2dMaterial(material))
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
                if(material == null || GetMaterialShader(material) == null) continue;
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

        private void RunDirectRootBoneRepair()
        {
            GameObject requestedRoot = modelRoot != null ? modelRoot : Selection.activeGameObject;
            if(requestedRoot == null)
            {
                SetStatus("请先指定模型根对象，或在层级窗口选中模型根对象。", MessageType.Warning);
                return;
            }

            RootBoneRepairResult result = ProcessDirectRootBoneRepairTarget(requestedRoot);
            if(!EditorUtility.IsPersistent(requestedRoot) && result.resolvedRoot != null)
            {
                modelRoot = result.resolvedRoot;
            }
            SetStatus(result.message, result.messageType);
        }

        private static RootBoneRepairResult ProcessDirectRootBoneRepairTarget(GameObject requestedRoot)
        {
            if(!EditorUtility.IsPersistent(requestedRoot))
            {
                return ApplyDirectRootBoneRepair(requestedRoot, true);
            }

            string assetPath = AssetDatabase.GetAssetPath(requestedRoot);
            if(string.IsNullOrEmpty(assetPath) ||
                !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return RootBoneRepairResult.Unchanged(
                    "Root Bone 直接修复不能写入 FBX/模型资源。请将模型放入场景，或使用可编辑的 Prefab。",
                    MessageType.Warning
                );
            }

            GameObject prefabRoot = null;
            try
            {
                prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);
                RootBoneRepairResult result = ApplyDirectRootBoneRepair(prefabRoot, false);
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
                    "直接修复 Prefab 的 Root Bone 失败：" + exception.Message,
                    MessageType.Error
                );
            }
            finally
            {
                if(prefabRoot != null) PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static RootBoneRepairResult ApplyDirectRootBoneRepair(GameObject requestedRoot, bool useUndo)
        {
            if(!TryResolveHumanoidRoot(requestedRoot, out GameObject avatarRoot, out Transform hips))
            {
                return RootBoneRepairResult.Unchanged(
                    "没有找到有效的 Humanoid Animator 或 Hips，无法直接修复 Root Bone。",
                    MessageType.Warning
                );
            }

            int undoGroup = BeginObjectUndo(useUndo, "直接修复 Waifu2d Root Bone");
            try
            {
                int rendererCount = 0;
                int changedRendererCount = 0;
                foreach(SkinnedMeshRenderer renderer in
                    avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if(renderer == null) continue;
                    rendererCount++;
                    if(renderer.rootBone == hips) continue;

                    Transform previousRootBone = renderer.rootBone != null
                        ? renderer.rootBone
                        : renderer.transform;
                    Bounds convertedBounds = TransformBounds(
                        renderer.localBounds,
                        hips.worldToLocalMatrix * previousRootBone.localToWorldMatrix
                    );
                    RecordObject(renderer, useUndo, "设置 SkinnedMeshRenderer Root Bone");
                    renderer.rootBone = hips;
                    renderer.localBounds = convertedBounds;
                    MarkObjectDirty(renderer);
                    changedRendererCount++;
                }

                int meshSettingsCount = 0;
                int changedMeshSettingsCount = 0;
                foreach(ModularAvatarMeshSettings settings in
                    avatarRoot.GetComponentsInChildren<ModularAvatarMeshSettings>(true))
                {
                    if(settings == null) continue;
                    meshSettingsCount++;
                    if(settings.RootBone != null && settings.RootBone.Get(settings) == hips.gameObject)
                    {
                        continue;
                    }

                    GameObject previousRootBone = settings.RootBone != null
                        ? settings.RootBone.Get(settings)
                        : null;
                    bool convertBounds = previousRootBone != null &&
                        previousRootBone.transform != hips &&
                        settings.InheritBounds != ModularAvatarMeshSettings.InheritMode.Inherit;
                    Bounds convertedBounds = convertBounds
                        ? TransformBounds(
                            settings.Bounds,
                            hips.worldToLocalMatrix * previousRootBone.transform.localToWorldMatrix
                        )
                        : settings.Bounds;
                    RecordObject(settings, useUndo, "设置 MA Mesh Settings Root Bone");
                    settings.RootBone = CreateAvatarObjectReference(avatarRoot, hips);
                    if(convertBounds) settings.Bounds = convertedBounds;
                    MarkObjectDirty(settings);
                    changedMeshSettingsCount++;
                }

                if(changedRendererCount == 0 && changedMeshSettingsCount == 0)
                {
                    return RootBoneRepairResult.Unchanged(
                        string.Format(
                            "所有 Root Bone 已经指向 {0}（SkinnedMeshRenderer {1} 个，MA Mesh Settings {2} 个）。",
                            hips.name,
                            rendererCount,
                            meshSettingsCount
                        ),
                        MessageType.Info
                    );
                }

                return RootBoneRepairResult.Changed(
                    string.Format(
                        "已直接将 {0}/{1} 个 SkinnedMeshRenderer 和 {2}/{3} 个 MA Mesh Settings 的 Root Bone 改为 {4}。此操作只能使用 Unity Undo 还原。",
                        changedRendererCount,
                        rendererCount,
                        changedMeshSettingsCount,
                        meshSettingsCount,
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
                ModularAvatarMeshSettings rootSettings =
                    avatarRoot.GetComponent<ModularAvatarMeshSettings>();
                LyumaWaifu2dMeshSettingsRestoreState restoreState =
                    avatarRoot.GetComponent<LyumaWaifu2dMeshSettingsRestoreState>();

                if(restoreState == null)
                {
                    restoreState = AddComponent<LyumaWaifu2dMeshSettingsRestoreState>(avatarRoot, useUndo);
                }
                RecordObject(restoreState, useUndo, "保存 MA Mesh Settings 原始状态");
                restoreState.hideFlags |= HideFlags.HideInInspector;
                restoreState.MigrateLegacySnapshot();

                if(rootSettings == null)
                {
                    rootSettings = AddComponent<ModularAvatarMeshSettings>(avatarRoot, useUndo);
                    restoreState.CaptureCreated(rootSettings);
                }
                else
                {
                    restoreState.CaptureExisting(rootSettings);
                }

                ModularAvatarMeshSettings[] allSettings =
                    avatarRoot.GetComponentsInChildren<ModularAvatarMeshSettings>(true);
                foreach(ModularAvatarMeshSettings settings in allSettings)
                {
                    if(settings == null || settings == rootSettings) continue;
                    restoreState.CaptureExisting(settings);
                }

                int changedSettingsCount = 0;
                foreach(ModularAvatarMeshSettings settings in allSettings)
                {
                    if(settings == null) continue;

                    GameObject previousRootBone = settings.RootBone != null
                        ? settings.RootBone.Get(settings)
                        : null;
                    bool convertBounds = settings != rootSettings &&
                        previousRootBone != null &&
                        previousRootBone.transform != hips &&
                        settings.InheritBounds != ModularAvatarMeshSettings.InheritMode.Inherit;
                    Bounds convertedBounds = convertBounds
                        ? TransformBounds(
                            settings.Bounds,
                            hips.worldToLocalMatrix * previousRootBone.transform.localToWorldMatrix
                        )
                        : settings.Bounds;

                    RecordObject(settings, useUndo, "设置 MA Mesh Settings Root Bone");
                    settings.RootBone = CreateAvatarObjectReference(avatarRoot, hips);
                    if(settings == rootSettings)
                    {
                        settings.InheritBounds = ModularAvatarMeshSettings.InheritMode.Set;
                        settings.Bounds = commonBounds;
                    }
                    else if(convertBounds)
                    {
                        settings.Bounds = convertedBounds;
                    }
                    MarkObjectDirty(settings);
                    changedSettingsCount++;
                }
                MarkObjectDirty(restoreState);

                return RootBoneRepairResult.Changed(
                    string.Format(
                        "已保存并修复 {0} 个 MA Mesh Settings：Root Bone 统一为 {1}，取消修复时可逐个还原。",
                        changedSettingsCount,
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
                RecordObject(restoreState, useUndo, "读取 MA Mesh Settings 原始状态");
                restoreState.MigrateLegacySnapshot();
                var snapshots = new List<LyumaWaifu2dMeshSettingsRestoreState.MeshSettingsSnapshot>(
                    restoreState.MeshSettingsSnapshots
                );

                int restoredCount = 0;
                int removedCount = 0;
                int missingCount = 0;
                if(snapshots.Count > 0)
                {
                    foreach(LyumaWaifu2dMeshSettingsRestoreState.MeshSettingsSnapshot snapshot in snapshots)
                    {
                        if(snapshot == null) continue;
                        ModularAvatarMeshSettings settings = snapshot.MeshSettings;
                        if(settings == null)
                        {
                            missingCount++;
                            continue;
                        }

                        if(snapshot.CreatedByTool)
                        {
                            DestroyObject(settings, useUndo);
                            removedCount++;
                            continue;
                        }

                        RecordObject(settings, useUndo, "恢复 MA Mesh Settings 原始状态");
                        snapshot.Restore();
                        MarkObjectDirty(settings);
                        restoredCount++;
                    }
                }
                else
                {
                    // Fallback for an old restore record whose tracked component was deleted.
                    ModularAvatarMeshSettings trackedSettings = restoreState.TrackedMeshSettings;
                    if(restoreState.CreatedMeshSettings)
                    {
                        if(trackedSettings != null)
                        {
                            DestroyObject(trackedSettings, useUndo);
                            removedCount++;
                        }
                    }
                    else
                    {
                        if(trackedSettings == null)
                        {
                            trackedSettings = avatarRoot.GetComponent<ModularAvatarMeshSettings>();
                            if(trackedSettings == null)
                            {
                                trackedSettings = AddComponent<ModularAvatarMeshSettings>(avatarRoot, useUndo);
                            }
                        }

                        if(trackedSettings != null)
                        {
                            RecordObject(trackedSettings, useUndo, "恢复 MA Mesh Settings 原始状态");
                            restoreState.Restore(trackedSettings);
                            MarkObjectDirty(trackedSettings);
                            restoredCount++;
                        }
                    }
                }

                DestroyObject(restoreState, useUndo);
                return RootBoneRepairResult.Changed(
                    string.Format(
                        "已还原 {0} 个 MA Mesh Settings，移除 {1} 个本工具创建的组件，丢失 {2} 个组件。",
                        restoredCount,
                        removedCount,
                        missingCount
                    ),
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

        private static AvatarObjectReference CreateAvatarObjectReference(
            GameObject avatarRoot,
            Transform target
        )
        {
            var reference = new AvatarObjectReference(target.gameObject);
            if(string.IsNullOrEmpty(reference.referencePath))
            {
                reference.referencePath = AnimationUtility.CalculateTransformPath(
                    target,
                    avatarRoot.transform
                );
            }
            return reference;
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

        private static Bounds TransformBounds(Bounds source, Matrix4x4 transform)
        {
            Bounds destination = new Bounds();
            bool hasDestination = false;
            EncapsulateTransformedBounds(
                ref destination,
                ref hasDestination,
                source,
                transform
            );
            return hasDestination ? destination : source;
        }

        private static bool HasWaifu2dMaterial(GameObject root)
        {
            if(root == null) return false;
            Waifu2dAssociatedMaterialScanner.Result scan =
                Waifu2dAssociatedMaterialScanner.Collect(new UnityEngine.Object[] { root });
            foreach(Material material in scan.AllMaterials)
            {
                if(IsWaifu2dMaterial(material))
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
            if(!IsSupportedMaterial(material))
            {
                return false;
            }

            if(!IsWaifu2dMaterial(material))
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

            return HasMaterialProperty(material, TwoDimensionalnessProperty) &&
                HasMaterialProperty(material, FacingDirectionProperty) &&
                HasMaterialProperty(material, LockAxisProperty) &&
                HasMaterialProperty(material, SquashZProperty);
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

            string safeRootName = MakeSafeFileName(root.name);
            string outputRootFolder = ResolveAnimationOutputFolder();
            if(string.IsNullOrEmpty(outputRootFolder)) return;
            string outputFolder = outputRootFolder + "/" + safeRootName;
            EnsureAssetFolder(outputFolder);

            string disabledPath = outputFolder + "/" + safeRootName + "_Lyuma2D_关闭.anim";
            string enabledPath = outputFolder + "/" + safeRootName + "_Lyuma2D_开启.anim";
            string blendTreePath = outputFolder + "/" + safeRootName + "_Lyuma2D_BlendTree.asset";
            string prefabPath = outputFolder + "/" + TogglePrefabFileName + ".prefab";

            AnimationClip disabledClip = CreateStrengthClip(root, animationScan.renderers, 0.0f);
            AnimationClip enabledClip = CreateStrengthClip(root, animationScan.renderers, 0.99f);
            disabledClip.name = Path.GetFileNameWithoutExtension(disabledPath);
            enabledClip.name = Path.GetFileNameWithoutExtension(enabledPath);

            disabledClip = SaveOrOverwriteClip(disabledClip, disabledPath);
            enabledClip = SaveOrOverwriteClip(enabledClip, enabledPath);

            BlendTree blendTree = CreateStrengthBlendTree(disabledClip, enabledClip);
            blendTree.name = Path.GetFileNameWithoutExtension(blendTreePath);
            blendTree = SaveOrOverwriteBlendTree(blendTree, blendTreePath);
            GameObject switchPrefab = SaveSwitchPrefab(blendTree, prefabPath);
            AssetDatabase.SaveAssets();

            Selection.objects = switchPrefab != null
                ? new UnityEngine.Object[] { disabledClip, enabledClip, blendTree, switchPrefab }
                : new UnityEngine.Object[] { disabledClip, enabledClip, blendTree };
            EditorGUIUtility.PingObject(switchPrefab != null ? switchPrefab : blendTree);
            SetStatus(
                string.Format(
                    "已在模型独立文件夹中生成 2 个动画、1 个 BlendTree 和 1 个 MA 开关 Prefab，共绑定 {0} 个 Renderer。" +
                    "\n未转换关联材质 {1} 个：{2}" +
                    "\n因混用未转换材质跳过 Renderer {3} 个：{4}" +
                    "\n已转换但无法确定目标 Renderer 的关联材质 {5} 个：{6}" +
                    "\n{7}\n{8}\n{9}\n{10}",
                    animationScan.renderers.Count,
                    animationScan.unconvertedMaterials.Count,
                    FormatObjectNames(animationScan.unconvertedMaterials),
                    animationScan.skippedRenderers.Count,
                    FormatObjectNames(animationScan.skippedRenderers),
                    animationScan.unresolvedConvertedMaterials.Count,
                    FormatObjectNames(animationScan.unresolvedConvertedMaterials),
                    disabledPath,
                    enabledPath,
                    blendTreePath,
                    prefabPath
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

        private static BlendTree CreateStrengthBlendTree(AnimationClip disabledClip, AnimationClip enabledClip)
        {
            var blendTree = new BlendTree
            {
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
            return blendTree;
        }

        private static BlendTree SaveOrOverwriteBlendTree(BlendTree generatedTree, string assetPath)
        {
            BlendTree existingTree = AssetDatabase.LoadAssetAtPath<BlendTree>(assetPath);
            if(existingTree == null)
            {
                AssetDatabase.CreateAsset(generatedTree, assetPath);
                return generatedTree;
            }

            // Keep the existing GUID so an already placed MA switch Prefab retains its reference.
            Undo.RecordObject(existingTree, "更新 Lyuma Waifu2d BlendTree");
            EditorUtility.CopySerialized(generatedTree, existingTree);
            existingTree.name = Path.GetFileNameWithoutExtension(assetPath);
            EditorUtility.SetDirty(existingTree);
            UnityEngine.Object.DestroyImmediate(generatedTree);
            return existingTree;
        }

        private static GameObject SaveSwitchPrefab(BlendTree blendTree, string assetPath)
        {
            var temporaryObject = new GameObject(ToggleDisplayName);
            try
            {
                ModularAvatarMenuItem menuItem = temporaryObject.AddComponent<ModularAvatarMenuItem>();
                menuItem.label = ToggleDisplayName;
                menuItem.PortableControl.Type = PortableControlType.Toggle;
                menuItem.PortableControl.Parameter = ToggleParameterName;
                menuItem.PortableControl.Value = 1.0f;
                menuItem.PortableControl.Icon = AssetDatabase.LoadAssetAtPath<Texture2D>(ToggleIconPath);
                menuItem.isSynced = true;
                menuItem.isSaved = true;
                menuItem.isDefault = false;
                menuItem.automaticValue = true;

                temporaryObject.AddComponent<ModularAvatarMenuInstaller>();
                ModularAvatarMergeBlendTree mergeBlendTree =
                    temporaryObject.AddComponent<ModularAvatarMergeBlendTree>();
                mergeBlendTree.Motion = blendTree;
                mergeBlendTree.PathMode = MergeAnimatorPathMode.Absolute;

                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(temporaryObject, assetPath);
                if(savedPrefab != null && savedPrefab.name != ToggleDisplayName)
                {
                    savedPrefab.name = ToggleDisplayName;
                    EditorUtility.SetDirty(savedPrefab);
                }
                return savedPrefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(temporaryObject);
            }
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
                    if(!IsSupportedMaterial(material))
                    {
                        continue;
                    }

                    mappedMaterials.Add(material);
                    if(IsWaifu2dMaterial(material) &&
                        HasMaterialProperty(material, TwoDimensionalnessProperty))
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
                    if(!IsSupportedMaterial(material))
                    {
                        continue;
                    }

                    if(!IsWaifu2dMaterial(material))
                    {
                        result.unconvertedMaterials.Add(material);
                    }
                    else if(HasMaterialProperty(material, TwoDimensionalnessProperty) &&
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
                if(!IsSupportedMaterial(material) || !unique.Add(material))
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
                if(IsSupportedMaterial(material))
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

        private static Shader GetMaterialShader(Material material)
        {
            if(material == null) return null;
            Material shaderOwner = GetShaderOwner(material);
            return shaderOwner != null && shaderOwner.shader != null
                ? shaderOwner.shader
                : material.shader;
        }

        private static bool IsSupportedMaterial(Material material)
        {
            Shader shader = GetMaterialShader(material);
            return shader != null && IsSupportedShader(shader);
        }

        private static bool IsWaifu2dMaterial(Material material)
        {
            Shader shader = GetMaterialShader(material);
            return shader != null && IsWaifu2dShader(shader);
        }

        private static bool HasMaterialProperty(Material material, string propertyName)
        {
            if(material == null) return false;
            if(material.HasProperty(propertyName)) return true;
            Material shaderOwner = GetShaderOwner(material);
            return shaderOwner != null && shaderOwner != material &&
                shaderOwner.HasProperty(propertyName);
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
