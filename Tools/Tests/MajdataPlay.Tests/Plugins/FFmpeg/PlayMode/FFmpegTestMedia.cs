using System;
using System.IO;
using System.Threading;
using FFmpeg.AutoGen;
using MajdataPlay.FFmpeg;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Creates small synthetic media files with the project's FFmpeg encoder for decoder, muxer and player tests.</summary>
    public static class FFmpegTestMedia
    {
        /// <summary>The default synthetic frame width in pixels.</summary>
        public const int DefaultWidth = 64;
        /// <summary>The default synthetic frame height in pixels.</summary>
        public const int DefaultHeight = 48;

        /// <summary>Ignores the calling test when the native FFmpeg libraries cannot be loaded in this Editor.</summary>
        public static void RequireNativeLibraries()
        {
            try
            {
                if (ffmpeg.avutil_version() >> 16 != ffmpeg.LibraryVersionMap["avutil"])
                {
                    Assert.Ignore("The loaded FFmpeg libraries do not match the pinned ABI.");
                }
            }
            catch (DllNotFoundException error)
            {
                Assert.Ignore("Native FFmpeg libraries are unavailable: " + error.Message);
            }
            catch (EntryPointNotFoundException error)
            {
                Assert.Ignore("Native FFmpeg libraries are incomplete: " + error.Message);
            }
        }

        /// <summary>Builds the full path of a file inside a temporary directory without touching the filesystem.</summary>
        /// <param name="directory">The owning temporary directory.</param>
        /// <param name="name">The file name.</param>
        /// <returns>The absolute path.</returns>
        public static string PathIn(TemporaryDirectory directory, string name)
        {
            return Path.Combine(directory.Location, name);
        }

        /// <summary>Deletes a temporary directory, retrying briefly while a decode or encode worker that outlives its session's Dispose still holds a file.</summary>
        /// <param name="directory">The directory to delete, or null when the fixture never created one.</param>
        /// <exception cref="IOException">A file is still in use after the retry window.</exception>
        public static void DisposeWhenReleased(TemporaryDirectory? directory)
        {
            if (directory == null)
            {
                return;
            }

            // Session Dispose only signals its worker thread, which closes the media file shortly afterwards.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (true)
            {
                try
                {
                    directory.Dispose();
                    return;
                }
                catch (IOException) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(20);
                }
            }
        }

        /// <summary>Creates a tightly packed RGBA frame whose colors depend on position and frame index.</summary>
        /// <param name="width">The frame width in pixels.</param>
        /// <param name="height">The frame height in pixels.</param>
        /// <param name="index">The frame index used to vary the content.</param>
        /// <returns>The packed RGBA bytes.</returns>
        public static byte[] CreateFrame(int width, int height, int index)
        {
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var offset = ((y * width) + x) * 4;
                    pixels[offset] = (byte)((x * 4) + index);
                    pixels[offset + 1] = (byte)((y * 5) + (index * 3));
                    pixels[offset + 2] = (byte)(index * 7);
                    pixels[offset + 3] = 255;
                }
            }

            return pixels;
        }

        /// <summary>Encodes a synthetic clip with consecutive frame timestamps.</summary>
        /// <param name="directory">The temporary directory that receives the file.</param>
        /// <param name="name">The file name, whose extension selects the container.</param>
        /// <param name="frames">The number of frames to encode.</param>
        /// <param name="frameRate">The frame rate and timestamp time base.</param>
        /// <param name="format">The compressed video format.</param>
        /// <param name="width">The frame width in pixels.</param>
        /// <param name="height">The frame height in pixels.</param>
        /// <returns>The absolute path of the finalized file.</returns>
        public static string CreateVideo(TemporaryDirectory directory, string name = "clip.mkv", int frames = 30,
            int frameRate = 30, VideoEncodingFormat format = VideoEncodingFormat.MPEG4, int width = DefaultWidth,
            int height = DefaultHeight)
        {
            var path = PathIn(directory, name);
            var options = new EncoderOptions
            {
                Width = width,
                Height = height,
                FrameRate = frameRate,
                Format = format,
                MaximumSoftwareThreads = 2,
                BitRate = 400000,
                MaximumBitRate = 800000,
                RateControlMode = VideoRateControlMode.VBR
            };
            using (var encoder = new FFmpegVideoEncoder(options))
            {
                encoder.Open(path, CancellationToken.None);
                for (var i = 0; i < frames; i++)
                {
                    encoder.Encode(CreateFrame(width, height, i), i, false);
                }

                encoder.Complete();
            }

            return path;
        }
    }
}
