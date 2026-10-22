using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using MajdataPlay.Utils;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Utils
{
    /// <summary>Covers the synchronous and asynchronous JSON helpers in <see cref="Serializer.Json"/>.</summary>
    public class SerializerJsonTests
    {
        /// <summary>The compact JSON produced for <see cref="CreateSample"/>.</summary>
        private const string SampleJson = "{\"Name\":\"song\",\"Count\":3,\"Items\":[1]}";

        /// <summary>JSON that no reader accepts.</summary>
        private const string InvalidJson = "{ invalid";

        /// <summary>A serializable sample type whose collection has a default element.</summary>
        public sealed class Sample
        {
            /// <summary>Gets or sets the name.</summary>
            public string? Name { get; set; }

            /// <summary>Gets or sets the count.</summary>
            public int Count { get; set; }

            /// <summary>Gets or sets the items, which start with one default element.</summary>
            public List<int> Items { get; set; } = new() { 1 };
        }

        /// <summary>Creates the sample that serializes to <see cref="SampleJson"/>.</summary>
        /// <returns>The sample.</returns>
        private static Sample CreateSample()
        {
            return new Sample { Name = "song", Count = 3 };
        }

        /// <summary>Creates a readable stream over UTF-8 text.</summary>
        /// <param name="text">The stream content.</param>
        /// <returns>The stream, positioned at the start.</returns>
        private static MemoryStream CreateStream(string text)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(text));
        }

        /// <summary>Decodes a UTF-8 stream's content, skipping a leading byte order mark.</summary>
        /// <param name="stream">The written stream.</param>
        /// <returns>The text without the byte order mark.</returns>
        private static string ReadWithoutPreamble(MemoryStream stream)
        {
            return Encoding.UTF8.GetString(stream.ToArray()).TrimStart('﻿');
        }

        /// <summary>Checks that Serialize produces compact JSON by default and honours explicit settings.</summary>
        [Test]
        public void Serialize_UsesDefaultOrGivenSettings()
        {
            Assert.That(Serializer.Json.Serialize(CreateSample()), Is.EqualTo(SampleJson));

            var indented = Serializer.Json.Serialize(CreateSample(), new JsonSerializerSettings { Formatting = Formatting.Indented });

            Assert.That(indented, Does.Contain("\n"));
            Assert.That(indented, Does.Contain("\"Name\": \"song\""));
        }

        /// <summary>Checks that Deserialize reads values and replaces, rather than appends to, initialized collections.</summary>
        [Test]
        public void Deserialize_ReplacesInitializedCollections()
        {
            var sample = Serializer.Json.Deserialize<Sample>("{\"Name\":\"x\",\"Items\":[5,6]}");

            Assert.That(sample, Is.Not.Null);
            Assert.That(sample!.Name, Is.EqualTo("x"));
            Assert.That(sample.Items, Is.EqualTo(new[] { 5, 6 }));
        }

        /// <summary>Checks that Deserialize throws for invalid JSON.</summary>
        [Test]
        public void Deserialize_Invalid_Throws()
        {
            Assert.Throws<JsonReaderException>(() => Serializer.Json.Deserialize<Sample>(InvalidJson));
        }

        /// <summary>Checks that TryDeserialize reports success with the result.</summary>
        [Test]
        public void TryDeserialize_Valid_ReturnsResult()
        {
            var ok = Serializer.Json.TryDeserialize<Sample>(SampleJson, out var result, out var exception);

            Assert.That(ok, Is.True);
            Assert.That(exception, Is.Null);
            Assert.That(result!.Count, Is.EqualTo(3));
        }

        /// <summary>Checks that TryDeserialize reports failure with the exception instead of throwing.</summary>
        [Test]
        public void TryDeserialize_Invalid_ReturnsException()
        {
            var ok = Serializer.Json.TryDeserialize<Sample>(InvalidJson, out var result, out var exception);

            Assert.That(ok, Is.False);
            Assert.That(result, Is.Null);
            Assert.That(exception, Is.InstanceOf<JsonException>());
        }

        /// <summary>Checks the string-returning SerializeAsync overloads with default and explicit serializers and settings.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SerializeAsync_ToString_UsesSerializerOrSettings() => UniTask.ToCoroutine(async () =>
        {
            var byDefault = await Serializer.Json.SerializeAsync(CreateSample());
            var byNullSettings = await Serializer.Json.SerializeAsync(CreateSample(), (JsonSerializerSettings?)null);
            var bySettings = await Serializer.Json.SerializeAsync(CreateSample(), new JsonSerializerSettings { Formatting = Formatting.Indented });
            var bySerializer = await Serializer.Json.SerializeAsync(CreateSample(), JsonSerializer.Create(new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));

            Assert.That(byDefault, Is.EqualTo(SampleJson));
            Assert.That(byNullSettings, Is.EqualTo(SampleJson));
            Assert.That(bySettings, Does.Contain("\n"));
            Assert.That(bySerializer, Is.EqualTo(SampleJson));
        });

        /// <summary>Checks the stream-writing SerializeAsync overloads and that they leave the stream open.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SerializeAsync_ToStream_WritesJsonAndLeavesStreamOpen() => UniTask.ToCoroutine(async () =>
        {
            using var byDefault = new MemoryStream();
            using var byNullSettings = new MemoryStream();
            using var bySettings = new MemoryStream();

            await Serializer.Json.SerializeAsync(byDefault, CreateSample());
            await Serializer.Json.SerializeAsync(byNullSettings, CreateSample(), (JsonSerializerSettings?)null);
            await Serializer.Json.SerializeAsync(bySettings, CreateSample(), new JsonSerializerSettings { Formatting = Formatting.Indented });

            Assert.That(byDefault.CanWrite, Is.True);
            Assert.That(byDefault.ToArray()[..3], Is.EqualTo(Encoding.UTF8.GetPreamble()), "The stream overload writes a UTF-8 byte order mark.");
            Assert.That(ReadWithoutPreamble(byDefault), Is.EqualTo(SampleJson));
            Assert.That(ReadWithoutPreamble(byNullSettings), Is.EqualTo(SampleJson));
            Assert.That(ReadWithoutPreamble(bySettings), Does.Contain("\n"));
        });

        /// <summary>Checks the stream-reading DeserializeAsync overloads with default and explicit serializers and settings.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DeserializeAsync_FromStream_ReadsJson() => UniTask.ToCoroutine(async () =>
        {
            var byDefault = await Serializer.Json.DeserializeAsync<Sample>(CreateStream(SampleJson));
            var byNullSettings = await Serializer.Json.DeserializeAsync<Sample>(CreateStream(SampleJson), (JsonSerializerSettings?)null);
            var bySettings = await Serializer.Json.DeserializeAsync<Sample>(CreateStream(SampleJson), new JsonSerializerSettings());

            Assert.That(byDefault!.Name, Is.EqualTo("song"));
            Assert.That(byDefault.Items, Is.EqualTo(new[] { 1 }));
            Assert.That(byNullSettings!.Count, Is.EqualTo(3));
            Assert.That(bySettings!.Name, Is.EqualTo("song"));
        });

        /// <summary>Checks that DeserializeAsync from a string reads the JSON.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DeserializeAsync_FromString_ReadsJson() => UniTask.ToCoroutine(async () =>
        {
            var sample = await Serializer.Json.DeserializeAsync<Sample>(SampleJson);

            Assert.That(sample!.Count, Is.EqualTo(3));
        });

        /// <summary>Checks the stream-based TryDeserializeAsync overloads for valid and invalid input.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TryDeserializeAsync_FromStream_ReportsOutcome() => UniTask.ToCoroutine(async () =>
        {
            var (okBySerializer, resultBySerializer, errorBySerializer) = await Serializer.Json.TryDeserializeAsync<Sample>(CreateStream(SampleJson));
            var (okBySettings, resultBySettings, errorBySettings) = await Serializer.Json.TryDeserializeAsync<Sample>(CreateStream(SampleJson), (JsonSerializerSettings?)null);
            var (failBySerializer, _, failureBySerializer) = await Serializer.Json.TryDeserializeAsync<Sample>(CreateStream(InvalidJson));
            var (failBySettings, failedResult, failureBySettings) = await Serializer.Json.TryDeserializeAsync<Sample>(CreateStream(InvalidJson), new JsonSerializerSettings());

            Assert.That(okBySerializer, Is.True);
            Assert.That(resultBySerializer!.Name, Is.EqualTo("song"));
            Assert.That(errorBySerializer, Is.Null);
            Assert.That(okBySettings, Is.True);
            Assert.That(resultBySettings!.Count, Is.EqualTo(3));
            Assert.That(errorBySettings, Is.Null);
            Assert.That(failBySerializer, Is.False);
            Assert.That(failureBySerializer, Is.InstanceOf<JsonException>());
            Assert.That(failBySettings, Is.False);
            Assert.That(failedResult, Is.Null);
            Assert.That(failureBySettings, Is.InstanceOf<JsonException>());
        });

        /// <summary>Checks the string-based TryDeserializeAsync for valid and invalid input.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TryDeserializeAsync_FromString_ReportsOutcome() => UniTask.ToCoroutine(async () =>
        {
            var (ok, result, error) = await Serializer.Json.TryDeserializeAsync<Sample>(SampleJson);
            var (failed, failedResult, failure) = await Serializer.Json.TryDeserializeAsync<Sample>(InvalidJson);

            Assert.That(ok, Is.True);
            Assert.That(result!.Name, Is.EqualTo("song"));
            Assert.That(error, Is.Null);
            Assert.That(failed, Is.False);
            Assert.That(failedResult, Is.Null);
            Assert.That(failure, Is.InstanceOf<JsonException>());
        });
    }
}
