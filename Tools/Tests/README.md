# Tests

Test and validation suites for MajdataPlay, kept outside the main Unity project.

| Directory | Contents |
| --- | --- |
| [`Modules/`](Modules/README.md) | Per-module standalone validation programs (.NET 9 harnesses that link production sources), isolated Unity validation scripts, and their documentation. |
| [`MajdataPlay.Tests/`](MajdataPlay.Tests/README.md) | The Unity Test Framework suite for the main project and `Assets/Plugins/MajdataPlay`. |

Each suite's README lists its prerequisites, commands, and what it does not
verify. Commands are written relative to the repository root, for example:

```powershell
dotnet run --project Tools/Tests/Modules/SettingValidation/SettingValidation.csproj
```
