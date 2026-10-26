# BASS HTTP stream validation

Streams WAV audio from loopback HTTP servers into the bundled Windows x64
`bass.dll` through `BassHttpFileSource`, the class that lets
`BassAudioSample.CreateFromUri` and `BassSimpleAudioSample.CreateFromUri`
download with the project's `HttpClient` instead of BASS's own network code.

The harness links the production `BassHttpFileSource`, the ManagedBass core
sources, `CurlHttpMessageHandler` with the curl sources and the bundled
`libcurl.dll`, `PooledArray`, and `UnsafeHelper`. Unity, the logger, and
`Pool<T>` are test doubles. The client is configured like
`MajEnv.SharedHttpClient` (gzip/deflate decompression).

Run from the repository root with .NET SDK 9 on Windows x64:

```powershell
dotnet run -c Release --project Tools/Tests/Modules/BassHttpStreamValidation/BassHttpStreamValidation.csproj
```

If the default SDK is newer and restore fails with `NU1101`, run from a directory
containing `global.json` that selects an installed 9.0 SDK. Pass the project by
absolute path. Build output goes to `Temp/BassHttpStreamValidation`. Pass check
names as arguments to run only those checks.

After every check, the harness also fails if a source is still registered for
BASS callbacks or if an error was logged. The checks cover:

- Decoding: a body with a Content-Length, a chunked body, and a gzip body all
  decode to the served PCM byte for byte. The gzip check makes sure the
  encoded Content-Length is not reported to BASS as the file length.
  BASS closes the source when the download reaches the end of the body.
- Failures: an error status throws `HttpRequestException`. A body BASS cannot
  identify throws `BassException(FileFormat)`. A connection that drops before
  BASS identifies the format throws `IOException` and logs a warning.
- Cancellation: a token that is already cancelled sends no request.
  Cancelling while waiting for headers, or while BASS blocks in the read
  callback during creation, throws `OperationCanceledException` and closes the
  connection. Cancelling after creation leaves the stream intact.
- Lifetime: `StreamFree` on a stream whose download thread is blocked on a
  stalled server returns promptly, closes the source, and closes the
  connection. Concurrent create/free cycles while downloads are running leak
  no sources.

The stall and concurrency checks set `Bass.NetPreBuffer` to 0 while they create
streams, so that creation does not wait for most of the body.

BASS's "no sound" device (0) is used, so only decoding channels
(`BassAudioSample`'s path) are created. The non-decoding stream used by
`BassSimpleAudioSample` and playback through the game's mixers are not covered.
This is a CoreCLR test. It does not cover Mono/IL2CPP reverse P/Invoke, other
platforms' BASS or libcurl builds, HTTPS, proxies, or the Unity scene lifecycle.
