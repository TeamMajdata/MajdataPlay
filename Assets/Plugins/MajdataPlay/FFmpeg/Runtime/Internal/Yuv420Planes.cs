#nullable enable
namespace MajdataPlay.FFmpeg.Internal
{
    /// <summary>Describes borrowed, writable 8-bit 4:2:0 planes in planar (YUV420P) or semi-planar (NV12) layout.</summary>
    /// <remarks>
    /// Chroma rows cover two luma rows and chroma samples cover two luma columns. In NV12 both chroma pointers address the
    /// same interleaved plane, one byte apart, with a sample step of two.
    /// </remarks>
    internal unsafe struct Yuv420Planes
    {
        /// <summary>The first luma row.</summary>
        public byte* Luma;
        /// <summary>The byte distance between consecutive luma rows.</summary>
        public int LumaStride;
        /// <summary>The first blue-difference (Cb) sample of the first chroma row.</summary>
        public byte* BlueDifference;
        /// <summary>The byte distance between consecutive blue-difference rows.</summary>
        public int BlueDifferenceStride;
        /// <summary>The first red-difference (Cr) sample of the first chroma row.</summary>
        public byte* RedDifference;
        /// <summary>The byte distance between consecutive red-difference rows.</summary>
        public int RedDifferenceStride;
        /// <summary>The byte distance between horizontally adjacent samples of one chroma component: one for planar and two for NV12.</summary>
        public int ChromaStep;
    }
}
