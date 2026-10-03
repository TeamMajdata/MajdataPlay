# Pinned managed binding

`LibVLCSharp.upstream.dll` is the unchanged binding previously shipped under
`Assets/Plugins/VLCUnity/Runtime/Plugins/Windows/x86_64/`. Its upstream version is
`4.0.0+a296e6f14b326bde2e7796439ea883b6c6040fb9` and SHA-256 is
`770E8773FDFE738B3485AE0F323EDA3C05BCFFA55EC2BFDCC4E37F9E5D591101`.
Keep this input outside Assets so Unity imports only the generated variants.

Run `Tools/VLCUnity/Prepare-Bindings.ps1` to regenerate both variants using the
Unity editor's bundled Cecil library. Pass `-CecilPath` for other installations.
The script verifies the input hash and writes only changed output bytes.
Its output is byte-identical under Windows PowerShell 5.1 and PowerShell 7 when
using the same Cecil version. `CanonicalMetadata.cs` orders equal-owner rows in
the CustomAttribute and MethodSemantics tables by their complete bytes; Cecil's
owner-only sort otherwise produces different row orders across .NET runtimes.
Neither table has incoming metadata indexes, and method/property/event identities
remain unchanged. Validation checks original framework references and accessor
associations as well as the transformed native imports.

The shared variant retains the upstream Windows loader. On other hosts,
`Core.InitializeUnity` returns before the obsolete hardcoded macOS paths; the
project's `VlcRuntime` sets module paths and validates the native bridge before
LibVLC construction. Unity resolves the platform's imported native libraries.
The x64 process predicate compares `RuntimeInformation.ProcessArchitecture`
with `Architecture.X64`; pointer size alone would misidentify ARM64 and select
the wrong native variadic argument layout in Linux/Android log callbacks.
It reuses the original ARM64 predicate's `netstandard` method reference and never
imports a reflected type from the PowerShell host runtime.

The iOS variant changes `libvlc`, `VLCUnityPlugin`, `libc`, and `libSystem` native
imports to `__Internal`, preserving their entry points and marshaling. It skips
the desktop loader and Windows error-mode setup. iOS links engine and bridge
archives into UnityFramework, with static module registration before `libvlc_new`.
The variants have mutually exclusive Unity plugin import settings.
The iOS variant also fixes the platform predicates to Apple (Mac true, Linux and
Windows false), so native logging and file helpers cannot select a desktop or
Linux ABI when iOS reports a different runtime OS name.

This is a small, explicit transformation of the existing ABI, not an upgrade to
the current LibVLCSharp or LibVLC 4 nightly. The native engine must remain at the
matching `d94fd0473f` revision. All public managed API and callback signatures are
unchanged.

LibVLCSharp copyright VideoLAN and contributors; LGPL-2.1-or-later.
[Matching source](https://github.com/videolan/libvlcsharp/tree/a296e6f14b326bde2e7796439ea883b6c6040fb9)
and [upstream license](https://github.com/videolan/libvlcsharp/blob/master/LICENSE).
