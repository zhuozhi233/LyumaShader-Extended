#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace LyumaShader
{
    internal sealed class Waifu2dLegacyUpgradeWindow : EditorWindow
    {
        private const string WindowTitle =
            "Waifu2d 1.x → 2.x 升级工具";

        [SerializeField] private GameObject modelRoot;
        private string statusMessage;
        private MessageType statusType = MessageType.Info;

        [MenuItem(
            "Tools/LyumaShader Extended/从 1.x 升级到 2.x"
        )]
        private static void OpenWindow()
        {
            Waifu2dLegacyUpgradeWindow window =
                GetWindow<Waifu2dLegacyUpgradeWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(430.0f, 245.0f);
            if(Selection.activeObject is GameObject selectedRoot)
                window.modelRoot = selectedRoot;
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            if(modelRoot == null &&
                Selection.activeObject is GameObject selectedRoot)
            {
                modelRoot = selectedRoot;
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "Waifu2d 1.x → 2.x",
                new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 16
                },
                GUILayout.Height(24.0f)
            );
            EditorGUILayout.HelpBox(
                "读取 1.x 已转换材质及其 2D 参数，把 Root Bone 和普通网格修复状态迁移到 NDMF 配置，随后恢复材质原 Shader。旧动画不会迁移，2D 开关由 NDMF 在构建时重新生成。",
                MessageType.Info
            );
            EditorGUILayout.Space(4.0f);

            modelRoot = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent(
                    "模型根对象",
                    "可选择场景模型或可编辑 Prefab。"
                ),
                modelRoot,
                typeof(GameObject),
                true
            );

            using(new EditorGUI.DisabledScope(modelRoot == null))
            {
                if(GUILayout.Button(
                    "检查并开始升级",
                    GUILayout.Height(34.0f)))
                {
                    LilToonWaifu2dBatchWindow.LegacyUpgradeRunResult
                        result =
                            LilToonWaifu2dBatchWindow.RunLegacyUpgrade(
                                modelRoot
                            );
                    modelRoot = result.ModelRoot != null
                        ? result.ModelRoot
                        : modelRoot;
                    statusMessage = result.Message;
                    statusType = result.MessageType;
                }
            }

            if(!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.Space(4.0f);
                EditorGUILayout.HelpBox(statusMessage, statusType);
            }
        }
    }
}
#endif
