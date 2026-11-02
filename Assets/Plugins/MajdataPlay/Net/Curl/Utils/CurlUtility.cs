using MajdataPlay.IO.Storage;
using MajdataPlay.Diagnostics;
using MajdataPlay.Net.Curl.Core;
using MajdataPlay.Net.Curl.Core.PInvoke;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Net.Curl.Utils
{
    internal static class CurlUtility
    {
        static string _androidCAPath = string.Empty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void SBUnityNMSL() // UNITY NMSL
        {
            _androidCAPath = Path.Combine(Application.persistentDataPath, "Runtime", "Networking", "ca.pem");
        }

        /// <summary>
        /// Points <paramref name="curlEasy"/> at the platform's CA certificate bundle file, where the platform needs one.
        /// The SSL option bits a platform needs come from <see cref="GetPlatformSslOptions"/> instead.
        /// </summary>
        /// <param name="curlEasy">The easy handle to configure.</param>
        public static void ApplySystemCA(CurlEasy curlEasy)
        {
#if UNITY_ANDROID
            var returnCode = default(CurlCode?);
            if(FileSystem.File.Open(_androidCAPath).Exists)
            {
                var code = LibCurl.Easy.SetOption(curlEasy.Handle, CurlOption.CaInfo, _androidCAPath);
                if (code != CurlCode.Ok)
                {
                    MajDebug.LogWarning(
                        $"[libcurl]Failed to set CA certificate bundle. " +
                        $"Path: \"{_androidCAPath}\", " +
                        $"Error: {code} ({GetCurlErrorMessage(code)})");
                }
            }
            else
            {
                MajDebug.LogWarning($"[libcurl]CA certificate bundle not found");
            }
#endif

        }

        /// <summary>
        /// Gets the <c>CURLOPT_SSL_OPTIONS</c> bits this platform needs to find trusted root certificates.
        /// </summary>
        /// <remarks>
        /// <c>CURLOPT_SSL_OPTIONS</c> is a single bitmask, so these bits must be combined with every other SSL option
        /// in one call; see <see cref="CurlHttpConfig.GetSslOptions"/>.
        /// </remarks>
        /// <returns><see cref="CurlSslOption.NativeCa"/> on Windows; otherwise <see cref="CurlSslOption.None"/>.</returns>
        public static CurlSslOption GetPlatformSslOptions()
        {
#if UNITY_STANDALONE_WIN || UNITY_WSA
            // The bundled Windows libcurl uses OpenSSL without a CA bundle. That build already falls back to the Windows
            // ROOT and CA stores, but the flag keeps them trusted if a later build ships a bundle or changes that default.
            return CurlSslOption.NativeCa;
#else
            return CurlSslOption.None;
#endif
        }
        public static void EnsureSuccess(IntPtr easyHandle, CurlCode code)
        {
            var ex = GetEasyException(easyHandle, code);
            if (ex is not null)
            {
                throw ex;
            }
        }
        /// <summary>
        /// Describes a failed libcurl call as an exception.
        /// </summary>
        /// <param name="easyHandle">The easy handle that failed, read for the OS error; <see cref="IntPtr.Zero"/> when there is none.</param>
        /// <param name="code">The result of the call.</param>
        /// <param name="detail">
        /// What libcurl wrote into the handle's error buffer, such as which timeout expired; when it is null or empty,
        /// the generic description of <paramref name="code"/> is used instead.
        /// </param>
        /// <returns>The exception, or <see langword="null"/> for <see cref="CurlCode.Ok"/>.</returns>
        public static CurlException? GetEasyException(IntPtr easyHandle, CurlCode code, string? detail = null)
        {
            if (code == CurlCode.Ok)
            {
                return default;
            }

            var curlMsg = string.IsNullOrEmpty(detail) ? GetCurlErrorMessage(code) : detail;
            var osErrno = 0L;
            var sysMsg = string.Empty;

            if (easyHandle != IntPtr.Zero && LibCurl.Easy.GetInfo(easyHandle, CurlInfo.OsErrno, out osErrno) == CurlCode.Ok)
            {
                if (osErrno != 0)
                {
                    sysMsg = new Win32Exception((int)osErrno).Message;
                }
            }
            var exMsg = $"Curl Error {(int)code} ({code}): {curlMsg}";
            if (osErrno != 0)
            {
                exMsg += $"\n  └─ OS Error {osErrno}: {sysMsg}";
            }

            return new CurlException(code, exMsg);
        }
        /// <summary>
        /// Converts the error that ended a transfer into the exception reported to the caller, following the
        /// <see cref="System.Net.Http.HttpClient"/> convention for timeouts.
        /// </summary>
        /// <param name="curlException">The error libcurl reported for the transfer.</param>
        /// <returns>
        /// For <see cref="CurlCode.OperationTimedOut"/>, a <see cref="TaskCanceledException"/> whose inner exception is a
        /// <see cref="TimeoutException"/> wrapping <paramref name="curlException"/>; its token is not cancelled, so callers
        /// can tell it from their own cancellation. Otherwise <paramref name="curlException"/> itself.
        /// </returns>
        public static Exception GetTransferException(CurlException curlException)
        {
            if (curlException.ErrorCode != CurlCode.OperationTimedOut)
            {
                return curlException;
            }
            var timeout = new TimeoutException(curlException.Message, curlException);
            return new TaskCanceledException(curlException.Message, timeout);
        }

        static string GetCurlErrorMessage(CurlCode code)
        {
            var errMsg = LibCurl.Easy.GetErrorMessage(code);

            if(string.IsNullOrEmpty(errMsg))
            {
                return "Unknown curl error";
            }
            else
            {
                return errMsg!;
            }
        }
    }
}
