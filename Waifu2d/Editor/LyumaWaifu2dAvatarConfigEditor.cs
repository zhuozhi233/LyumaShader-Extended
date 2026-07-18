#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace LyumaShader
{
    [CustomEditor(typeof(LyumaWaifu2dAvatarConfig))]
    [CanEditMultipleObjects]
    internal sealed class LyumaWaifu2dAvatarConfigEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            LyumaWaifu2dAvatarConfig configuration =
                target as LyumaWaifu2dAvatarConfig;
            int total = 0;
            int enabled = 0;
            if(configuration != null &&
                !serializedObject.isEditingMultipleObjects &&
                configuration.Materials != null)
            {
                total = configuration.Materials.Count;
                foreach(LyumaWaifu2dAvatarConfig.MaterialRule rule in
                    configuration.Materials)
                {
                    if(rule != null &&
                        rule.Material != null &&
                        rule.Convert)
                    {
                        enabled++;
                    }
                }
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                "Waifu2d 配置",
                EditorStyles.boldLabel
            );
            GUILayout.FlexibleSpace();
            if(serializedObject.isEditingMultipleObjects)
            {
                EditorGUILayout.LabelField(
                    "已选择多个配置",
                    EditorStyles.miniLabel,
                    GUILayout.ExpandWidth(false)
                );
            }
            else
            {
                EditorGUILayout.LabelField(
                    string.Format("{0}/{1} 个材质", enabled, total),
                    EditorStyles.miniLabel,
                    GUILayout.ExpandWidth(false)
                );
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                "设置会在 NDMF 构建副本中应用。",
                EditorStyles.miniLabel
            );
            EditorGUILayout.Space(3.0f);

            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold
            };
            if(GUILayout.Button(
                "打开 Waifu2d 配置工具",
                buttonStyle,
                GUILayout.Height(30.0f)))
            {
                LilToonWaifu2dBatchWindow.OpenForConfiguration(
                    configuration
                );
            }
            EditorGUILayout.EndVertical();
        }
    }
}
#endif
