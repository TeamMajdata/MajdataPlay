#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MajdataPlay.Rendering.Editor
{
    /// <summary>
    /// MRP Unlit 的最小材质编辑器。接替 URP 的 Surface/Blend 状态同步，不引用 URP Editor。
    /// Blend、ZWrite 与关键字都保存在材质中，Player 渲染不依赖这个编辑器类。
    /// </summary>
    public sealed class MajUnlitShaderGUI : ShaderGUI
    {
        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            EditorGUI.BeginChangeCheck();
            base.OnGUI(materialEditor, properties);
            if (!EditorGUI.EndChangeCheck()) return;

            // 支持多选材质；只修改当前 Inspector 中使用这个 ShaderGUI 的材质。
            foreach (Object target in materialEditor.targets)
                if (target is Material material) ConfigureSurface(material);
        }

        public override void ValidateMaterial(Material material)
        {
            ConfigureSurface(material);
        }

        private static void ConfigureSurface(Material material)
        {
            if (!material.HasProperty("_Surface")) return;

            bool transparent = material.GetFloat("_Surface") > 0.5f;
            bool alphaClip = material.GetFloat("_AlphaClip") > 0.5f;
            int blend = Mathf.Clamp(Mathf.RoundToInt(material.GetFloat("_Blend")), 0, 3);
            SetKeyword(material, "_SURFACE_TYPE_TRANSPARENT", transparent);
            SetKeyword(material, "_ALPHATEST_ON", alphaClip);
            SetKeyword(material, "_ALPHAPREMULTIPLY_ON", transparent && blend == 1);
            SetKeyword(material, "_ALPHAMODULATE_ON", transparent && blend == 3);

            BlendMode source = BlendMode.One, destination = BlendMode.Zero;
            BlendMode sourceAlpha = BlendMode.One, destinationAlpha = BlendMode.Zero;
            if (transparent)
            {
                source = blend == 1 ? BlendMode.One : BlendMode.SrcAlpha;
                destination = blend == 2 ? BlendMode.One : BlendMode.OneMinusSrcAlpha;
                destinationAlpha = destination;
                if (blend == 3)
                {
                    source = BlendMode.DstColor;
                    destination = BlendMode.Zero;
                    sourceAlpha = BlendMode.Zero;
                    destinationAlpha = BlendMode.One;
                }
            }

            material.SetFloat("_SrcBlend", (float)source);
            material.SetFloat("_DstBlend", (float)destination);
            material.SetFloat("_SrcBlendAlpha", (float)sourceAlpha);
            material.SetFloat("_DstBlendAlpha", (float)destinationAlpha);
            material.SetFloat("_ZWrite", transparent ? 0f : 1f);
            // Alpha-to-coverage 仅用于不透明裁剪边缘；不改变普通透明 Sprite 的混合方式。
            material.SetFloat("_AlphaToMask", alphaClip && !transparent ? 1f : 0f);
            material.SetOverrideTag("RenderType", transparent ? "Transparent"
                : alphaClip ? "TransparentCutout" : "Opaque");
            int queue = transparent ? (int)RenderQueue.Transparent
                : alphaClip ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
            material.renderQueue = queue + Mathf.RoundToInt(material.GetFloat("_QueueOffset"));
        }

        private static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
        }
    }
}
#endif
