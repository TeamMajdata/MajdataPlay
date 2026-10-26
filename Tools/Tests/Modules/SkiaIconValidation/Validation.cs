#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MajdataPlay.IO.Storage;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MajdataPlay.Tests.SkiaIcons
{
    /// <summary>Checks the production icon assigner in an isolated local UPM package.</summary>
    public static class Validation
    {
        /// <summary>The generated icons' physical directory inside the isolated project.</summary>
        private const string IconFolder = "ThirdParty/SkiaForUnity/Editor/Icons";

        /// <summary>The same directory as exposed by Unity's asset database.</summary>
        private const string IconAssetFolder = "Packages/com.ammariqais.skiaforunity/Editor/Icons";

        /// <summary>The obsolete project asset directory, used only by migration fixtures.</summary>
        private const string LegacyFolder = "Assets/SkiaSharp";

        /// <summary>The four production component names represented by empty fixture scripts.</summary>
        private static readonly string[] s_scriptNames =
        {
            "HB_TEXTBlock", "HBTextAnimator", "HBInputField", "SkiaGraphic"
        };

        /// <summary>Counts successful assertions for the final log marker.</summary>
        private static int s_assertions;

        /// <summary>Runs all editor checks and exits Unity with a process result.</summary>
        public static void Run()
        {
            try
            {
                Assert(Application.unityVersion == "6000.3.17f1", "The required Unity version is running.");
                var assigner = Assembly.Load("SkiaSharp.Editor").GetType("SkiaSharp.Unity.HB.Editor.HBIconAssigner", true)!;
                var method = assigner.GetMethod("AssignIcons", BindingFlags.Static | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(assigner.FullName, "AssignIcons");
                var scripts = FindScripts();
                Assign(method);
                AssertNoLegacyFolder();
                ValidateIcons(scripts);
                var snapshots = CaptureGeneratedFiles();

                Assign(method);
                AssertUnchanged(snapshots);
                foreach (var script in scripts)
                {
                    EditorGUIUtility.SetIconForObject(script, null);
                }
                Assign(method);
                ValidateIcons(scripts);
                AssertUnchanged(snapshots);
                AssertNoLegacyFolder();

                var custom = CreateFixtureIcon("Assets/CustomIcons", "Custom.png");
                EditorGUIUtility.SetIconForObject(scripts[0], custom);
                Assign(method);
                Assert(EditorGUIUtility.GetIconForObject(scripts[0]) == custom, "A user-assigned custom icon is preserved.");
                AssertUnchanged(snapshots);

                var legacy = CreateFixtureIcon(LegacyFolder + "/Editor/Icons", "HB_TEXTBlock Icon.png");
                EditorGUIUtility.SetIconForObject(scripts[0], legacy);
                Assign(method);
                Assert(AssetDatabase.GetAssetPath(EditorGUIUtility.GetIconForObject(scripts[0]))
                    == IconAssetFolder + "/HB_TEXTBlock Icon.png", "A legacy generated icon is rebound to the package icon.");
                AssertUnchanged(snapshots);
                Assert(AssetDatabase.DeleteAsset(LegacyFolder), "The isolated legacy migration fixture can be removed.");
                Assign(method);
                AssertNoLegacyFolder();
                ValidateIcons(scripts);
                AssertUnchanged(snapshots);

                Debug.Log($"SKIA_ICON_VALIDATION_PASSED assertions={s_assertions}");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("SKIA_ICON_VALIDATION_FAILED");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Loads the four MonoScript fixtures from the local package.</summary>
        /// <returns>The fixture scripts in the same order as the production icon list.</returns>
        /// <exception cref="InvalidOperationException">A fixture script is missing or does not resolve to its class.</exception>
        private static MonoScript[] FindScripts()
        {
            var scripts = new MonoScript[s_scriptNames.Length];
            for (var index = 0; index < scripts.Length; index++)
            {
                var name = s_scriptNames[index];
                var path = $"Packages/com.ammariqais.skiaforunity/Runtime/{name}.cs";
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                Assert(script != null && script.GetClass()?.Name == name, $"The package fixture {name} is a MonoScript.");
                scripts[index] = script!;
            }
            return scripts;
        }

        /// <summary>Invokes the production callback without modifying its fields.</summary>
        /// <param name="method">The private static production callback.</param>
        /// <exception cref="TargetInvocationException">The production callback failed.</exception>
        private static void Assign(MethodInfo method)
        {
            method.Invoke(null, null);
        }

        /// <summary>Checks physical generation, virtual importing, import settings, and assigned icons.</summary>
        /// <param name="scripts">The four component script fixtures.</param>
        /// <exception cref="InvalidOperationException">An icon was generated, imported, or assigned incorrectly.</exception>
        private static void ValidateIcons(MonoScript[] scripts)
        {
            Assert(FileSystem.Directory.Open(IconFolder).Exists, "The physical ThirdParty icon directory exists.");
            for (var index = 0; index < scripts.Length; index++)
            {
                var fileName = s_scriptNames[index] + " Icon.png";
                var physicalPath = IconFolder + "/" + fileName;
                var assetPath = IconAssetFolder + "/" + fileName;
                Assert(FileSystem.File.Open(physicalPath).Exists, $"{fileName} exists physically in ThirdParty.");
                Assert(FileSystem.File.Open(physicalPath + ".meta").Exists, $"{fileName} has import metadata.");
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                Assert(texture != null && texture.width == 64 && texture.height == 64, $"{fileName} loads through Packages as a 64x64 texture.");
                Assert(EditorGUIUtility.GetIconForObject(scripts[index]) == texture, $"{fileName} is assigned to its script.");
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                Assert(importer != null && importer.textureType == TextureImporterType.GUI
                    && importer.npotScale == TextureImporterNPOTScale.None
                    && importer.textureCompression == TextureImporterCompression.Uncompressed
                    && importer.maxTextureSize == 64, $"{fileName} retains the production icon import settings.");
            }
        }

        /// <summary>Snapshots generated PNG bytes, metadata bytes, and modification times.</summary>
        /// <returns>One snapshot for every icon and its metadata file.</returns>
        /// <exception cref="InvalidOperationException">Generated metadata is unavailable.</exception>
        private static Dictionary<string, (byte[] Bytes, DateTime? Modified)> CaptureGeneratedFiles()
        {
            var snapshots = new Dictionary<string, (byte[] Bytes, DateTime? Modified)>();
            foreach (var file in FileSystem.Directory.Open(IconFolder).EnumerateFiles())
            {
                snapshots.Add(file.Location, (file.ReadAllBytes(), file.Entry?.LastWriteTimeUtc));
            }
            var folderMeta = FileSystem.File.Open(IconFolder + ".meta");
            Assert(folderMeta.Exists, "The generated icon folder has metadata.");
            snapshots.Add(folderMeta.Location, (folderMeta.ReadAllBytes(), folderMeta.Entry?.LastWriteTimeUtc));
            Assert(snapshots.Count == 9, "Only four PNG files, their metadata, and the folder metadata are generated.");
            return snapshots;
        }

        /// <summary>Checks that reassigning icons does not rewrite generated assets or metadata.</summary>
        /// <param name="snapshots">The generated files recorded after the initial assignment.</param>
        /// <exception cref="InvalidOperationException">A generated file changed or disappeared.</exception>
        private static void AssertUnchanged(Dictionary<string, (byte[] Bytes, DateTime? Modified)> snapshots)
        {
            foreach (var snapshot in snapshots)
            {
                var file = FileSystem.File.Open(snapshot.Key);
                Assert(file.Exists && file.ReadAllBytes().SequenceEqual(snapshot.Value.Bytes)
                    && file.Entry?.LastWriteTimeUtc == snapshot.Value.Modified,
                    $"Repeated assignment leaves {snapshot.Key} and its timestamp unchanged.");
            }
        }

        /// <summary>Creates an imported PNG only for custom-icon and legacy-migration fixtures.</summary>
        /// <param name="directory">A fixture directory inside this isolated project's Assets tree.</param>
        /// <param name="name">The fixture PNG filename.</param>
        /// <returns>The imported fixture texture.</returns>
        /// <exception cref="InvalidOperationException">The fixture could not be imported.</exception>
        private static Texture2D CreateFixtureIcon(string directory, string name)
        {
            var folder = FileSystem.Directory.Create(directory);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white });
                texture.Apply();
                FileSystem.File.Create(folder.Location, name, overwrite: true).WriteAllBytes(texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(directory + "/" + name)
                ?? throw new InvalidOperationException("The fixture icon was not imported.");
        }

        /// <summary>Checks that ordinary assignments do not recreate the obsolete Assets directory.</summary>
        /// <exception cref="InvalidOperationException">The obsolete directory or its metadata exists.</exception>
        private static void AssertNoLegacyFolder()
        {
            Assert(!FileSystem.Directory.Open(LegacyFolder).Exists, "Assets/SkiaSharp was not created.");
            Assert(!FileSystem.File.Open(LegacyFolder + ".meta").Exists, "Assets/SkiaSharp.meta was not created.");
        }

        /// <summary>Records one successful assertion or fails the isolated validation.</summary>
        /// <param name="condition">Whether the tested behavior is correct.</param>
        /// <param name="message">The behavior being checked.</param>
        /// <exception cref="InvalidOperationException">The asserted condition is false.</exception>
        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
            s_assertions++;
        }
    }
}
