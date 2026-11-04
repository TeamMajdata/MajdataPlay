#nullable enable

namespace MajdataPlay.FFmpeg
{
    /// <summary>Selects how hardware codec setup continues after the GPU that renders Unity cannot decode or encode the requested media.</summary>
    /// <remarks>
    /// Windows enumerates DXGI adapters and Linux enumerates DRM render nodes. Apple platforms and Android select their hardware
    /// codec internally, so both routes behave the same there. Unity's textures cannot be shared with another GPU, so decoding
    /// on another GPU always downloads frames for CPU texture upload.
    /// </remarks>
    public enum HardwareFallbackRoute
    {
        /// <summary>Tries the hardware codec of each remaining GPU in system order, then falls back to the software codec.</summary>
        OtherGpus,
        /// <summary>Falls back directly to the software codec without trying other GPUs.</summary>
        SoftwareCodec
    }
}
