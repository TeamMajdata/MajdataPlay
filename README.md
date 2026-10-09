# MajdataPlay

![license GPL-3.0](https://img.shields.io/badge/license-GPL--3.0-blue)
![GitHub Release](https://img.shields.io/github/v/release/LingFeng-bbben/MajdataPlay)
![Discord](https://badgen.net/discord/online-members/AcWgZN7j6K)
![Unity](https://img.shields.io/badge/Unity-6000.3.17f1-7e7e7e)

> [!NOTE]
> This software has no affair with the `big S four letter` company, please support the arcade whenever you can.

A Simai Player.

This project is based on [@LeZi9916](https://github.com/LeZi9916) 's DJAuto branch for [MajdataView](https://github.com/LingFeng-bbben/MajdataView).

Simai is a maimai chart discription language developed by [Celeca](https://twitter.com/formiku39854)

## Supported platforms

- Windows
- [Linux](https://github.com/LingFeng-bbben/MajdataPlay/wiki/%E5%B9%B3%E5%8F%B0%E7%9B%B8%E5%85%B3#linux) (Partially Supported)
- macOS
- Android
- iOS

> [!Note]
> On macOS, only the DAO device is tested and fully supported. Other devices are untested.

## Install

# !DOWNLOAD NOW

## !DOWNLOAD NOW

### !DOWNLOAD NOW

#### !DOWNLOAD NOW

##### !DOWNLOAD NOW

[<img src="Doc/googleplay.svg" height="60">](https://play.google.com/store/apps/details?id=net.majdata.majdataplay) [<img src="Doc/appstore.svg" height="60">](https://testflight.apple.com/join/PwxCNk5n)

## Getting Started

> [!WARNING]
> This repository contains a Unity project, not a standard C#/.NET project.
>
> Do not open or build it using dotnet, Visual Studio solution files, or other .NET build tools.
>
> The project must be opened using the Unity Editor.

### Requirements

- Unity Editor (version specified in ProjectSettings/ProjectVersion.txt), with Android Build Support (SDK API 36 and OpenJDK) for Java-wrapper generation, including desktop Editor imports

- .NET 9 SDK for the independent Android Java source-generator build tool (not for building the Unity project)

- git

- Unity Hub

### Clone the Repository

Clone the project using Git:

```bash
git clone https://github.com/LingFeng-bbben/MajdataPlay.git MajdataPlay
cd MajdataPlay
```

### Initialize Submodules

This project uses Git submodules. After cloning, run:

```bash
git submodule update --init --recursive
```

If you already cloned the repository without submodules, you can also run:

```bash
git submodule sync
git submodule update --init --recursive
```

### Install the Required Unity Version

This project must be opened with the Unity version specified in:

```text
ProjectSettings/ProjectVersion.txt
```

Example:

```text
m_EditorVersion: 6000.3.17f1
```

Install this version using Unity Hub.

### Install the Android Java Source Generator

The storage backend uses generated C# wrappers during Unity script compilation.
Build and install the ignored Roslyn analyzer before the first Unity import:

```powershell
./Tools/AndroidJavaGenerator/build.ps1 -Install
```

The script builds only the explicitly provided independent tool under
`Tools/Tests/AndroidJavaGeneratorBuild/`, not a Unity-generated project.
Pass `-UnityPath` for a non-default Editor installation. See
`Tools/AndroidJavaGenerator/README.md` for SDK/JDK discovery, restore prerequisites,
and other-host setup. Fresh checkouts and CI need the same analyzer and Java
extraction prerequisites; generated wrappers and analyzer binaries are not
checked into the repository.

### Open the Project in Unity Hub

1. Open Unity Hub

2. Click Add Project

3. Select the cloned project folder

4. Ensure the correct Unity version is selected

5. Open the project

## Guidance

[Docs](https://docs.majdata.net) | [WIKI](https://github.com/LingFeng-bbben/MajdataPlay/wiki)

## Releases

[Stable](https://github.com/TeamMajdata/MajdataPlay_Build) | [Nightly](https://github.com/LingFeng-bbben/MajdataPlay/releases/tag/nightly)

## Reporting Problems

Feel free if you wanna participate in coding or testing!!

Please report problems to [issues page](https://github.com/LingFeng-bbben/MajdataPlay/issues).

The log files should be in `Logs/`

## References

- [istareatscreens/MychIO](https://github.com/istareatscreens/MychIO)
  - Special thanks to istareatscreens for the early I/O solution for this project!
- [Cysharp/UniTask](https://github.com/Cysharp/UniTask)
- [Cysharp/ZString](https://github.com/Cysharp/ZString)
- [IntergatedCircuits/HidSharp](https://github.com/IntergatedCircuits/HidSharp)
- [ManagedBass/ManagedBass](https://github.com/ManagedBass/ManagedBass)
- [un4seen/bass](https://www.un4seen.com/)
- [mono/SkiaSharp](https://github.com/mono/SkiaSharp)
- [ammariqais/SkiaForUnity](https://github.com/ammariqais/SkiaForUnity)
- [videolan/vlc-unity](https://github.com/videolan/vlc-unity)
