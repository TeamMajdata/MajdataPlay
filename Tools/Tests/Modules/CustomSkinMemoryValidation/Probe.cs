using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using MajdataPlay.Scenes.Game.Notes.Skins;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Scripting;

#nullable enable
namespace CustomSkinMemoryValidation
{
    /// <summary>
    /// Measures the process memory peak while the production CustomSkin loads a real skin directory.
    /// Shared by the Editor entry point and the standalone player build.
    /// </summary>
    public static class SkinMemoryProbe
    {
        /// <summary>
        /// Holds the number of bytes in one mebibyte.
        /// </summary>
        private const double BytesPerMebibyte = 1024d * 1024d;

        /// <summary>
        /// Loads the skin named by the command line once and checks the memory budget.
        /// </summary>
        /// <returns>The process exit code: zero when every check passes; otherwise one.</returns>
        public static async UniTask<int> RunFromCommandLineAsync()
        {
            try
            {
                var skinPath = RequireArgument("-skinPath");
                var budget = (long)(double.Parse(RequireArgument("-budgetMiB"), CultureInfo.InvariantCulture) * BytesPerMebibyte);
                var asynchronous = RequireArgument("-loadMode") switch
                {
                    "async" => true,
                    "sync" => false,
                    var mode => throw new ArgumentException($"Unknown load mode '{mode}'.")
                };
                await MeasureAsync(skinPath, budget, asynchronous);
                Debug.Log("CUSTOM_SKIN_MEMORY_VALIDATION_PASSED");
                return 0;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return 1;
            }
        }

        /// <summary>
        /// Loads, checks and unloads the skin while sampling process memory.
        /// </summary>
        /// <param name="skinPath">The skin directory.</param>
        /// <param name="budget">The memory budget in bytes.</param>
        /// <param name="asynchronous">Whether to use the asynchronous load path.</param>
        /// <returns>The asynchronous measurement.</returns>
        /// <exception cref="InvalidOperationException">A check or the budget fails.</exception>
        private static async UniTask MeasureAsync(string skinPath, long budget, bool asynchronous)
        {
            var host = Application.isEditor ? "editor" : "player";
            var mode = asynchronous ? "async" : "sync";
            Debug.Log($"CUSTOM_SKIN_MEMORY_DEVICE host={host} api={SystemInfo.graphicsDeviceType} gpu={SystemInfo.graphicsDeviceName} maxTextureSize={SystemInfo.maxTextureSize} skin={skinPath}");

            // Let start-up work settle so it is not attributed to the skin load.
            await UniTask.Delay(TimeSpan.FromSeconds(3), DelayType.Realtime);
            CollectGarbage();

            // In a player the collector is paused while loading, so Mono heap growth equals what the load allocated.
            var gcPaused = !Application.isEditor;
            if (gcPaused)
            {
                GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
            }
            var collectionsBefore = GC.CollectionCount(0);
            var managedBefore = Profiler.GetMonoUsedSizeLong();
            var before = MemoryCounters.Capture();
            var graphicsBefore = Profiler.GetAllocatedMemoryForGraphicsDriver();
            var started = DateTime.UtcNow;
            CustomSkin skin;
            MemoryCounters peak;
            using (var sampler = new MemorySampler())
            {
                if (asynchronous)
                {
                    skin = CustomSkin.Create(skinPath);
                    await skin.LoadAsync();
                }
                else
                {
                    skin = new CustomSkin(skinPath, true);
                }
                peak = sampler.Stop();
            }
            var elapsed = DateTime.UtcNow - started;
            var after = MemoryCounters.Capture();
            var graphicsAfter = Profiler.GetAllocatedMemoryForGraphicsDriver();
            var managedGrowth = Profiler.GetMonoUsedSizeLong() - managedBefore;
            var collections = GC.CollectionCount(0) - collectionsBefore;
            if (gcPaused)
            {
                GarbageCollector.GCMode = GarbageCollector.Mode.Enabled;
            }
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "CUSTOM_SKIN_MEMORY_MANAGED host={0} mode={1} gcPaused={2} monoHeapGrowthMiB={3:F2} collections={4}",
                host, mode, gcPaused, ToMebibytes(managedGrowth), collections));

            Check(skin.IsLoaded, "The skin reports that it is loaded.");
            Check(skin.Tap != null && skin.Just[0] != null && skin.EachLines[0] != null && skin.Wifi[10] != null,
                "Representative atlas sprites are assigned.");
            var atlas = skin.Tap!.texture;
            Check(atlas != null, "Atlas sprites reference a texture.");
            Check(skin.EachLines[0]!.texture == atlas && skin.Wifi[10]!.texture == atlas, "Atlas sprites share one texture.");
            Check(!atlas!.isReadable, "The completed atlas releases its CPU pixel copy.");
            Check(skin.SubDisplay != null && !skin.SubDisplay.texture.isReadable, "The independent sprite releases its CPU pixel copy.");

            var peakPrivateGrowth = peak.PrivateBytes - before.PrivateBytes;
            var graphicsGrowth = Math.Max(0, graphicsAfter - graphicsBefore);
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "CUSTOM_SKIN_MEMORY_CASE host={0} mode={1} elapsedMs={2:F0} atlas={3}x{4} mipmaps={5} " +
                "privateBeforeMiB={6:F1} privatePeakMiB={7:F1} privateAfterMiB={8:F1} privatePeakGrowthMiB={9:F1} " +
                "workingSetBeforeMiB={10:F1} workingSetPeakMiB={11:F1} graphicsDriverAfterMiB={12:F1} graphicsDriverGrowthMiB={13:F1} samples={14}",
                host, mode, elapsed.TotalMilliseconds, atlas.width, atlas.height, atlas.mipmapCount,
                ToMebibytes(before.PrivateBytes), ToMebibytes(peak.PrivateBytes), ToMebibytes(after.PrivateBytes),
                ToMebibytes(peakPrivateGrowth), ToMebibytes(before.WorkingSetBytes), ToMebibytes(peak.WorkingSetBytes),
                ToMebibytes(graphicsAfter), ToMebibytes(graphicsGrowth), peak.SampleCount));

            if (Application.isEditor)
            {
                // The Editor's own footprint is not part of the game; budget what the load adds on top of it.
                var growth = peakPrivateGrowth + graphicsGrowth;
                Check(growth <= budget, $"Skin loading peak growth {ToMebibytes(growth):F1} MiB exceeds {ToMebibytes(budget):F1} MiB.");
            }
            else
            {
                // Conservative absolute figure: the process commit peak plus every graphics-driver allocation,
                // even though a discrete GPU keeps most of the latter outside process memory.
                var total = peak.PrivateBytes + Math.Max(0, graphicsAfter);
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "CUSTOM_SKIN_MEMORY_PROGRAM_PEAK mode={0} privatePeakPlusGraphicsMiB={1:F1} budgetMiB={2:F1}",
                    mode, ToMebibytes(total), ToMebibytes(budget)));
                Check(total <= budget, $"Program peak {ToMebibytes(total):F1} MiB exceeds {ToMebibytes(budget):F1} MiB.");
            }

            await skin.UnloadAsync();
            CollectGarbage();
            var unloaded = MemoryCounters.Capture();
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "CUSTOM_SKIN_MEMORY_UNLOAD host={0} mode={1} privateAfterUnloadMiB={2:F1} releasedMiB={3:F1} graphicsDriverAfterUnloadMiB={4:F1}",
                host, mode, ToMebibytes(unloaded.PrivateBytes), ToMebibytes(after.PrivateBytes - unloaded.PrivateBytes),
                ToMebibytes(Profiler.GetAllocatedMemoryForGraphicsDriver())));
        }

        /// <summary>
        /// Forces managed collection and finalization before a baseline is captured.
        /// </summary>
        private static void CollectGarbage()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        /// <summary>
        /// Reads the value following a command-line switch.
        /// </summary>
        /// <param name="name">The switch name, including its leading dash.</param>
        /// <returns>The switch value.</returns>
        /// <exception cref="ArgumentException">The switch or its value is missing.</exception>
        private static string RequireArgument(string name)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var i = 0; i < arguments.Length - 1; i++)
            {
                if (arguments[i] == name)
                {
                    return arguments[i + 1];
                }
            }
            throw new ArgumentException($"Missing command-line argument {name}.");
        }

        /// <summary>
        /// Converts a byte count to mebibytes.
        /// </summary>
        /// <param name="bytes">The byte count.</param>
        /// <returns>The size in mebibytes.</returns>
        private static double ToMebibytes(long bytes)
        {
            return bytes / BytesPerMebibyte;
        }

        /// <summary>
        /// Requires a validation condition to hold.
        /// </summary>
        /// <param name="condition">The condition to verify.</param>
        /// <param name="message">The failure explanation.</param>
        /// <exception cref="InvalidOperationException">The condition is false.</exception>
        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }

    /// <summary>
    /// Starts the measurement when the standalone player launches.
    /// </summary>
    internal static class PlayerEntry
    {
        /// <summary>
        /// Runs the probe in a player and quits with its result; does nothing in the Editor.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            if (Application.isEditor)
            {
                return;
            }
            RunAsync().Forget();
        }

        /// <summary>
        /// Runs the probe and quits the player with its exit code.
        /// </summary>
        /// <returns>The fire-and-forget run; failures are reported through the exit code.</returns>
        private static async UniTaskVoid RunAsync()
        {
            var exitCode = await SkinMemoryProbe.RunFromCommandLineAsync();
            Application.Quit(exitCode);
        }
    }

    /// <summary>
    /// Holds one reading of the Windows process memory counters.
    /// </summary>
    internal readonly struct MemoryCounters
    {
        /// <summary>
        /// Gets the committed private bytes of the process.
        /// </summary>
        public long PrivateBytes { get; }

        /// <summary>
        /// Gets the resident working set of the process.
        /// </summary>
        public long WorkingSetBytes { get; }

        /// <summary>
        /// Gets the number of readings merged into this value.
        /// </summary>
        public int SampleCount { get; }

        /// <summary>
        /// Creates a reading.
        /// </summary>
        /// <param name="privateBytes">The committed private bytes.</param>
        /// <param name="workingSetBytes">The resident working set.</param>
        /// <param name="sampleCount">The number of merged readings.</param>
        public MemoryCounters(long privateBytes, long workingSetBytes, int sampleCount)
        {
            PrivateBytes = privateBytes;
            WorkingSetBytes = workingSetBytes;
            SampleCount = sampleCount;
        }

        /// <summary>
        /// Reads the current process counters.
        /// </summary>
        /// <returns>The current reading.</returns>
        /// <exception cref="Win32Exception">The counters cannot be queried.</exception>
        public static MemoryCounters Capture()
        {
            var counters = new ProcessMemoryCountersEx { Size = (uint)Marshal.SizeOf<ProcessMemoryCountersEx>() };
            if (!GetProcessMemoryInfo(GetCurrentProcess(), out counters, counters.Size))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            return new MemoryCounters((long)counters.PrivateUsage, (long)counters.WorkingSetSize, 1);
        }

        /// <summary>
        /// Combines two readings by keeping each counter's maximum.
        /// </summary>
        /// <param name="other">The other reading.</param>
        /// <returns>The element-wise maximum.</returns>
        public MemoryCounters Max(MemoryCounters other)
        {
            return new MemoryCounters(Math.Max(PrivateBytes, other.PrivateBytes),
                Math.Max(WorkingSetBytes, other.WorkingSetBytes), SampleCount + other.SampleCount);
        }

        /// <summary>
        /// Mirrors PROCESS_MEMORY_COUNTERS_EX.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessMemoryCountersEx
        {
            /// <summary>The structure size in bytes.</summary>
            public uint Size;
            /// <summary>The page fault count.</summary>
            public uint PageFaultCount;
            /// <summary>The peak working set size.</summary>
            public UIntPtr PeakWorkingSetSize;
            /// <summary>The working set size.</summary>
            public UIntPtr WorkingSetSize;
            /// <summary>The peak paged pool usage.</summary>
            public UIntPtr QuotaPeakPagedPoolUsage;
            /// <summary>The paged pool usage.</summary>
            public UIntPtr QuotaPagedPoolUsage;
            /// <summary>The peak non-paged pool usage.</summary>
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            /// <summary>The non-paged pool usage.</summary>
            public UIntPtr QuotaNonPagedPoolUsage;
            /// <summary>The page file usage.</summary>
            public UIntPtr PagefileUsage;
            /// <summary>The peak page file usage.</summary>
            public UIntPtr PeakPagefileUsage;
            /// <summary>The committed private bytes.</summary>
            public UIntPtr PrivateUsage;
        }

        /// <summary>
        /// Gets the pseudo handle of the current process.
        /// </summary>
        /// <returns>The pseudo handle.</returns>
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        /// <summary>
        /// Reads process memory counters.
        /// </summary>
        /// <param name="process">The process handle.</param>
        /// <param name="counters">Receives the counters.</param>
        /// <param name="size">The counter structure size.</param>
        /// <returns>Whether the query succeeded.</returns>
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetProcessMemoryInfo(IntPtr process, out ProcessMemoryCountersEx counters, uint size);
    }

    /// <summary>
    /// Polls process memory on a background thread to capture transient peaks.
    /// </summary>
    internal sealed class MemorySampler : IDisposable
    {
        /// <summary>
        /// Holds the polling thread.
        /// </summary>
        private readonly Thread _thread;

        /// <summary>
        /// Signals the polling thread to stop.
        /// </summary>
        private volatile bool _stopping;

        /// <summary>
        /// Holds the running maximum; written only by the polling thread until it is joined.
        /// </summary>
        private MemoryCounters _peak;

        /// <summary>
        /// Starts polling immediately.
        /// </summary>
        public MemorySampler()
        {
            _peak = MemoryCounters.Capture();
            _thread = new Thread(Poll) { IsBackground = true, Name = "CustomSkinMemorySampler" };
            _thread.Start();
        }

        /// <summary>
        /// Stops polling and returns the maximum observed counters.
        /// </summary>
        /// <returns>The element-wise maximum of every reading.</returns>
        public MemoryCounters Stop()
        {
            _stopping = true;
            _thread.Join();
            _peak = _peak.Max(MemoryCounters.Capture());
            return _peak;
        }

        /// <summary>
        /// Stops polling if it is still running.
        /// </summary>
        public void Dispose()
        {
            if (!_stopping)
            {
                Stop();
            }
        }

        /// <summary>
        /// Samples the counters every millisecond until stopped.
        /// </summary>
        private void Poll()
        {
            while (!_stopping)
            {
                _peak = _peak.Max(MemoryCounters.Capture());
                Thread.Sleep(1);
            }
        }
    }
}
