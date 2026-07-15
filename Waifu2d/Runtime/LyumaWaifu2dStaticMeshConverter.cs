using nadena.dev.ndmf;
using UnityEngine;

namespace LyumaShader
{
    /// <summary>Marks a hierarchy whose Waifu2d static meshes are converted on the NDMF build copy.</summary>
    [AddComponentMenu("LyumaShader Extended/Waifu2d Static Mesh Converter")]
    [DisallowMultipleComponent]
    public sealed class LyumaWaifu2dStaticMeshConverter : MonoBehaviour, INDMFEditorOnly
    {
    }
}
