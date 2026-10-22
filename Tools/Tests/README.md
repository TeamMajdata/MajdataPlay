# Tests

Test and validation suites for MajdataPlay, kept outside the main Unity project, and the runner for the
Unity Test Framework suite that lives inside it under `Assets/Tests`.

| Path | Contents |
| --- | --- |
| [`Modules/`](Modules/README.md) | Per-module standalone validation programs (.NET 9 harnesses that link production sources), isolated Unity validation scripts, and their documentation. |
| [`Run-UnityTests.ps1`](Run-UnityTests.ps1) | Runs the Unity Test Framework suite, which lives in the main project under [`Assets/Tests`](../../Assets/Tests/README.md), in an isolated copy of the project. |

Each suite's README lists its prerequisites, commands, and what it does not
verify. Commands are written relative to the repository root, for example:

```powershell
dotnet run --project Tools/Tests/Modules/SettingValidation/SettingValidation.csproj
```
