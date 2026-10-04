using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using MajdataPlay.Net.Curl.Core.PInvoke;
using UnityEngine;

public static class CurlAbiSmoke
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Run()
    {
        var report = Path.Combine(Application.persistentDataPath, "curl-abi.txt");
        try
        {
            if (LibCurl.Init(CurlInitOption.Default) != CurlCode.Ok)
                throw new Exception("curl_global_init failed");
            try
            {
                var rawType = typeof(LibCurl).GetNestedType("CurlVersionInfoRawData", BindingFlags.NonPublic);
                var offset = Marshal.OffsetOf(rawType, "Protocols").ToInt32();
                var expected = IntPtr.Size == 4 ? 32 : 64;
                if (offset != expected)
                    throw new Exception($"Protocols offset {offset}, expected {expected}");
                var info = LibCurl.GetVersionInfo(0);
                if (string.IsNullOrEmpty(info.Version) || string.IsNullOrEmpty(info.Host)
                    || info.VersionNum == 0 || !info.Protocols.Contains("http") || !info.Protocols.Contains("https"))
                    throw new Exception("Invalid native version info or protocols");
                // Repeat parsing to catch unstable pointer reads, without making network requests.
                for (var i = 0; i < 100; i++)
                {
                    var next = LibCurl.GetVersionInfo(0);
                    if (next.Version != info.Version || !next.Protocols.SequenceEqual(info.Protocols))
                        throw new Exception("Version info changed during repeated reads");
                }
                File.WriteAllText(report, $"PASS: IL2CPP pointer={IntPtr.Size * 8}, Protocols offset={offset}, "
                    + $"curl={info.Version}, host={info.Host}, SSL={info.SslVersion}, zlib={info.LibzVersion}, "
                    + $"protocols={string.Join(",", info.Protocols)}; 101 reads");
            }
            finally { LibCurl.CleanUp(); }
        }
        catch (Exception error) { File.WriteAllText(report, "FAIL: " + error); }
    }
}

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
