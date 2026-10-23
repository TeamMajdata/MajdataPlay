using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MajdataPlay.Diagnostics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

#nullable enable
namespace MajdataPlay.Rendering
{
    /// <summary>Prewarms captured Android graphics states while the scene loading screen remains responsive.</summary>
    internal static class AndroidGraphicsStateWarmup
    {
        /// <summary>The Resources folder containing device captures included in the Player.</summary>
        internal const string ResourcePath = "GraphicsStates/Android";

        /// <summary>Keeps collections alive so their completed warmup state survives scene changes.</summary>
        private static GraphicsStateCollection[]? s_collections;

        /// <summary>Clears cached assets when starting a new Player session, including domain-reload-free Play Mode.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            s_collections = null;
        }

        /// <summary>Loads matching device captures and waits for their warmup jobs on the Unity main thread.</summary>
        /// <param name="cancellationToken">Cancels loading when the owning scene switcher is destroyed.</param>
        /// <returns>A task that completes once all matching collections have finished warming up.</returns>
        /// <exception cref="OperationCanceledException">The owner was destroyed before warmup finished.</exception>
        internal static async UniTask WarmUpAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (s_collections == null)
            {
                var request = Resources.LoadAll<GraphicsStateCollection>(ResourcePath);
                s_collections = request;
                MajDebug.LogDebug(nameof(AndroidGraphicsStateWarmup), $"Loaded {request.Length} Android graphics state collections.");
            }

            var qualityLevelName = QualitySettings.names[QualitySettings.GetQualityLevel()];
            var matched = false;
            foreach (var collection in s_collections)
            {
                if (!IsCompatible(collection, SystemInfo.graphicsDeviceType, qualityLevelName))
                {
                    continue;
                }

                matched = true;
                await WarmUpAsync(collection, cancellationToken);
            }

            if (!matched)
            {
                MajDebug.LogWarning(nameof(AndroidGraphicsStateWarmup),
                    $"No captured Android graphics states for {SystemInfo.graphicsDeviceType}/{qualityLevelName}; shaders will compile on first use.");
            }
        }

        /// <summary>Checks whether a nonempty capture describes the current Android rendering configuration.</summary>
        /// <param name="collection">The capture to inspect; missing or destroyed assets are rejected.</param>
        /// <param name="graphicsDeviceType">The active graphics API.</param>
        /// <param name="qualityLevelName">The active quality preset name.</param>
        /// <returns>Whether this capture can be used for the current configuration.</returns>
        internal static bool IsCompatible(GraphicsStateCollection? collection, GraphicsDeviceType graphicsDeviceType, string qualityLevelName)
        {
            return collection != null
                && collection.variantCount > 0
                && collection.runtimePlatform == RuntimePlatform.Android
                && collection.graphicsDeviceType == graphicsDeviceType
                && string.Equals(collection.qualityLevelName, qualityLevelName, StringComparison.Ordinal);
        }

        /// <summary>Schedules one collection's warmup and yields frames until the native job finishes.</summary>
        /// <param name="collection">A compatible capture whose shaders remain loaded for the duration of the job.</param>
        /// <param name="cancellationToken">Stops the scene transition if its owner is destroyed.</param>
        /// <returns>A task that completes after warmup, or immediately for empty or already warmed captures.</returns>
        /// <exception cref="OperationCanceledException">Cancellation was requested; any scheduled job is joined before returning.</exception>
        internal static async UniTask WarmUpAsync(GraphicsStateCollection collection, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (collection.variantCount == 0 || collection.isWarmedUp)
            {
                return;
            }

            var startedAt = Time.realtimeSinceStartupAsDouble;
            var handle = collection.WarmUp();
            try
            {
                while (!handle.IsCompleted)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
            }
            finally
            {
                // Unity jobs cannot be cancelled. Always release their resources before returning,
                // including when destruction cancels the surrounding scene transition.
                handle.Complete();
            }

            cancellationToken.ThrowIfCancellationRequested();
            MajDebug.LogInfo(nameof(AndroidGraphicsStateWarmup),
                $"Warmed {collection.variantCount} variants / {collection.totalGraphicsStateCount} graphics states in {(Time.realtimeSinceStartupAsDouble - startedAt) * 1000:F1} ms ({collection.graphicsDeviceType}).");
        }
    }
}
