using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.IO;
using ManagedBass;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>
    /// Verifies <see cref="BassHttpFileSource"/> with an in-memory HTTP handler, so streams are created from HTTP responses
    /// without any network access.
    /// </summary>
    public class BassHttpFileSourceTests
    {
        /// <summary>The address used for every request; the handler never contacts it.</summary>
        private static readonly Uri s_uri = new Uri("https://example.invalid/audio/track.wav?token=secret");

        /// <summary>Initializes BASS on the silent device once for the fixture.</summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            BassTestSupport.InitNoSoundDevice();
        }

        /// <summary>Frees BASS after the fixture.</summary>
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            BassTestSupport.FreeNoSoundDevice();
        }

        /// <summary>Checks that missing arguments and an already cancelled token are rejected before any request.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InvalidArguments_AreRejected()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var client = new HttpClient(new StaticHandler(HttpStatusCode.OK, Array.Empty<byte>()));
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();

                await AssertThrowsAsync<ArgumentNullException>(() => BassHttpFileSource.CreateStreamAsync(null!, s_uri, BassFlags.Decode).AsTask());
                await AssertThrowsAsync<ArgumentNullException>(() => BassHttpFileSource.CreateStreamAsync(client, null!, BassFlags.Decode).AsTask());
                await AssertThrowsAsync<OperationCanceledException>(() => BassHttpFileSource.CreateStreamAsync(client, s_uri, BassFlags.Decode, cancelled.Token).AsTask());
            });
        }

        /// <summary>Checks that an error status code is reported as an HTTP failure.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ErrorStatus_Throws()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var client = new HttpClient(new StaticHandler(HttpStatusCode.NotFound, Array.Empty<byte>()));

                await AssertThrowsAsync<HttpRequestException>(() => BassHttpFileSource.CreateStreamAsync(client, s_uri, BassFlags.Decode).AsTask());
                Assert.That(BassHttpFileSource.ActiveSourceCount, Is.EqualTo(0));
            });
        }

        /// <summary>Checks that a WAV body becomes a decodable stream and the source is released when the stream is freed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator WavBody_CreatesADecodableStream()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var wav = BassTestSupport.CreateSquareWave(2, 22050, 0.5f);
                using var client = new HttpClient(new StaticHandler(HttpStatusCode.OK, wav));

                var stream = await BassHttpFileSource.CreateStreamAsync(client, s_uri, BassFlags.Decode);
                try
                {
                    Assert.That(stream, Is.Not.Zero);
                    Assert.That(Bass.ChannelGetInfo(stream).Channels, Is.EqualTo(2));
                    var buffer = new byte[4096];
                    Assert.That(Bass.ChannelGetData(stream, buffer, buffer.Length), Is.GreaterThan(0));
                }
                finally
                {
                    Bass.StreamFree(stream);
                }
                await WaitUntil(() => BassHttpFileSource.ActiveSourceCount == 0);
                Assert.That(BassHttpFileSource.ActiveSourceCount, Is.EqualTo(0));
            });
        }

        /// <summary>Checks that an encoded body is created with an unknown length.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator EncodedBody_UsesUnknownLength()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var wav = BassTestSupport.CreateSquareWave(1, 8000, 0.5f);
                using var client = new HttpClient(new StaticHandler(HttpStatusCode.OK, wav, "identity", "br"));

                var stream = await BassHttpFileSource.CreateStreamAsync(client, s_uri, BassFlags.Decode);

                Assert.That(stream, Is.Not.Zero);
                Bass.StreamFree(stream);
                await WaitUntil(() => BassHttpFileSource.ActiveSourceCount == 0);
            });
        }

        /// <summary>Checks that a body BASS cannot decode is reported as a BASS failure and releases the source.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator UnsupportedBody_ThrowsBassException()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var garbage = new byte[2048];
                for (var i = 0; i < garbage.Length; i++)
                {
                    garbage[i] = (byte)(i * 7);
                }
                using var client = new HttpClient(new StaticHandler(HttpStatusCode.OK, garbage));

                await AssertThrowsAsync<BassException>(() => BassHttpFileSource.CreateStreamAsync(client, s_uri, BassFlags.Decode).AsTask());
                Assert.That(BassHttpFileSource.ActiveSourceCount, Is.EqualTo(0));
            });
        }

        /// <summary>Checks that a body failing before the format is known is reported as an IO failure.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator FailingBody_ThrowsIOException()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var client = new HttpClient(new FailingBodyHandler());

                await AssertThrowsAsync<IOException>(() => BassHttpFileSource.CreateStreamAsync(client, s_uri, BassFlags.Decode).AsTask());
                Assert.That(BassHttpFileSource.ActiveSourceCount, Is.EqualTo(0));
            });
        }

        /// <summary>Checks that both BASS sample types stream online audio through the shared HTTP client without seeking.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Samples_CreateFromUri_UseTheSharedClient()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Swap only the handler inside the existing shared client. Replacing the read-only static itself is
                // unsafe on Mono: methods JIT-compiled meanwhile keep the temporary client's address after it is collected.
                var field = FindHandlerField();
                var original = field.GetValue(MajEnv.SharedHttpClient);
                var savedMatrix = AudioManager.MixingMatrix;
                var mixer = ManagedBass.Mix.BassMix.CreateMixerStream(BassTestSupport.SampleRate, 2, BassFlags.MixerNonStop | BassFlags.Decode | BassFlags.Float);
                var wav = BassTestSupport.CreateSquareWave(2, 22050, 0.5f);
                using var handler = new StaticHandler(HttpStatusCode.OK, wav);
                field.SetValue(MajEnv.SharedHttpClient, handler);
                PrivateMember.SetStaticProperty(typeof(AudioManager), nameof(AudioManager.MixingMatrix), new float[,] { { 1f, 0f }, { 0f, 1f } });
                try
                {
                    using var simple = await Task.Run(() => BassSimpleAudioSample.CreateFromUri(s_uri));
                    using var mixed = await BassAudioSample.CreateFromUriAsync(s_uri, mixer);

                    Assert.That(simple.CanSeek, Is.False);
                    Assert.That(mixed.CanSeek, Is.False);
                    Assert.That(simple.IsEmpty || mixed.IsEmpty, Is.False);
                    Assert.Throws<NotSupportedException>(() => _ = simple.CurrentSec);
                    Assert.Throws<NotSupportedException>(() => _ = mixed.CurrentSec);
                }
                finally
                {
                    field.SetValue(MajEnv.SharedHttpClient, original);
                    PrivateMember.SetStaticProperty(typeof(AudioManager), nameof(AudioManager.MixingMatrix), savedMatrix);
                    Bass.StreamFree(mixer);
                }
            });
        }

        /// <summary>Finds the handler field of <see cref="HttpMessageInvoker"/>, which every request of a client goes through.</summary>
        /// <returns>The field.</returns>
        /// <exception cref="MissingFieldException">The runtime's HttpMessageInvoker has no handler field.</exception>
        private static System.Reflection.FieldInfo FindHandlerField()
        {
            foreach (var field in typeof(HttpMessageInvoker).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
            {
                if (field.FieldType == typeof(HttpMessageHandler))
                {
                    return field;
                }
            }
            throw new MissingFieldException(typeof(HttpMessageInvoker).FullName, "handler");
        }

        /// <summary>Awaits an operation and asserts that it fails with the expected exception type.</summary>
        /// <typeparam name="TException">The expected exception type or a base type of it.</typeparam>
        /// <param name="operation">The operation to run.</param>
        /// <returns>A task that completes after the assertion.</returns>
        private static async UniTask AssertThrowsAsync<TException>(Func<Task> operation) where TException : Exception
        {
            Exception? caught = null;
            try
            {
                await operation();
            }
            catch (Exception exception)
            {
                caught = exception;
            }
            Assert.That(caught, Is.InstanceOf<TException>());
        }

        /// <summary>Waits for a condition for up to five seconds.</summary>
        /// <param name="condition">The condition.</param>
        /// <returns>A task that completes when the condition holds or the time runs out.</returns>
        private static async UniTask WaitUntil(Func<bool> condition)
        {
            var started = DateTime.UtcNow;
            while (!condition() && DateTime.UtcNow - started < TimeSpan.FromSeconds(5))
            {
                await Task.Delay(10);
            }
        }

        /// <summary>Answers every request with a fixed status and body.</summary>
        private sealed class StaticHandler : HttpMessageHandler
        {
            /// <summary>The response status.</summary>
            private readonly HttpStatusCode _status;

            /// <summary>The response body.</summary>
            private readonly byte[] _body;

            /// <summary>The content encodings announced in the response.</summary>
            private readonly string[] _encodings;

            /// <summary>Creates the handler.</summary>
            /// <param name="status">The response status.</param>
            /// <param name="body">The response body.</param>
            /// <param name="encodings">The content encodings to announce.</param>
            public StaticHandler(HttpStatusCode status, byte[] body, params string[] encodings)
            {
                _status = status;
                _body = body;
                _encodings = encodings;
            }

            /// <summary>Returns the configured response.</summary>
            /// <param name="request">The request.</param>
            /// <param name="cancellationToken">The cancellation token.</param>
            /// <returns>The response.</returns>
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var content = new ByteArrayContent(_body);
                foreach (var encoding in _encodings)
                {
                    content.Headers.ContentEncoding.Add(encoding);
                }
                return Task.FromResult(new HttpResponseMessage(_status) { Content = content, RequestMessage = request });
            }
        }

        /// <summary>Answers every request with a body that fails when read.</summary>
        private sealed class FailingBodyHandler : HttpMessageHandler
        {
            /// <summary>Returns a response whose body throws.</summary>
            /// <param name="request">The request.</param>
            /// <param name="cancellationToken">The cancellation token.</param>
            /// <returns>The response.</returns>
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new FailingStream()) });
            }
        }

        /// <summary>A readable stream whose reads always fail.</summary>
        private sealed class FailingStream : Stream
        {
            /// <inheritdoc/>
            public override bool CanRead => true;

            /// <inheritdoc/>
            public override bool CanSeek => false;

            /// <inheritdoc/>
            public override bool CanWrite => false;

            /// <inheritdoc/>
            public override long Length => throw new NotSupportedException();

            /// <inheritdoc/>
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            /// <inheritdoc/>
            public override void Flush()
            {
            }

            /// <summary>Always fails.</summary>
            /// <param name="buffer">The destination.</param>
            /// <param name="offset">The offset.</param>
            /// <param name="count">The count.</param>
            /// <returns>Never returns.</returns>
            /// <exception cref="IOException">Always.</exception>
            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new IOException("connection reset");
            }

            /// <inheritdoc/>
            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc/>
            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc/>
            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }
        }
    }
}
