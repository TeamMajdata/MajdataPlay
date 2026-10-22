using System;
using System.IO;
using System.Linq;
using MajdataPlay.Editor.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.EditorTools
{
    /// <summary>Covers reading, validating and saving editable language resources.</summary>
    public class TranslationDocumentTests
    {
        /// <summary>The directory receiving the resources written by a test.</summary>
        private TemporaryDirectory _directory = null!;

        /// <summary>Creates the temporary directory.</summary>
        [SetUp]
        public void SetUp()
        {
            _directory = new TemporaryDirectory();
        }

        /// <summary>Deletes the temporary directory.</summary>
        [TearDown]
        public void TearDown()
        {
            _directory.Dispose();
        }

        /// <summary>Gets a path inside the temporary directory.</summary>
        /// <param name="name">The relative file name.</param>
        /// <returns>The full path.</returns>
        private string PathOf(string name)
        {
            return Path.Combine(_directory.Location, name);
        }

        /// <summary>Reads a file written by a test.</summary>
        /// <param name="name">The file name.</param>
        /// <returns>The file text.</returns>
        private string ReadBack(string name)
        {
            return _directory.Directory.FindFile(name)!.ReadAllText();
        }

        /// <summary>Checks that the dictionary schema is read with metadata, null texts and source snapshot.</summary>
        [Test]
        public void Read_DictionarySchema()
        {
            const string source = "{\"Code\":\"en-US\",\"Author\":\"Team\",\"Translations\":{\"B\":\"Bee\",\"A\":null}}";

            var document = TranslationDocument.Read("lang.json", source);

            Assert.That(document.Code, Is.EqualTo("en-US"));
            Assert.That(document.Author, Is.EqualTo("Team"));
            Assert.That(document.AssetPath, Is.EqualTo("lang.json"));
            Assert.That(document.SourceJson, Is.EqualTo(source));
            Assert.That(document.UsesMappingTable, Is.False);
            Assert.That(document.Entries.Count, Is.EqualTo(2));
            Assert.That(document.Entries.Find(entry => entry.Key == "B")!.Text, Is.EqualTo("Bee"));
            Assert.That(document.Entries.Find(entry => entry.Key == "A")!.Text, Is.Empty);
        }

        /// <summary>Checks that the legacy mapping schema keeps the last duplicate like the runtime does.</summary>
        [Test]
        public void Read_MappingSchema_LastDuplicateWins()
        {
            const string source = "{\"Translations\":{},\"MappingTable\":[{\"Origin\":\"K\",\"Content\":\"first\"},{\"Origin\":\"K\",\"Content\":\"last\"},{\"Origin\":\"N\"}]}";

            var document = TranslationDocument.Read("lang.json", source);

            Assert.That(document.UsesMappingTable, Is.True);
            Assert.That(document.Code, Is.Empty);
            Assert.That(document.Author, Is.Empty);
            Assert.That(document.Entries.Count, Is.EqualTo(2));
            Assert.That(document.Entries[0].Text, Is.EqualTo("last"));
            Assert.That(document.Entries[1].Text, Is.Empty);
        }

        /// <summary>Checks that a document without translations of either schema is empty.</summary>
        [Test]
        public void Read_NoTranslations_IsEmpty()
        {
            var document = TranslationDocument.Read("lang.json", "{\"Code\":\"x\"}");

            Assert.That(document.Entries, Is.Empty);
            Assert.That(document.UsesMappingTable, Is.False);
        }

        /// <summary>Checks that malformed entries and JSON are rejected.</summary>
        [Test]
        public void Read_RejectsMalformedContent()
        {
            Assert.That(() => TranslationDocument.Read("a", "{\"MappingTable\":[1]}"), Throws.TypeOf<FormatException>());
            Assert.That(() => TranslationDocument.Read("a", "{\"MappingTable\":[{\"Origin\":\" \",\"Content\":\"x\"}]}"), Throws.TypeOf<FormatException>());
            Assert.That(() => TranslationDocument.Read("a", "{\"Translations\":{\"K\":5}}"), Throws.TypeOf<FormatException>());
            Assert.That(() => TranslationDocument.Read("a", "{not json"), Throws.InstanceOf<JsonException>());
        }

        /// <summary>Checks that key validation rejects empty and duplicate keys only.</summary>
        [Test]
        public void ValidateKeys_RejectsEmptyAndDuplicateKeys()
        {
            Assert.DoesNotThrow(() => TranslationDocument.ValidateKeys(new[] { "A", "a", "B" }));
            Assert.That(() => TranslationDocument.ValidateKeys(new[] { "A", "A" }), Throws.InvalidOperationException);
            Assert.That(() => TranslationDocument.ValidateKeys(new[] { "A", " " }), Throws.InvalidOperationException);
        }

        /// <summary>Checks that saving the dictionary schema sorts keys, updates metadata and keeps unrelated fields.</summary>
        [Test]
        public void Save_DictionarySchema_SortsAndRetainsMetadata()
        {
            const string source = "{\"Code\":\"en\",\"Author\":\"A\",\"Extra\":42,\"Translations\":{\"B\":\"b\"}}";
            _directory.WriteText("lang.json", source);
            var document = TranslationDocument.Read(PathOf("lang.json"), source);
            document.Entries.Add(new TranslationEntry { Key = "A", Text = "a" });
            document.Code = "en-GB";
            document.IsDirty = true;

            document.Save();

            var saved = JObject.Parse(ReadBack("lang.json"));
            Assert.That(saved.Value<string>("Code"), Is.EqualTo("en-GB"));
            Assert.That(saved.Value<int>("Extra"), Is.EqualTo(42));
            Assert.That(((JObject)saved["Translations"]!).Properties().Count(), Is.EqualTo(2));
            Assert.That(((JObject)saved["Translations"]!).Properties().First().Name, Is.EqualTo("A"));
            Assert.That(document.IsDirty, Is.False);
            Assert.That(document.SourceJson, Is.EqualTo(ReadBack("lang.json")));
        }

        /// <summary>Checks that saving the mapping schema writes origin and content pairs.</summary>
        [Test]
        public void Save_MappingSchema_WritesPairs()
        {
            const string source = "{\"MappingTable\":[{\"Origin\":\"K\",\"Content\":\"v\"}]}";
            _directory.WriteText("map.json", source);
            var document = TranslationDocument.Read(PathOf("map.json"), source);
            document.Entries[0].Text = "changed";

            document.Save();

            var saved = JObject.Parse(ReadBack("map.json"));
            Assert.That(saved["Translations"], Is.Null);
            Assert.That(saved["MappingTable"]![0]!.Value<string>("Origin"), Is.EqualTo("K"));
            Assert.That(saved["MappingTable"]![0]!.Value<string>("Content"), Is.EqualTo("changed"));
            Assert.That(TranslationDocument.Read(PathOf("map.json"), ReadBack("map.json")).Entries[0].Text, Is.EqualTo("changed"));
        }

        /// <summary>Checks that saving refuses to overwrite a file changed or deleted after loading.</summary>
        [Test]
        public void Save_RefusesExternalChanges()
        {
            const string source = "{\"Translations\":{\"K\":\"v\"}}";
            var file = _directory.WriteText("lang.json", source);
            var document = TranslationDocument.Read(PathOf("lang.json"), source);

            file.WriteAllText("{\"Translations\":{}}");
            Assert.That(() => document.Save(), Throws.InstanceOf<IOException>());

            file.Delete();
            Assert.That(() => document.Save(), Throws.InstanceOf<IOException>());
        }

        /// <summary>Checks that duplicate keys are rejected before anything is written.</summary>
        [Test]
        public void Save_RejectsDuplicateKeysWithoutWriting()
        {
            const string source = "{\"Translations\":{\"K\":\"v\"}}";
            _directory.WriteText("lang.json", source);
            var document = TranslationDocument.Read(PathOf("lang.json"), source);
            document.Entries.Add(new TranslationEntry { Key = "K", Text = "again" });

            Assert.That(() => document.Save(), Throws.InvalidOperationException);
            Assert.That(ReadBack("lang.json"), Is.EqualTo(source));
        }

        /// <summary>Checks that a new resource is written into a directory that is created on demand.</summary>
        [Test]
        public void WriteIfUnchanged_CreatesNewResourceAndDirectory()
        {
            var path = Path.Combine(_directory.Location, "nested", "new.json");

            TranslationDocument.WriteIfUnchanged(path, string.Empty, "[]");

            Assert.That(_directory.Directory.FindDirectory("nested")!.FindFile("new.json")!.ReadAllText(), Is.EqualTo("[]"));
            Assert.That(() => TranslationDocument.WriteIfUnchanged(Path.Combine(_directory.Location, "missing.json"), "old", "[]"), Throws.InstanceOf<IOException>());
        }
    }
}
