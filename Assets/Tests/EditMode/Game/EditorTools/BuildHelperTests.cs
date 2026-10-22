using System;
using System.Collections.Generic;
using System.IO;
using MajdataPlay.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.EditorTools
{
    /// <summary>Covers the small build and project generation hooks of the editor tooling without building players.</summary>
    public class BuildHelperTests
    {
        /// <summary>Tracks objects created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Creates the object tracker.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
        }

        /// <summary>Destroys the test's objects.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
        }

        /// <summary>Checks that generated project files are switched to the preview language version.</summary>
        [Test]
        public void LangVersionPostprocessor_ReplacesEveryLangVersion()
        {
            const string project = "<Project><LangVersion>9.0</LangVersion><X/><LangVersion>latest</LangVersion></Project>";

            var result = PrivateMember.InvokeStatic<string>(typeof(LangVersionPostprocessor), "OnGeneratedCSProject", "A.csproj", project);

            Assert.That(result, Is.EqualTo("<Project><LangVersion>preview</LangVersion><X/><LangVersion>preview</LangVersion></Project>"));
            Assert.That(PrivateMember.InvokeStatic<string>(typeof(LangVersionPostprocessor), "OnGeneratedCSProject", "A.csproj", "<Project/>"), Is.EqualTo("<Project/>"));
        }

        /// <summary>Checks the translation key lists kept by the template generator and its no-op build hook.</summary>
        [Test]
        public void TranslationTemplateGenerator_ListsSceneCommonAndErrorKeys()
        {
            TranslationTemplateGenerator.OnPreprocessBuild(null!);

            var scenes = PrivateMember.GetStaticField<Dictionary<string, string[]>>(typeof(TranslationTemplateGenerator), "_scene_translations");
            var common = PrivateMember.GetStaticField<string[]>(typeof(TranslationTemplateGenerator), "_common_translation");
            var errors = PrivateMember.GetStaticField<string[]>(typeof(TranslationTemplateGenerator), "_err_translations");
            Assert.That(scenes.Keys, Is.EquivalentTo(new[] { "Login", "SortFind", "List" }));
            Assert.That(scenes["SortFind"], Does.Contain("SortbyRank"));
            Assert.That(common, Does.Contain("MAJTEXT_SAY"));
            Assert.That(errors, Has.All.StartsWith("MAJTEXT_ERR_"));
        }

        /// <summary>Checks that scene processing outside player builds keeps picking geometry.</summary>
        [Test]
        public void CubismPickingBuildProcessor_KeepsGeometryOutsidePlayerBuilds()
        {
            var processor = new CubismPickingBuildProcessor();
            var meshFilter = _objects.Add<MeshFilter>("Picking");

            processor.OnProcessScene(SceneManager.GetActiveScene(), null!);

            Assert.That(processor.callbackOrder, Is.EqualTo(0));
            Assert.That(meshFilter != null, Is.True);
        }

        /// <summary>Checks that the build processor runs first and restores nothing when no SDK override was made.</summary>
        [Test]
        public void BuildProcessor_PostprocessWithoutOverrideOnlyLogs()
        {
            var processor = new BuildProcessor();
            PrivateMember.SetStaticField(typeof(BuildProcessor), "_originSdkVersion", null);
            LogAssert.Expect(LogType.Warning, "OnPostprocessBuild");

            processor.OnPostprocessBuild(null!);

            Assert.That(processor.callbackOrder, Is.EqualTo(0));
            Assert.That(PrivateMember.GetStaticField<AndroidSdkVersions?>(typeof(BuildProcessor), "_originSdkVersion"), Is.Null);
        }

        /// <summary>Checks that the build processor restores a recorded Android SDK version and clears the record.</summary>
        [Test]
        public void BuildProcessor_PostprocessRestoresRecordedSdkVersion()
        {
            var processor = new BuildProcessor();
            var current = PlayerSettings.Android.targetSdkVersion;
            PrivateMember.SetStaticField(typeof(BuildProcessor), "_originSdkVersion", current);
            LogAssert.Expect(LogType.Warning, "OnPostprocessBuild");

            processor.OnPostprocessBuild(null!);

            Assert.That(PlayerSettings.Android.targetSdkVersion, Is.EqualTo(current));
            Assert.That(PrivateMember.GetStaticField<AndroidSdkVersions?>(typeof(BuildProcessor), "_originSdkVersion"), Is.Null);
        }

        /// <summary>Checks that command-line parsing reads flags with and without values from the running editor.</summary>
        [Test]
        public void CustomBuild_ParsesEditorCommandLine()
        {
            var arguments = new object?[] { null };

            PrivateMember.InvokeStatic(typeof(CustomBuild), "ParseCommandLineArguments", arguments);

            var options = (Dictionary<string, string>)arguments[0]!;
            Assert.That(options.ContainsKey("projectPath"), Is.True);
            Assert.That(Path.GetFullPath(options["projectPath"]).TrimEnd('\\', '/'), Is.EqualTo(Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd('\\', '/')).IgnoreCase);
            Assert.That(options["batchmode"], Is.Empty);
        }

        /// <summary>Checks that an empty build summary can be reported.</summary>
        [Test]
        public void CustomBuild_ReportsBuildSummary()
        {
            Assert.DoesNotThrow(() => PrivateMember.InvokeStatic(typeof(CustomBuild), "ReportSummary", default(BuildSummary)));
        }
    }
}
