using System;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

#nullable enable

namespace MajdataPlay.Platform.Android.Editor
{
    /// <summary>
    /// Invalidates source-generator output when Java input assets change and exposes manual refresh.
    /// </summary>
    /// <remarks>
    /// Unity does not track files read by Roslyn generators as C# compilation inputs. Imported Java
    /// assets trigger a cache-invalidated compilation; external SDK, JDK, and Java-input changes
    /// need the menu. Clearing the script-build cache prevents reuse of wrappers generated from
    /// an unchanged C# declaration with stale external Java inputs.
    /// </remarks>
    internal sealed class JavaWrapperRefresh : AssetPostprocessor
    {
        /// <summary>
        /// Indicates whether a coalesced compilation request is already queued.
        /// </summary>
        private static bool s_refreshQueued;

        /// <summary>
        /// Requests regeneration of every wrapper using the current Java files and SDK configuration.
        /// </summary>
        [MenuItem("Tools/MajdataPlay/Android/Regenerate Java wrappers")]
        private static void Regenerate()
        {
            CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache);
            Debug.Log("[Android Java generator] Script compilation requested; Java sources and SDK metadata will be read again.");
        }

        /// <summary>
        /// Schedules one compilation after Java source or bytecode assets are imported or removed.
        /// </summary>
        /// <param name="importedAssets">The imported asset paths.</param>
        /// <param name="deletedAssets">The removed asset paths.</param>
        /// <param name="movedAssets">The destination paths of moved assets.</param>
        /// <param name="movedFromAssetPaths">The source paths of moved assets.</param>
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (s_refreshQueued || !(ContainsJavaInput(importedAssets) || ContainsJavaInput(deletedAssets)
                || ContainsJavaInput(movedAssets) || ContainsJavaInput(movedFromAssetPaths)))
            {
                return;
            }

            s_refreshQueued = true;
            EditorApplication.delayCall += RefreshAfterImport;
        }

        /// <summary>
        /// Coalesces Java asset changes into one compilation request outside the import callback.
        /// </summary>
        private static void RefreshAfterImport()
        {
            s_refreshQueued = false;
            CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache);
        }

        /// <summary>
        /// Checks for Java inputs without allocating a LINQ query or temporary collection.
        /// </summary>
        /// <param name="paths">The imported, deleted, or moved asset paths.</param>
        /// <returns>True when a Java source, class file, or JAR archive is present.</returns>
        private static bool ContainsJavaInput(string[] paths)
        {
            foreach (var path in paths)
            {
                if (path.EndsWith(".java", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".class", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
