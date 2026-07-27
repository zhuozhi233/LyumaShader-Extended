#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace LyumaShader
{
    internal static class AaoShaderInformationBridge
    {
        private const string RegistryTypeName =
            "Anatawa12.AvatarOptimizer.API.ShaderInformationRegistry";

        private static readonly HashSet<string> RegisteredShaderGuids =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> ReportedWarnings =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly List<IDisposable> Registrations =
            new List<IDisposable>();

        private static bool apiLookupComplete;
        private static Type registryType;
        private static MethodInfo getShaderInformation;
        private static MethodInfo registerShaderInformationWithGuid;

        internal static void RegisterOfficialLilToonShader(
            Shader waifu2dShader
        )
        {
            if(waifu2dShader == null ||
                !LilToonWaifu2dAdapter.IsWaifu2dShader(waifu2dShader))
            {
                return;
            }

            Shader originalShader =
                LilToonWaifu2dAdapter.GetOriginalShader(waifu2dShader);
            if(originalShader == null) return;

            EnsureApiLookup();
            if(registryType == null) return;
            if(getShaderInformation == null ||
                registerShaderInformationWithGuid == null)
            {
                ReportOnce(
                    "unsupported-api",
                    "Lyuma Waifu2d：检测到 AAO，但当前 AAO 版本没有可用的着色器信息接口。" +
                    "已安全跳过本次纹理优化兼容。"
                );
                return;
            }

            string shaderPath = AssetDatabase.GetAssetPath(waifu2dShader);
            string shaderGuid = string.IsNullOrEmpty(shaderPath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(shaderPath);
            if(string.IsNullOrEmpty(shaderGuid) ||
                RegisteredShaderGuids.Contains(shaderGuid))
            {
                return;
            }

            try
            {
                object existingInformation = getShaderInformation.Invoke(
                    null,
                    new object[] { waifu2dShader }
                );
                if(existingInformation != null)
                {
                    RegisteredShaderGuids.Add(shaderGuid);
                    return;
                }

                object originalInformation = getShaderInformation.Invoke(
                    null,
                    new object[] { originalShader }
                );
                if(originalInformation == null) return;

                object registration =
                    registerShaderInformationWithGuid.Invoke(
                        null,
                        new[] { (object)shaderGuid, originalInformation }
                    );
                IDisposable disposable = registration as IDisposable;
                if(disposable != null) Registrations.Add(disposable);
                RegisteredShaderGuids.Add(shaderGuid);
            }
            catch(Exception exception)
            {
                Exception reason =
                    exception is TargetInvocationException invocation &&
                    invocation.InnerException != null
                        ? invocation.InnerException
                        : exception;
                ReportOnce(
                    shaderGuid,
                    "Lyuma Waifu2d：无法向 AAO 注册着色器“" +
                    waifu2dShader.name + "”，已安全跳过本次纹理优化兼容。\n" +
                    reason.Message
                );
            }
        }

        private static void EnsureApiLookup()
        {
            if(apiLookupComplete) return;
            apiLookupComplete = true;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for(int index = 0; index < assemblies.Length; index++)
            {
                registryType = assemblies[index].GetType(
                    RegistryTypeName,
                    false
                );
                if(registryType != null) break;
            }
            if(registryType == null) return;

            getShaderInformation = registryType.GetMethod(
                "GetShaderInformation",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(Shader) },
                null
            );

            MethodInfo[] methods = registryType.GetMethods(
                BindingFlags.Public | BindingFlags.Static
            );
            for(int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                if(method.Name != "RegisterShaderInformationWithGUID")
                    continue;
                ParameterInfo[] parameters = method.GetParameters();
                if(parameters.Length != 2 ||
                    parameters[0].ParameterType != typeof(string))
                {
                    continue;
                }
                registerShaderInformationWithGuid = method;
                break;
            }
        }

        private static void ReportOnce(string key, string message)
        {
            if(!ReportedWarnings.Add(key)) return;
            Debug.LogWarning(message);
        }
    }
}
#endif
