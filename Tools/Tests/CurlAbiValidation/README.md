# Curl version-info ABI validation

`LibCurl.GetVersionInfo` must match the native `curl_version_info_data` layout.
On Android ARMv7, C `long` is 4 bytes: `protocols` is at offset 32.
The previous fixed `Int64` declaration moved it to offset 36, which reads `ares`
instead. The bundled curl returns null there, so dereferencing it crashes.

The native layout probe reads the old pointer value without dereferencing it.
The Unity smoke test compiles the product P/Invoke sources with ARMv7 IL2CPP,
checks the marshalled offset, and reads the real library's version and HTTP/HTTPS
protocols 101 times. It uses a separate application ID and makes no requests.

Build from the repository root (Unity 6000.3.17f1 with Android support):

```powershell
& Tools/Tests/CurlAbiValidation/build.ps1
adb install -r Tools/Tests/CurlAbiValidation/.work/P/Output/CurlAbi.apk
adb shell am start -W -n net.majdata.curlabi.validation/com.unity3d.player.UnityPlayerGameActivity
adb shell cat /sdcard/Android/data/net.majdata.curlabi.validation/files/curl-abi.txt
```

Wait for startup before reading the report; expect `PASS: IL2CPP pointer=32,
Protocols offset=32`. For a repeat run, remove only the test application's
previous `curl-abi.txt`, force-stop that test app, and launch it again.

Native probe (replace the NDK path if necessary):

```powershell
$ndkBin = 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data/PlaybackEngines/AndroidPlayer/NDK/toolchains/llvm/prebuilt/windows-x86_64/bin'
& "$ndkBin/clang.exe" --target=armv7a-linux-androideabi25 -fPIE -pie Tools/Tests/CurlAbiValidation/native-layout.c -ldl -o Tools/Tests/CurlAbiValidation/.work/native-layout
adb shell mkdir -p /data/local/tmp/majdata-curl-abi-20261004
adb push Tools/Tests/CurlAbiValidation/.work/native-layout /data/local/tmp/majdata-curl-abi-20261004/native-layout
adb push Assets/Plugins/MajdataPlay/Net/Curl/Plugins/Android/armeabi-v7a/libcurl.so /data/local/tmp/majdata-curl-abi-20261004/libcurl.so
adb shell chmod 755 /data/local/tmp/majdata-curl-abi-20261004/native-layout
adb shell /data/local/tmp/majdata-curl-abi-20261004/native-layout /data/local/tmp/majdata-curl-abi-20261004/libcurl.so
```

Expected on ARMv7: `C long=4; correct Protocols offset=32; old offset=36`,
`old Protocols value=0x0`, and valid `http https` entries.
The probe also accepts the installed game's absolute `libcurl.so` path.

## Verified on 2026-10-04

- Xiaomi Mi MIX 2S, Android 15: native probe against both the repository library
  and the installed game's library confirmed offset 32 versus the old offset 36,
  with a null value at the old offset.
- Unity 6000.3.17f1, ARMv7 IL2CPP Development player: passed 101 reads, reporting
  curl `8.21.0-DEV`, host `armv7-none-linux-androideabi22`, OpenSSL `3.6.2`,
  zlib `1.3.0.1-motley`, and protocols `http,https`.
- Windows x64 .NET 9 harness using the same product P/Invoke sources and bundled
  DLL: passed initialization/version parsing, native-long size 4 and protocol
  pointer offset 64.

The complete game was not rebuilt or tested. The original crash's IL2CPP symbols
have a mismatched Build ID, so its raw addresses have not been symbolicated.

Reference: https://curl.se/libcurl/c/curl_version_info.html
