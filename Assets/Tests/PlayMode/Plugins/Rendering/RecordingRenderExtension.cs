using MajdataPlay.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Rendering
{
    /// <summary>Records the calls made by the pipeline to a camera render extension.</summary>
    public sealed class RecordingRenderExtension : IMajCameraRenderExtension
    {
        /// <summary>Creates a recording extension.</summary>
        /// <param name="intermediate">Whether the extension requires an intermediate target.</param>
        public RecordingRenderExtension(bool intermediate)
        {
            RequiresIntermediateTarget = intermediate;
        }

        /// <summary>Gets or sets whether Prepare accepts cameras.</summary>
        public bool Accept { get; set; } = true;
        /// <summary>Gets whether the extension requires an intermediate target.</summary>
        public bool RequiresIntermediateTarget { get; }
        /// <summary>Gets the number of Prepare calls.</summary>
        public int Prepared { get; private set; }
        /// <summary>Gets the number of Render calls.</summary>
        public int Rendered { get; private set; }
        /// <summary>Gets the number of EndCamera calls.</summary>
        public int Ended { get; private set; }
        /// <summary>Gets whether the extension was disposed.</summary>
        public bool Disposed { get; private set; }
        /// <summary>Gets the color target of the last Render call.</summary>
        public RenderTexture? LastColor { get; private set; }
        /// <summary>Gets the depth target of the last Render call.</summary>
        public RenderTexture? LastDepth { get; private set; }
        /// <summary>Gets the width of the last Render call.</summary>
        public int LastWidth { get; private set; }

        /// <summary>Records a prepare call.</summary>
        /// <param name="camera">The camera about to render.</param>
        /// <returns>Whether the extension draws this camera.</returns>
        public bool Prepare(Camera camera)
        {
            Prepared++;
            return Accept;
        }

        /// <summary>Records a render call.</summary>
        /// <param name="buffer">The pipeline command buffer.</param>
        /// <param name="camera">The rendering camera.</param>
        /// <param name="colorTarget">The optional color target.</param>
        /// <param name="depthTarget">The optional depth target.</param>
        /// <param name="width">The target width.</param>
        /// <param name="height">The target height.</param>
        public void Render(CommandBuffer buffer, Camera camera, RenderTexture? colorTarget, RenderTexture? depthTarget, int width, int height)
        {
            Rendered++;
            LastColor = colorTarget;
            LastDepth = depthTarget;
            LastWidth = width;
        }

        /// <summary>Records the end of a camera.</summary>
        public void EndCamera()
        {
            Ended++;
        }

        /// <summary>Records disposal.</summary>
        public void Dispose()
        {
            Disposed = true;
        }
    }
}
