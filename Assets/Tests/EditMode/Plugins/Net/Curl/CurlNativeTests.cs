#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using MajdataPlay.Net.Curl;
using MajdataPlay.Net.Curl.Core;
using MajdataPlay.Net.Curl.Core.PInvoke;
using MajdataPlay.Net.Curl.Lifecycle;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.Net.Curl
{
    /// <summary>
    /// Covers the thin wrappers over the bundled libcurl: easy and multi handles, version information and the global
    /// lifecycle. Only local, deterministic calls are made; nothing contacts a network.
    /// </summary>
    public class CurlNativeTests
    {
        /// <summary>A URL whose scheme libcurl rejects before any network activity.</summary>
        private const string UnsupportedUrl = "unsupported-scheme://local/";

        /// <summary>An option number libcurl does not know.</summary>
        private const CurlOption UnknownOption = (CurlOption)9999;

        /// <summary>Gets the number of live handles registered with the libcurl global state.</summary>
        private static int RefCount => PrivateMember.GetStaticField<int>(typeof(LibCurlLifecycle), "s_refCount");

        /// <summary>Checks that libcurl reports a version with HTTP support.</summary>
        [Test]
        public void GetVersionInfo_ReportsHttpSupport()
        {
            LibCurlLifecycle.Retain();
            try
            {
                var info = LibCurl.GetVersionInfo(0);

                Assert.That(info.Version, Is.Not.Empty);
                Assert.That(info.VersionNum, Is.GreaterThan(0u));
                Assert.That(info.Host, Is.Not.Empty);
                Assert.That(info.Protocols, Does.Contain("http"));
                Assert.That(PrivateMember.GetStaticField<bool>(typeof(LibCurlLifecycle), "s_isGlobalInited"), Is.True);
            }
            finally
            {
                LibCurlLifecycle.Release();
            }
        }

        /// <summary>Checks that every easy handle holds one reference to the global state until it is disposed.</summary>
        [Test]
        public void EasyHandle_RetainsGlobalStateUntilDisposed()
        {
            var before = RefCount;

            var easy = new CurlEasy();
            var during = RefCount;
            easy.Dispose();
            easy.Dispose();

            Assert.That(during, Is.EqualTo(before + 1));
            Assert.That(RefCount, Is.EqualTo(before));
            Assert.That(easy.IsAllocated, Is.False);
        }

        /// <summary>Checks that libcurl describes error codes and success.</summary>
        [Test]
        public void GetErrorMessage_DescribesCodes()
        {
            Assert.That(LibCurl.Easy.GetErrorMessage(CurlCode.Ok), Is.EqualTo("No error"));
            Assert.That(LibCurl.Easy.GetErrorMessage(CurlCode.CouldNotConnect), Is.Not.Empty);
        }

        /// <summary>Checks that string, numeric, pointer and enum options are accepted on a live easy handle.</summary>
        [Test]
        public void SetOption_AcceptsKnownOptions()
        {
            using var easy = new CurlEasy();

            Assert.DoesNotThrow(() => easy.SetOption(CurlOption.Url, UnsupportedUrl));
            Assert.DoesNotThrow(() => easy.SetOption(CurlOption.Verbose, 0L));
            Assert.DoesNotThrow(() => easy.SetOption(CurlOption.Private, IntPtr.Zero));
            Assert.That(LibCurl.Easy.SetOption(easy.Handle, CurlOption.SslOptions, CurlSslOption.NoRevoke), Is.EqualTo(CurlCode.Ok));
        }

        /// <summary>Checks that rejected options surface as invalid operations with libcurl's message.</summary>
        [Test]
        public void SetOption_RejectsUnknownOptions()
        {
            using var easy = new CurlEasy();

            var exception = Assert.Throws<InvalidOperationException>(() => easy.SetOption(UnknownOption, 1L));
            Assert.Throws<InvalidOperationException>(() => easy.SetOption(UnknownOption, "x"));
            Assert.Throws<InvalidOperationException>(() => easy.SetOption(UnknownOption, IntPtr.Zero));

            Assert.That(exception.Message, Does.StartWith("libcurl error: "));
        }

        /// <summary>Checks the try variants on a live handle for accepted and rejected options.</summary>
        [Test]
        public void TrySetOption_ReportsResult()
        {
            using var easy = new CurlEasy();

            Assert.That(easy.TrySetOption(CurlOption.Url, UnsupportedUrl, out var stringResult), Is.True);
            Assert.That(easy.TrySetOption(CurlOption.Verbose, 0L, out var longResult), Is.True);
            Assert.That(easy.TrySetOption(CurlOption.Private, IntPtr.Zero, out var pointerResult), Is.True);
            Assert.That(easy.TrySetOption(UnknownOption, 1L, out var unknownResult), Is.False);

            Assert.That(stringResult, Is.EqualTo(CurlCode.Ok));
            Assert.That(longResult, Is.EqualTo(CurlCode.Ok));
            Assert.That(pointerResult, Is.EqualTo(CurlCode.Ok));
            Assert.That(unknownResult, Is.Not.EqualTo(CurlCode.Ok));
        }

        /// <summary>Checks that performing a transfer with an unsupported scheme fails without network access.</summary>
        [Test]
        public void Perform_ReportsUnsupportedProtocol()
        {
            using var easy = new CurlEasy();
            easy.SetOption(CurlOption.Url, UnsupportedUrl);

            var result = easy.Perform();
            var tried = easy.TryPerform(out var tryResult);
            var info = LibCurl.Easy.GetInfo(easy.Handle, CurlInfo.EffectiveUrl, out string? effectiveUrl);

            Assert.That(result, Is.EqualTo(CurlCode.UnsupportedProtocol));
            Assert.That(tried, Is.False);
            Assert.That(tryResult, Is.EqualTo(CurlCode.UnsupportedProtocol));
            Assert.That(info, Is.EqualTo(CurlCode.Ok));
            Assert.That(effectiveUrl, Does.StartWith("unsupported-scheme"));
        }

        /// <summary>Checks that numeric, floating-point and pointer information can be queried from a fresh handle.</summary>
        [Test]
        public void GetInfo_ReadsEveryKind()
        {
            using var easy = new CurlEasy();

            Assert.That(LibCurl.Easy.GetInfo(easy.Handle, CurlInfo.ResponseCode, out long responseCode), Is.EqualTo(CurlCode.Ok));
            Assert.That(LibCurl.Easy.GetInfo(easy.Handle, CurlInfo.TotalTime, out double totalTime), Is.EqualTo(CurlCode.Ok));
            Assert.That(LibCurl.Easy.GetInfo(easy.Handle, CurlInfo.Private, out IntPtr privateData), Is.EqualTo(CurlCode.Ok));
            Assert.That(LibCurl.Easy.GetInfo(easy.Handle, CurlInfo.ContentType, out string? contentType), Is.EqualTo(CurlCode.Ok));

            Assert.That(responseCode, Is.Zero);
            Assert.That(totalTime, Is.Zero);
            Assert.That(privateData, Is.EqualTo(IntPtr.Zero));
            Assert.That(contentType, Is.Null);
        }

        /// <summary>Checks that pausing reports libcurl's result consistently through both variants.</summary>
        [Test]
        public void Pause_ReportsResult()
        {
            using var easy = new CurlEasy();

            var result = easy.Pause(CurlPauseAction.None);
            var tried = easy.TryPause(CurlPauseAction.None, out var tryResult);

            Assert.That(tryResult, Is.EqualTo(result));
            Assert.That(tried, Is.EqualTo(result == CurlCode.Ok));
        }

        /// <summary>Checks that header lists can be built, replaced and cleared, including before any header exists.</summary>
        [Test]
        public void Headers_CanBeReplacedAndCleared()
        {
            using var easy = new CurlEasy();
            var headers = new List<KeyValuePair<string, IEnumerable<string>>>
            {
                new("X-One", new[] { "a", "b" }),
                new("X-Two", new[] { "c" })
            };

            Assert.DoesNotThrow(easy.ClearSList);
            Assert.DoesNotThrow(() => easy.SetHeaders(headers));
            Assert.DoesNotThrow(() => easy.SetHeaders(headers));
            Assert.DoesNotThrow(() => easy.AppendSList("X-Three: d"));
            Assert.DoesNotThrow(easy.ClearSList);
            Assert.DoesNotThrow(() => easy.AppendSList("X-Four: e"));
        }

        /// <summary>Checks that every operation of a disposed easy handle is refused or reported as not performed.</summary>
        [Test]
        public void DisposedEasyHandle_RefusesOperations()
        {
            var easy = new CurlEasy();
            easy.AppendSList("X: y");
            easy.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _ = easy.Handle);
            Assert.Throws<ObjectDisposedException>(() => easy.SetOption(CurlOption.Verbose, 0L));
            Assert.Throws<ObjectDisposedException>(() => easy.SetOption(CurlOption.Url, "x"));
            Assert.Throws<ObjectDisposedException>(() => easy.SetOption(CurlOption.Private, IntPtr.Zero));
            Assert.Throws<ObjectDisposedException>(() => easy.Pause(CurlPauseAction.None));
            Assert.Throws<ObjectDisposedException>(() => easy.Perform());
            Assert.Throws<ObjectDisposedException>(() => easy.SetHeaders(Array.Empty<KeyValuePair<string, IEnumerable<string>>>()));
            Assert.Throws<ObjectDisposedException>(() => easy.AppendSList("x"));
            Assert.Throws<ObjectDisposedException>(easy.ClearSList);
            Assert.That(easy.TrySetOption(CurlOption.Url, "x", out var stringResult), Is.False);
            Assert.That(easy.TrySetOption(CurlOption.Verbose, 0L, out var longResult), Is.False);
            Assert.That(easy.TrySetOption(CurlOption.Private, IntPtr.Zero, out var pointerResult), Is.False);
            Assert.That(easy.TryPause(CurlPauseAction.None, out var pauseResult), Is.False);
            Assert.That(easy.TryPerform(out var performResult), Is.False);
            Assert.That(new[] { stringResult, longResult, pointerResult, pauseResult, performResult }, Is.All.Null);
        }

        /// <summary>Checks the raw multi-handle calls on an idle handle.</summary>
        [Test]
        public void MultiHandle_IdleCallsSucceed()
        {
            LibCurlLifecycle.Retain();
            var multi = LibCurl.Multi.Init();
            try
            {
                Assert.That(multi, Is.Not.EqualTo(IntPtr.Zero));
                Assert.That(LibCurl.Multi.SetOption(multi, CurlMOption.MaxHostConnections, 4L), Is.EqualTo(CurlMCode.Ok));
                Assert.That(LibCurl.Multi.Perform(multi, out var running), Is.EqualTo(CurlMCode.Ok));
                Assert.That(running, Is.Zero);
                Assert.That(LibCurl.Multi.GetMessage(multi, out var remaining), Is.Null);
                Assert.That(remaining, Is.Zero);
                Assert.That(LibCurl.Multi.Wakeup(multi), Is.EqualTo(CurlMCode.Ok));
                Assert.That(LibCurl.Multi.Poll(multi, IntPtr.Zero, 0, 0, out _), Is.EqualTo(CurlMCode.Ok));
            }
            finally
            {
                Assert.That(LibCurl.Multi.CleanUp(multi), Is.EqualTo(CurlMCode.Ok));
                LibCurlLifecycle.Release();
            }
        }

        /// <summary>Checks that a multi handle stops its worker and refuses requests once disposed.</summary>
        [Test]
        public void CurlMulti_RefusesRequestsAfterDisposal()
        {
            var before = RefCount;
            var multi = new CurlMulti();
            var during = RefCount;

            multi.Dispose();
            multi.DisposeAsync().AsTask().GetAwaiter().GetResult();

            Assert.That(during, Is.EqualTo(before + 1));
            Assert.That(RefCount, Is.EqualTo(before));
            Assert.That(multi.IsAllocated, Is.False);
            Assert.Throws<ObjectDisposedException>(() => _ = multi.Handle);
            Assert.Throws<ObjectDisposedException>(() => multi.AddToQueue(new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:1/"), null, default));
        }

        /// <summary>Checks that a request that cannot be built releases its easy handle and global reference.</summary>
        [Test]
        public void CurlMulti_ReleasesEasyHandleWhenRequestIsInvalid()
        {
            using var multi = new CurlMulti();
            var before = RefCount;

            Assert.Throws<ArgumentNullException>(() => multi.AddToQueue(null!, null, default));

            Assert.That(RefCount, Is.EqualTo(before));
        }

        /// <summary>Checks that an option failure while building a transfer releases the transfer's native resources.</summary>
        [Test]
        public void CurlMulti_ReleasesTransferWhenOptionsFail()
        {
            using var multi = new CurlMulti();
            var before = RefCount;
            var config = new CurlHttpConfig { UseProxy = true, Proxy = new UnresolvableProxy() };

            Assert.Throws<InvalidOperationException>(
                () => multi.AddToQueue(new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:1/"), null, config));

            Assert.That(RefCount, Is.EqualTo(before));
        }

        /// <summary>A proxy that claims to handle every URI but resolves none.</summary>
        private sealed class UnresolvableProxy : System.Net.IWebProxy
        {
            /// <inheritdoc />
            public System.Net.ICredentials? Credentials { get; set; }

            /// <inheritdoc />
            public Uri GetProxy(Uri destination)
            {
                throw new InvalidOperationException("The proxy cannot be resolved.");
            }

            /// <inheritdoc />
            public bool IsBypassed(Uri host)
            {
                return false;
            }
        }
    }
}
