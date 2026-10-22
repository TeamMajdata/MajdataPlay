using System;
using MajdataPlay.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Rendering
{
    /// <summary>Activates a temporary, configured Maj Render Pipeline asset and restores the project's pipeline afterwards.</summary>
    public sealed class PipelineScope : IDisposable
    {
        /// <summary>Stores the quality-level pipeline that was active before the scope.</summary>
        private readonly RenderPipelineAsset? _previousQuality;
        /// <summary>Stores the default pipeline that was active before the scope.</summary>
        private readonly RenderPipelineAsset? _previousDefault;
        /// <summary>Restores the static diagnostics written by main-screen renders.</summary>
        private readonly DiagnosticsSnapshot _diagnostics = new DiagnosticsSnapshot();

        /// <summary>Creates and activates a pipeline asset with the requested serialized settings.</summary>
        /// <param name="msaa">The serialized MSAA mode.</param>
        /// <param name="renderScale">The serialized render scale.</param>
        /// <param name="optimizeAttachments">Whether native render passes are enabled.</param>
        /// <param name="finalCopy">Whether the final copy shader is assigned.</param>
        /// <param name="resolveToScreen">Whether direct screen resolves are allowed.</param>
        public PipelineScope(MajMSAAMode msaa = MajMSAAMode.Disabled, float renderScale = 1f, bool optimizeAttachments = true,
            bool finalCopy = true, bool resolveToScreen = true)
        {
            Asset = ScriptableObject.CreateInstance<MajRenderPipelineAsset>();
            Asset.name = "Test MRP";
            Asset.hideFlags = HideFlags.DontSave;
            PrivateMember.SetField(Asset, "_msaaMode", msaa);
            PrivateMember.SetField(Asset, "_renderScale", renderScale);
            PrivateMember.SetField(Asset, "_optimizeAttachments", optimizeAttachments);
            PrivateMember.SetField(Asset, "_resolveToScreen", resolveToScreen);
            PrivateMember.SetField(Asset, "_finalCopyShader", finalCopy ? Shader.Find("Hidden/Maj/FinalCopy") : null);
            _previousQuality = QualitySettings.renderPipeline;
            _previousDefault = GraphicsSettings.defaultRenderPipeline;
            if (_previousQuality != null)
            {
                QualitySettings.renderPipeline = Asset;
            }
            else
            {
                GraphicsSettings.defaultRenderPipeline = Asset;
            }
        }

        /// <summary>Gets the temporary pipeline asset.</summary>
        public MajRenderPipelineAsset Asset { get; }

        /// <summary>Restores the previous pipeline assets and diagnostics and destroys the temporary asset.</summary>
        public void Dispose()
        {
            if (_previousQuality != null)
            {
                QualitySettings.renderPipeline = _previousQuality;
            }
            else
            {
                GraphicsSettings.defaultRenderPipeline = _previousDefault;
            }

            _diagnostics.Dispose();
            Object.DestroyImmediate(Asset);
        }

        /// <summary>Reads the color at the center of a render texture.</summary>
        /// <param name="texture">The texture to read.</param>
        /// <returns>The center pixel.</returns>
        public static Color ReadCenter(RenderTexture texture)
        {
            var previous = RenderTexture.active;
            var readback = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = texture;
                readback.ReadPixels(new Rect(texture.width / 2, texture.height / 2, 1, 1), 0, 0);
                readback.Apply();
                return readback.GetPixel(0, 0);
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(readback);
            }
        }
    }
}
