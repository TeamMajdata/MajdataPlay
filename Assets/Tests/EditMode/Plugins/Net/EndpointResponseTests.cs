#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Cysharp.Threading.Tasks;
using MajdataPlay.Net;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MajdataPlay.Tests.Plugins.Net
{
    /// <summary>Covers the buffered endpoint response, its deserialization helpers and its typed wrapper.</summary>
    public class EndpointResponseTests
    {
        /// <summary>The endpoint URI used by every response.</summary>
        private static readonly Uri s_endpoint = new("https://example.invalid/api/chart");

        /// <summary>A simple payload type used for deserialization.</summary>
        public sealed class Payload
        {
            /// <summary>Gets or sets the payload name.</summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>Gets or sets the payload level.</summary>
            public int Level { get; set; }
        }

        /// <summary>Creates a response over UTF-8 JSON text.</summary>
        /// <param name="json">The response body.</param>
        /// <param name="deserializable">Whether the response may be deserialized.</param>
        /// <param name="headers">The response headers, or null for none.</param>
        /// <returns>The response.</returns>
        private static EndpointResponse Create(string json, bool deserializable = true,
            IReadOnlyDictionary<string, IEnumerable<string>>? headers = null)
        {
            return new EndpointResponse(Encoding.UTF8.GetBytes(json), JsonSerializer.CreateDefault(), new JsonSerializerSettings())
            {
                Endpoint = s_endpoint,
                IsSuccessfully = deserializable,
                IsDeserializable = deserializable,
                StatusCode = HttpStatusCode.OK,
                ErrorCode = HttpErrorCode.NoError,
                Message = "message",
                Headers = headers!
            };
        }

        /// <summary>Checks that the parameterless response has no data and empty headers.</summary>
        [Test]
        public void DefaultResponse_IsEmpty()
        {
            var response = new EndpointResponse
            {
                Endpoint = s_endpoint,
                IsSuccessfully = false,
                IsDeserializable = false,
                ErrorCode = HttpErrorCode.Unreachable,
                Message = "unreachable"
            };

            Assert.That(response.Length, Is.Zero);
            Assert.That(response.AsMemory().IsEmpty, Is.True);
            Assert.That(response.Headers, Is.SameAs(EndpointResponse.EMPTY_HEADERS));
            Assert.That(response.StatusCode, Is.Null);
            Assert.That(response.TryGetHeader("x"), Is.Empty);
        }

        /// <summary>Checks that the data constructor validates its serializer arguments.</summary>
        [Test]
        public void DataConstructor_ValidatesSerializers()
        {
            var data = new byte[] { 1 };

            Assert.Throws<ArgumentNullException>(() => _ = new EndpointResponse(data, null!, new JsonSerializerSettings())
            {
                Endpoint = s_endpoint,
                IsSuccessfully = true,
                IsDeserializable = true,
                ErrorCode = HttpErrorCode.NoError,
                Message = string.Empty
            });
            Assert.Throws<ArgumentNullException>(() => _ = new EndpointResponse(data, JsonSerializer.CreateDefault(), null!)
            {
                Endpoint = s_endpoint,
                IsSuccessfully = true,
                IsDeserializable = true,
                ErrorCode = HttpErrorCode.NoError,
                Message = string.Empty
            });
        }

        /// <summary>Checks that the body is exposed unchanged and null headers become the shared empty set.</summary>
        [Test]
        public void DataConstructor_ExposesBody()
        {
            var response = Create("{}");

            Assert.That(response.Length, Is.EqualTo(2));
            Assert.That(response.AsSpan().ToArray(), Is.EqualTo(Encoding.UTF8.GetBytes("{}")));
            Assert.That(response.AsMemory().Length, Is.EqualTo(2));
            Assert.That(response.Headers, Is.SameAs(EndpointResponse.EMPTY_HEADERS));
        }

        /// <summary>Checks that JSON bodies deserialize with the default and an explicit encoding.</summary>
        [Test]
        public void Deserialize_ParsesJson()
        {
            var response = Create("{\"Name\":\"譜面\",\"Level\":13}");

            var payload = response.Deserialize<Payload>()!;
            var explicitEncoding = response.Deserialize<Payload>(Encoding.UTF8)!;

            Assert.That(payload.Name, Is.EqualTo("譜面"));
            Assert.That(payload.Level, Is.EqualTo(13));
            Assert.That(explicitEncoding.Level, Is.EqualTo(13));
        }

        /// <summary>Checks that deserialization is refused for non-deserializable responses and a null encoding.</summary>
        [Test]
        public void Deserialize_ValidatesState()
        {
            var refused = Create("{}", deserializable: false);
            var response = Create("{}");

            Assert.Throws<InvalidOperationException>(() => refused.Deserialize<Payload>());
            Assert.Throws<ArgumentNullException>(() => response.Deserialize<Payload>(null!));
        }

        /// <summary>Checks that the try pattern reports success, parse failures and refused responses.</summary>
        [Test]
        public void TryDeserialize_ReportsOutcome()
        {
            var valid = Create("{\"Level\":2}");
            var invalid = Create("{not json");
            var refused = Create("{}", deserializable: false);

            var validResult = valid.TryDeserialize<Payload>(out var payload, out var validError);
            var invalidResult = invalid.TryDeserialize<Payload>(out _, out var invalidError);
            var refusedResult = refused.TryDeserialize<Payload>(Encoding.UTF8, out var refusedPayload, out var refusedError);

            Assert.That(validResult, Is.True);
            Assert.That(payload!.Level, Is.EqualTo(2));
            Assert.That(validError, Is.Null);
            Assert.That(invalidResult, Is.False);
            Assert.That(invalidError, Is.InstanceOf<JsonException>());
            Assert.That(refusedResult, Is.False);
            Assert.That(refusedPayload, Is.Null);
            Assert.That(refusedError, Is.InstanceOf<InvalidOperationException>());
            Assert.Throws<ArgumentNullException>(() => valid.TryDeserialize<Payload>(null!, out _, out _));
        }

        /// <summary>Checks asynchronous deserialization, including its validation.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DeserializeAsync_ParsesJson() => UniTask.ToCoroutine(async () =>
        {
            var response = Create("{\"Name\":\"async\"}");
            var refused = Create("{}", deserializable: false);

            var payload = await response.DeserializeAsync<Payload>();
            var explicitEncoding = await response.DeserializeAsync<Payload>(Encoding.UTF8);
            var refusedError = default(Exception);
            var encodingError = default(Exception);
            try
            {
                await refused.DeserializeAsync<Payload>();
            }
            catch (Exception exception)
            {
                refusedError = exception;
            }
            try
            {
                await response.DeserializeAsync<Payload>(null!);
            }
            catch (Exception exception)
            {
                encodingError = exception;
            }

            Assert.That(payload!.Name, Is.EqualTo("async"));
            Assert.That(explicitEncoding!.Name, Is.EqualTo("async"));
            Assert.That(refusedError, Is.InstanceOf<InvalidOperationException>());
            Assert.That(encodingError, Is.InstanceOf<ArgumentNullException>());
        });

        /// <summary>Checks header lookup, including missing headers and invalid names.</summary>
        [Test]
        public void TryGetHeader_FindsValues()
        {
            var headers = new Dictionary<string, IEnumerable<string>> { ["X-Token"] = new[] { "a", "b" } };
            var response = Create("{}", headers: headers);

            Assert.That(response.TryGetHeader("X-Token"), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(response.TryGetHeader("X-Missing"), Is.Empty);
            Assert.Throws<ArgumentNullException>(() => response.TryGetHeader(null!));
            Assert.Throws<ArgumentNullException>(() => response.TryGetHeader(string.Empty));
        }

        /// <summary>Checks that the text description includes the response state and headers.</summary>
        [Test]
        public void ToString_DescribesResponse()
        {
            var headers = new Dictionary<string, IEnumerable<string>> { ["X-Token"] = new[] { "a", "b" } };

            var text = Create("{}", headers: headers).ToString();

            Assert.That(text, Does.Contain("Endpoint: " + s_endpoint));
            Assert.That(text, Does.Contain("StatusCode: OK"));
            Assert.That(text, Does.Contain("ErrorCode: NoError"));
            Assert.That(text, Does.Contain("IsDeserializable: True"));
            Assert.That(text, Does.Contain("X-Token: a;b"));
        }

        /// <summary>Checks that the typed wrapper forwards every member to the untyped response.</summary>
        [Test]
        public void TypedResponse_ForwardsMembers()
        {
            var headers = new Dictionary<string, IEnumerable<string>> { ["X"] = new[] { "1" } };
            var inner = Create("{\"Level\":7}", headers: headers);

            var typed = new EndpointResponse<Payload>(inner);

            Assert.That(typed.Length, Is.EqualTo(inner.Length));
            Assert.That(typed.IsSuccessfully, Is.True);
            Assert.That(typed.IsDeserializable, Is.True);
            Assert.That(typed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(typed.ErrorCode, Is.EqualTo(HttpErrorCode.NoError));
            Assert.That(typed.Headers, Is.SameAs(headers));
            Assert.That(typed.Message, Is.EqualTo("message"));
            Assert.That(typed.Deserialize()!.Level, Is.EqualTo(7));
            Assert.That(typed.Deserialize(Encoding.UTF8)!.Level, Is.EqualTo(7));
            Assert.That(typed.TryDeserialize(out var payload, out _), Is.True);
            Assert.That(payload!.Level, Is.EqualTo(7));
            Assert.That(typed.TryDeserialize(Encoding.UTF8, out _, out _), Is.True);
            Assert.That(typed.AsSpan().Length, Is.EqualTo(inner.Length));
            Assert.That(typed.AsMemory().Length, Is.EqualTo(inner.Length));
            Assert.That(typed.TryGetHeader("X"), Is.EqualTo(new[] { "1" }));
            Assert.That(typed.ToString(), Is.EqualTo(inner.ToString()));
        }

        /// <summary>Checks the typed wrapper's asynchronous deserialization.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TypedResponse_DeserializesAsync() => UniTask.ToCoroutine(async () =>
        {
            var typed = new EndpointResponse<Payload>(Create("{\"Level\":9}"));

            var first = await typed.DeserializeAsync();
            var second = await typed.DeserializeAsync(Encoding.UTF8);

            Assert.That(first!.Level, Is.EqualTo(9));
            Assert.That(second!.Level, Is.EqualTo(9));
        });
    }
}
