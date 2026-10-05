# AGENTS.md

本文件适用于仓库根目录及其所有子目录。若更深层目录存在自己的 `AGENTS.md`，以更深层文件为准。

## 项目概览

MajdataPlay 是一个跨平台 Simai 谱面播放器，也是一个 Unity 项目。

当前固定使用 **Unity 6000.3.17f1**（准确版本见 `ProjectSettings/ProjectVersion.txt`），目标平台包括 Windows（x86/64）、Linux、macOS、Android（ARMv7和ARM64） 和 iOS，使用mono和il2cpp后端。

## 必须遵守的规则

- 使用仓库指定的 Unity Editor 版本打开、导入、编译和构建主项目。
- 根目录的 `.sln`、`.slnx` 和 `.csproj` 是 Unity/IDE 生成物，不得手工维护，也不得用 `dotnet build`、Visual Studio 或普通 MSBuild 将它们当成标准 .NET 项目构建。
- `Tools/Tests/**` 下明确提供的独立 `.csproj` 是例外；它们按各自 README 使用 .NET 9 运行。
- 克隆后先执行 `git submodule update --init --recursive`。不要在未初始化的子模块上判断缺失类型或自行复制依赖。
- 开始修改前运行 `git status --short`。工作区中的既有修改属于用户；不得覆盖、回退、格式化或清理与任务无关的改动。
- 不要编辑或提交 Unity/IDE 生成目录和本地数据：`Library/`、`Temp/`、`obj/`、`Build/`、`Logs/`、`UserSettings/`、`.vs/`、`.idea/`、`Cache/`、`RecordOutputs/`，以及根目录生成的解决方案和项目文件。
- `settings.json`、数据库、谱面、皮肤、日志、密钥和打包产物是本地运行数据或敏感数据，不得纳入提交。
- `Assets/Plugins/HidSharp`、`Assets/Plugins/ManagedBass`、`Assets/Plugins/UniTask`、`Assets/Plugins/MajSimai`、`Assets/Plugins/MajRadar` 和 `ThirdParty/FFmpeg.AutoGen` 是 Git 子模块。除非任务明确要求更新对应依赖，否则不要修改子模块内容或指针。
- 修改 Unity 资产时必须保留对应 `.meta` 文件。新增、移动或删除资产时让资产与 `.meta` 成对变化，不要重建现有 GUID。

## 仓库结构

- `Assets/Scripts/Global/`：全局服务、生命周期和管理器。
- `Assets/Scripts/Scenes/`：按场景划分的 UI 与业务逻辑；主流程从 `Init`、`Title`、`Login`、`List` 到 `Game`、`Result` 等场景。
- `Assets/Scripts/IO/`：输入设备、输出设备及其管理层。
- `Assets/Scripts/Misc/`：设置、数据库、网络、录制、UI、扩展等共享游戏代码。
- `Assets/Scripts/Rendering/`：运行时渲染组件。
- `Assets/Scripts/Editor/`：只在 Unity Editor 中运行的构建和编辑器工具。
- `Assets/Plugins/MajdataPlay/`：拆分为独立 `.asmdef` 的可复用底层库，包括 Buffers、Collections、Diagnostics、Drawing、FFmpeg、IO、Net、Numerics、Platform、Runtime、Syscall、Threading、UnsafeKit 和 Utils。
- `Assets/Scenes/`：Unity 场景；启用场景以 `ProjectSettings/EditorBuildSettings.asset` 为准。
- `Packages/`：Unity Package Manager 清单和锁文件。
- `ProjectSettings/`：共享的 Unity 工程配置。
- `Tools/FFmpeg/`：FFmpeg 原生库与 Unity 图形桥接的构建工具。
- `Tools/Tests/`：独立验证程序、隔离 Unity 测试工程脚本及对应说明。
- `ThirdParty/`：第三方源码；优先保持上游结构，避免无关格式化。

在添加跨目录引用前先检查最近的 `.asmdef`。新代码应放入职责最接近的现有程序集，不要仅为绕过引用错误而把代码移回默认 `Assembly-CSharp` 或扩大程序集依赖。

## 工作方式

1. 阅读根目录 `README.md`、目标目录 README、相关 `.asmdef` 和相邻实现。
2. 用 `rg`/`rg --files` 查找调用方、序列化字段、场景或预制体引用以及平台条件编译分支。
3. 只做完成任务所需的最小改动；不要顺便升级包、重写序列化资产或格式化整个文件树。
4. 行为修复应优先补充最接近该模块的回归测试或验证用例。
5. 为后期维护考虑，编码时应优先考虑最高可读性和最高可维护性。
6. 运行与改动范围匹配的验证。无法运行硬件、Unity Player 或目标平台测试时，明确记录“未验证”，不得用编译成功推断运行成功。
7. 完成前检查 `git diff --check`、`git status --short` 和最终 diff，确认没有生成文件、秘密或无关改动。

## C# 与 Unity 代码约定

- `.editorconfig` 是格式与命名规则的权威来源，应严格遵守`.editorconfig`的格式和命名规则。C# 使用 4 空格缩进、CRLF、花括号、块级命名空间以及命名空间外的 `using`。
- 所有C#代码不要使用单行语句样式。
- 所有C#代码都应当启用Nullable（包括Unity object），并在源码中添加`#nullable enable`
- 为统一代码风格，局部参数尽可能地使用`var`。
- 从`MonoBehaviour`派生的类中，所有由Unity进行序列化和反序列化序列化的字段都应当为`private`，并且使用`SerializeField`和`FormerlySerializedAs`特性，`FormerlySerializedAs`特性中的命名应当为PascalCase；如果该字段有公开访问的需求，可以使用自动属性，并对属性添加`[field: SerializeField]`和`[field: FormerlySerializedAs]`特性；成员声明应当避免和特性处于同一行。
- 命名空间应与目录和程序集职责一致，通常位于 `MajdataPlay.*` 下。
- 遵循相邻代码的语言特性和可见性；不要为风格偏好大范围改写已有文件。
- 运行时代码优先使用 `MajdataPlay.Diagnostics.MajDebug`；编辑器工具可使用 `UnityEngine.Debug`。日志应包含可定位上下文，但不得输出密钥、令牌或用户隐私。
- 项目广泛使用 UniTask。异步操作应传播 `CancellationToken`，并与对象销毁、场景切换或显式关闭生命周期绑定；避免无法观察异常的裸 fire-and-forget。
- Unity 对象和大多数 Unity API 只能在主线程访问。后台解码、I/O 或网络任务切回主线程后再触碰 Unity 对象。
- 游戏循环、输入、渲染、谱面解析和视频路径对分配敏感。避免每帧 LINQ、临时集合、字符串拼接和无界队列；优先复用现有池、缓冲区和缓存。
- 修改 `unsafe`、P/Invoke 或原生结构布局时，显式核对各目标 ABI 的指针宽度、C `long`、对齐、调用约定、所有权和线程约束。
- 平台实现应留在相应 Platform 目录或平台受限 `.asmdef` 中，并保留 Editor 可编译性。不要用单一平台的成功掩盖其他平台分支的编译问题。
- 所有成员均需添加英文XML文档（Unity message方法非必要可以不添加），文档应说明成员的用途；如果方法有参数，应当说明各个参数的用途；如果方法有返回值，应当说明方法会返回什么；如果方法有可能抛出异常，文档也应当列出可能的异常类型。

## Unity 资产与序列化

- 场景、Prefab、材质、动画控制器等 YAML 资产优先通过 Unity Editor 修改。必须手工修改时，只编辑目标字段并检查 GUID、fileID 和 YAML diff。
- 重命名序列化字段时使用适当的迁移手段（例如 `FormerlySerializedAs`），避免静默丢失已有场景/Prefab 数据。
- 不要无故重新保存大型场景、Prefab 或 `ProjectSettings`；Unity 版本、序列化模式或换行变化可能制造大量噪声。
- 新增运行时资源时核对导入设置、目标平台覆盖、Addressables/StreamingAssets 路径和大小写。Linux 与移动平台文件系统对路径大小写更敏感。
- 不要把测试生成物写回正式场景或 Player Settings。现有隔离验证脚本应继续使用其忽略的 `.work/` 或 `Temp/` 目录。

## 验证指南

验证范围应与改动匹配，而不是机械运行所有耗时测试。

### 主项目

- 在 Unity 6000.3.17f1 中等待完整导入和脚本编译，确认 Console 没有新增错误。
- 场景或交互改动应在相关场景进行 Play Mode 冒烟测试；场景列表以 `ProjectSettings/EditorBuildSettings.asset` 为准。
- `Assets/Plugins/MajdataPlay/Net/Tests/Editor` 中的测试通过 Unity Test Runner 的 EditMode 运行。
- 发布构建由 `.github/workflows/main.yml` 的 game-ci 配置覆盖。除非任务要求，不要为了普通代码改动在本机生成完整多平台发布包。

### 独立快速验证（需要 .NET 9）

```powershell
dotnet run --project Tools/Tests/SettingValidation/SettingValidation.csproj
dotnet run --project Tools/Tests/IOValidation/IOValidation.csproj
```

这些项目链接生产源码并使用窄测试替身；它们通过不代表 Unity 生命周期、真实设备或渲染集成已经通过。

### 渲染、网络与原生模块

- RawSprite 绘制模式：`./Tools/Tests/ValidateRawSpriteDrawModes.ps1`
- Setting/TMP 集成：按 `Tools/Tests/SettingTextValidation.md` 运行隔离 Unity 验证。
- Curl ABI：按 `Tools/Tests/CurlAbiValidation/README.md`；Android ARMv7 验证需要 Unity Android 支持、NDK、ADB 和真机/设备。
- FFmpeg 托管、Unity Player、Android、Linux 和 Apple 验证：严格按 `Tools/Tests/FFmpegValidation/README.md` 选择命令和前置条件。
- FFmpeg 原生库构建：先读 `Tools/FFmpeg/README.md` 与 `Tools/FFmpeg/Native/README.md`；可先用 `./Tools/FFmpeg/build.ps1 -Probe` 只检查工具链。

FFmpeg、Curl、串口/HID、GPU 共享、触摸映射和移动平台改动依赖真实原生库、驱动或硬件。报告结果时区分：托管测试、Unity Editor、Player、目标架构和实机验证；不要把其中一层的结果外推到其他层。

## 完成标准

- 改动聚焦且符合现有程序集/目录边界。
- Unity 资产与 `.meta` 状态一致，没有意外 GUID 变化。
- 相关验证已通过，或清楚列出未运行项及原因。
- 没有新增 Unity/IDE 生成文件、本地数据、构建产物或秘密。
- 最终说明包含修改内容、验证命令与结果，以及仍存在的平台或硬件验证限制。
