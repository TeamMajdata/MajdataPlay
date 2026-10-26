#nullable enable
using System;
using System.IO;
using System.Net;
using System.Security.Authentication;
using MajdataPlay.Net.Curl;
using MajdataPlay.Net.Curl.Core.PInvoke;
using MajdataPlay.Net.Curl.Utils;
using NUnit.Framework;
using UnityEngine;

namespace MajdataPlay.Tests.Plugins.Net.Curl
{
    /// <summary>Covers the handler settings snapshot, its option mapping and the curl error helpers.</summary>
    public class CurlConfigTests
    {
        /// <summary>Invokes a private mapping method of a configuration snapshot.</summary>
        /// <typeparam name="T">The return type.</typeparam>
        /// <param name="name">The method name.</param>
        /// <param name="argument">The single argument.</param>
        /// <returns>The mapped value.</returns>
        private static T Map<T>(string name, object argument)
        {
            return PrivateMember.Invoke<T>(new CurlHttpConfig(), name, argument);
        }

        /// <summary>Checks the handler defaults, which mirror HttpClientHandler except for byte-based header limits.</summary>
        [Test]
        public void Handler_HasDocumentedDefaults()
        {
            using var handler = new CurlHttpMessageHandler();

            Assert.That(handler.SslProtocols, Is.EqualTo(SslProtocols.Tls12 | SslProtocols.Tls13));
            Assert.That(handler.Proxy, Is.Null);
            Assert.That(handler.PreAuthenticate, Is.False);
            Assert.That(handler.MaxResponseHeadersLength, Is.EqualTo(64 * 1024));
            Assert.That(handler.ResponseBodyBufferSize, Is.EqualTo(64 * 1024));
            Assert.That(handler.MaxRequestContentBufferSize, Is.EqualTo(int.MaxValue));
            Assert.That(handler.MaxConnectionsPerServer, Is.EqualTo(8));
            Assert.That(handler.MaxAutomaticRedirections, Is.EqualTo(50));
            Assert.That(handler.DefaultProxyCredentials, Is.Null);
            Assert.That(handler.Credentials, Is.Null);
            Assert.That(handler.CookieContainer, Is.Null);
            Assert.That(handler.CheckCertificateRevocationList, Is.False);
            Assert.That(handler.AutomaticDecompression, Is.EqualTo(DecompressionMethods.None));
            Assert.That(handler.AllowAutoRedirect, Is.True);
            Assert.That(handler.UseDefaultCredentials, Is.False);
            Assert.That(handler.UseProxy, Is.False);
        }

        /// <summary>Checks that the per-request snapshot copies every handler setting.</summary>
        [Test]
        public void Handler_SnapshotCopiesEverySetting()
        {
            var proxy = new WebProxy("http://127.0.0.1:1");
            var credentials = new NetworkCredential("user", "pass");
            var proxyCredentials = new NetworkCredential("proxy", "secret");
            var cookies = new CookieContainer();
            using var handler = new CurlHttpMessageHandler
            {
                SslProtocols = SslProtocols.Tls12,
                Proxy = proxy,
                PreAuthenticate = true,
                MaxResponseHeadersLength = 1,
                ResponseBodyBufferSize = 2,
                MaxRequestContentBufferSize = 3,
                MaxConnectionsPerServer = 4,
                MaxAutomaticRedirections = 5,
                DefaultProxyCredentials = proxyCredentials,
                Credentials = credentials,
                CookieContainer = cookies,
                CheckCertificateRevocationList = true,
                AutomaticDecompression = DecompressionMethods.GZip,
                AllowAutoRedirect = false,
                UseDefaultCredentials = true,
                UseProxy = true
            };

            var config = PrivateMember.Invoke<CurlHttpConfig>(handler, "CopyCurrentConfig");

            Assert.That(config.SslProtocols, Is.EqualTo(SslProtocols.Tls12));
            Assert.That(config.Proxy, Is.SameAs(proxy));
            Assert.That(config.PreAuthenticate, Is.True);
            Assert.That(config.MaxResponseHeadersLength, Is.EqualTo(1));
            Assert.That(config.ResponseBodyBufferSize, Is.EqualTo(2));
            Assert.That(config.MaxRequestContentBufferSize, Is.EqualTo(3));
            Assert.That(config.MaxConnectionsPerServer, Is.EqualTo(4));
            Assert.That(config.MaxAutomaticRedirections, Is.EqualTo(5));
            Assert.That(config.DefaultProxyCredentials, Is.SameAs(proxyCredentials));
            Assert.That(config.Credentials, Is.SameAs(credentials));
            Assert.That(config.CookieContainer, Is.SameAs(cookies));
            Assert.That(config.CheckCertificateRevocationList, Is.True);
            Assert.That(config.AutomaticDecompression, Is.EqualTo(DecompressionMethods.GZip));
            Assert.That(config.AllowAutoRedirect, Is.False);
            Assert.That(config.UseDefaultCredentials, Is.True);
            Assert.That(config.UseProxy, Is.True);
        }

        /// <summary>Checks that TLS 1.3 wins over TLS 1.2 and other protocol sets use libcurl's default.</summary>
        [Test]
        public void MapSslProtocols_PicksHighestSupportedMinimum()
        {
            Assert.That(Map<long>("MapSslProtocols", SslProtocols.Tls12 | SslProtocols.Tls13), Is.EqualTo(7));
            Assert.That(Map<long>("MapSslProtocols", SslProtocols.Tls13), Is.EqualTo(7));
            Assert.That(Map<long>("MapSslProtocols", SslProtocols.Tls12), Is.EqualTo(6));
            Assert.That(Map<long>("MapSslProtocols", SslProtocols.None), Is.EqualTo(0));
        }

        /// <summary>Checks the accepted encodings requested for gzip, and gzip with deflate.</summary>
        [Test]
        public void GetEncodingString_ListsGzipAndDeflate()
        {
            Assert.That(Map<string>("GetEncodingString", DecompressionMethods.GZip), Is.EqualTo("gzip"));
            Assert.That(Map<string>("GetEncodingString", DecompressionMethods.GZip | DecompressionMethods.Deflate), Is.EqualTo("gzip,deflate"));
        }

        /// <summary>Checks that deflate alone produces a well-formed encoding list.</summary>
        [Test]
        public void GetEncodingString_DeflateOnlyHasNoLeadingComma()
        {
            Assert.That(Map<string>("GetEncodingString", DecompressionMethods.Deflate), Is.EqualTo("deflate"));
        }

        /// <summary>Checks that revocation checks are disabled unless requested, on top of the platform CA bits.</summary>
        [Test]
        public void GetSslOptions_CombinesPlatformAndRevocationBits()
        {
            var platform = CurlUtility.GetPlatformSslOptions();

            Assert.That(new CurlHttpConfig().GetSslOptions(), Is.EqualTo(platform | CurlSslOption.NoRevoke));
            Assert.That(new CurlHttpConfig { CheckCertificateRevocationList = true }.GetSslOptions(), Is.EqualTo(platform));
        }

        /// <summary>Checks that the Windows Editor trusts the native certificate store.</summary>
        [Test]
        public void GetPlatformSslOptions_UsesNativeCaOnWindows()
        {
            Assert.That(CurlUtility.GetPlatformSslOptions(), Is.EqualTo(CurlSslOption.NativeCa));
        }

        /// <summary>Checks that a success code produces no exception.</summary>
        [Test]
        public void GetEasyException_IgnoresSuccess()
        {
            Assert.That(CurlUtility.GetEasyException(IntPtr.Zero, CurlCode.Ok), Is.Null);
            Assert.DoesNotThrow(() => CurlUtility.EnsureSuccess(IntPtr.Zero, CurlCode.Ok));
        }

        /// <summary>Checks that an error code without an easy handle produces a coded exception with libcurl's description.</summary>
        [Test]
        public void GetEasyException_DescribesErrorCode()
        {
            var exception = CurlUtility.GetEasyException(IntPtr.Zero, CurlCode.CouldNotConnect)!;
            var thrown = Assert.Throws<CurlException>(() => CurlUtility.EnsureSuccess(IntPtr.Zero, CurlCode.UrlMalformed));

            Assert.That(exception.ErrorCode, Is.EqualTo(CurlCode.CouldNotConnect));
            Assert.That(exception.Message, Does.StartWith("Curl Error 7 (CouldNotConnect): "));
            Assert.That(exception.Message, Does.Not.Contain("OS Error"));
            Assert.That(thrown.ErrorCode, Is.EqualTo(CurlCode.UrlMalformed));
        }

        /// <summary>Checks that an easy handle without an OS error adds no OS details.</summary>
        [Test]
        public void GetEasyException_ReadsOsErrorFromHandle()
        {
            using var easy = new MajdataPlay.Net.Curl.Core.CurlEasy();

            var exception = CurlUtility.GetEasyException(easy.Handle, CurlCode.CouldNotResolveHost)!;

            Assert.That(exception.ErrorCode, Is.EqualTo(CurlCode.CouldNotResolveHost));
            Assert.That(exception.Message, Does.Not.Contain("OS Error"));
        }

        /// <summary>Checks that the message constructors keep the error code, message and inner exception.</summary>
        [Test]
        public void CurlException_MessageConstructorsKeepCode()
        {
            var inner = new IOException();

            var withMessage = new CurlException(CurlCode.OperationTimedOut, "timeout");
            var withInner = new CurlException(CurlCode.WriteError, "send", inner);

            Assert.That(withMessage.ErrorCode, Is.EqualTo(CurlCode.OperationTimedOut));
            Assert.That(withMessage.Message, Is.EqualTo("timeout"));
            Assert.That(withMessage.InnerException, Is.Null);
            Assert.That(withInner.ErrorCode, Is.EqualTo(CurlCode.WriteError));
            Assert.That(withInner.InnerException, Is.SameAs(inner));
        }

        /// <summary>Checks that the code-only constructor describes the code in its message.</summary>
        [Test]
        public void CurlException_CodeConstructorDescribesCode()
        {
            var exception = new CurlException(CurlCode.CouldNotConnect);

            Assert.That(exception.Message, Is.EqualTo("Curl Error 7: CouldNotConnect"));
        }

        /// <summary>Checks that the code-only constructor exposes the error code.</summary>
        [Test]
        public void CurlException_CodeConstructorKeepsCode()
        {
            var exception = new CurlException(CurlCode.CouldNotConnect);

            Assert.That(exception.ErrorCode, Is.EqualTo(CurlCode.CouldNotConnect));
        }

        /// <summary>Checks the magic callback return values libcurl expects.</summary>
        [Test]
        public void CallbackReturns_MatchLibcurlConstants()
        {
            Assert.That(CurlCallbackReturn.Read.Abort, Is.EqualTo((UIntPtr)0x10000000u));
            Assert.That(CurlCallbackReturn.Read.Pause, Is.EqualTo((UIntPtr)0x10000001u));
            Assert.That(CurlCallbackReturn.Write.Pause, Is.EqualTo((UIntPtr)0x10000001u));
            Assert.That(CurlCallbackReturn.Write.Error, Is.EqualTo((UIntPtr)0xFFFFFFFFu));
        }

        /// <summary>Checks that the runtime initializer places the Android CA bundle below the persistent data path.</summary>
        [Test]
        public void RuntimeInitializer_ComputesCaBundlePath()
        {
            var previous = PrivateMember.GetStaticField<string>(typeof(CurlUtility), "_androidCAPath");
            try
            {
                PrivateMember.InvokeStatic(typeof(CurlUtility), "SBUnityNMSL");

                var path = PrivateMember.GetStaticField<string>(typeof(CurlUtility), "_androidCAPath");
                Assert.That(path, Is.EqualTo(Path.Combine(Application.persistentDataPath, "Runtime", "Networking", "ca.pem")));
            }
            finally
            {
                PrivateMember.SetStaticField(typeof(CurlUtility), "_androidCAPath", previous);
            }
        }
    }
}
