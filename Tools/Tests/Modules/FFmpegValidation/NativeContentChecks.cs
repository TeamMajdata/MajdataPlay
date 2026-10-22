#nullable enable

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using MajdataPlay.FFmpeg;
using MajdataPlay.FFmpeg.Internal;
using MajdataPlay.FFmpeg.Interop;

namespace MajdataPlay.FFmpeg.Validation
{
    /// <summary>Checks opaque native content inputs without invoking Android JNI or Unity native APIs.</summary>
    internal static class NativeContentChecks
    {
        /// <summary>Checks managed source normalization and the Editor initialization guard without constructing a Unity component.</summary>
        /// <returns>The number of source normalization and Editor initialization guard assertions completed.</returns>
        /// <exception cref="InvalidOperationException">The normalization entry point is missing, changes an input, or content initialization does not reject the Editor.</exception>
        internal static int RunManaged()
        {
            var method = typeof(FFmpegVideoPlayer).GetMethod("NormalizeSource",
                BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null);
            if (method is null || method.ReturnType != typeof(string))
            {
                throw new InvalidOperationException("FFmpegVideoPlayer.NormalizeSource(string) is required for native content checks.");
            }

            var normalize = method.CreateDelegate<Func<string, string>>();
            var checks = 0;
            var opaqueSources = new[]
            {
                "content://com.android.externalstorage.documents/document/primary%3AMovies%2Fclip.mp4",
                "content://Com.Android.ExternalStorage.Documents/tree/primary%3aMovies/document/primary%3aMovies%2fclip.mp4",
                "CONTENT://Documents.EXAMPLE/document/raw%3A%2Fstorage%2Femulated%2F0%2Fclip.mp4",
                "CoNtEnT://UPPERCASE.Provider/document/volume%3AMovies%2Fname%252F%253A.mp4",
                "content://MiXeD.Provider/document/primary%3AMovies%2Fclip.mp4?token=a%2Bb&documentId=raw%3A%2Fvideo&name=%22demo%22#part%2F%3A",
                "content://MiXeD.Provider/document/primary%3AMovies%2Fmy \"quoted\" clip.mp4",
                "content://MiXeD.Provider/document/primary%3AMovies%2Fclip.mp4\"",
                "content://MiXeD.Provider/document/primary%3AMovies%2Fclip.mp4 \t",
                "content:opaque%2Fdocument%3Aid?Name=%22Quoted%22"
            };
            foreach (var source in opaqueSources)
            {
                Check(string.Equals(normalize(source), source, StringComparison.Ordinal),
                    "Content input must remain opaque, including case, escapes, query, quotes and trailing whitespace: " + source);
                checks++;
            }

            var localPath = Path.Combine(Path.GetTempPath(), "FFmpeg content checks", "clip #1%2F.mp4");
            var fileUri = new Uri(localPath).AbsoluteUri;
            var https = "https://MiXeD.Example/Movies/%2Fclip%3A.mp4?signature=AbC%2b%2f&name=%22clip%22";
            var http = "http://MiXeD.Example/Movies/clip.mp4?name=%22clip%22";
            var legacySources = new (string Source, string Expected)[]
            {
                (localPath, localPath),
                (" \t" + localPath + "\r\n", localPath),
                (" \"" + localPath + "\" ", localPath),
                (fileUri, localPath),
                (" \t" + fileUri + "\r\n", localPath),
                (" \"" + fileUri + "\" ", localPath),
                (https, https),
                (" \t\"" + https + "\"\r\n", https),
                (http, http),
                (" \t\"" + http + "\"\r\n", http)
            };
            foreach (var fixture in legacySources)
            {
                Check(string.Equals(normalize(fixture.Source), fixture.Expected, StringComparison.Ordinal),
                    "Local, file and HTTP inputs must retain their existing trim, quote and file URI behavior: " + fixture.Source);
                checks++;
            }

            var editorRejected = false;
            try
            {
                AndroidContentInterop.Initialize();
            }
            catch (PlatformNotSupportedException)
            {
                editorRejected = true;
            }

            Check(editorRejected,
                "Android content initialization must reject the Editor branch without invoking JNI or Unity native APIs.");
            checks++;

            Console.WriteLine("Native content managed: " + checks
                + " checks; opaque content inputs, legacy local/file/HTTP behavior and Editor initialization guard; no Unity or JNI calls.");
            return checks;
        }

        /// <summary>Checks the loaded Windows input protocol table and explicit rejection of unsupported content inputs.</summary>
        /// <returns>The number of real native protocol and software decoder rejection assertions completed.</returns>
        /// <remarks>The caller must configure native library resolution first. These checks do not validate Android playback.</remarks>
        /// <exception cref="PlatformNotSupportedException">The validation process is not running on Windows.</exception>
        /// <exception cref="InvalidOperationException">The loaded protocols or unsupported-input diagnostic differ from the expected Windows behavior.</exception>
        /// <exception cref="DllNotFoundException">The required FFmpeg native libraries are unavailable.</exception>
        /// <exception cref="EntryPointNotFoundException">The loaded FFmpeg library does not expose a required entry point.</exception>
        internal static int RunWindows()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new PlatformNotSupportedException("The native content protocol absence check requires Windows FFmpeg libraries.");
            }

            var checks = 0;
            for (var iteration = 0; iteration < 3; iteration++)
            {
                Check(FFmpegVideoDecoder.SupportsInputProtocol("file"),
                    "The loaded Windows input protocol table must contain file on every fresh enumeration.");
                checks++;
                Check(!FFmpegVideoDecoder.SupportsInputProtocol("content"),
                    "The loaded Windows input protocol table must not claim Android content support.");
                checks++;
            }

            foreach (var source in new[]
            {
                "content://validation.example/document/primary%3AVideos%2Fsoftware.mp4?name=%22opaque%22",
                "CoNtEnT://Validation.EXAMPLE/document/primary%3AVideos%2Fsoftware.mp4"
            })
            {
                using var decoder = new FFmpegVideoDecoder();
                var rejected = false;
                try
                {
                    decoder.Open(source, CancellationToken.None);
                }
                catch (NotSupportedException error)
                {
                    rejected = error.Message.Contains("content", StringComparison.OrdinalIgnoreCase)
                        && error.Message.Contains("protocol", StringComparison.OrdinalIgnoreCase);
                }

                Check(rejected,
                    "A software decoder must explicitly reject unavailable content input before native opening or hardware initialization.");
                checks++;
            }

            Console.WriteLine("Windows native content: " + checks
                + " checks; avio_enum_protocols reports file present/content absent; software Open rejects content explicitly. Android JNI/device playback is NOT VERIFIED.");
            return checks;
        }

        /// <summary>Rejects an observed normalization or protocol invariant violation.</summary>
        /// <param name="condition">Whether the expected behavior was observed.</param>
        /// <param name="message">The diagnostic identifying the failed invariant.</param>
        /// <exception cref="InvalidOperationException">The condition is false.</exception>
        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
