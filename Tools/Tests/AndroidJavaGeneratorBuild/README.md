# Android Java generator build

This is an independent .NET 9 SDK build project for the Roslyn analyzer, **not**
a Unity-generated project. Never build the root Unity `.csproj` or solution.

```powershell
./Tools/AndroidJavaGenerator/build.ps1
./Tools/AndroidJavaGenerator/build.ps1 -Install
```

The analyzer targets .NET Standard 2.0 and Roslyn 4.3.1. Outputs go to the ignored
`Temp/AndroidJavaGeneratorBuild/` directory. Installation copies only the analyzer
DLL into its dedicated, ignored Unity analyzer path; the `.meta` installed from the tracked template assigns
`RoslynAnalyzer` and disables all runtime plugin platforms. Do not copy Roslyn's
own DLLs into Assets or reference the analyzer in an `.asmdef`.

See `Tools/AndroidJavaGenerator/README.md` for binding configuration, SDK/JDK
resolution, documentation provenance, and generation diagnostics. Validation is
separate from this build; use `Tools/Tests/AndroidJavaGeneratorValidation`.
