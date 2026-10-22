#nullable enable
using MajdataPlay.IO.Storage;
using MajdataPlay.Rendering;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace MajdataPlay.Tests.ShaderWarmupValidation
{
    /// <summary>Runs package-free regressions against the linked production warmup diagnostic.</summary>
    internal static class Program
    {
        /// <summary>Counts assertions executed across the selected preprocessor branch.</summary>
        private static int s_assertions;

        /// <summary>Counts independently reported failing cases.</summary>
        private static int s_failed;

        /// <summary>Owns this run's ignored fixture directory.</summary>
        private static StorageDirectory s_fixtures = null!;

        /// <summary>Runs the branch-appropriate cases and reports an explicit completion marker.</summary>
        /// <returns>Zero on success, or one if any regression fails.</returns>
        private static int Main()
        {
            s_fixtures = FileSystem.Directory.Create(AppContext.BaseDirectory)
                .CreateDirectory("fixtures")
                .CreateDirectory(Guid.NewGuid().ToString("N"));
#if UNITY_ANDROID && !UNITY_EDITOR
            var cases = new (string Name, Action Run)[]
            {
                ("default Release Android probe: stable order and durable native boundaries", TestDefaultDiagnostic),
                ("selection: ranges refer to the complete stable inventory", TestRanges),
                ("selection: exact names include all duplicate-name identities", TestExactName),
                ("selection: name and range intersect without renumbering", TestFilterRangeIntersection),
                ("selection: missing, case-mismatched, and partial names are rejected", TestUnmatchedNames),
                ("configuration: invalid options never invoke warmup", TestInvalidOptions),
                ("configuration: malformed JSON is rejected", TestMalformedJson),
                ("configuration: omitted fields retain safe defaults", TestPartialConfiguration),
                ("skip mode: neither enumeration nor native warmup", TestSkip),
                ("all mode: original bulk API without prior shader enumeration", TestAll),
                ("failure: individual warmup error retains unfinished BEGIN and propagates", TestIndividualFailure),
                ("failure: global warmup error retains unfinished BEGIN and propagates", TestGlobalFailure),
                ("journals: repeated game entries preserve every previous file", TestJournalRetention),
                ("I/O: journal directory failure prevents native warmup", TestJournalDirectoryFailure),
                ("I/O: unreadable configuration prevents native warmup", TestConfigurationReadFailure),
                ("I/O: configuration creation failure prevents native warmup", TestConfigurationCreationFailure),
                ("Unity null: already destroyed shaders are excluded", TestDestroyedBeforeEnumeration),
                ("Unity null: shaders destroyed after snapshot are skipped", TestDestroyedAfterSnapshot),
                ("empty inventory: diagnostic completes without native calls", TestEmptyInventory),
                ("journal fields: shader names cannot forge extra markers", TestEscapedNames)
            };
#else
            var cases = new (string Name, Action Run)[]
            {
                ("non-Android-Player branch preserves original bulk warmup", TestPlatformBypass),
                ("non-Android-Player branch preserves original exception", TestPlatformBypassFailure)
            };
#endif
            foreach (var test in cases)
            {
                try
                {
                    test.Run();
                    Console.WriteLine($"PASS {test.Name}");
                }
                catch (Exception exception)
                {
                    s_failed++;
                    Console.Error.WriteLine($"FAIL {test.Name}: {exception}");
                }
            }
            var marker = s_failed == 0 ? "SHADER_WARMUP_VALIDATION_PASSED" : "SHADER_WARMUP_VALIDATION_FAILED";
            Console.WriteLine($"{marker} ({cases.Length - s_failed} passed, {s_failed} failed, {s_assertions} assertions)");
            Console.WriteLine($"Ignored fixture directory: {s_fixtures.Location}");
            return s_failed == 0 ? 0 : 1;
        }

        /// <summary>Creates isolated storage and resets every observable Unity boundary.</summary>
        /// <param name="shaders">The intentionally unordered loaded shader snapshot.</param>
        /// <returns>The new persistent-data fixture directory.</returns>
        private static StorageDirectory Reset(params Shader[] shaders)
        {
            var directory = s_fixtures.CreateDirectory(Guid.NewGuid().ToString("N"));
            Application.persistentDataPath = directory.Location;
            Resources.LoadedObjects = shaders;
            Resources.EnumerationCalls = 0;
            Shader.GlobalWarmupCalls = 0;
            Shader.BeforeGlobalWarmup = null;
            ShaderWarmup.Calls.Clear();
            ShaderWarmup.BeforeWarmup = null;
            Debug.Messages.Clear();
            return directory;
        }

        /// <summary>Writes an editable configuration through the real project storage facade.</summary>
        /// <param name="directory">The current persistent-data fixture.</param>
        /// <param name="json">The complete configuration text.</param>
        private static void WriteConfiguration(StorageDirectory directory, string json)
        {
            using var stream = FileSystem.File.OpenWrite(directory.Location + "/ShaderWarmupDiagnostics.json", overwrite: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(json);
        }

        /// <summary>Finds every session journal without assuming a timestamp order.</summary>
        /// <param name="directory">The current persistent-data fixture.</param>
        /// <returns>The journal handles sorted by their unique filenames.</returns>
        private static StorageFile[] GetJournals(StorageDirectory directory)
        {
            var journalDirectory = directory.FindDirectoryAt("Logs/ShaderWarmup");
            if (journalDirectory is null)
            {
                return Array.Empty<StorageFile>();
            }
            return journalDirectory.EnumerateFiles()
                .OrderBy(file => file.Entry!.Name, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>Reads the only expected journal after its owner has closed it.</summary>
        /// <param name="directory">The current persistent-data fixture.</param>
        /// <returns>The complete session text.</returns>
        private static string ReadJournal(StorageDirectory directory)
        {
            var files = GetJournals(directory);
            Equal(1, files.Length, "Exactly one journal should exist for this fixture.");
            return files[0].ReadAllText();
        }

        /// <summary>Simulates an external reader inspecting a flushed journal during a native call.</summary>
        /// <param name="directory">The current persistent-data fixture.</param>
        /// <returns>The real on-disk journal content visible before warmup returns.</returns>
        private static string ReadOpenJournal(StorageDirectory directory)
        {
            var files = GetJournals(directory);
            Equal(1, files.Length, "The session journal must exist before native warmup starts.");
            // The production facade opens its writer with FileShare.Read. Its ordinary reader uses FileShare.Read
            // as well, which cannot share that existing writer on Windows. This read-only test probe uses
            // FileShare.ReadWrite to model an external Android reader without changing the production storage layer.
            using var stream = new FileStream(files[0].Location, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        /// <summary>Checks the selected shader identities in attempted-call order.</summary>
        /// <param name="expected">The expected instance identities.</param>
        private static void EqualShaderIds(params int[] expected)
        {
            var actual = ShaderWarmup.Calls.Select(shader => shader.GetInstanceID()).ToArray();
            Check(expected.SequenceEqual(actual), $"Expected shader IDs [{string.Join(",", expected)}], got [{string.Join(",", actual)}].");
        }

        /// <summary>Verifies the vertex declaration without claiming actual pipeline-state compatibility.</summary>
        /// <param name="setup">The setup passed to the native-boundary stub.</param>
        private static void AssertLayout(ShaderWarmupSetup setup)
        {
            var attributes = new[]
            {
                VertexAttribute.Position,
                VertexAttribute.Normal,
                VertexAttribute.Tangent,
                VertexAttribute.Color,
                VertexAttribute.TexCoord0,
                VertexAttribute.TexCoord1
            };
            var dimensions = new[] { 3, 3, 4, 4, 4, 4 };
            Equal(attributes.Length, setup.vdecl.Length, "The expected diagnostic channels must be supplied.");
            for (var index = 0; index < attributes.Length; index++)
            {
                Equal(attributes[index], setup.vdecl[index].attribute, "Vertex channels must preserve their semantic order.");
                Equal(VertexAttributeFormat.Float32, setup.vdecl[index].format, "Diagnostic channels retain Float32 precision.");
                Equal(dimensions[index], setup.vdecl[index].dimension, "Each diagnostic channel keeps its component count.");
                Equal(0, setup.vdecl[index].stream, "The diagnostic declaration uses one vertex stream.");
            }
        }

        /// <summary>Verifies default generation, stable identities, and durable markers before each native boundary.</summary>
        private static void TestDefaultDiagnostic()
        {
            var directory = Reset(new Shader("Zulu", 9), new Shader("Alpha", 30), new Shader("Alpha", 10));
            ShaderWarmup.BeforeWarmup = (shader, setup) =>
            {
                var journal = ReadOpenJournal(directory);
                var identity = $"id={shader.GetInstanceID()} shader=\"{shader.name}\"";
                Contains(journal, "SESSION", "The session header must be durable before the native call.");
                Contains(journal, "development=False", "The default Android probe must work in a Release build.");
                Contains(journal, "build=\"validation-build-guid\"", "The journal identifies the exact build.");
                Contains(journal, "INVENTORY index=2 id=9 shader=\"Zulu\"", "The full inventory is durable before any native call.");
                Check(journal.Split('\n').Any(line => line.Contains("BEGIN_SHADER", StringComparison.Ordinal) && line.Contains(identity, StringComparison.Ordinal)), "The current BEGIN must be visible before native execution.");
                Check(!journal.Split('\n').Any(line => line.Contains("RETURN_SHADER", StringComparison.Ordinal) && line.Contains(identity, StringComparison.Ordinal)), "The current RETURN cannot precede the native call.");
                AssertLayout(setup);
            };
            ShaderWarmupDiagnostics.Run();
            EqualShaderIds(10, 30, 9);
            Equal(0, Shader.GlobalWarmupCalls, "Android defaults must avoid the original bulk call.");
            Equal(1, Resources.EnumerationCalls, "Loaded shaders should be snapshotted once.");
            var journal = ReadJournal(directory);
            Equal(3, CountMarker(journal, "BEGIN_SHADER"), "Every selected shader has one BEGIN.");
            Equal(3, CountMarker(journal, "RETURN_SHADER"), "Every returned shader has one RETURN.");
            Equal(1, CountMarker(journal, "COMPLETE"), "A fully returned diagnostic records COMPLETE once.");
            Contains(journal, "Pass/variant unknown", "Shader-level diagnostics must not claim a specific pass or variant.");
            Check(Debug.Messages.Any(message => message.Contains("BEGIN_SHADER", StringComparison.Ordinal)), "The same marker reaches the synchronous Unity logger.");
            using var configuration = JsonDocument.Parse(directory.FindFile("ShaderWarmupDiagnostics.json")!.ReadAllText());
            Equal("individual", configuration.RootElement.GetProperty("Mode").GetString(), "Generated defaults enable individual diagnostics.");
            Equal(string.Empty, configuration.RootElement.GetProperty("ShaderName").GetString(), "Generated defaults include every loaded shader name.");
            Equal(0, configuration.RootElement.GetProperty("StartIndex").GetInt32(), "The first complete inventory is selected by default.");
            Equal(0, configuration.RootElement.GetProperty("Count").GetInt32(), "Zero count means all remaining inventory entries.");
        }

        /// <summary>Verifies finite, remaining, and overflow-safe ranges against complete inventory indices.</summary>
        private static void TestRanges()
        {
            var selections = new (int Start, int Count, int[] Ids)[]
            {
                (1, 2, new[] { 20, 30 }),
                (1, 0, new[] { 20, 30, 40 }),
                (2, int.MaxValue, new[] { 30, 40 })
            };
            foreach (var selection in selections)
            {
                var directory = Reset(new Shader("D", 40), new Shader("B", 20), new Shader("A", 10), new Shader("C", 30));
                WriteConfiguration(directory, $"{{\"Mode\":\"individual\",\"StartIndex\":{selection.Start},\"Count\":{selection.Count}}}");
                ShaderWarmupDiagnostics.Run();
                EqualShaderIds(selection.Ids);
                Equal(4, CountMarker(ReadJournal(directory), "INVENTORY"), "Selection does not truncate or renumber the inventory.");
            }
        }

        /// <summary>Verifies exact duplicate-name matching and stable instance-ID ordering.</summary>
        private static void TestExactName()
        {
            var directory = Reset(new Shader("Maj/Repeat", 20), new Shader("Other", 1), new Shader("Maj/Repeat", 10));
            WriteConfiguration(directory, "{\"ShaderName\":\"Maj/Repeat\"}");
            ShaderWarmupDiagnostics.Run();
            EqualShaderIds(10, 20);
            Equal(3, CountMarker(ReadJournal(directory), "INVENTORY"), "The filter keeps a full diagnostic inventory.");
        }

        /// <summary>Checks that range selection precedes exact-name filtering without index remapping.</summary>
        private static void TestFilterRangeIntersection()
        {
            var directory = Reset(new Shader("Zulu", 40), new Shader("Repeat", 20), new Shader("Alpha", 1), new Shader("Repeat", 10));
            WriteConfiguration(directory, "{\"ShaderName\":\"Repeat\",\"StartIndex\":2,\"Count\":1}");
            ShaderWarmupDiagnostics.Run();
            EqualShaderIds(20);
            Contains(ReadJournal(directory), "BEGIN_SHADER index=2 id=20", "Indices refer to the complete sorted snapshot.");
        }

        /// <summary>Checks that missing or inexact names fail instead of silently declaring success.</summary>
        private static void TestUnmatchedNames()
        {
            foreach (var name in new[] { "Missing", "maj/repeat", "Maj/Rep" })
            {
                var directory = Reset(new Shader("Maj/Repeat", 1));
                WriteConfiguration(directory, JsonSerializer.Serialize(new { ShaderName = name }));
                Throws<ArgumentException>(ShaderWarmupDiagnostics.Run);
                AssertNoWarmup();
                var journal = ReadJournal(directory);
                Contains(journal, "MANAGED_EXCEPTION", "Rejected names must leave a useful journal.");
                Equal(0, CountMarker(journal, "COMPLETE"), "Rejected selections are not complete.");
            }
        }

        /// <summary>Checks unsafe modes, negative ranges, mode conflicts, and empty selected ranges.</summary>
        private static void TestInvalidOptions()
        {
            var configurations = new[]
            {
                "{\"Mode\":\"unknown\"}",
                "{\"Mode\":null}",
                "{\"StartIndex\":-1}",
                "{\"Count\":-1}",
                "{\"Mode\":\"all\",\"ShaderName\":\"Maj/A\"}",
                "{\"Mode\":\"all\",\"StartIndex\":1}",
                "{\"Mode\":\"skip\",\"Count\":1}",
                "{\"StartIndex\":2}",
                "{\"StartIndex\":1}"
            };
            foreach (var configuration in configurations)
            {
                var directory = Reset(new Shader("Maj/A", 1));
                WriteConfiguration(directory, configuration);
                Throws<ArgumentException>(ShaderWarmupDiagnostics.Run);
                AssertNoWarmup();
                var journal = ReadJournal(directory);
                Contains(journal, "MANAGED_EXCEPTION", "Invalid options must be recorded.");
                Equal(0, CountMarker(journal, "COMPLETE"), "Invalid options cannot report successful completion.");
            }
        }

        /// <summary>Checks that malformed JSON never reaches either warmup API.</summary>
        private static void TestMalformedJson()
        {
            var directory = Reset(new Shader("Maj/A", 1));
            WriteConfiguration(directory, "{\"Mode\":");
            Throws<ArgumentException>(ShaderWarmupDiagnostics.Run);
            AssertNoWarmup();
            Contains(ReadJournal(directory), "MANAGED_EXCEPTION", "JSON errors must preserve their diagnostic context.");
        }

        /// <summary>Checks omitted fields and documented whitespace normalization.</summary>
        private static void TestPartialConfiguration()
        {
            var directory = Reset(new Shader("Maj/A", 1));
            WriteConfiguration(directory, "{\"Mode\":\" INDIVIDUAL \"}");
            ShaderWarmupDiagnostics.Run();
            EqualShaderIds(1);
            Contains(ReadJournal(directory), "CONFIG mode=individual shader=\"\" start=0 count=0", "Missing fields retain the production initializers.");
        }

        /// <summary>Checks the no-warmup A/B mode without enumerating shader resources.</summary>
        private static void TestSkip()
        {
            var directory = Reset(new Shader("Maj/A", 1));
            WriteConfiguration(directory, "{\"Mode\":\"skip\"}");
            ShaderWarmupDiagnostics.Run();
            AssertNoWarmup();
            Equal(0, Resources.EnumerationCalls, "Skip must not perturb the loaded shader snapshot.");
            var journal = ReadJournal(directory);
            Equal(1, CountMarker(journal, "SKIP_WARMUP"), "The journal explicitly identifies skipped warmup.");
            Equal(1, CountMarker(journal, "COMPLETE"), "A successful skip returns normally.");
        }

        /// <summary>Checks the original global API A/B mode and its durable boundary markers.</summary>
        private static void TestAll()
        {
            var directory = Reset(new Shader("Maj/A", 1));
            WriteConfiguration(directory, "{\"Mode\":\"all\"}");
            Shader.BeforeGlobalWarmup = () =>
            {
                Equal(0, Resources.EnumerationCalls, "Original bulk mode must not enumerate resources first.");
                var journal = ReadOpenJournal(directory);
                Equal(1, CountMarker(journal, "BEGIN_ALL"), "BEGIN_ALL must be durable before the original native API.");
                Equal(0, CountMarker(journal, "RETURN_ALL"), "RETURN_ALL cannot be written before the native call returns.");
            };
            ShaderWarmupDiagnostics.Run();
            Equal(1, Shader.GlobalWarmupCalls, "Bulk mode invokes the original entry point once.");
            EqualShaderIds();
            var result = ReadJournal(directory);
            Equal(1, CountMarker(result, "RETURN_ALL"), "Successful bulk mode reports one CPU return.");
            Equal(1, CountMarker(result, "COMPLETE"), "Successful bulk mode completes its managed probe.");
        }

        /// <summary>Checks that an individual managed error preserves the unfinished shader identity.</summary>
        private static void TestIndividualFailure()
        {
            var directory = Reset(new Shader("A", 1), new Shader("B", 2));
            var expected = new InvalidOperationException("simulated native-boundary managed failure");
            ShaderWarmup.BeforeWarmup = (_, _) =>
            {
                Contains(ReadOpenJournal(directory), "BEGIN_SHADER index=0 id=1", "The failed call has a durable identity before it throws.");
                throw expected;
            };
            var actual = Throws<InvalidOperationException>(ShaderWarmupDiagnostics.Run);
            Check(ReferenceEquals(expected, actual), "The original managed error must propagate unchanged.");
            EqualShaderIds(1);
            var journal = ReadJournal(directory);
            Equal(1, CountMarker(journal, "BEGIN_SHADER"), "Later shaders must not execute after an error.");
            Equal(0, CountMarker(journal, "RETURN_SHADER"), "A throwing call must not have a RETURN marker.");
            Equal(0, CountMarker(journal, "COMPLETE"), "A throwing probe must not declare completion.");
            Contains(journal, "MANAGED_EXCEPTION", "Managed failures must be identifiable in the journal.");
        }

        /// <summary>Checks that an original bulk call failure propagates without forged return markers.</summary>
        private static void TestGlobalFailure()
        {
            var directory = Reset(new Shader("A", 1));
            WriteConfiguration(directory, "{\"Mode\":\"all\"}");
            var expected = new InvalidOperationException("simulated original bulk failure");
            Shader.BeforeGlobalWarmup = () => throw expected;
            Check(ReferenceEquals(expected, Throws<InvalidOperationException>(ShaderWarmupDiagnostics.Run)), "Bulk failures must preserve their exception identity.");
            var journal = ReadJournal(directory);
            Equal(1, CountMarker(journal, "BEGIN_ALL"), "The attempted bulk call is journaled.");
            Equal(0, CountMarker(journal, "RETURN_ALL"), "A failed bulk call has no RETURN marker.");
            Equal(0, CountMarker(journal, "COMPLETE"), "A failed bulk probe is not complete.");
        }

        /// <summary>Checks that a subsequent game entry cannot overwrite the preceding crash evidence.</summary>
        private static void TestJournalRetention()
        {
            var directory = Reset(new Shader("A", 1));
            ShaderWarmupDiagnostics.Run();
            var first = GetJournals(directory)[0];
            var firstText = first.ReadAllText();
            ShaderWarmupDiagnostics.Run();
            var files = GetJournals(directory);
            Equal(2, files.Length, "Every session creates a unique file.");
            Equal(firstText, first.ReadAllText(), "The previous journal remains byte-for-byte unchanged.");
            Check(files.Select(file => file.Location).Distinct(StringComparer.Ordinal).Count() == 2, "Journal paths cannot collide across immediate consecutive calls.");
        }

        /// <summary>Checks that a colliding journal directory fails before any native work.</summary>
        private static void TestJournalDirectoryFailure()
        {
            var directory = Reset(new Shader("A", 1));
            directory.CreateFile("Logs");
            Throws<IOException>(ShaderWarmupDiagnostics.Run);
            AssertNoWarmup();
            Equal(0, Resources.EnumerationCalls, "Journal initialization failure occurs before resource enumeration.");
        }

        /// <summary>Checks that an unreadable configuration never falls back to unlogged native work.</summary>
        private static void TestConfigurationReadFailure()
        {
            var directory = Reset(new Shader("A", 1));
            WriteConfiguration(directory, "{\"Mode\":\"individual\"}");
            using var locked = directory.FindFile("ShaderWarmupDiagnostics.json")!.OpenWrite(append: true);
            Throws<IOException>(ShaderWarmupDiagnostics.Run);
            AssertNoWarmup();
            var journal = ReadJournal(directory);
            Contains(journal, "MANAGED_EXCEPTION", "The configuration read failure is recorded without being hidden.");
            Equal(0, CountMarker(journal, "BEGIN_SHADER"), "No shader may start after failed configuration I/O.");
        }

        /// <summary>Checks configuration creation failure using a real directory/file collision.</summary>
        private static void TestConfigurationCreationFailure()
        {
            var directory = Reset(new Shader("A", 1));
            directory.CreateDirectory("ShaderWarmupDiagnostics.json");
            ThrowsAny(ShaderWarmupDiagnostics.Run, typeof(IOException), typeof(UnauthorizedAccessException));
            AssertNoWarmup();
            Contains(ReadJournal(directory), "MANAGED_EXCEPTION", "Failed default creation must not silently continue.");
        }

        /// <summary>Checks Unity destroyed-object semantics before snapshot metadata access.</summary>
        private static void TestDestroyedBeforeEnumeration()
        {
            var destroyed = new Shader("Destroyed", 99) { Destroyed = true };
            var directory = Reset(destroyed, new Shader("A", 1));
            ShaderWarmupDiagnostics.Run();
            EqualShaderIds(1);
            var journal = ReadJournal(directory);
            Equal(1, CountMarker(journal, "INVENTORY"), "Destroyed native objects must not enter the live snapshot.");
            Check(!journal.Contains("Destroyed", StringComparison.Ordinal), "Metadata is never accessed through a destroyed Unity object.");
        }

        /// <summary>Checks destruction after snapshot but before the next per-shader call.</summary>
        private static void TestDestroyedAfterSnapshot()
        {
            var later = new Shader("B", 2);
            var directory = Reset(new Shader("A", 1), later);
            ShaderWarmup.BeforeWarmup = (_, _) =>
            {
                later.Destroyed = true;
            };
            ShaderWarmupDiagnostics.Run();
            EqualShaderIds(1);
            var journal = ReadJournal(directory);
            Contains(journal, "SKIP_DESTROYED index=1 id=2", "A stale snapshot identity is retained without touching its dead native shader.");
            Equal(1, CountMarker(journal, "COMPLETE"), "Remaining live work completes normally.");
        }

        /// <summary>Checks the valid empty loaded-resource snapshot.</summary>
        private static void TestEmptyInventory()
        {
            var directory = Reset();
            ShaderWarmupDiagnostics.Run();
            AssertNoWarmup();
            var journal = ReadJournal(directory);
            Contains(journal, "RETURN_ENUMERATE loaded=0", "The empty inventory is explicit.");
            Equal(1, CountMarker(journal, "COMPLETE"), "An empty unfiltered inventory is a valid completed probe.");
        }

        /// <summary>Checks that special asset names remain one unambiguous journal line.</summary>
        private static void TestEscapedNames()
        {
            var directory = Reset(new Shader("Maj/Quote\"\\\r\nCOMPLETE forged", 1));
            ShaderWarmupDiagnostics.Run();
            var journal = ReadJournal(directory);
            Contains(journal, "shader=\"Maj/Quote\\\"\\\\\\r\\nCOMPLETE forged\"", "Shader names are escaped before logging.");
            Equal(1, CountMarker(journal, "COMPLETE"), "Embedded newlines cannot create a forged completion marker.");
        }

        /// <summary>Checks that Editor and other platforms keep the existing warmup behavior.</summary>
        private static void TestPlatformBypass()
        {
            var directory = Reset(new Shader("A", 1));
            ShaderWarmupDiagnostics.Run();
            Equal(1, Shader.GlobalWarmupCalls, "The non-Android-Player branch retains the original API.");
            EqualShaderIds();
            Equal(0, Resources.EnumerationCalls, "The bypass does not snapshot shaders.");
            Equal(0, GetJournals(directory).Length, "The bypass creates no diagnostic journals.");
            Check(directory.FindFile("ShaderWarmupDiagnostics.json") is null, "The bypass creates no diagnostic configuration.");
            Equal(0, Debug.Messages.Count, "The bypass adds no diagnostic logger traffic.");
        }

        /// <summary>Checks that bypassed platforms retain the original native entry point's exception behavior.</summary>
        private static void TestPlatformBypassFailure()
        {
            var directory = Reset(new Shader("A", 1));
            var expected = new InvalidOperationException("original warmup failure");
            Shader.BeforeGlobalWarmup = () => throw expected;
            Check(ReferenceEquals(expected, Throws<InvalidOperationException>(ShaderWarmupDiagnostics.Run)), "The bypass must not wrap or swallow the original failure.");
            Equal(0, GetJournals(directory).Length, "The bypass does not introduce crash-journal behavior.");
        }

        /// <summary>Checks that neither native warmup entry point has been called.</summary>
        private static void AssertNoWarmup()
        {
            Equal(0, Shader.GlobalWarmupCalls, "The original bulk warmup must not run.");
            Equal(0, ShaderWarmup.Calls.Count, "Individual warmup must not run.");
        }

        /// <summary>Counts structured operation markers, excluding arbitrary occurrences in quoted metadata.</summary>
        /// <param name="journal">The complete session journal.</param>
        /// <param name="marker">The exact operation marker to count.</param>
        /// <returns>The number of lines whose first operation token matches the marker.</returns>
        private static int CountMarker(string journal, string marker)
        {
            var prefix = "[ShaderWarmupDiagnostics] " + marker;
            return journal.Split('\n').Count(line =>
            {
                var index = line.IndexOf(prefix, StringComparison.Ordinal);
                if (index < 0)
                {
                    return false;
                }
                var next = index + prefix.Length;
                return next == line.Length || char.IsWhiteSpace(line[next]);
            });
        }

        /// <summary>Asserts that an action throws the expected managed exception family.</summary>
        /// <typeparam name="T">The required exception type.</typeparam>
        /// <param name="action">The synchronous production action.</param>
        /// <returns>The actual exception for identity checks.</returns>
        /// <exception cref="InvalidOperationException">The action did not throw the expected exception.</exception>
        private static T Throws<T>(Action action) where T : Exception
        {
            try
            {
                action();
            }
            catch (T exception)
            {
                s_assertions++;
                return exception;
            }
            throw new InvalidOperationException($"Expected {typeof(T).Name}.");
        }

        /// <summary>Asserts a portable I/O failure whose exact exception family differs between host platforms.</summary>
        /// <param name="action">The operation which must fail.</param>
        /// <param name="types">The supported host-specific exception families.</param>
        /// <exception cref="InvalidOperationException">The operation returned or threw an unexpected exception.</exception>
        private static void ThrowsAny(Action action, params Type[] types)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Check(types.Any(type => type.IsInstanceOfType(exception)), $"Unexpected exception type {exception.GetType().Name}.");
                return;
            }
            throw new InvalidOperationException("Expected an I/O exception.");
        }

        /// <summary>Asserts a diagnostic substring with context on failure.</summary>
        /// <param name="text">The observed text.</param>
        /// <param name="expected">The required substring.</param>
        /// <param name="message">The assertion context.</param>
        private static void Contains(string text, string expected, string message)
        {
            Check(text.Contains(expected, StringComparison.Ordinal), message + " Missing: " + expected);
        }

        /// <summary>Asserts equality through the value type's standard comparer.</summary>
        /// <typeparam name="T">The compared value type.</typeparam>
        /// <param name="expected">The expected value.</param>
        /// <param name="actual">The observed value.</param>
        /// <param name="message">The assertion context.</param>
        private static void Equal<T>(T expected, T actual, string message)
        {
            Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{message} Expected={expected}, actual={actual}.");
        }

        /// <summary>Counts one assertion and throws on failure.</summary>
        /// <param name="condition">Whether the required invariant holds.</param>
        /// <param name="message">The actionable assertion context.</param>
        /// <exception cref="InvalidOperationException">The required invariant did not hold.</exception>
        private static void Check(bool condition, string message)
        {
            s_assertions++;
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
