using System;
using System.IO;
using System.Reflection;
using System.Text;
using MajdataPlay.IO.Storage;
using MajdataPlay.Rendering;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>Verifies the journal and shader selection of the Android shader warmup probe <see cref="ShaderWarmupDiagnostics"/>.</summary>
    public class ShaderWarmupDiagnosticsTests
    {
        /// <summary>The private options type of the probe.</summary>
        private static readonly Type s_optionsType = typeof(ShaderWarmupDiagnostics).GetNestedType("Options", BindingFlags.NonPublic)!;

        /// <summary>The temporary directory holding the journal.</summary>
        private TemporaryDirectory _temporary = null!;

        /// <summary>Creates the temporary directory.</summary>
        [SetUp]
        public void SetUp()
        {
            _temporary = new TemporaryDirectory();
        }

        /// <summary>Deletes the temporary directory.</summary>
        [TearDown]
        public void TearDown()
        {
            _temporary.Dispose();
        }

        /// <summary>Checks that quoting escapes backslashes, quotes and line breaks.</summary>
        [Test]
        public void Quote_EscapesSpecialCharacters()
        {
            var quoted = PrivateMember.InvokeStatic<string>(typeof(ShaderWarmupDiagnostics), "Quote", "a\"b\\c\r\nd");

            Assert.That(quoted, Is.EqualTo("\"a\\\"b\\\\c\\r\\nd\""));
        }

        /// <summary>Checks that markers are written to the journal with the probe tag.</summary>
        [Test]
        public void Mark_WritesTaggedLines()
        {
            var journal = Journal(writer => PrivateMember.InvokeStatic(typeof(ShaderWarmupDiagnostics), "Mark", writer, "HELLO"));

            Assert.That(journal, Does.Contain("[ShaderWarmupDiagnostics] HELLO"));
        }

        /// <summary>Checks that an exact shader name warms only that shader and records its identity.</summary>
        [Test]
        public void WarmIndividualShaders_ByName_WarmsOneShader()
        {
            var shader = Shader.Find("Sprites/Default");
            var options = CreateOptions(shader.name, 0, 0);

            var journal = Journal(writer => Warm(writer, options));

            Assert.That(journal, Does.Contain("BEGIN_ENUMERATE"));
            Assert.That(journal, Does.Contain("INVENTORY index="));
            Assert.That(journal, Does.Contain("BEGIN_SHADER"));
            Assert.That(journal, Does.Contain("RETURN_SHADER"));
            Assert.That(journal, Does.Contain("INDIVIDUAL_RETURNED count=1"));
        }

        /// <summary>Checks that a range beyond the inventory and a filter matching nothing are rejected.</summary>
        [Test]
        public void WarmIndividualShaders_InvalidSelection_Throws()
        {
            var beyond = CreateOptions(string.Empty, int.MaxValue, 0);
            var missing = CreateOptions("No/Such/Shader", 0, 1);

            var first = Assert.Throws<TargetInvocationException>(() => Journal(writer => Warm(writer, beyond)));
            var second = Assert.Throws<TargetInvocationException>(() => Journal(writer => Warm(writer, missing)));

            Assert.That(first.InnerException, Is.TypeOf<ArgumentException>());
            Assert.That(second.InnerException, Is.TypeOf<ArgumentException>());
        }

        /// <summary>Checks that the options default to individual warmup of the complete inventory.</summary>
        [Test]
        public void Options_HaveDocumentedDefaults()
        {
            var options = Activator.CreateInstance(s_optionsType, true)!;

            Assert.That(s_optionsType.GetField("Mode")!.GetValue(options), Is.EqualTo("individual"));
            Assert.That(s_optionsType.GetField("ShaderName")!.GetValue(options), Is.EqualTo(string.Empty));
            Assert.That(s_optionsType.GetField("StartIndex")!.GetValue(options), Is.EqualTo(0));
            Assert.That(s_optionsType.GetField("Count")!.GetValue(options), Is.EqualTo(0));
        }

        /// <summary>Creates probe options.</summary>
        /// <param name="shaderName">The exact shader name filter.</param>
        /// <param name="startIndex">The first inventory index.</param>
        /// <param name="count">The number of entries to visit, or zero for all.</param>
        /// <returns>The options instance.</returns>
        private static object CreateOptions(string shaderName, int startIndex, int count)
        {
            var options = Activator.CreateInstance(s_optionsType, true)!;
            s_optionsType.GetField("ShaderName")!.SetValue(options, shaderName);
            s_optionsType.GetField("StartIndex")!.SetValue(options, startIndex);
            s_optionsType.GetField("Count")!.SetValue(options, count);
            return options;
        }

        /// <summary>Invokes the individual warmup.</summary>
        /// <param name="writer">The journal writer.</param>
        /// <param name="options">The options.</param>
        private static void Warm(StreamWriter writer, object options)
        {
            PrivateMember.InvokeStatic(typeof(ShaderWarmupDiagnostics), "WarmIndividualShaders", writer, options);
        }

        /// <summary>Runs an action against a journal file and returns the journal text.</summary>
        /// <param name="action">The action writing the journal.</param>
        /// <returns>The journal contents.</returns>
        private string Journal(Action<StreamWriter> action)
        {
            var path = Path.Combine(_temporary.Location, Guid.NewGuid().ToString("N") + ".log");
            using (var writer = new StreamWriter(FileSystem.File.OpenWrite(path), new UTF8Encoding(false)))
            {
                action(writer);
            }
            return FileSystem.File.Open(path).ReadAllText();
        }
    }
}
