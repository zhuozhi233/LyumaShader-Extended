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
        private const string LegacyToggleParameterName = "2D";
        private const string ToggleDisplayName =
            "<b><size=35><line-height=100%><voffset=3.8em>2D</b>";
        private const string TogglePrefabFileName = "切换2D开关";
        private const string WindowTitle = "Waifu2d 配置工具 by 浊鸷";
        private const string TwoDimensionalnessProperty = "_2d_coef";
        private const string FacingDirectionProperty = "_facing_coef";
        private const string LockAxisProperty = "_lock2daxis_coef";
        private const string SquashZProperty = "_zcorrect_coef";
        private const string OutlineIn2DProperty = "_lyuma_outline_2d";
        private const string CustomLogicProperty = "_lyuma_custom_logic_2d";
        private const int CurrentSettingsVersion = 5;
        private const int MaterialsPerPage = 24;
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
        [SerializeField] private bool outlineIn2D = true;
        [SerializeField] private int settingsVersion;

        [SerializeField] private int selectedMainPage;
        [SerializeField] private bool thirdPartyShaderVariantRiskAccepted;
        [SerializeField] private int materialPage;
        private string materialSearch = string.Empty;
        private readonly Dictionary<int, bool> materialDetailFoldouts =
            new Dictionary<int, bool>();
        private readonly Dictionary<Material, MaterialUiInfo> materialUiInfoCache =
            new Dictionary<Material, MaterialUiInfo>();
        private readonly HashSet<Material> scheduledMotchiriMaterials =
            new HashSet<Material>();
        private GameObject scheduledMotchiriCacheRoot;
        private bool scheduledMotchiriCacheValid;
        private bool showLegacyTargetMaterials;
        private Vector2 scrollPosition;
        private string statusMessage = "请选择目标模型。";
        private MessageType statusType = MessageType.Info;

        private sealed class MaterialUiInfo
        {
            internal static readonly MaterialUiInfo Empty =
                new MaterialUiInfo();

            internal Shader Shader;
            internal bool IsVariant;
            internal bool IsOfficialLilToon;
            internal bool IsCustom;
            internal bool IsMotchiri;
            internal bool IsPoiyomi;
            internal bool IsThirdPartyShaderVariant;
            internal bool CanBecomeCustom;
        }

        private sealed class LegacyMaterialSnapshot
        {
            internal Material Material;
            internal Material ShaderOwner;
            internal Shader OriginalShader;
            internal bool MergeCustomShader;
            internal bool EnableCustomLogicIn2D;
            internal float TwoDimensionalness;
            internal float FacingDirection;
            internal float LockAxis;
            internal float SquashZ;
            internal bool OutlineIn2D;
        }

        internal struct LegacyUpgradeRunResult
        {
            internal GameObject ModelRoot;
            internal string Message;
            internal MessageType MessageType;
        }

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
            window.InvalidateMaterialUiCache();
            window.LoadWindowParametersFromConfiguration();
            ScanResult result = CollectMaterials(
                new UnityEngine.Object[] { configuration.gameObject }
            );
            window.SetTargets(result, "当前模型");
            window.ReconcileMaterialRules(
                configuration,
                window.targetMaterials,
                false
            );
            window.selectedMainPage = 0;
            window.Repaint();
        }

        internal static LegacyUpgradeRunResult RunLegacyUpgrade(
            GameObject root
        )
        {
            LilToonWaifu2dBatchWindow worker =
                CreateInstance<LilToonWaifu2dBatchWindow>();
            try
            {
                worker.modelRoot = root;
                worker.UpgradeLegacyConfiguration();
                return new LegacyUpgradeRunResult
                {
                    ModelRoot = worker.modelRoot,
                    Message = worker.statusMessage,
                    MessageType = worker.statusType
                };
            }
            finally
            {
                DestroyImmediate(worker);
            }
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
            outlineIn2D = true;
            if(twoDimensionalness <= 0.0f) twoDimensionalness = 0.99f;
            if(Mathf.Approximately(squashZ, 0.975f) || Mathf.Approximately(squashZ, 0.8f))
                squashZ = 1.0f;
            selectedMainPage = 0;
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
                materialPage = 0;
                InvalidateMaterialUiCache();
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

            EditorGUI.BeginChangeCheck();
            string newMaterialSearch = EditorGUILayout.TextField(
                materialSearch,
                EditorStyles.toolbarSearchField
            );
            if(EditorGUI.EndChangeCheck())
            {
                materialSearch = newMaterialSearch;
                materialPage = 0;
            }

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
                MaterialUiInfo materialInfo = GetMaterialUiInfo(material);
                if(materialInfo.IsVariant) materialVariantCount++;
                if(materialInfo.IsThirdPartyShaderVariant)
                {
                    thirdPartyShaderVariantCount++;
                }
                if(materialInfo.IsOfficialLilToon)
                {
                    lilToonCount++;
                }
                else if(materialInfo.IsCustom)
                {
                    lilCustomCount++;
                }
                else if(materialInfo.IsPoiyomi)
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

            List<Material> visibleMaterials = GetVisibleMaterials();
            ClampMaterialPage(visibleMaterials.Count);
            DrawMaterialPager(visibleMaterials.Count);
            if(configuration == null)
            {
                EditorGUILayout.HelpBox(
                    "点击“一键配置”或“重新扫描”后即可设置各材质规则。",
                    MessageType.Info
                );
                using(new EditorGUI.DisabledScope(true))
                {
                    int start = materialPage * MaterialsPerPage;
                    int end = Mathf.Min(
                        start + MaterialsPerPage,
                        visibleMaterials.Count
                    );
                    for(int index = start; index < end; index++)
                    {
                        EditorGUILayout.ObjectField(
                            visibleMaterials[index],
                            typeof(Material),
                            false
                        );
                    }
                }
            }
            else
            {
                DrawMaterialRules(configuration, visibleMaterials);
            }
            DrawMaterialPager(visibleMaterials.Count);
        }

        private void DrawMaterialRules(
            LyumaWaifu2dAvatarConfig configuration,
            List<Material> visibleMaterials
        )
        {
            if(configuration == null) return;
            int start = materialPage * MaterialsPerPage;
            int end = Mathf.Min(
                start + MaterialsPerPage,
                visibleMaterials.Count
            );
            for(int index = start; index < end; index++)
            {
                Material material = visibleMaterials[index];
                LyumaWaifu2dAvatarConfig.MaterialRule rule =
                    configuration.FindRule(material);
                if(rule == null) continue;

                MaterialUiInfo materialInfo = GetMaterialUiInfo(material);
                Shader shader = materialInfo.Shader;
                bool isCustom = materialInfo.IsCustom;
                bool isMotchiri = materialInfo.IsMotchiri;
                bool isThirdPartyShaderVariant =
                    materialInfo.IsThirdPartyShaderVariant;
                bool canBecomeCustom = materialInfo.CanBecomeCustom;
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

        private void DrawMaterialParameterOverrides(
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
                    rule.DisableOutlineIn2D =
                        configuration.DisableOutlineIn2D;
                    rule.UseGlobalTwoDimensionalness = false;
                    rule.UseGlobalFacingDirection = false;
                    rule.UseGlobalLockAxis = false;
                    rule.UseGlobalSquashZ = false;
                    rule.OverrideOutlineIn2D = true;
                }
                rule.OverrideParameters = overrideParameters;
                SaveConfiguration(configuration);
            }

            if(!rule.OverrideParameters) return;

            EditorGUI.indentLevel++;
            float currentTwoDimensionalness =
                !rule.UseGlobalTwoDimensionalness
                ? rule.TwoDimensionalness
                : configuration.TwoDimensionalness;
            float currentFacingDirection =
                !rule.UseGlobalFacingDirection
                ? rule.FacingDirection
                : configuration.FacingDirection;
            float currentLockAxis =
                !rule.UseGlobalLockAxis
                ? rule.LockAxis
                : configuration.LockAxis;
            float currentSquashZ =
                !rule.UseGlobalSquashZ
                ? rule.SquashZ
                : configuration.SquashZ;
            bool currentOutlineIn2D = rule.OverrideOutlineIn2D
                ? !rule.DisableOutlineIn2D
                : !configuration.DisableOutlineIn2D;

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            bool overrideTwoDimensionalness = GUILayout.Toggle(
                !rule.UseGlobalTwoDimensionalness,
                !rule.UseGlobalTwoDimensionalness ? "独立" : "全局",
                EditorStyles.miniButton,
                GUILayout.Width(46.0f)
            );
            using(new EditorGUI.DisabledScope(
                !overrideTwoDimensionalness))
            {
                currentTwoDimensionalness = EditorGUILayout.Slider(
                    "2D 强度",
                    currentTwoDimensionalness,
                    0.0f,
                    1.0f
                );
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            bool overrideFacingDirection = GUILayout.Toggle(
                !rule.UseGlobalFacingDirection,
                !rule.UseGlobalFacingDirection ? "独立" : "全局",
                EditorStyles.miniButton,
                GUILayout.Width(46.0f)
            );
            using(new EditorGUI.DisabledScope(!overrideFacingDirection))
            {
                currentFacingDirection = EditorGUILayout.Slider(
                    "朝向",
                    currentFacingDirection,
                    -1.0f,
                    1.0f
                );
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            bool overrideLockAxis = GUILayout.Toggle(
                !rule.UseGlobalLockAxis,
                !rule.UseGlobalLockAxis ? "独立" : "全局",
                EditorStyles.miniButton,
                GUILayout.Width(46.0f)
            );
            using(new EditorGUI.DisabledScope(!overrideLockAxis))
            {
                currentLockAxis = EditorGUILayout.Slider(
                    "锁定 2D 轴",
                    currentLockAxis,
                    0.0f,
                    1.0f
                );
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            bool overrideSquashZ = GUILayout.Toggle(
                !rule.UseGlobalSquashZ,
                !rule.UseGlobalSquashZ ? "独立" : "全局",
                EditorStyles.miniButton,
                GUILayout.Width(46.0f)
            );
            using(new EditorGUI.DisabledScope(!overrideSquashZ))
            {
                currentSquashZ = EditorGUILayout.Slider(
                    "Z 深度修正",
                    currentSquashZ,
                    0.0f,
                    1.0f
                );
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            bool overrideOutlineIn2D = GUILayout.Toggle(
                rule.OverrideOutlineIn2D,
                rule.OverrideOutlineIn2D ? "独立" : "全局",
                EditorStyles.miniButton,
                GUILayout.Width(46.0f)
            );
            using(new EditorGUI.DisabledScope(!overrideOutlineIn2D))
            {
                currentOutlineIn2D = EditorGUILayout.ToggleLeft(
                    "启用 2D 轮廓",
                    currentOutlineIn2D
                );
            }
            EditorGUILayout.EndHorizontal();

            if(EditorGUI.EndChangeCheck())
            {
                RecordConfiguration(
                    configuration,
                    "修改 Waifu2d 材质参数"
                );
                rule.UseGlobalTwoDimensionalness =
                    !overrideTwoDimensionalness;
                rule.UseGlobalFacingDirection =
                    !overrideFacingDirection;
                rule.UseGlobalLockAxis = !overrideLockAxis;
                rule.UseGlobalSquashZ = !overrideSquashZ;
                rule.OverrideOutlineIn2D = overrideOutlineIn2D;
                if(overrideTwoDimensionalness)
                    rule.TwoDimensionalness =
                        currentTwoDimensionalness;
                if(overrideFacingDirection)
                    rule.FacingDirection = currentFacingDirection;
                if(overrideLockAxis)
                    rule.LockAxis = currentLockAxis;
                if(overrideSquashZ)
                    rule.SquashZ = currentSquashZ;
                if(overrideOutlineIn2D)
                    rule.DisableOutlineIn2D =
                        !currentOutlineIn2D;
                SaveConfiguration(configuration);
            }
            EditorGUI.indentLevel--;
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

            Shader shader = GetMaterialUiInfo(material).Shader;
            return shader != null &&
                shader.name.IndexOf(
                    search,
                    StringComparison.OrdinalIgnoreCase
                ) >= 0;
        }

        private List<Material> GetVisibleMaterials()
        {
            var visibleMaterials = new List<Material>();
            foreach(Material material in targetMaterials)
            {
                if(MatchesMaterialSearch(material))
                    visibleMaterials.Add(material);
            }
            return visibleMaterials;
        }

        private void ClampMaterialPage(int materialCount)
        {
            int pageCount = Mathf.Max(
                1,
                Mathf.CeilToInt(materialCount / (float)MaterialsPerPage)
            );
            materialPage = Mathf.Clamp(materialPage, 0, pageCount - 1);
        }

        private void DrawMaterialPager(int materialCount)
        {
            int pageCount = Mathf.Max(
                1,
                Mathf.CeilToInt(materialCount / (float)MaterialsPerPage)
            );
            if(pageCount <= 1) return;

            EditorGUILayout.BeginHorizontal();
            using(new EditorGUI.DisabledScope(materialPage <= 0))
            {
                if(GUILayout.Button("上一页", GUILayout.Width(68.0f)))
                    materialPage--;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(
                string.Format(
                    "第 {0} / {1} 页 · 每页最多 {2} 个",
                    materialPage + 1,
                    pageCount,
                    MaterialsPerPage
                ),
                EditorStyles.centeredGreyMiniLabel,
                GUILayout.Width(170.0f)
            );
            GUILayout.FlexibleSpace();
            using(new EditorGUI.DisabledScope(materialPage >= pageCount - 1))
            {
                if(GUILayout.Button("下一页", GUILayout.Width(68.0f)))
                    materialPage++;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2.0f);
        }

        private MaterialUiInfo GetMaterialUiInfo(Material material)
        {
            if(material == null) return MaterialUiInfo.Empty;
            Shader shader = GetMaterialShader(material);
            MaterialUiInfo cached;
            if(materialUiInfoCache.TryGetValue(material, out cached) &&
                cached.Shader == shader &&
                cached.IsVariant == material.isVariant)
            {
                return cached;
            }

            bool scheduledForMotchiri =
                IsMaterialScheduledForMotchiri(material);
            bool isCustom = shader != null &&
                GenericLilCustomWaifu2dAdapter.IsSupported(shader);
            bool isMotchiri = shader != null &&
                shader.name.IndexOf(
                    "motchiri",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0;
            if(scheduledForMotchiri)
            {
                isCustom = true;
                isMotchiri = true;
            }

            cached = new MaterialUiInfo
            {
                Shader = shader,
                IsVariant = material.isVariant,
                IsOfficialLilToon = shader != null &&
                    LilToonWaifu2dAdapter.IsSupported(shader),
                IsCustom = isCustom,
                IsMotchiri = isMotchiri,
                IsPoiyomi = shader != null &&
                    PoiyomiWaifu2dAdapter.IsSupported(shader),
                IsThirdPartyShaderVariant =
                    isCustom || scheduledForMotchiri,
                CanBecomeCustom = isCustom ||
                    (shader != null &&
                        LilToonWaifu2dAdapter.IsSupported(shader))
            };
            materialUiInfoCache[material] = cached;
            return cached;
        }

        private void InvalidateMaterialUiCache()
        {
            materialUiInfoCache.Clear();
            scheduledMotchiriMaterials.Clear();
            scheduledMotchiriCacheRoot = null;
            scheduledMotchiriCacheValid = false;
        }

        private void EnsureScheduledMotchiriMaterialCache()
        {
            if(scheduledMotchiriCacheValid &&
                scheduledMotchiriCacheRoot == modelRoot)
            {
                return;
            }

            scheduledMotchiriMaterials.Clear();
            scheduledMotchiriCacheRoot = modelRoot;
            scheduledMotchiriCacheValid = true;
            if(modelRoot == null) return;

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

                    Material material = renderer.sharedMaterials[slot];
                    if(material != null)
                        scheduledMotchiriMaterials.Add(material);
                }
            }
        }

        private bool IsMaterialScheduledForMotchiri(Material material)
        {
            if(material == null) return false;
            EnsureScheduledMotchiriMaterialCache();
            return scheduledMotchiriMaterials.Contains(material);
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

        private bool ConfirmThirdPartyShaderVariants(
            List<Material> materials
        )
        {
            if(materials == null || materials.Count == 0) return false;
            if(materials.Count == 1)
                return ConfirmThirdPartyShaderVariant(materials[0]);

            return EditorUtility.DisplayDialog(
                "启用第三方变体着色器",
                "检测到 " + materials.Count +
                " 个使用第三方变体着色器的材质（例如“" +
                materials[0].name + "”）。\n\n" +
                "第三方着色器的结构和顶点逻辑可能与官方版本不同，自动适配不保证可用，" +
                "可能出现着色器编译错误、原功能失效或渲染异常。\n\n" +
                "确定要手动启用这些材质吗？确认后界面会显示“启用所有材质”按钮。",
                "确定",
                "取消"
            );
        }

        private bool PromptToEnableThirdPartyShaderVariants(
            List<Material> materials
        )
        {
            var thirdPartyMaterials = new List<Material>();
            if(materials != null)
            {
                foreach(Material material in materials)
                {
                    if(material != null &&
                        IsThirdPartyShaderVariant(material))
                    {
                        thirdPartyMaterials.Add(material);
                    }
                }
            }
            if(thirdPartyMaterials.Count == 0) return false;

            bool enableThirdParty = EditorUtility.DisplayDialog(
                "检测到 lilToon Custom Shader",
                "模型中检测到 " + thirdPartyMaterials.Count +
                " 个 lilToon Custom Shader 或其他第三方变体着色器材质。\n\n" +
                "不建议自动启用：第三方变体可能无法正确合并，也可能导致原功能失效、" +
                "着色器编译错误或渲染异常。\n\n" +
                "是否仍要启用这些材质？",
                "仍要启用",
                "不启用（推荐）"
            );
            if(!enableThirdParty) return false;
            if(!ConfirmThirdPartyShaderVariants(thirdPartyMaterials))
                return false;

            thirdPartyShaderVariantRiskAccepted = true;
            EditorUtility.SetDirty(this);
            return true;
        }

        private bool IsThirdPartyShaderVariant(Material material)
        {
            return material != null &&
                GetMaterialUiInfo(material).IsThirdPartyShaderVariant;
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
                // MergeCustomShader is relevant only to detected Custom
                // Shader families. Convert remains the independent switch
                // that controls whether the material participates.
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

            int thirdPartyMaterialCount = 0;
            foreach(Material material in targetMaterials)
            {
                if(IsThirdPartyShaderVariant(material))
                    thirdPartyMaterialCount++;
            }
            bool enableThirdParty =
                PromptToEnableThirdPartyShaderVariants(targetMaterials);
            RecordConfiguration(configuration, "配置 Lyuma Waifu2d NDMF");
            ReconcileMaterialRules(configuration, targetMaterials, true);
            foreach(Material material in targetMaterials)
            {
                LyumaWaifu2dAvatarConfig.MaterialRule rule =
                    configuration.FindRule(material);
                if(rule == null) continue;
                bool isThirdParty = IsThirdPartyShaderVariant(material);
                rule.Convert = !isThirdParty || enableThirdParty;
                if(isThirdParty && enableThirdParty)
                    rule.MergeCustomShader = true;
            }
            CopyWindowParametersToConfiguration(configuration);
            configuration.GenerateToggle = true;
            configuration.RepairRootBones = true;
            configuration.ConvertStaticMeshes = true;
            PrepareConfiguredShaders(configuration);
            SaveConfiguration(configuration);
            SetStatus(
                string.Format(
                    "已更新 NDMF 配置：启用 {0} 个材质，并开启 2D 开关、Root Bone 修复和普通网格修复。" +
                    (thirdPartyMaterialCount > 0
                        ? enableThirdParty
                            ? "\n已按用户确认启用第三方变体着色器；其兼容性不作保证。"
                            : "\n第三方变体着色器保持停用。"
                        : string.Empty) +
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

            bool previewIn2D = configuration != null &&
                configuration.PreviewIn2D;
            EditorGUI.BeginChangeCheck();
            bool newPreviewIn2D = EditorGUILayout.ToggleLeft(
                new GUIContent(
                    "预览显示 2D",
                    "控制 NDMF 构建与游戏预览中生成材质的默认状态。开启时使用每个材质配置的 2D 强度，关闭时使用 0；不会修改 2D 开关动画的数值。"
                ),
                previewIn2D
            );
            if(EditorGUI.EndChangeCheck())
            {
                SetPreviewIn2D(newPreviewIn2D);
                configuration = GetConfiguration(false);
            }

            EditorGUILayout.Space(3.0f);
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
                configuration = GetConfiguration(false);
            }

            EditorGUILayout.Space(3.0f);
            bool protectParticleMaterials =
                configuration != null &&
                configuration.ProtectParticleMaterials;
            EditorGUI.BeginChangeCheck();
            bool newProtectParticleMaterials =
                EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        "保护粒子材质",
                        "粒子独占材质会跳过 Waifu2d；与普通网格共享的材质会为粒子生成不带 2D 的构建期副本，2D 开关动画也会跳过粒子。"
                    ),
                    protectParticleMaterials
                );
            if(EditorGUI.EndChangeCheck())
            {
                SetParticleMaterialProtection(
                    newProtectParticleMaterials
                );
            }
        }

        private void DrawToggleMenuSettings(
            LyumaWaifu2dAvatarConfig configuration
        )
        {
            EditorGUI.indentLevel++;
            ModularAvatarMenuItem directMenuItem =
                GetDirectToggleMenuItem(configuration.ToggleMenuParent);
            bool useDirectMenuItem = directMenuItem != null;
            bool useMenuItemSettings =
                !configuration.OverrideDirectMenuItemSettings;

            bool displayMenuItemSettings =
                useDirectMenuItem && useMenuItemSettings;
            EditorGUI.BeginDisabledGroup(displayMenuItemSettings);
            EditorGUI.BeginChangeCheck();
            string displayedName = displayMenuItemSettings
                ? string.IsNullOrEmpty(directMenuItem.label)
                    ? directMenuItem.gameObject.name
                    : directMenuItem.label
                : string.Equals(
                    configuration.ToggleMenuName,
                    ToggleDisplayName,
                    StringComparison.Ordinal
                )
                    ? string.Empty
                    : configuration.ToggleMenuName;
            string newName = EditorGUILayout.TextField(
                new GUIContent(
                    "菜单名称",
                    "留空时使用默认的 2D 富文本名称。直接复用 Menu Item 且关闭“使用这个组件的参数”时，会在构建副本中用此名称覆盖它。"
                ),
                displayedName
            );
            Texture2D displayedIcon = displayMenuItemSettings &&
                directMenuItem.PortableControl != null
                    ? directMenuItem.PortableControl.Icon
                    : configuration.ToggleMenuIcon;
            Texture2D newIcon = (Texture2D)EditorGUILayout.ObjectField(
                new GUIContent(
                    "菜单图标",
                    "留空时使用包内的默认透明图片。直接复用 Menu Item 且关闭“使用这个组件的参数”时，会在构建副本中用此图标覆盖它。"
                ),
                displayedIcon,
                typeof(Texture2D),
                false
            );
            bool displayedDefaultEnabled = displayMenuItemSettings
                ? directMenuItem.isDefault
                : configuration.ToggleDefaultEnabled;
            bool displayedSaved = displayMenuItemSettings
                ? directMenuItem.isSaved
                : configuration.ToggleSaved;
            bool displayedSynced = displayMenuItemSettings
                ? directMenuItem.isSynced
                : configuration.ToggleSynced;
            EditorGUILayout.BeginHorizontal();
            bool newDefaultEnabled = EditorGUILayout.ToggleLeft(
                new GUIContent(
                    "默认启用",
                    "生成的 2D 开关是否默认开启。"
                ),
                displayedDefaultEnabled
            );
            bool newSaved = EditorGUILayout.ToggleLeft(
                new GUIContent(
                    "保存",
                    "是否在切换 Avatar 后保存参数状态。"
                ),
                displayedSaved
            );
            bool newSynced = EditorGUILayout.ToggleLeft(
                new GUIContent(
                    "同步",
                    "是否通过网络同步参数状态。"
                ),
                displayedSynced
            );
            EditorGUILayout.EndHorizontal();
            if(EditorGUI.EndChangeCheck())
            {
                RecordConfiguration(
                    configuration,
                    "修改 Waifu2d 菜单设置"
                );
                configuration.ToggleMenuName = newName;
                configuration.ToggleMenuIcon = newIcon;
                configuration.ToggleDefaultEnabled = newDefaultEnabled;
                configuration.ToggleSaved = newSaved;
                configuration.ToggleSynced = newSynced;
                SaveConfiguration(configuration);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            GameObject newParent = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent(
                    "菜单位置",
                    "留空时安装到菜单根；指定 Sub Menu 时创建到其内部，其他 MA Menu Item 会被直接用作 2D 开关。也支持 MA Menu Group 或 MA Menu Installer。"
                ),
                configuration.ToggleMenuParent,
                typeof(GameObject),
                true
            );
            bool menuParentChanged = EditorGUI.EndChangeCheck();
            bool menuSettingsSourceChanged = false;
            if(useDirectMenuItem)
            {
                EditorGUI.BeginChangeCheck();
                useMenuItemSettings = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        "使用这个组件的参数",
                        "启用时保留拖入的 MA Menu Item 的名称、类型、图标、默认启用、保存和同步设置；关闭时改用配置工具中的设置。两种模式都会直接复用这个 Menu Item。"
                    ),
                    useMenuItemSettings,
                    GUILayout.Width(155.0f)
                );
                menuSettingsSourceChanged =
                    EditorGUI.EndChangeCheck();
            }
            EditorGUILayout.EndHorizontal();

            if(menuSettingsSourceChanged)
            {
                RecordConfiguration(
                    configuration,
                    "修改 Waifu2d 菜单参数来源"
                );
                configuration.OverrideDirectMenuItemSettings =
                    !useMenuItemSettings;
                SaveConfiguration(configuration);
            }

            if(menuParentChanged)
            {
                if(IsValidToggleMenuParent(newParent))
                {
                    RecordConfiguration(
                        configuration,
                        "修改 Waifu2d 菜单位置"
                    );
                    configuration.ToggleMenuParent = newParent;
                    SaveConfiguration(configuration);
                    ModularAvatarMenuItem selectedMenuItem =
                        newParent != null
                            ? newParent.GetComponent<
                                ModularAvatarMenuItem
                            >()
                            : null;
                    bool isSubMenu = selectedMenuItem != null &&
                        selectedMenuItem.PortableControl != null &&
                        selectedMenuItem.PortableControl.Type ==
                            PortableControlType.SubMenu;
                    string statusMessage;
                    if(newParent == null || newParent == modelRoot)
                    {
                        statusMessage =
                            "2D 开关将安装到模型菜单根。";
                    }
                    else if(isSubMenu)
                    {
                        statusMessage =
                            "2D 开关将在构建时创建到指定的 Sub Menu 内。";
                    }
                    else if(selectedMenuItem != null)
                    {
                        statusMessage =
                            "构建时将直接使用指定的 MA Menu Item 作为 2D 开关。";
                    }
                    else
                    {
                        statusMessage =
                            "2D 开关将生成到指定的 MA 菜单位置。";
                    }
                    SetStatus(
                        statusMessage,
                        MessageType.Info
                    );
                }
                else
                {
                    SetStatus(
                        "菜单位置必须位于当前模型内部，并且带有 MA Menu Item、MA Menu Group 或 MA Menu Installer。",
                        MessageType.Warning
                    );
                }
            }

            directMenuItem =
                GetDirectToggleMenuItem(configuration.ToggleMenuParent);
            if(directMenuItem != null)
            {
                useMenuItemSettings =
                    !configuration.OverrideDirectMenuItemSettings;
                EditorGUILayout.HelpBox(
                    useMenuItemSettings
                        ? "当前直接复用 MA Menu Item；名称、类型、图标、默认启用、保存和同步使用组件原有设置。"
                        : "当前直接复用 MA Menu Item；构建时会用配置工具中的名称、图标、默认启用、保存和同步设置覆盖该组件，并将类型设为 Toggle。不会生成独立的 Menu Item。",
                    MessageType.Info
                );
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

            return candidate.GetComponent<ModularAvatarMenuItem>() != null ||
                candidate.GetComponent<ModularAvatarMenuGroup>() != null ||
                candidate.GetComponent<
                    ModularAvatarMenuInstaller
                >() != null;
        }

        private static ModularAvatarMenuItem GetDirectToggleMenuItem(
            GameObject candidate
        )
        {
            if(candidate == null) return null;
            ModularAvatarMenuItem item =
                candidate.GetComponent<ModularAvatarMenuItem>();
            if(item == null ||
                (item.PortableControl != null &&
                    item.PortableControl.Type ==
                        PortableControlType.SubMenu))
            {
                return null;
            }
            return item;
        }

        private void UpgradeLegacyConfiguration()
        {
            if(modelRoot == null)
            {
                SetStatus("请先指定需要升级的模型根对象。", MessageType.Warning);
                return;
            }

            ScanResult scan = CollectMaterials(
                new UnityEngine.Object[] { modelRoot }
            );
            SetTargets(scan, "1.x 模型");

            var snapshots = new List<LegacyMaterialSnapshot>();
            var originalShaders = new Dictionary<Material, Shader>();
            var failedShaderOwners = new List<Material>();
            bool containsCustomShader = false;
            foreach(Material material in targetMaterials)
            {
                if(material == null || !IsWaifu2dMaterial(material))
                    continue;

                Material shaderOwner = GetShaderOwner(material);
                Shader originalShader;
                if(!originalShaders.TryGetValue(
                        shaderOwner,
                        out originalShader))
                {
                    originalShader = shaderOwner != null
                        ? GetOriginalShader(shaderOwner.shader)
                        : null;
                    if(originalShader == null)
                    {
                        if(shaderOwner != null &&
                            !failedShaderOwners.Contains(shaderOwner))
                        {
                            failedShaderOwners.Add(shaderOwner);
                        }
                        continue;
                    }
                    originalShaders.Add(shaderOwner, originalShader);
                }

                bool mergeCustomShader =
                    GenericLilCustomWaifu2dAdapter.IsSupported(
                        originalShader
                    );
                containsCustomShader |= mergeCustomShader;
                snapshots.Add(new LegacyMaterialSnapshot
                {
                    Material = material,
                    ShaderOwner = shaderOwner,
                    OriginalShader = originalShader,
                    MergeCustomShader = mergeCustomShader,
                    EnableCustomLogicIn2D = GetMaterialFloat(
                        material,
                        CustomLogicProperty,
                        0.0f
                    ) > 0.5f,
                    TwoDimensionalness = GetMaterialFloat(
                        material,
                        TwoDimensionalnessProperty,
                        0.99f
                    ),
                    FacingDirection = GetMaterialFloat(
                        material,
                        FacingDirectionProperty,
                        0.0f
                    ),
                    LockAxis = GetMaterialFloat(
                        material,
                        LockAxisProperty,
                        1.0f
                    ),
                    SquashZ = GetMaterialFloat(
                        material,
                        SquashZProperty,
                        1.0f
                    ),
                    OutlineIn2D = GetMaterialFloat(
                        material,
                        OutlineIn2DProperty,
                        1.0f
                    ) > 0.5f
                });
            }

            if(failedShaderOwners.Count > 0)
            {
                SetStatus(
                    "升级已中止：以下 1.x 材质无法找到转换前的 Shader，尚未修改任何内容：\n" +
                    FormatObjectNames(failedShaderOwners),
                    MessageType.Error
                );
                return;
            }

            bool hasLegacyRootBoneRepair =
                FindRootBoneRestoreRoot(modelRoot) != null;
            bool hasLegacyStaticMeshRepair =
                modelRoot
                    .GetComponentsInChildren<
                        LyumaWaifu2dStaticMeshConverter
                    >(true)
                    .Length > 0;
            int legacyToggleCount =
                CountLegacyToggleItems(modelRoot);
            LyumaWaifu2dAvatarConfig existingConfiguration =
                GetConfiguration(false);
            bool hadConfiguration = existingConfiguration != null;

            if(snapshots.Count == 0 &&
                !hasLegacyRootBoneRepair &&
                !hasLegacyStaticMeshRepair &&
                legacyToggleCount == 0)
            {
                SetStatus(
                    hadConfiguration
                        ? "没有检测到需要从 1.x 迁移的内容；当前模型已经存在 2.x NDMF 配置。"
                        : "没有检测到 1.x 转换材质、修复组件或旧版 2D 开关。",
                    MessageType.Info
                );
                return;
            }

            string confirmation = string.Format(
                "将把以下 1.x 状态迁移到 2.x NDMF 配置：\n\n" +
                "· 已转换材质：{0} 个\n" +
                "· Root Bone 修复：{1}\n" +
                "· 普通网格修复：{2}\n" +
                "· 待清理的旧版 MA 2D 开关：{3}\n\n" +
                "迁移完成后会把材质恢复为原 Shader，并移除旧版修复组件与 MA 开关对象。" +
                "旧动画、BlendTree 和 Prefab 资源不会迁移；2.x 开关由 NDMF 在构建时重新生成。" +
                (containsCustomShader
                    ? "\n\n其中包含第三方 Custom Shader；将按 1.x 原状态继续启用，兼容性不作保证。"
                    : string.Empty),
                snapshots.Count,
                hasLegacyRootBoneRepair ? "已启用" : "未启用",
                hasLegacyStaticMeshRepair ? "已启用" : "未启用",
                legacyToggleCount > 0
                    ? legacyToggleCount + " 个"
                    : "未安装"
            );
            if(!EditorUtility.DisplayDialog(
                    "从 1.x 升级到 2.x",
                    confirmation,
                    "开始升级",
                    "取消"))
            {
                SetStatus("已取消 1.x 升级。", MessageType.Info);
                return;
            }

            LyumaWaifu2dAvatarConfig configuration =
                GetConfiguration(true);
            if(configuration == null) return;

            var existingRuleMaterials = new HashSet<Material>();
            if(configuration.Materials != null)
            {
                foreach(LyumaWaifu2dAvatarConfig.MaterialRule existingRule in
                    configuration.Materials)
                {
                    if(existingRule != null &&
                        existingRule.Material != null)
                    {
                        existingRuleMaterials.Add(existingRule.Material);
                    }
                }
            }

            RecordConfiguration(
                configuration,
                "从 Lyuma Waifu2d 1.x 升级"
            );
            ReconcileMaterialRules(
                configuration,
                targetMaterials,
                true
            );
            var snapshotByMaterial =
                new Dictionary<Material, LegacyMaterialSnapshot>();
            foreach(LegacyMaterialSnapshot snapshot in snapshots)
            {
                snapshotByMaterial[snapshot.Material] = snapshot;
            }

            foreach(Material material in targetMaterials)
            {
                LyumaWaifu2dAvatarConfig.MaterialRule rule =
                    configuration.FindRule(material);
                if(rule == null) continue;

                LegacyMaterialSnapshot snapshot;
                if(snapshotByMaterial.TryGetValue(material, out snapshot))
                {
                    rule.Convert = true;
                    rule.MergeCustomShader = snapshot.MergeCustomShader;
                    rule.EnableCustomLogicIn2D =
                        snapshot.EnableCustomLogicIn2D;
                    rule.FlattenMaterialVariant = material.isVariant;
                    rule.OverrideParameters = true;
                    rule.TwoDimensionalness =
                        snapshot.TwoDimensionalness;
                    rule.FacingDirection = snapshot.FacingDirection;
                    rule.LockAxis = snapshot.LockAxis;
                    rule.SquashZ = snapshot.SquashZ;
                    rule.OverrideOutlineIn2D = true;
                    rule.DisableOutlineIn2D = !snapshot.OutlineIn2D;
                }
                else if(!existingRuleMaterials.Contains(material))
                {
                    rule.Convert = false;
                }
            }

            if(!hadConfiguration)
            {
                configuration.RepairRootBones =
                    hasLegacyRootBoneRepair;
                configuration.ConvertStaticMeshes =
                    hasLegacyStaticMeshRepair;
            }
            else
            {
                configuration.RepairRootBones |=
                    hasLegacyRootBoneRepair;
                configuration.ConvertStaticMeshes |=
                    hasLegacyStaticMeshRepair;
            }
            SaveConfiguration(configuration);

            BeginMaterialUndo(
                "恢复 1.x Waifu2d 材质并升级到 2.x",
                out int undoGroup
            );
            foreach(KeyValuePair<Material, Shader> pair in
                originalShaders)
            {
                Material shaderOwner = pair.Key;
                if(shaderOwner == null) continue;
                int renderQueue = shaderOwner.renderQueue;
                Undo.RecordObject(
                    shaderOwner,
                    "恢复 1.x Waifu2d 材质"
                );
                shaderOwner.shader = pair.Value;
                shaderOwner.renderQueue = renderQueue;
                EditorUtility.SetDirty(shaderOwner);
            }
            FinishMaterialUndo(undoGroup);

            RootBoneRepairResult rootBoneRestoreResult =
                hasLegacyRootBoneRepair
                    ? ProcessRootBoneRepairTarget(
                        modelRoot,
                        true,
                        false
                    )
                    : RootBoneRepairResult.Unchanged(
                        string.Empty,
                        MessageType.Info
                    );
            int removedLegacyArtifacts = RemoveLegacyBuildArtifacts(
                modelRoot,
                hasLegacyStaticMeshRepair,
                legacyToggleCount > 0
            );

            InvalidateMaterialUiCache();
            configuration = GetConfiguration(false);
            if(configuration != null)
            {
                PrepareConfiguredShaders(configuration);
                SaveConfiguration(configuration);
            }
            ScanResult upgradedScan = CollectMaterials(
                new UnityEngine.Object[] { modelRoot }
            );
            SetTargets(upgradedScan, "升级后的模型");
            LoadWindowParametersFromConfiguration();
            thirdPartyShaderVariantRiskAccepted =
                containsCustomShader;

            SetStatus(
                string.Format(
                    "1.x → 2.x 升级完成：迁移 {0} 个材质规则，恢复 {1} 个基础材质 Shader，" +
                    "移除 {2} 个旧版构建组件或开关对象。" +
                    (hasLegacyRootBoneRepair &&
                        !rootBoneRestoreResult.changed
                        ? "\nRoot Bone 旧状态未发生修改：" +
                            rootBoneRestoreResult.message
                        : string.Empty) +
                    "\n后续转换、动画和网格修复将由 NDMF 构建副本处理。",
                    snapshots.Count,
                    originalShaders.Count,
                    removedLegacyArtifacts
                ),
                hasLegacyRootBoneRepair &&
                    !rootBoneRestoreResult.changed &&
                    rootBoneRestoreResult.messageType != MessageType.Info
                    ? rootBoneRestoreResult.messageType
                    : MessageType.Info
            );
        }

        private static float GetMaterialFloat(
            Material material,
            string propertyName,
            float fallback
        )
        {
            return material != null &&
                material.HasProperty(propertyName)
                ? material.GetFloat(propertyName)
                : fallback;
        }

        private static int CountLegacyToggleItems(
            GameObject root
        )
        {
            if(root == null) return 0;
            int count = 0;

            foreach(ModularAvatarMenuItem item in
                root.GetComponentsInChildren<
                    ModularAvatarMenuItem
                >(true))
            {
                if(!IsLegacyToggleItem(item)) continue;
                count++;
            }
            return count;
        }

        private static bool IsLegacyToggleItem(
            ModularAvatarMenuItem item
        )
        {
            if(item == null ||
                item.PortableControl == null ||
                !IsLegacyToggleParameter(
                    item.PortableControl.Parameter))
            {
                return false;
            }

            ModularAvatarMergeBlendTree mergeBlendTree =
                item.GetComponent<ModularAvatarMergeBlendTree>();
            BlendTree tree = mergeBlendTree != null
                ? mergeBlendTree.Motion as BlendTree
                : null;
            return tree != null &&
                IsLegacyToggleParameter(tree.blendParameter);
        }

        private static bool IsLegacyToggleParameter(
            string parameterName
        )
        {
            return string.Equals(
                    parameterName,
                    ToggleParameterName,
                    StringComparison.Ordinal) ||
                string.Equals(
                    parameterName,
                    LegacyToggleParameterName,
                    StringComparison.Ordinal);
        }

        private static int RemoveLegacyBuildArtifacts(
            GameObject requestedRoot,
            bool removeStaticMeshRepair,
            bool removeToggle
        )
        {
            if(requestedRoot == null) return 0;
            if(!EditorUtility.IsPersistent(requestedRoot))
            {
                int removed = RemoveLegacyBuildArtifactsFromRoot(
                    requestedRoot,
                    removeStaticMeshRepair,
                    removeToggle,
                    true
                );
                if(removed > 0) SaveConfigurationObject(requestedRoot);
                return removed;
            }

            string assetPath = AssetDatabase.GetAssetPath(requestedRoot);
            if(string.IsNullOrEmpty(assetPath) ||
                !assetPath.EndsWith(
                    ".prefab",
                    StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            GameObject prefabRoot = null;
            try
            {
                prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);
                int removed = RemoveLegacyBuildArtifactsFromRoot(
                    prefabRoot,
                    removeStaticMeshRepair,
                    removeToggle,
                    false
                );
                if(removed > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(
                        prefabRoot,
                        assetPath
                    );
                    AssetDatabase.SaveAssets();
                }
                return removed;
            }
            finally
            {
                if(prefabRoot != null)
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static int RemoveLegacyBuildArtifactsFromRoot(
            GameObject root,
            bool removeStaticMeshRepair,
            bool removeToggle,
            bool useUndo
        )
        {
            int removed = 0;
            if(removeStaticMeshRepair)
            {
                foreach(LyumaWaifu2dStaticMeshConverter marker in
                    root.GetComponentsInChildren<
                        LyumaWaifu2dStaticMeshConverter
                    >(true))
                {
                    if(marker == null) continue;
                    DestroyObject(marker, useUndo);
                    removed++;
                }
            }

            if(!removeToggle) return removed;
            var toggleItems = new List<ModularAvatarMenuItem>();
            foreach(ModularAvatarMenuItem item in
                root.GetComponentsInChildren<
                    ModularAvatarMenuItem
                >(true))
            {
                if(IsLegacyToggleItem(item))
                    toggleItems.Add(item);
            }
            foreach(ModularAvatarMenuItem item in toggleItems)
            {
                if(item == null) continue;
                GameObject toggleObject = item.gameObject;
                if(CanRemoveLegacyToggleObject(toggleObject))
                {
                    DestroyObject(toggleObject, useUndo);
                    removed++;
                    continue;
                }

                ModularAvatarMergeBlendTree mergeBlendTree =
                    toggleObject.GetComponent<
                        ModularAvatarMergeBlendTree
                    >();
                ModularAvatarMenuInstaller installer =
                    toggleObject.GetComponent<
                        ModularAvatarMenuInstaller
                    >();
                DestroyObject(item, useUndo);
                DestroyObject(mergeBlendTree, useUndo);
                if(installer != null)
                    DestroyObject(installer, useUndo);
                removed++;
            }
            return removed;
        }

        private static bool CanRemoveLegacyToggleObject(
            GameObject toggleObject
        )
        {
            if(toggleObject == null ||
                toggleObject.transform.childCount > 0)
            {
                return false;
            }

            foreach(Component component in
                toggleObject.GetComponents<Component>())
            {
                if(component is Transform ||
                    component is ModularAvatarMenuItem ||
                    component is ModularAvatarMenuInstaller ||
                    component is ModularAvatarMergeBlendTree)
                {
                    continue;
                }
                return false;
            }
            return true;
        }

        private void RunLegacyCompleteWorkflow()
        {
            if(modelRoot == null)
            {
                SetStatus("请先指定模型根对象。", MessageType.Warning);
                return;
            }

            ScanResult scan = CollectMaterials(
                new UnityEngine.Object[] { modelRoot }
            );
            SetTargets(scan, "模型");
            List<Material> materials = GetUsableTargets();
            if(materials.Count == 0)
            {
                SetStatus(
                    "模型中没有找到受支持的 lilToon、lilToon Custom 或 Poiyomi 材质，已停止一键执行。",
                    MessageType.Warning
                );
                return;
            }

            bool enableThirdParty =
                PromptToEnableThirdPartyShaderVariants(materials);
            var materialsToConvert = new List<Material>();
            foreach(Material material in materials)
            {
                if(!IsThirdPartyShaderVariant(material) ||
                    enableThirdParty)
                {
                    materialsToConvert.Add(material);
                }
            }
            ConvertMaterials(materialsToConvert, "模型");

            RootBoneRepairResult rootBoneResult =
                ProcessRootBoneRepairTarget(modelRoot, false, false);
            if(!EditorUtility.IsPersistent(modelRoot) &&
                rootBoneResult.resolvedRoot != null)
            {
                modelRoot = rootBoneResult.resolvedRoot;
                InvalidateMaterialUiCache();
            }

            if(modelRoot.GetComponent<LyumaWaifu2dStaticMeshConverter>() == null)
            {
                Undo.AddComponent<LyumaWaifu2dStaticMeshConverter>(modelRoot);
                EditorUtility.SetDirty(modelRoot);
            }

            GenerateStrengthAnimations();
        }

        private void RunLegacyCompleteRemoval()
        {
            if(modelRoot == null)
            {
                SetStatus("请先指定模型根对象。", MessageType.Warning);
                return;
            }

            ScanResult scan = CollectMaterials(
                new UnityEngine.Object[] { modelRoot }
            );
            SetTargets(scan, "模型");
            List<Material> materials = GetUsableTargets();
            if(materials.Count > 0)
            {
                RevertMaterials(materials, "模型");
            }
            else
            {
                RootBoneRepairResult restoreResult =
                    ProcessRootBoneRepairTarget(modelRoot, true, false);
                SetStatus(
                    restoreResult.message,
                    restoreResult.messageType
                );
            }

            LyumaWaifu2dStaticMeshConverter marker =
                modelRoot.GetComponent<LyumaWaifu2dStaticMeshConverter>();
            bool removedBuildConverter = marker != null;
            if(marker != null)
            {
                Undo.DestroyObjectImmediate(marker);
                EditorUtility.SetDirty(modelRoot);
            }

            SetStatus(
                statusMessage + (removedBuildConverter
                    ? "\n已移除 NDMF 普通 Mesh 构建期修复。"
                    : "\n模型上没有 NDMF 普通 Mesh 构建期修复。"),
                statusType
            );
        }

        private void DrawDirectToolsSection()
        {
            EditorGUILayout.HelpBox(
                "这里保留 1.1.9 的完整操作入口，可直接继续处理或还原由 1.x 转换过的模型。" +
                "直接材质和网格操作会修改原资源；使用前请确认目标正确。",
                MessageType.Warning
            );
            EditorGUILayout.LabelField("1.x 兼容一键处理", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button("一键应用（1.x 兼容）", GUILayout.Height(32.0f)))
            {
                RunLegacyCompleteWorkflow();
            }
            if(GUILayout.Button("一键还原（1.x 兼容）", GUILayout.Height(32.0f)))
            {
                RunLegacyCompleteRemoval();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8.0f);
            DrawLegacyTargetSection();
            EditorGUILayout.Space(8.0f);
            DrawLegacyConversionSection();
            EditorGUILayout.Space(8.0f);
            DrawLegacyGeneralParametersSection();
            EditorGUILayout.Space(8.0f);
            DrawLegacyAnimationSection();
        }

        private void DrawLegacyTargetSection()
        {
            EditorGUILayout.LabelField("目标与扫描", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button("扫描模型中的材质"))
            {
                if(modelRoot == null)
                {
                    SetStatus("请先拖入模型根对象。", MessageType.Warning);
                }
                else
                {
                    SetTargets(
                        CollectMaterials(
                            new UnityEngine.Object[] { modelRoot }
                        ),
                        "模型"
                    );
                }
            }
            if(GUILayout.Button("读取当前多选"))
            {
                SetTargets(CollectMaterials(Selection.objects), "当前多选");
            }
            if(GUILayout.Button("清空", GUILayout.Width(60.0f)))
            {
                targetMaterials.Clear();
                materialPage = 0;
                InvalidateMaterialUiCache();
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
                Shader materialShader = GetMaterialShader(material);
                if(materialShader == null) continue;
                if(LilToonWaifu2dAdapter.IsSupported(materialShader))
                {
                    if(LilToonWaifu2dAdapter.IsWaifu2dShader(materialShader))
                        lilToonConvertedCount++;
                    else
                        lilToonOriginalCount++;
                }
                else if(GenericLilCustomWaifu2dAdapter.IsSupported(materialShader))
                {
                    if(GenericLilCustomWaifu2dAdapter.IsWaifu2dShader(materialShader))
                        lilCustomConvertedCount++;
                    else
                        lilCustomOriginalCount++;
                }
                else if(PoiyomiWaifu2dAdapter.IsSupported(materialShader))
                {
                    if(PoiyomiWaifu2dAdapter.IsWaifu2dShader(materialShader))
                        poiyomiConvertedCount++;
                    else
                        poiyomiOriginalCount++;
                }
            }

            EditorGUILayout.LabelField(
                "已扫描的支持材质",
                targetMaterials.Count + " 个"
            );
            EditorGUILayout.LabelField(
                "lilToon",
                string.Format(
                    "待转换 {0} / 已转换 {1}",
                    lilToonOriginalCount,
                    lilToonConvertedCount
                )
            );
            EditorGUILayout.LabelField(
                "lilToon Custom",
                string.Format(
                    "待转换 {0} / 已转换 {1}",
                    lilCustomOriginalCount,
                    lilCustomConvertedCount
                )
            );
            EditorGUILayout.LabelField(
                "Poiyomi",
                string.Format(
                    "待转换 {0} / 已转换 {1}",
                    poiyomiOriginalCount,
                    poiyomiConvertedCount
                )
            );

            showLegacyTargetMaterials = EditorGUILayout.Foldout(
                showLegacyTargetMaterials,
                "查看目标材质",
                true
            );
            if(!showLegacyTargetMaterials) return;

            List<Material> visibleMaterials = GetVisibleMaterials();
            ClampMaterialPage(visibleMaterials.Count);
            DrawMaterialPager(visibleMaterials.Count);
            int start = materialPage * MaterialsPerPage;
            int end = Mathf.Min(
                start + MaterialsPerPage,
                visibleMaterials.Count
            );
            EditorGUI.indentLevel++;
            using(new EditorGUI.DisabledScope(true))
            {
                for(int index = start; index < end; index++)
                {
                    EditorGUILayout.ObjectField(
                        visibleMaterials[index],
                        typeof(Material),
                        false
                    );
                }
            }
            EditorGUI.indentLevel--;
            DrawMaterialPager(visibleMaterials.Count);
        }

        private void DrawLegacyConversionSection()
        {
            EditorGUILayout.LabelField(
                "应用与还原 Lyuma Waifu2d",
                EditorStyles.boldLabel
            );
            EditorGUILayout.HelpBox(
                "转换和还原入口与 1.1.9 一致，可处理当前扫描结果或当前多选。" +
                "lilToon Custom 等第三方变体仍不保证兼容。",
                MessageType.Info
            );
            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button(
                "转换已扫描材质",
                GUILayout.Height(28.0f)))
            {
                ConvertMaterials(GetUsableTargets(), "已扫描材质");
            }
            if(GUILayout.Button(
                "转换当前多选",
                GUILayout.Height(28.0f)))
            {
                ScanSelectionAndRun(ConvertMaterials, "当前多选");
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button(
                "还原已扫描材质",
                GUILayout.Height(28.0f)))
            {
                RevertMaterials(GetUsableTargets(), "已扫描材质");
            }
            if(GUILayout.Button(
                "还原当前多选",
                GUILayout.Height(28.0f)))
            {
                ScanSelectionAndRun(RevertMaterials, "当前多选");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6.0f);
            EditorGUILayout.LabelField("Root Bone 修复", EditorStyles.boldLabel);
            bool hasRootBoneRepair = modelRoot != null &&
                FindRootBoneRestoreRoot(modelRoot) != null;
            string rootBoneButton = hasRootBoneRepair
                ? "取消修复"
                : "修复蒙皮网格异常（运行时生效-非破坏）";
            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button(rootBoneButton, GUILayout.Height(28.0f)))
            {
                RunRootBoneRepair(hasRootBoneRepair);
            }
            if(GUILayout.Button(
                "强制修复全部蒙皮网格（无法还原）",
                GUILayout.Height(28.0f)))
            {
                RunDirectRootBoneRepair();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6.0f);
            EditorGUILayout.LabelField(
                "普通 MeshRenderer 修复",
                EditorStyles.boldLabel
            );
            bool hasBuildConverter = modelRoot != null &&
                modelRoot.GetComponent<LyumaWaifu2dStaticMeshConverter>() != null;
            string buildConverterButton = hasBuildConverter
                ? "取消修复"
                : "修复普通网格异常（运行时生效-非破坏）";
            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button(buildConverterButton, GUILayout.Height(28.0f)))
            {
                if(hasBuildConverter) RemoveStaticMeshBuildConverter();
                else AddStaticMeshBuildConverter();
            }
            if(GUILayout.Button(
                "强制修复全部普通网格（无法还原）",
                GUILayout.Height(28.0f)))
            {
                ConvertStaticMeshesDirectly();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLegacyGeneralParametersSection()
        {
            EditorGUILayout.LabelField("批量参数", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "勾选需要写入的参数。未勾选的参数保持原值。",
                MessageType.None
            );
            DrawOptionalSlider(
                ref applyTwoDimensionalness,
                ref twoDimensionalness,
                new GUIContent(
                    "2D 强度",
                    "2D Amount：0 为 3D，0.99 为推荐的 2D 值"
                ),
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
                new GUIContent(
                    "Z 深度修正",
                    "Squash Z：推荐 1.0；使用压平后的稳定深度"
                ),
                0.0f,
                1.0f
            );
            outlineIn2D = EditorGUILayout.ToggleLeft(
                new GUIContent(
                    "启用 2D 轮廓",
                    "关闭后只在进入 2D 状态时隐藏材质轮廓；3D 状态不受影响"
                ),
                outlineIn2D
            );

            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button(
                "应用到已扫描材质",
                GUILayout.Height(26.0f)))
            {
                ApplyGeneralParameters(
                    GetUsableTargets(),
                    "已扫描材质"
                );
            }
            if(GUILayout.Button(
                "应用到当前多选",
                GUILayout.Height(26.0f)))
            {
                ScanSelectionAndRun(
                    ApplyGeneralParameters,
                    "当前多选"
                );
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLegacyAnimationSection()
        {
            EditorGUILayout.LabelField(
                "生成 2D 开关动画",
                EditorStyles.boldLabel
            );
            EditorGUILayout.HelpBox(
                "生成 1.x 使用的两个动画、BlendTree 和 MA 开关 Prefab。" +
                "已有生成资源会更新内容并保留资源索引。",
                MessageType.Info
            );
            animationOutputFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                new GUIContent(
                    "动画输出文件夹",
                    "留空时使用 " + DefaultAnimationFolder
                ),
                animationOutputFolder,
                typeof(DefaultAsset),
                false
            );
            if(GUILayout.Button(
                "生成 2D 开关动画与 MA Prefab",
                GUILayout.Height(32.0f)))
            {
                GenerateStrengthAnimations();
            }
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
            outlineIn2D = EditorGUILayout.ToggleLeft(
                new GUIContent(
                    "启用 2D 轮廓",
                    "关闭后只在进入 2D 状态时隐藏材质轮廓；3D 状态不受影响"
                ),
                outlineIn2D
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
            configuration.DisableOutlineIn2D = !outlineIn2D;
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
            configuration.DisableOutlineIn2D = !outlineIn2D;
        }

        private void LoadWindowParametersFromConfiguration()
        {
            LyumaWaifu2dAvatarConfig configuration = GetConfiguration(false);
            if(configuration == null) return;
            twoDimensionalness = configuration.TwoDimensionalness;
            facingDirection = configuration.FacingDirection;
            lockAxis = configuration.LockAxis;
            squashZ = configuration.SquashZ;
            outlineIn2D = !configuration.DisableOutlineIn2D;
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

        private void SetPreviewIn2D(bool enabled)
        {
            LyumaWaifu2dAvatarConfig configuration = GetConfiguration(true);
            if(configuration == null) return;
            RecordConfiguration(configuration, "修改 Waifu2d 预览默认状态");
            configuration.PreviewIn2D = enabled;
            SaveConfiguration(configuration);
            SetStatus(
                enabled
                    ? "预览材质默认使用各材质配置的 2D 强度。"
                    : "预览材质的默认 2D 强度已设为 0。",
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

        private void SetParticleMaterialProtection(bool enabled)
        {
            LyumaWaifu2dAvatarConfig configuration =
                GetConfiguration(true);
            if(configuration == null) return;
            RecordConfiguration(
                configuration,
                "修改 Waifu2d 粒子材质保护"
            );
            configuration.ProtectParticleMaterials = enabled;
            SaveConfiguration(configuration);
            SetStatus(
                enabled
                    ? "已启用粒子材质保护；粒子独占材质会跳过转换，共享材质会在构建时为粒子分离非 2D 副本。"
                    : "已关闭粒子材质保护；粒子将与普通网格一样应用 Waifu2d。",
                enabled ? MessageType.Info : MessageType.Warning
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
                    : "已停用构建期 2D 开关；构建材质会使用预览默认状态。",
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
            InvalidateMaterialUiCache();

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
                material.SetFloat(
                    OutlineIn2DProperty,
                    outlineIn2D ? 1.0f : 0.0f
                );
                EditorUtility.SetDirty(material);
                changed++;
            }
            FinishMaterialUndo(undoGroup);
            InvalidateMaterialUiCache();

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
            InvalidateMaterialUiCache();

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
            var reference = new AvatarObjectReference();
            reference.Set(target.gameObject);
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

            Material shaderOwner = GetShaderOwner(material);
            if(shaderOwner != null &&
                shaderOwner.shader != null &&
                IsWaifu2dShader(shaderOwner.shader) &&
                !HasMaterialProperty(shaderOwner, OutlineIn2DProperty))
            {
                Shader updatedShader =
                    GetWaifu2dShader(shaderOwner.shader);
                if(updatedShader == null) return false;

                int renderQueue = shaderOwner.renderQueue;
                Undo.RecordObject(
                    shaderOwner,
                    "更新 Lyuma Waifu2d 着色器"
                );
                shaderOwner.shader = updatedShader;
                shaderOwner.SetFloat(OutlineIn2DProperty, 1.0f);
                shaderOwner.renderQueue = renderQueue;
                EditorUtility.SetDirty(shaderOwner);
                converted++;
            }

            if(!IsWaifu2dMaterial(material))
            {
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
                HasMaterialProperty(material, SquashZProperty) &&
                HasMaterialProperty(material, OutlineIn2DProperty);
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
            EditorGUIUtility.PingObject(
                switchPrefab != null
                    ? (UnityEngine.Object)switchPrefab
                    : blendTree
            );
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
            materialPage = 0;
            InvalidateMaterialUiCache();
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
