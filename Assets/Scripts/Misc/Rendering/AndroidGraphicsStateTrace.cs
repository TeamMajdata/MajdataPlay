#nullable enable
#if UNITY_ANDROID && DEVELOPMENT_BUILD && !UNITY_EDITOR
using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.SceneManagement;

namespace MajdataPlay.Rendering
{
    /// <summary>
    /// Captures graphics states on Android development players for subsequent gameplay loading warmup.
    /// </summary>
    internal static class AndroidGraphicsStateTrace
    {
        /// <summary>Retains the states encountered at each Unity quality level during this run.</summary>
        private static readonly Dictionary<int, GraphicsStateCollection> s_collections = new();

        /// <summary>The collection currently receiving graphics state traces.</summary>
        private static GraphicsStateCollection? s_collection;

        /// <summary>The quality level associated with the current collection.</summary>
        private static int s_qualityLevel;

        /// <summary>Whether tracing should resume after a checkpoint.</summary>
        private static bool s_hasFocus = true;

        /// <summary>Starts capture before the first scene and subscribes to infrequent save points.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            QualitySettings.activeQualityLevelChanged += OnQualityLevelChanged;
            Application.focusChanged += OnFocusChanged;
            Application.quitting += OnQuitting;
            if (!SystemInfo.supportsParallelPSOCreation)
            {
                MajDebug.LogWarning(nameof(AndroidGraphicsStateTrace),
                    "Parallel PSO creation is unavailable; capture may contain shader variants without graphics states.");
            }
            BeginTrace();
        }

        /// <summary>Starts or resumes the collection for the current Unity quality level.</summary>
        private static void BeginTrace()
        {
            try
            {
                s_qualityLevel = QualitySettings.GetQualityLevel();
                if (!s_collections.TryGetValue(s_qualityLevel, out var collection))
                {
                    collection = new GraphicsStateCollection();
                    s_collections.Add(s_qualityLevel, collection);
                }
                s_collection = collection;
                if (collection.isTracing)
                {
                    return;
                }
                if (!collection.BeginTrace())
                {
                    MajDebug.LogWarning(nameof(AndroidGraphicsStateTrace),
                        "Could not begin graphics state tracing; another collection may already be tracing.");
                    return;
                }
                MajDebug.LogDebug(nameof(AndroidGraphicsStateTrace),
                    $"Tracing {collection.graphicsDeviceType}, quality {collection.qualityLevelName}.");
            }
            catch (Exception exception)
            {
                MajDebug.LogError(nameof(AndroidGraphicsStateTrace), "Failed to begin graphics state tracing.");
                MajDebug.LogException(nameof(AndroidGraphicsStateTrace), exception);
            }
        }

        /// <summary>Stops capture long enough to save a stable collection, optionally resuming afterward.</summary>
        /// <param name="resume">Whether to resume capture while the player has focus.</param>
        private static void Checkpoint(bool resume)
        {
            var collection = s_collection;
            if (collection == null)
            {
                return;
            }
            try
            {
                if (collection.isTracing)
                {
                    collection.EndTrace();
                }
                if (collection.variantCount == 0)
                {
                    MajDebug.LogDebug(nameof(AndroidGraphicsStateTrace), "No shader variants have been captured yet.");
                    return;
                }
                // Unity's serializer requires a native file path. Resolve the child through the
                // local persistent-data directory instead of constructing a provider URI manually.
                var directory = FileSystem.Directory.Open(Application.persistentDataPath).CreateDirectory("GraphicsStates");
                var fileName = $"Android-{collection.graphicsDeviceType}-Quality{s_qualityLevel}.graphicsstate";
                var file = directory.FindFile(fileName) ?? directory.CreateFile(fileName);
                if (!collection.SaveToFile(file.Location))
                {
                    MajDebug.LogError(nameof(AndroidGraphicsStateTrace), $"Failed to save {fileName}.");
                    return;
                }
                MajDebug.LogInfo(nameof(AndroidGraphicsStateTrace),
                    $"Saved {fileName}: {collection.variantCount} variants, {collection.totalGraphicsStateCount} graphics states.");
            }
            catch (Exception exception)
            {
                MajDebug.LogError(nameof(AndroidGraphicsStateTrace), "Failed to checkpoint graphics state tracing.");
                MajDebug.LogException(nameof(AndroidGraphicsStateTrace), exception);
            }
            finally
            {
                if (resume && s_hasFocus)
                {
                    BeginTrace();
                }
            }
        }

        /// <summary>Saves states from the preceding scene before rendering the newly loaded scene.</summary>
        /// <param name="scene">The scene that has finished loading.</param>
        /// <param name="mode">Whether the scene was loaded additively.</param>
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Checkpoint(true);
        }

        /// <summary>Keeps traces from different Unity quality levels in separate collections.</summary>
        /// <param name="previousLevel">The Unity quality level that was active before this change.</param>
        /// <param name="currentLevel">The newly active Unity quality level.</param>
        private static void OnQualityLevelChanged(int previousLevel, int currentLevel)
        {
            Checkpoint(true);
        }

        /// <summary>Flushes the current trace when Android backgrounds the player and resumes on return.</summary>
        /// <param name="hasFocus">Whether the player now has application focus.</param>
        private static void OnFocusChanged(bool hasFocus)
        {
            s_hasFocus = hasFocus;
            if (hasFocus)
            {
                BeginTrace();
            }
            else
            {
                Checkpoint(false);
            }
        }

        /// <summary>Saves the final trace and releases the native collection objects on normal exit.</summary>
        private static void OnQuitting()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            QualitySettings.activeQualityLevelChanged -= OnQualityLevelChanged;
            Application.focusChanged -= OnFocusChanged;
            Application.quitting -= OnQuitting;
            Checkpoint(false);
            foreach (var collection in s_collections.Values)
            {
                UnityEngine.Object.Destroy(collection);
            }
            s_collections.Clear();
            s_collection = null;
        }
    }
}
#endif
