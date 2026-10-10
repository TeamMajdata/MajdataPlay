using MajdataPlay.Diagnostics;
using MajdataPlay.Net.Curl.Core;
using MajdataPlay.Net.Curl.Core.PInvoke;
using System;
#nullable enable
namespace MajdataPlay.Net.Curl.Lifecycle
{
    /// <summary>
    /// Owns the process-wide libcurl state created by <c>curl_global_init</c> and shared by every easy
    /// and multi handle. The state is initialized on first use and released exactly once, while the
    /// process is shutting down.
    /// </summary>
    internal static class LibCurlLifecycle
    {
        /// <summary>
        /// Number of live handles that depend on the initialized libcurl global state.
        /// </summary>
        static int s_refCount;

        /// <summary>
        /// Whether <c>curl_global_init</c> has succeeded and <c>curl_global_cleanup</c> has not run yet.
        /// </summary>
        static bool s_isGlobalInited;

        /// <summary>
        /// Serializes global initialization and cleanup.
        /// </summary>
        static readonly object s_initLock = new object();

        static LibCurlLifecycle()
        {
            // curl_global_cleanup() must only run once every curl user is gone and while the process is
            // effectively single-threaded. A GC-triggered cleanup cannot honour that contract: the
            // collector may fire at any moment, including between two requests, and on OpenSSL 3.x the
            // cleanup also runs the irreversible OPENSSL_cleanup(), after which no TLS handshake in this
            // process can succeed. Re-running curl_global_init afterwards is not a safe recovery, so the
            // global state is released once, at shutdown, instead of on every collection.
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }

        /// <summary>
        /// Registers one more user of the libcurl global state, initializing that state on first use.
        /// Every successful call must be paired with exactly one <see cref="Release"/> call.
        /// </summary>
        /// <exception cref="CurlException">
        /// Thrown when <c>curl_global_init</c> reports a code other than <see cref="CurlCode.Ok"/>. In
        /// that case the reference count is left untouched.
        /// </exception>
        public static void Retain()
        {
            lock (s_initLock)
            {
                if (!s_isGlobalInited)
                {
                    var returnCode = LibCurl.Init(CurlInitOption.Default);
                    if (returnCode != CurlCode.Ok)
                    {
                        throw new CurlException(returnCode);
                    }
                    var info = LibCurl.GetVersionInfo(0);
                    MajDebug.LogInfo($"""
                                    [libcurl]version info:
                                    Age: {info.Age}
                                    Version: {info.Version}
                                    VersionNum: 0x{info.VersionNum:X6} ({info.VersionNum})
                                    Host: {info.Host}
                                    Features: 0x{info.Features:X8} ({info.Features})
                                    SslVersion: {info.SslVersion}
                                    SslVersionNum: {info.SslVersionNum}
                                    LibzVersion: {info.LibzVersion}
                                    Protocols: {(info.Protocols == null ? "<null>" : string.Join(", ", info.Protocols))}
                                    """);
                    s_isGlobalInited = true;
                }

                s_refCount++;
            }
        }

        /// <summary>
        /// Releases one user of the libcurl global state. The state itself stays initialized until the
        /// process exits, so a later <see cref="Retain"/> call never has to re-initialize it.
        /// </summary>
        public static void Release()
        {
            lock (s_initLock)
            {
                s_refCount--;
            }
        }

        /// <summary>
        /// Releases the libcurl global state once, while the process is shutting down.
        /// </summary>
        /// <param name="sender">The source of the event; unused.</param>
        /// <param name="e">The event data; unused.</param>
        static void OnProcessExit(object? sender, EventArgs e)
        {
            lock (s_initLock)
            {
                if (!s_isGlobalInited)
                {
                    return;
                }

                try
                {
                    LibCurl.CleanUp();
                }
                catch (Exception)
                {
                    // Shutdown is already in progress, so the managed logging facility may itself be torn
                    // down; a failure here is deliberately swallowed. The operating system reclaims the
                    // state together with the process.
                }
                finally
                {
                    s_isGlobalInited = false;
                }
            }
        }
    }
}
