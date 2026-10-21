# Audio recorder latency validation

Checks on the real Windows output device that `BassAudioRecorder.PlaybackDelay`
places captured audio where the device plays it. `FFmpegRecorder` delays the
recorded PCM by this value when muxing, so a wrong delay shows up directly as an
audio/video offset in recordings.

The harness links the production `BassAudioRecorder`, `BassOutputLatencyProbe`,
`IAudioRecorder`, and `AudioSampleRingBuffer` sources with the ManagedBass core,
BASSmix, BASSWASAPI, and BASSASIO sources, and copies the bundled Windows x64
`bass.dll`, `bassmix.dll`, `basswasapi.dll`, and `bassasio.dll`. It defines
`UNITY_STANDALONE_WIN` so the WASAPI and ASIO probes are compiled. Only the
logger and Unity's `MonoPInvokeCallback` marker are test doubles.

Run from the repository root with .NET SDK 9 on Windows x64 with a working
default output device:

```powershell
dotnet run -c Release --project Tools/Tests/AudioRecorderLatencyValidation/AudioRecorderLatencyValidation.csproj
```

If the default SDK is newer and restore fails with `NU1101`, run from a directory
containing `global.json` that selects an installed 9.0 SDK. Pass the project by
absolute path. Build output goes to `Temp/AudioRecorderLatencyValidation`.

Each case plays eight 10 ms bursts of an 18 kHz tone at about -50 dBFS, so the
run stays inaudible, records them with the production recorder, and prints a
per-burst table. The final line is `AUDIO_RECORDER_LATENCY_VALIDATION_PASSED` or
`AUDIO_RECORDER_LATENCY_VALIDATION_FAILED`, and the exit code is nonzero on
failure.

- BassSimple uses the game's configuration (update period 100 ms, playback
  buffer 1000 ms, device keeps running) with a 32 ms / 8 ms and the desktop
  default 64 ms / 16 ms device buffer and period. The device output stream and
  recorder are created as `AudioManager` does. Ground truth is the playback start
  that BASS reports for each burst stream through `ChannelGetPosition`, which is
  also what the game's BassSimple sync loop follows. Every delayed onset must be
  within 5 ms of it.
- WASAPI shared mirrors `AudioManager`: a no-sound BASS device, a non-stop float
  decode mixer, and an event-driven output callback that renders the mixer. The
  callback also measures the buffered latency with a second probe. There is no
  independent playback position, so the checks are that the latency is between 0
  and 200 ms and that every delayed onset follows the moment its burst joined the
  mixer by that latency, within -1 ms to one output block plus 2 ms.
- `--exclusive` adds the Windows default exclusive, asynchronous, raw WASAPI
  output (20 ms buffer, 5 ms period), falling back in the game's order, with the
  same checks. It takes the device exclusively for a few seconds, so it is
  opt-in.

The WASAPI cases also print the spread between each output callback's wall time
and the amount of audio rendered so far. A large spread means the output thread
stalled; the recorder assumes the captured audio is continuous, so a stall
shifts every later onset earlier. Captured audio is stored in preallocated
arrays because the capture DSP and the WASAPI callback are managed code that a
garbage collection would pause.

Passing this harness verifies the latency measurement and first-block timing on
this machine's device and drivers only. It does not cover ASIO (which needs an
ASIO driver), other platforms, Bluetooth or other delays the driver does not
report, display latency, Unity, or the muxed media files.
