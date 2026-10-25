# ManagedBass validation

Checks the ManagedBass submodule (`Assets/Plugins/ManagedBass`) outside the Unity Editor: as a plain .NET library on
CoreCLR, and in an Android IL2CPP Player. The Unity Test Framework tests of the same behavior on Mono are in
[`Assets/Tests/EditMode/ThirdParty/ManagedBass`](../../../../Assets/Tests/EditMode/ThirdParty/ManagedBass).

## Why

IL2CPP only marshals a delegate to native code when it points to a static method that carries an attribute named
`MonoPInvokeCallbackAttribute`; for anything else it throws `NotSupportedException` when the delegate is passed. ManagedBass
used to pass lambdas and unmarked methods to BASS (the free sync of `ChannelReferences`, the pin release of
`CreateStream(byte[], ...)`, the one-time sync wrapper, `MediaPlayer`, `StreamFileProcedures`). Its wrappers now pass static,
marked trampolines and route each call to the caller's delegate through `CallbackContext`, so any delegate works on every
runtime. Callers of the raw P/Invokes that take delegates (BASS_FX BPM, BASSASIO and BASSWASAPI callbacks, and
`FileProcedurePointers`) still pass static, marked methods themselves on IL2CPP.

## CoreCLR (non-Unity) runner

`Libraries/` builds ManagedBass and its add-ons as separate assemblies with the names of the NuGet packages
(`ManagedBass`, `ManagedBass.Mix`, ...) for `netstandard2.0` and `net8.0`, as a project outside Unity would. This also checks
the `InternalsVisibleTo` names the add-ons need. The runner targets `net8.0` and rolls forward to newer runtimes.

Run from the repository root on Windows x64 with any .NET SDK that can build `net8.0`; restore needs nuget.org for the
.NET Standard and `System.Runtime.CompilerServices.Unsafe` packages:

```powershell
dotnet run -c Release --project Tools/Tests/Modules/ManagedBassValidation/ManagedBassValidation.csproj
```

Pass check names as arguments to run only those checks. Build output goes to `Temp/ManagedBassValidation`. Each check
also fails if a callback context outlives its channel. The checks use BASS's "no sound" device with decoding channels and
capturing lambdas:

- Stream, DSP, one-time, repeating and free syncs, user file streams (including `Extensions.StreamFileProcedures`,
  whose seeks used to be applied relative to the current position), streams from managed memory, mixer source syncs, and
  the AAC and Opus file stream wrappers: callbacks receive the caller's user value and are released with their channel.
- A free sync on a channel without other references still fires, although BASS runs it after the free sync that releases
  the channel's callbacks (BASS calls the free syncs of a channel in reverse order).
- Callbacks survive garbage collections after the caller dropped them, and channels created and freed on many threads at
  once release every callback.
- `EnumExtensions.Has` no longer reads past four-byte enums. It used to report `SyncFlags.Onetime` for nearly every sync in
  optimized CoreCLR code.

`BassMix.ChannelSetSync(..., SyncProcedureEx, ...)` is not covered: the bundled BASSmix 2.4.12 crashes in
`BASS_ChannelGetData` once a sync with its type flag `0x1000000` is set, also with a static callback passed by a plain
P/Invoke.

## Android IL2CPP Player

[`Il2Cpp/run-android.ps1`](Il2Cpp/run-android.ps1) stages an isolated Unity project (default
`%LOCALAPPDATA%\MajMBA`, kept short for Android path limits) with the production ManagedBass core and BASSmix sources,
the bundled BASS libraries of one ABI and a smoke script, and builds an Android IL2CPP Player with Low stripping, as the
game does:

```powershell
./Tools/Tests/Modules/ManagedBassValidation/Il2Cpp/run-android.ps1 -Architecture arm64
./Tools/Tests/Modules/ManagedBassValidation/Il2Cpp/run-android.ps1 -Architecture arm64 -SkipBuild -Serial <device-serial>
```

After the build it checks that IL2CPP generated a reverse P/Invoke wrapper for every native callback of ManagedBass
(`MANAGEDBASS_IL2CPP_CODEGEN_PASSED`); without a wrapper IL2CPP throws the `NotSupportedException` above. With `-Serial` it
installs and starts the Player on that device and waits for `MANAGEDBASS_IL2CPP PASSED` in logcat. The smoke script repeats
the CoreCLR checks with capturing lambdas, and as a control passes a capturing lambda to a plain P/Invoke, which must throw
`NotSupportedException` on IL2CPP. `-Architecture x86_64` builds for x86_64 emulators.

## Limitations

The CoreCLR runner covers Windows x64 BASS only and no real audio device. The Player build needs the Unity Android build
support; running it needs an unlocked device or emulator of the selected ABI. Windows, Linux, macOS and iOS IL2CPP Players
are not built (Windows IL2CPP needs the Visual Studio C++ tools). Recording (`RecordStart`), URL streams with a
`DownloadProcedure` and the iOS notification callback are only checked for their IL2CPP wrappers, not called.
