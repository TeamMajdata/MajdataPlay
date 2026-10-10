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

        public static void ApplySystemCA(CurlEasy curlEasy)
        {
            var returnCode = default(CurlCode?);
#if UNITY_ANDROID
            if(FileSystem.File.Open(_androidCAPath).Exists)
            {
                returnCode = LibCurl.Easy.SetOption(curlEasy.Handle, CurlOption.CaInfo, _androidCAPath);
                if (returnCode is CurlCode code && code != CurlCode.Ok)
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
#elif UNITY_STANDALONE_WIN || UNITY_WSA
            returnCode = LibCurl.Easy.SetOption(curlEasy.Handle, CurlOption.SslOptions, CurlSslOption.NativeCa);
            if (returnCode is CurlCode code && code != CurlCode.Ok)
            {
                MajDebug.LogWarning(
                    $"[libcurl]Failed to enable native CA certificate store. " +
                    $"Option: CURLSSLOPT_NATIVE_CA, " +
                    $"Error: {code} ({GetCurlErrorMessage(code)})");
            }
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
        public static CurlException? GetEasyException(IntPtr easyHandle, CurlCode code)
        {
            if (code == CurlCode.Ok)
            {
                return default;
            }

            var curlMsg = GetCurlErrorMessage(code);
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
