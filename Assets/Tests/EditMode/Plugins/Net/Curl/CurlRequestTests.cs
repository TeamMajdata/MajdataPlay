#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using Cysharp.Threading.Tasks;
using MajdataPlay.Net.Curl.Core;
using MajdataPlay.Net.Curl.Core.PInvoke;
using MajdataPlay.Net.Curl.Utils;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MajdataPlay.Tests.Plugins.Net.Curl
{
    /// <summary>Covers the request wrapper: option application and the upload read and seek callbacks.</summary>
    public class CurlRequestTests
    {
        /// <summary>A readable stream that refuses seeking.</summary>
        private sealed class NonSeekableStream : MemoryStream
        {
            /// <summary>Creates a stream over the given bytes.</summary>
            /// <param name="bytes">The bytes to read.</param>
            public NonSeekableStream(byte[] bytes) : base(bytes)
            {
            }

            /// <inheritdoc />
            public override bool CanSeek => false;
        }

        /// <summary>Runs an action with a strong GC handle to a request, as libcurl's callbacks see it.</summary>
        /// <param name="request">The request.</param>
        /// <param name="action">Receives the user data pointer.</param>
        private static void WithHandle(CurlRequest request, Action<IntPtr> action)
        {
            var handle = GCHandle.Alloc(request);
            try
            {
                action(GCHandle.ToIntPtr(handle));
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>Checks that the wrapper exposes the message's URI, method and content.</summary>
        [Test]
        public void Properties_ForwardToMessage()
        {
            using var message = new HttpRequestMessage(HttpMethod.Put, "http://127.0.0.1:9/a") { Content = new StringContent("x") };

            var request = new CurlRequest(message, null);

            Assert.That(request.Message, Is.SameAs(message));
            Assert.That(request.RequestUri, Is.EqualTo(message.RequestUri));
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Put));
            Assert.That(request.Content, Is.SameAs(message.Content));
            Assert.Throws<ArgumentNullException>(() => _ = new CurlRequest(null!, null));
        }

        /// <summary>Checks that the asynchronous factory opens the content stream only when there is content.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CreateAsync_OpensContentStream() => UniTask.ToCoroutine(async () =>
        {
            using var withContent = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:9/") { Content = new StringContent("abc") };
            using var withoutContent = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:9/");

            var uploading = await CurlRequest.CreateAsync(withContent);
            var plain = await CurlRequest.CreateAsync(withoutContent);
            var buffer = new byte[8];

            Assert.That(CurlCallbackInvoker.Read(IntPtr.Zero, buffer), Is.EqualTo(UIntPtr.Zero));
            WithHandle(uploading, userdata => Assert.That(CurlCallbackInvoker.Read(userdata, buffer), Is.EqualTo((UIntPtr)3u)));
            WithHandle(plain, userdata => Assert.That(CurlCallbackInvoker.Read(userdata, buffer), Is.EqualTo(UIntPtr.Zero)));
            Assert.That(Encoding.ASCII.GetString(buffer, 0, 3), Is.EqualTo("abc"));
        });

        /// <summary>Checks that options can be applied once, for seekable and non-seekable uploads and plain requests.</summary>
        [Test]
        public void ApplyTo_AppliesOnce()
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:9/") { Content = new StringContent("abc") };
            message.Headers.Add("X-Test", new[] { "a", "b" });
            using var seekable = new CurlEasy();
            using var nonSeekable = new CurlEasy();
            using var plain = new CurlEasy();
            var seekableRequest = new CurlRequest(message, new MemoryStream(new byte[] { 1 }));
            var nonSeekableRequest = new CurlRequest(message, new NonSeekableStream(new byte[] { 1 }));
            var plainRequest = new CurlRequest(new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:9/"), null);

            seekableRequest.ApplyTo(seekable);
            nonSeekableRequest.ApplyTo(nonSeekable);
            plainRequest.ApplyTo(plain);

            Assert.Throws<InvalidOperationException>(() => seekableRequest.ApplyTo(seekable));
            seekableRequest.ReleaseHandle();
            nonSeekableRequest.ReleaseHandle();
            plainRequest.ReleaseHandle();
            Assert.DoesNotThrow(seekableRequest.ReleaseHandle);
        }

        /// <summary>Checks that the read callback copies upload bytes in blocks and aborts when the stream fails.</summary>
        [Test]
        public void ReadCallback_CopiesBlocksAndAbortsOnFailure()
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:9/");
            var content = new MemoryStream(Encoding.ASCII.GetBytes("abcdef"));
            var request = new CurlRequest(message, content);
            var first = new byte[4];
            var second = new byte[4];

            WithHandle(request, userdata =>
            {
                Assert.That(CurlCallbackInvoker.Read(userdata, first), Is.EqualTo((UIntPtr)4u));
                Assert.That(CurlCallbackInvoker.Read(userdata, second), Is.EqualTo((UIntPtr)2u));
                Assert.That(CurlCallbackInvoker.Read(userdata, second), Is.EqualTo(UIntPtr.Zero));
                content.Dispose();
                Assert.That(CurlCallbackInvoker.Read(userdata, second), Is.EqualTo(CurlCallbackReturn.Read.Abort));
            });

            Assert.That(Encoding.ASCII.GetString(first), Is.EqualTo("abcd"));
            Assert.That(Encoding.ASCII.GetString(second, 0, 2), Is.EqualTo("ef"));
        }

        /// <summary>Checks every outcome of the seek callback.</summary>
        [Test]
        public void SeekCallback_ReportsOutcome()
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:9/");
            var content = new MemoryStream(new byte[10]);
            var seekable = new CurlRequest(message, content);
            var nonSeekable = new CurlRequest(message, new NonSeekableStream(new byte[1]));
            var empty = new CurlRequest(message, null);

            Assert.That(CurlCallbackInvoker.Seek(IntPtr.Zero, 0, SeekOrigin.Begin), Is.EqualTo(CurlSeekCallbackReturn.Fail));
            WithHandle(empty, userdata => Assert.That(CurlCallbackInvoker.Seek(userdata, 0, SeekOrigin.Begin), Is.EqualTo(CurlSeekCallbackReturn.Fail)));
            WithHandle(nonSeekable, userdata => Assert.That(CurlCallbackInvoker.Seek(userdata, 0, SeekOrigin.Begin), Is.EqualTo(CurlSeekCallbackReturn.CantSeek)));
            WithHandle(seekable, userdata =>
            {
                Assert.That(CurlCallbackInvoker.Seek(userdata, 4, SeekOrigin.Begin), Is.EqualTo(CurlSeekCallbackReturn.Ok));
                Assert.That(content.Position, Is.EqualTo(4));
                Assert.That(CurlCallbackInvoker.Seek(userdata, -100, SeekOrigin.Begin), Is.EqualTo(CurlSeekCallbackReturn.Fail));
            });
        }
    }
}
