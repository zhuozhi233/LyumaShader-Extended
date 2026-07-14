#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace LyumaShader
{
    /// <summary>
    /// Discovers lilToon Custom Shader families from their .lilcontainer source
    /// and creates a Lyuma-owned composed copy. Original third-party files are
    /// never edited.
    /// </summary>
    internal static class GenericLilCustomWaifu2dAdapter
    {
        internal const string GeneratedRoot = "Assets/LyumaShader/Waifu2d/Generated/LilCustom";
        private const string ManifestFileName = "lyuma_waifu2d_manifest.json";
        private const string DataBlockFileName = "lilCustomShaderDatas.lilblock";
        private const string PropertiesBlockFileName = "lilCustomShaderProperties.lilblock";
        private const string InsertBlockFileName = "lilCustomShaderInsert.lilblock";
        private const string CustomHlslFileName = "custom.hlsl";
        private const string BridgeFileName = "lyuma_waifu2d_bridge.hlsl";

        private static readonly Dictionary<int, FamilyManifest> SourceFamilyCache =
            new Dictionary<int, FamilyManifest>();
        private static readonly Dictionary<int, FamilyManifest> GeneratedFamilyCache =
            new Dictionary<int, FamilyManifest>();
        private static readonly HashSet<int> UnsupportedShaderCache = new HashSet<int>();

        [Serializable]
        internal sealed class FamilyManifest
        {
            public string sourceFolder;
            public string outputFolder;
            public string sourceFamilyName;
            public string generatedFamilyName;
            public string originalEditorName;
            public string sourceGuid;
        }

        internal static bool IsSupported(Shader shader)
        {
            if(shader == null) return false;
            if(IsWaifu2dShader(shader)) return true;
            FamilyManifest ignored;
            return TryDiscoverSourceFamily(shader, out ignored);
        }

        internal static bool IsWaifu2dShader(Shader shader)
        {
            if(shader == null) return false;
            FamilyManifest ignored;
            return TryGetGeneratedManifest(shader, out ignored);
        }

        internal static Shader GetWaifu2dShader(Shader source)
        {
            if(source == null) return null;
            if(IsWaifu2dShader(source)) return source;

            FamilyManifest sourceManifest;
            if(!TryDiscoverSourceFamily(source, out sourceManifest)) return null;

            FamilyManifest generatedManifest = EnsureGeneratedFamily(sourceManifest);
            if(generatedManifest == null) return null;
            return FindMappedShader(source.name, generatedManifest.sourceFamilyName, generatedManifest.generatedFamilyName);
        }

        internal static Shader GetWaifu2dShaderForFamily(Shader source, string requiredSourceFolder)
        {
            if(source == null) return null;
            FamilyManifest sourceManifest;
            if(!TryDiscoverSourceFamily(source, out sourceManifest) ||
                !PathsEqual(sourceManifest.sourceFolder, requiredSourceFolder))
            {
                return null;
            }

            FamilyManifest generatedManifest = EnsureGeneratedFamily(sourceManifest);
            return generatedManifest == null
                ? null
                : FindMappedShader(source.name, generatedManifest.sourceFamilyName, generatedManifest.generatedFamilyName);
        }

        internal static Shader GetOriginalShader(Shader source)
        {
            FamilyManifest manifest;
            if(!TryGetGeneratedManifest(source, out manifest)) return null;
            return FindMappedShader(source.name, manifest.generatedFamilyName, manifest.sourceFamilyName);
        }

        internal static bool TryGetManifest(Shader shader, out FamilyManifest manifest)
        {
            return TryGetGeneratedManifest(shader, out manifest);
        }

        internal static void InitializeMaterial(Material material)
        {
            if(material == null) return;
            material.SetFloat("_2d_coef", 0.99f);
            material.SetFloat("_facing_coef", 0.0f);
            material.SetFloat("_lock2daxis_coef", 1.0f);
            material.SetFloat("_zcorrect_coef", 0.8f);
        }

        private static bool TryDiscoverSourceFamily(Shader shader, out FamilyManifest manifest)
        {
            manifest = null;
            if(shader == null) return false;

            int shaderId = shader.GetInstanceID();
            if(SourceFamilyCache.TryGetValue(shaderId, out manifest)) return manifest != null;
            if(UnsupportedShaderCache.Contains(shaderId)) return false;

            string shaderPath = NormalizePath(AssetDatabase.GetAssetPath(shader));
            if(string.IsNullOrEmpty(shaderPath) ||
                !shaderPath.EndsWith(".lilcontainer", StringComparison.OrdinalIgnoreCase) ||
                shaderPath.StartsWith(GeneratedRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                UnsupportedShaderCache.Add(shaderId);
                return false;
            }

            string sourceFolder = NormalizePath(Path.GetDirectoryName(shaderPath));
            string dataBlockPath = FindFamilyFile(sourceFolder, DataBlockFileName);
            if(string.IsNullOrEmpty(dataBlockPath))
            {
                UnsupportedShaderCache.Add(shaderId);
                return false;
            }

            string familyFolder = NormalizePath(Path.GetDirectoryName(dataBlockPath));
            string dataBlock = File.ReadAllText(dataBlockPath);
            string familyName = ReadQuotedDirective(dataBlock, "ShaderName");
            string editorName = ReadQuotedDirective(dataBlock, "EditorName");
            if(string.IsNullOrEmpty(familyName) ||
                shader.name.IndexOf(familyName, StringComparison.Ordinal) < 0 ||
                !File.Exists(familyFolder + "/" + PropertiesBlockFileName) ||
                !File.Exists(familyFolder + "/" + InsertBlockFileName) ||
                !File.Exists(familyFolder + "/" + CustomHlslFileName))
            {
                UnsupportedShaderCache.Add(shaderId);
                return false;
            }

            string sourceGuid = AssetDatabase.AssetPathToGUID(dataBlockPath);
            string guidSuffix = string.IsNullOrEmpty(sourceGuid)
                ? Math.Abs(familyFolder.GetHashCode()).ToString("x8")
                : sourceGuid.Substring(0, Math.Min(8, sourceGuid.Length));
            string safeFamilyName = MakeSafeName(familyName);
            string outputFolder = GeneratedRoot + "/" + safeFamilyName + "_" + guidSuffix;
            string generatedFamilyName = "LyumaShader/Waifu2d/LilCustom/" + safeFamilyName + "_" + guidSuffix;

            manifest = new FamilyManifest
            {
                sourceFolder = familyFolder,
                outputFolder = outputFolder,
                sourceFamilyName = familyName,
                generatedFamilyName = generatedFamilyName,
                originalEditorName = string.IsNullOrEmpty(editorName) ? "lilToon.lilToonInspector" : editorName,
                sourceGuid = sourceGuid
            };
            SourceFamilyCache[shaderId] = manifest;
            return true;
        }

        private static FamilyManifest EnsureGeneratedFamily(FamilyManifest sourceManifest)
        {
            if(sourceManifest == null) return null;
            string manifestPath = sourceManifest.outputFolder + "/" + ManifestFileName;
            if(File.Exists(manifestPath))
            {
                FamilyManifest existing = ReadManifest(manifestPath);
                if(existing != null &&
                    PathsEqual(existing.sourceFolder, sourceManifest.sourceFolder) &&
                    existing.sourceGuid == sourceManifest.sourceGuid)
                {
                    return existing;
                }
            }

            try
            {
                Directory.CreateDirectory(GeneratedRoot);
                CopyDirectoryWithoutMeta(sourceManifest.sourceFolder, sourceManifest.outputFolder);

                string generatedDataPath = sourceManifest.outputFolder + "/" + DataBlockFileName;
                string generatedPropertiesPath = sourceManifest.outputFolder + "/" + PropertiesBlockFileName;
                string generatedInsertPath = sourceManifest.outputFolder + "/" + InsertBlockFileName;
                string generatedCustomHlslPath = sourceManifest.outputFolder + "/" + CustomHlslFileName;

                ReplaceQuotedDirective(generatedDataPath, "ShaderName", sourceManifest.generatedFamilyName);
                ReplaceQuotedDirective(
                    generatedDataPath,
                    "EditorName",
                    typeof(GenericLilCustomWaifu2dInspector).FullName
                );
                AppendWaifu2dProperties(generatedPropertiesPath);
                WriteBridge(sourceManifest.outputFolder + "/" + BridgeFileName);
                AppendBridgeInclude(generatedInsertPath);
                AppendVertexHook(sourceManifest.outputFolder, generatedCustomHlslPath);

                File.WriteAllText(
                    manifestPath,
                    JsonUtility.ToJson(sourceManifest, true),
                    new UTF8Encoding(false)
                );

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                AssetDatabase.ImportAsset(
                    sourceManifest.outputFolder,
                    ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate
                );
                return sourceManifest;
            }
            catch(Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Lyuma Waifu2d - lilToon Custom",
                    "无法自动组合这个 lilToon Custom Shader：\n" + sourceManifest.sourceFamilyName +
                    "\n\n" + exception.Message,
                    "OK"
                );
                return null;
            }
        }

        private static bool TryGetGeneratedManifest(Shader shader, out FamilyManifest manifest)
        {
            manifest = null;
            if(shader == null) return false;
            int shaderId = shader.GetInstanceID();
            if(GeneratedFamilyCache.TryGetValue(shaderId, out manifest)) return manifest != null;

            string shaderPath = NormalizePath(AssetDatabase.GetAssetPath(shader));
            if(string.IsNullOrEmpty(shaderPath) ||
                !shaderPath.StartsWith(GeneratedRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string directory = NormalizePath(Path.GetDirectoryName(shaderPath));
            while(!string.IsNullOrEmpty(directory) &&
                directory.StartsWith(GeneratedRoot, StringComparison.OrdinalIgnoreCase))
            {
                string manifestPath = directory + "/" + ManifestFileName;
                if(File.Exists(manifestPath))
                {
                    manifest = ReadManifest(manifestPath);
                    if(manifest != null &&
                        shader.name.IndexOf(manifest.generatedFamilyName, StringComparison.Ordinal) >= 0)
                    {
                        GeneratedFamilyCache[shaderId] = manifest;
                        return true;
                    }
                    break;
                }
                directory = NormalizePath(Path.GetDirectoryName(directory));
            }
            return false;
        }

        private static FamilyManifest ReadManifest(string manifestPath)
        {
            try
            {
                return JsonUtility.FromJson<FamilyManifest>(File.ReadAllText(manifestPath));
            }
            catch(Exception exception)
            {
                Debug.LogWarning("Lyuma Waifu2d: invalid lilToon Custom manifest at " + manifestPath + "\n" + exception.Message);
                return null;
            }
        }

        private static Shader FindMappedShader(string shaderName, string fromFamily, string toFamily)
        {
            int familyIndex = shaderName.IndexOf(fromFamily, StringComparison.Ordinal);
            if(familyIndex < 0) return null;
            string mappedName = shaderName.Substring(0, familyIndex) + toFamily +
                shaderName.Substring(familyIndex + fromFamily.Length);
            return Shader.Find(mappedName);
        }

        private static string FindFamilyFile(string startFolder, string fileName)
        {
            string folder = startFolder;
            for(int depth = 0; depth < 4 && !string.IsNullOrEmpty(folder); depth++)
            {
                string candidate = folder + "/" + fileName;
                if(File.Exists(candidate)) return candidate;
                string parent = NormalizePath(Path.GetDirectoryName(folder));
                if(parent == folder || string.IsNullOrEmpty(parent) || !parent.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                folder = parent;
            }
            return null;
        }

        private static string ReadQuotedDirective(string content, string directive)
        {
            Match match = Regex.Match(
                content,
                "(?m)^\\s*" + Regex.Escape(directive) + "\\s+\"([^\"]+)\""
            );
            return match.Success ? match.Groups[1].Value : null;
        }

        private static void ReplaceQuotedDirective(string filePath, string directive, string value)
        {
            string content = File.ReadAllText(filePath);
            var pattern = new Regex(
                "(?m)^(\\s*" + Regex.Escape(directive) + "\\s+\")[^\"]*(\")"
            );
            if(!pattern.IsMatch(content))
            {
                throw new InvalidDataException(filePath + " does not contain " + directive + ".");
            }
            content = pattern.Replace(content, match => match.Groups[1].Value + value + match.Groups[2].Value, 1);
            File.WriteAllText(filePath, content, new UTF8Encoding(false));
        }

        private static void AppendWaifu2dProperties(string propertiesPath)
        {
            string content = File.ReadAllText(propertiesPath);
            if(content.IndexOf("_2d_coef", StringComparison.Ordinal) >= 0)
            {
                throw new InvalidDataException("Reserved Waifu2d properties already exist in " + propertiesPath);
            }
            content +=
                "\n\n        // Lyuma Waifu2d\n" +
                "        _2d_coef         (\"2D Amount\", Range(0, 1)) = 0.99\n" +
                "        _facing_coef     (\"Facing Direction\", Range(-1, 1)) = 0.0\n" +
                "        _lock2daxis_coef (\"Lock 2D Axis\", Range(0, 1)) = 1.0\n" +
                "        _zcorrect_coef   (\"Squash Z (0.8 recommended)\", Float) = 0.8\n";
            File.WriteAllText(propertiesPath, content, new UTF8Encoding(false));
        }

        private static void WriteBridge(string bridgePath)
        {
            string shaderAssetFolder = LilToonWaifu2dAdapter.GetShaderAssetFolder();
            if(string.IsNullOrEmpty(shaderAssetFolder))
            {
                throw new DirectoryNotFoundException("Could not locate the LyumaShader lilToon shader folder.");
            }
            string content =
                "// AUTOGENERATED Lyuma Waifu2d bridge for a lilToon Custom Shader family.\n" +
                "uniform float _2d_coef;\n" +
                "uniform float _facing_coef;\n" +
                "uniform float _lock2daxis_coef;\n" +
                "uniform float _zcorrect_coef;\n" +
                "#include \"" + shaderAssetFolder.Replace('\\', '/') + "/custom_insert.hlsl\"\n";
            File.WriteAllText(bridgePath, content, new UTF8Encoding(false));
        }

        private static void AppendBridgeInclude(string insertBlockPath)
        {
            string content = File.ReadAllText(insertBlockPath);
            if(content.IndexOf(BridgeFileName, StringComparison.OrdinalIgnoreCase) < 0)
            {
                content += "\n#include \"" + BridgeFileName + "\"\n";
                File.WriteAllText(insertBlockPath, content, new UTF8Encoding(false));
            }
        }

        private static void AppendVertexHook(string outputFolder, string fallbackCustomHlslPath)
        {
            bool foundDefinition = false;
            foreach(string hlslPath in Directory.GetFiles(outputFolder, "*.hlsl", SearchOption.AllDirectories))
            {
                if(hlslPath.EndsWith(BridgeFileName, StringComparison.OrdinalIgnoreCase)) continue;
                string[] lines = File.ReadAllLines(hlslPath);
                bool changed = false;
                for(int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    if(!Regex.IsMatch(lines[lineIndex], @"^\s*#\s*define\s+LIL_CUSTOM_VERTEX_WS\b")) continue;
                    foundDefinition = true;
                    int endIndex = lineIndex;
                    while(endIndex < lines.Length - 1 && lines[endIndex].TrimEnd().EndsWith("\\", StringComparison.Ordinal))
                    {
                        endIndex++;
                    }
                    if(lines[endIndex].IndexOf("LyumaWaifu2dApply", StringComparison.Ordinal) < 0)
                    {
                        lines[endIndex] += " LyumaWaifu2dApply(vertexInput);";
                        changed = true;
                    }
                    lineIndex = endIndex;
                }
                if(changed) File.WriteAllLines(hlslPath, lines, new UTF8Encoding(false));
            }

            if(!foundDefinition)
            {
                string content = File.ReadAllText(fallbackCustomHlslPath);
                content +=
                    "\n\n// Lyuma Waifu2d: run after the source family's object-space vertex hook.\n" +
                    "#define LIL_CUSTOM_VERTEX_WS \\\n" +
                    "    LyumaWaifu2dApply(vertexInput);\n";
                File.WriteAllText(fallbackCustomHlslPath, content, new UTF8Encoding(false));
            }
        }

        private static void CopyDirectoryWithoutMeta(string sourceFolder, string destinationFolder)
        {
            Directory.CreateDirectory(destinationFolder);
            foreach(string directory in Directory.GetDirectories(sourceFolder, "*", SearchOption.AllDirectories))
            {
                string relative = NormalizePath(directory).Substring(NormalizePath(sourceFolder).Length).TrimStart('/');
                Directory.CreateDirectory(destinationFolder + "/" + relative);
            }
            foreach(string sourceFile in Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories))
            {
                if(sourceFile.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                string extension = Path.GetExtension(sourceFile);
                // Shader families sometimes keep their inspector beside the shader
                // sources. Recompiling a copied editor script would create duplicate
                // types; the generated ShaderGUI delegates to the original assembly.
                if(extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".asmdef", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".asmref", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string normalizedSource = NormalizePath(sourceFile);
                string relative = normalizedSource.Substring(NormalizePath(sourceFolder).Length).TrimStart('/');
                string destinationFile = destinationFolder + "/" + relative;
                Directory.CreateDirectory(NormalizePath(Path.GetDirectoryName(destinationFile)));
                File.Copy(sourceFile, destinationFile, true);
            }
        }

        private static string MakeSafeName(string value)
        {
            if(string.IsNullOrEmpty(value)) return "LilCustom";
            var builder = new StringBuilder(value.Length);
            foreach(char character in value)
            {
                builder.Append(char.IsLetterOrDigit(character) || character == '-' || character == '_'
                    ? character
                    : '_');
            }
            return builder.ToString().Trim('_');
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/').TrimEnd('/');
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Delegates the material UI to the source Custom Shader inspector, adds the
    /// Waifu2d controls, and remaps render-mode shader changes back to the
    /// generated family.
    /// </summary>
    public sealed class GenericLilCustomWaifu2dInspector : ShaderGUI
    {
        private static readonly Dictionary<string, ShaderGUI> InspectorCache =
            new Dictionary<string, ShaderGUI>(StringComparer.Ordinal);
        private static readonly HashSet<string> FailedInspectorTypes =
            new HashSet<string>(StringComparer.Ordinal);
        private static bool showWaifu2d = true;

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            Material primaryMaterial = materialEditor.target as Material;
            GenericLilCustomWaifu2dAdapter.FamilyManifest manifest = null;
            if(primaryMaterial != null)
            {
                GenericLilCustomWaifu2dAdapter.TryGetManifest(primaryMaterial.shader, out manifest);
            }

            ShaderGUI sourceInspector = manifest == null ? null : GetSourceInspector(manifest.originalEditorName);
            if(sourceInspector != null)
            {
                try
                {
                    sourceInspector.OnGUI(materialEditor, properties);
                }
                catch(ExitGUIException)
                {
                    RemapChangedShaders(materialEditor.targets, manifest);
                    throw;
                }
                catch(Exception exception)
                {
                    FailedInspectorTypes.Add(manifest.originalEditorName);
                    Debug.LogException(exception);
                    materialEditor.PropertiesDefaultGUI(properties);
                }
            }
            else
            {
                materialEditor.PropertiesDefaultGUI(properties);
            }

            if(manifest != null) RemapChangedShaders(materialEditor.targets, manifest);
            DrawWaifu2dProperties(materialEditor);
        }

        private static ShaderGUI GetSourceInspector(string typeName)
        {
            if(string.IsNullOrEmpty(typeName) || FailedInspectorTypes.Contains(typeName)) return null;
            ShaderGUI inspector;
            if(InspectorCache.TryGetValue(typeName, out inspector)) return inspector;

            Type inspectorType = null;
            foreach(Type type in TypeCache.GetTypesDerivedFrom<ShaderGUI>())
            {
                if(type.FullName == typeName)
                {
                    inspectorType = type;
                    break;
                }
            }
            if(inspectorType == null || inspectorType == typeof(GenericLilCustomWaifu2dInspector) || inspectorType.IsAbstract)
            {
                FailedInspectorTypes.Add(typeName);
                return null;
            }

            try
            {
                inspector = Activator.CreateInstance(inspectorType) as ShaderGUI;
                if(inspector == null) throw new InvalidOperationException("Could not create " + typeName);
                InspectorCache[typeName] = inspector;
                return inspector;
            }
            catch(Exception exception)
            {
                FailedInspectorTypes.Add(typeName);
                Debug.LogException(exception);
                return null;
            }
        }

        private static void RemapChangedShaders(UnityEngine.Object[] targets, GenericLilCustomWaifu2dAdapter.FamilyManifest manifest)
        {
            foreach(UnityEngine.Object target in targets)
            {
                Material material = target as Material;
                if(material == null || material.shader == null ||
                    GenericLilCustomWaifu2dAdapter.IsWaifu2dShader(material.shader))
                {
                    continue;
                }

                Shader generatedShader = GenericLilCustomWaifu2dAdapter.GetWaifu2dShaderForFamily(
                    material.shader,
                    manifest.sourceFolder
                );
                if(generatedShader == null) continue;

                int renderQueue = material.renderQueue;
                Undo.RecordObject(material, "Keep Lyuma Waifu2d Custom Shader variant");
                material.shader = generatedShader;
                material.renderQueue = renderQueue;
                EditorUtility.SetDirty(material);
            }
        }

        private static void DrawWaifu2dProperties(MaterialEditor materialEditor)
        {
            MaterialProperty[] currentProperties = MaterialEditor.GetMaterialProperties(materialEditor.targets);
            MaterialProperty amount = FindProperty("_2d_coef", currentProperties, false);
            MaterialProperty facing = FindProperty("_facing_coef", currentProperties, false);
            MaterialProperty lockAxis = FindProperty("_lock2daxis_coef", currentProperties, false);
            MaterialProperty squashZ = FindProperty("_zcorrect_coef", currentProperties, false);
            if(amount == null || facing == null || lockAxis == null || squashZ == null) return;

            EditorGUILayout.Space();
            showWaifu2d = EditorGUILayout.Foldout(showWaifu2d, "Lyuma Waifu2d", true);
            if(!showWaifu2d) return;
            EditorGUI.indentLevel++;
            materialEditor.ShaderProperty(amount, "2D Amount / 2D 强度");
            materialEditor.ShaderProperty(facing, "Facing Direction / 朝向");
            materialEditor.ShaderProperty(lockAxis, "Lock 2D Axis / 锁定 2D 轴");
            materialEditor.ShaderProperty(squashZ, "Squash Z / Z 深度修正");
            EditorGUILayout.HelpBox("Recommended Squash Z / 推荐 Z 深度修正: 0.8", MessageType.Info);
            EditorGUI.indentLevel--;
        }
    }
}
#endif
