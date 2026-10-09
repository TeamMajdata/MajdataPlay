# Android Java wrappers

The `JavaClassAttribute` and `JavaApiConfigurationAttribute` request generated
`JavaObject`-derived wrappers for Java classes and interfaces. The generator
is authored outside Assets and installed as an analyzer, not as a runtime plugin.

See `Tools/AndroidJavaGenerator/README.md` at the repository root for installation,
SDK/JDK configuration, Java inputs, member mapping, documentation provenance,
ownership, threading, diagnostics, and managed/Unity/device validation limits.

```powershell
./Tools/AndroidJavaGenerator/build.ps1 -Install
```

Only JNI invocation is Android-player-specific; the attributes and generated
wrapper declarations remain available in the Editor. Existing manually maintained
`Runtime/Activity.cs`, `Intent.cs`, and `KeyEvent.cs` constants are not rewritten
or silently migrated by installation. Declare new partial wrapper types with
unique C# names instead.
