# Recorder lifecycle validation

Requires .NET 9. From the repository root:

```powershell
dotnet run --project Tools/Tests/RecorderLifecycleValidation/RecorderLifecycleValidation.csproj -c Release
```

This executable links the complete production `FFmpegRecorder`, `IRecorder`, and
`IAudioRecorder` sources. Narrow substitutes provide a controllable camera
capturer, audio source, muxer, and in-memory storage. The production recorder
starts its real background PCM drain task; no Unity, BASS, GPU, or FFmpeg native
library is loaded.

Cases cover successful output and source cleanup, audio read/write failures,
waiting for video finalization after another source fails, mux and output move
failures, recoverable source retention, audio/video stop failures, initial path
creation and capturer start failures, and asynchronous disposal.

Artifacts stay under ignored `Temp/RecorderLifecycleValidation/`. Passing these
tests verifies managed lifecycle and file ownership decisions only. It does not
validate actual media timestamps, codecs, Unity rendering, native storage, or
device capture. The game menu still uses OBS; this harness does not enable the
FFmpeg recorder there.
