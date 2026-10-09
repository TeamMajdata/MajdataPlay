# Android Java → Unity C# JNI source generator

本工具为 `MajdataPlay.Platform.Android` 提供 `[JavaClass("binary.ClassName")]`。
生成代码留在 Roslyn compilation 内，不把 `.g.cs` 写回 `Assets`，不改 Unity
生成的解决方案或项目文件。每个 wrapper 都派生自 `JavaObject`。

## 安装与最小使用方式

前置条件：仓库固定版本 **Unity 6000.3.17f1**、其 Android Build Support
（SDK + OpenJDK 17）、.NET 9 SDK。构建项目位于 `Tools/Tests/`，它是明确提供
的独立工具项目，而非 Unity 生成的 `.csproj`。

```powershell
./Tools/AndroidJavaGenerator/build.ps1 -Install
```

只会安装 analyzer DLL 及专用 `.meta`，安装结果被 `.gitignore` 忽略。DLL 的
`RoslynAnalyzer` label、关闭所有 Player/Editor 插件平台的导入设置来自本目录
的 metadata 模板。不要复制 `Microsoft.CodeAnalysis*.dll` 到 `Assets`，不要把
analyzer 加入 runtime `.asmdef` references。新克隆的仓库必须执行安装命令。
首次构建通过本地 NuGet cache 还原 Roslyn 4.3.1/.NET Standard 2.0；若机器没有
缓存，可先显式还原：

```powershell
dotnet restore Tools/Tests/AndroidJavaGeneratorBuild/AndroidJavaGeneratorBuild.csproj --source https://api.nuget.org/v3/index.json
./Tools/AndroidJavaGenerator/build.ps1 -Install
```

在 Android 模块（`Assets/Plugins/MajdataPlay/Platform/Android/`）中声明一个
非泛型、非嵌套、非 static 的 `partial class`：

```csharp
using MajdataPlay.Platform.Android;

#nullable enable

namespace MajdataPlay.Platform.Android.Bindings
{
    /// <summary>
    /// Provides the installed SDK's Android build-version fields.
    /// </summary>
    [JavaClass("android.os.Build$VERSION", ApiLevel = 36)]
    public partial class BuildVersion
    {
    }

    /// <summary>
    /// Wraps an existing Java Runnable instance.
    /// </summary>
    [JavaClass("java.lang.Runnable", ApiLevel = 36)]
    public partial class Runnable
    {
    }
}
```

生成 `BuildVersion.SdkInt` 等只有 getter 的属性；`Runnable.Run()` 在已有 Java
实例上调用接口方法。每个 wrapper 有
`Wrapper(AndroidJavaObject javaObject, bool ownsReference = true)`。
具体类按所选 SDK 中自己声明的 public 构造器逐个生成 C# 构造器，保留参数与
精确 JNI descriptor；public 默认构造器也会生成。private、protected 和包级
构造器均忽略，构造器不会继承。没有 public 构造器的类型、Java 接口与抽象类
只生成已有引用的包装构造器，不补充无参构造器或创建普通 `java.lang.Object`。
嵌套 Java 类型使用 `$` 二进制名，例如 `android.os.Build$VERSION`，
而不是点分隔的源码名。

## 自定义 `.java`、`.class` 与 `.jar`

```csharp
using MajdataPlay.Platform.Android;

#nullable enable

[assembly: JavaApiConfiguration(
    ApiLevel = 36,
    Sources = new[] { "Assets/Plugins/Android/ExampleApi.jar" },
    ClassPath = new[] { "ThirdParty/ExampleApi/dependencies.jar" },
    DocumentationPaths = new[] { "ThirdParty/ExampleApi/sources.zip" })]

namespace MajdataPlay.Platform.Android.Bindings
{
    /// <summary>
    /// Calls the Java API packaged independently in the Android player.
    /// </summary>
    [JavaClass("com.example.MyJavaClass")]
    public partial class MyJavaClass
    {
    }
}
```

- `Sources` 支持单个 `.java`、递归包含 `.java` 的目录、单个 `.class` 或 `.jar`。
  `.java` 用 Java 编译器解析和分析；不会生成目标字节码或执行类初始化。
  `.class` 与 `.jar` 用编译器的 symbol model 读取，不反射加载目标代码。
- 单个 `.class` 必须位于与 class-file 内真实 binary name 一致的包目录中；
  读取 constant pool 后确定 classpath root，不通过猜测文件名推断类型。
- `ClassPath` 补充依赖 JAR、class 根目录或 class 文件。所有被公开签名、父类
  和接口引用的依赖必须可以解析；缺少依赖会产生错误诊断，不输出猜测类型。
- 相对路径以 Unity 工程根目录为基准，支持绝对路径和环境变量展开。
  不建议把开发机绝对路径提交到 C# 配置中。
- `Sources`、`ClassPath` 和 `DocumentationPaths` 支持区分大小写的 `{UnityData}` 路径 token。
  它复用从 compilation 的 Unity managed references 或 `UNITY_EDITOR_PATH` 已发现的
  Editor/Data 目录（macOS 为 Unity.app/Contents），通常无需额外环境变量或开发机硬编码路径。
  如果路径需要该 token 但未发现 Unity，报告包含原始路径与发现方式的 `AJG003` 配置错误。
  环境变量仍按原有规则展开，Java 嵌套类型文件名中的 `$` 保持字面值。
- assembly 配置只作用于声明它的程序集。类上的输入数组追加到全局数组，
  显式设置的 scalar 值覆盖 assembly 配置。
- 如果 wrapper 放入另外的 `.asmdef`，该程序集必须引用 Android runtime，
  同时确保 Unity 的 analyzer 作用域包含它；必要时为该程序集安装同一个
  analyzer，勿扩展到不需要 Android 绑定的平台程序集。

生产 `StorageAccess` 的 Java 源可以显式使用 Unity 的 Android `classes.jar`：

```csharp
[JavaClass("net.majdata.majdataplay.StorageAccess",
    Sources = new[] { "Assets/Plugins/Android/src/java/net/majdata/majdataplay/StorageAccess.java" },
    ClassPath = new[] { "{UnityData}/PlaybackEngines/AndroidPlayer/Variations/mono/Release/Classes/classes.jar" })]
```

该 token 只解析配置的路径；生成器不会自动添加其他 Unity JAR。

**代码生成不等于打包**：`Sources` 和 `ClassPath` 只供分析。
自定义 Java 代码仍应通过 Unity Android 插件/Gradle 正常进入 APK/AAB，
SDK stubs `android.jar` 绝不能作为运行库打包。

## SDK 与 JDK 配置

按以下顺序寻找工具：

1. 类上明确设置的 `AndroidSdkPath` / `JavaHome`；
2. assembly `JavaApiConfiguration` 的对应配置；
3. SDK：`UNITY_ANDROID_SDK`、`ANDROID_SDK_ROOT`、`ANDROID_HOME`；
   JDK：`UNITY_JAVA_HOME`、`JAVA_HOME`；
4. 从 `UNITY_EDITOR_PATH` 或 compilation 的 Unity managed references 定位
   Editor/Data，再使用 `PlaybackEngines/AndroidPlayer/SDK` 与 `OpenJDK`。

`ApiLevel = 0` 选择真正存在 `android.jar` 的最高已安装整数 API 平台。
**推荐显式固定 API level**，以免不同机器生成不同 API surface；该配置不改变
Player Settings，也不代表设备上对应 API 必然可用。较新 API 必须由调用方
根据 Android 版本守卫。

Java helper 的默认位置是本工程
`Tools/AndroidJavaGenerator/Java/JavaApiExtractor.java`；可用
`MAJDATA_JAVA_EXTRACTOR` 指定外部副本。
`MAJDATA_JAVA_EXTRACTOR_TIMEOUT_SECONDS` 默认 120，允许 1–3600。
提取进程有取消和输出大小限制，不运行 shell，也不使用跨 compilation 的静态
元数据缓存。每次实际执行 generator 时重新读取 Java 输入。
Roslyn 若复用完全不变的 compilation 和已缓存 driver，会跳过 generator 执行；
这种情况下外部文件变化不会自动进入 driver 缓存。

## 成员映射与 JNI 精度

- public 实例/static 方法、public 构造函数、public 字段、public 常量均映射。
- Java fields → C# 只有 getter 的 PascalCase 属性；static fields → static
  getter。拥有编译期 `ConstantValue` 的 public static final primitive/String
  → `const`。没有编译期值的 final fields 仍从 JNI getter 读取。
- Java enum 的常量 → static getter，`values` / `valueOf` → 方法。
- public inherited members 默认展开到 wrapper；可用
  `IncludeInheritedMembers = false` 仅生成声明成员。Java 继承并不自动生成
  C# 继承层次，所有 wrapper 以 `JavaObject` 为直接基类。
- Java `byte` → C# `sbyte`；`char` → UTF-16 `char`；其余 primitive 精确对应。
- `String` → `string?`。被同一 compilation `[JavaClass]` 标记的引用类型 →
  对应 wrapper；其他 Java 引用 → `AndroidJavaObject?`，不虚构 C# 类型。
  Java 的子类/接口关系不会自动成为 C# 转换；需要不同的 typed view 时，可用
  `new InterfaceWrapper(instance.JavaReference, ownsReference: false)` 显式借用。
- Java 数组 → nullable C# jagged arrays，支持 null 数组、null 元素和嵌套数组。
- Java generics 使用真实 JVM erasure；JNI descriptor 保留声明类型，不由
  C# 参数值推断。null、Java 接口参数、Java 子类参数及构造函数重载均使用
  编译器给出的 exact descriptor。
- Java 多个不同 overload 可能在 C# 擦除为相同类型；此时生成 deterministic
  alias/factory 并给出重命名诊断，以保留每个 Java 成员。不要仅依赖简单
  method name；检查生成代码或诊断中的 alias。与手写成员产生不可安全解决
  的冲突会报错。
- 非 static Java member class 的构造函数需要 enclosing instance；它不是
  普通无参类，生成器会把该隐式 JNI 参数放在最前面。
- Java 公共嵌套类型可用独立 `[JavaClass("Outer$Inner")]` 声明请求；不会为
  整个依赖图递归自动生成无限量 wrapper。

接口 wrapper **不是** `AndroidJavaProxy` callback 实现。若需要实现 Java
监听器/接口回调，请使用现有 Unity `AndroidJavaProxy`，然后显式取得/传入
需要的 Java reference；本工具只包装 Java 已有实例。

## 英文 XML API 文档

生成成员均有 English XML `<summary>`、`<param>`、`<returns>` 和相关异常说明。
优先使用输入 Java 源码中的 Javadoc；`DocumentationPaths` 可以指向匹配的
Java source 目录或 source `.zip` / `.jar`。

安装的 Android SDK 若有同版本 sources 或 `android-stubs-src.jar`，会作为
文档候选输入。**有些 SDK stub-source 包删掉了 Javadoc**；这时不会编造官方
说明，而是输出描述实际成员用途的英文 fallback，且 SDK Java 类型附带
Android 官方 reference 的 `<seealso href="...">`。提供 SDK Manager 下载的
同版本 Android Sources 后可补全官方 API 内容。二进制本身不能恢复被移除的
Javadoc；无法确定与真实 descriptor 匹配的注释不会冒充该 overload 的文档。

## 运行时所有权、线程与平台

```csharp
using var intent = new IntentWrapper();
// Returned wrappers/AndroidJavaObject references also need deterministic disposal.
```

- wrapper 创建的对象与 Java 返回的新引用默认归 wrapper 所有；`Dispose()`
  幂等释放持有的 Unity reference。传入已有 reference 时默认采用所有权；
  使用 `ownsReference: false` 借用 `AndroidRuntime.CurrentActivity` 等外部管理
  的 reference，借用 wrapper 不延长外部 reference 生命周期。
- 所有 wrapper 派生自 `JavaObject`，而 `JavaObject` 本身是可直接构造的具体类：
  `new JavaObject()` 等价于 Java 的 `new Object()`，
  `new JavaObject(className, constructorSignature, args)` 用精确 descriptor
  构造任意 Java 类，`new JavaObject(javaObject, ownsReference)` 包装已有引用。
- `JavaObject.Equals`、`GetHashCode`、`ToString` 调用 Java 对象对应的虚方法，
  `==` / `!=` 与 `Equals` 使用相同的 Java 相等语义。同一 wrapper、null 和
  非 wrapper 比较不调用 JNI；不同 wrapper 比较、哈希与字符串转换要求引用
  未释放，并在 JVM-attached 线程运行。需要访问已释放引用时抛出
  `ObjectDisposedException`，不会把已释放对象视作 null。
- 不可同时从外部 Dispose 已被 wrapper 采用的 reference，不可把 Dispose
  与同一 reference 上的 JNI 调用并发执行。
- 对象/对象数组结果会提升为独立 global references，单次调用的 JNI locals
  用 local frame 回收；数组转换还逐元素回收 locals，避免大数组积累临时引用。
- Android main thread或已明确 `AttachCurrentThread` 的后台线程才能调用。
  本工具不自动 attach/detach 线程，不隐藏 Unity 对线程和生命周期的要求。
- 在 Unity Editor 和非 Android Player 中尝试 JNI 会立即抛出
  `PlatformNotSupportedException`；`const` 和 wrapper 元数据无需 JVM。
- Java invocation failures 先清除 JVM pending exception，再以
  `JavaInvocationException` 提供 Java message/stack。Unity 的 JNI lookup
  helper 仍可能抛出 `AndroidJavaException`。
- 使用 Unity 公开 JNI API，不增加自定义 P/Invoke；Java/JNI 的 long 固定为
  64 位，与 ABI 的 native C long 无关。

## 输入刷新

Unity 不会追踪 analyzer 自行读取的外部文件。Android Editor companion 对
`Assets` 内 `.java` / `.class` / `.jar` 的导入、删除、移动合并触发一次
带 `CleanBuildCache` 的 C# compilation，防止 Unity/Bee 直接复用未改变 C#
输入的旧编译产物。外部文件、SDK 或 JDK 改动后使用菜单：

`Tools → MajdataPlay → Android → Regenerate Java wrappers`

也可以修改 wrapper C# source 触发新的编译输入。单纯复用旧 Roslyn driver、
或触发被 Unity build cache 跳过的普通编译请求，不能保证刷新；使用上述菜单。
没有实际 C# recompilation 时，不应认为
外部 Java 输入已经刷新。

## 验证与明确限制

```powershell
dotnet run --project Tools/Tests/AndroidJavaGeneratorValidation/AndroidJavaGeneratorValidation.csproj
./Tools/Tests/ValidateAndroidJavaGeneratorUnity.ps1
```

托管验证链接生产 runtime/generator，并使用真实 Unity assemblies 和 Unity
Android SDK；source/class/JAR fixtures 位于 ignored Temp 中。
第二条命令只启动固定版本的**隔离** Unity 工程，验证真实 Unity Roslyn
analyzer loading、SDK wrappers 编译、public 构造器重载、接口和 getter-only
属性、无 public 构造器/接口/抽象类只包装已有引用，以及 Editor JNI guard。
它们均不代表 Android Player、Mono/IL2CPP、ARMv7/ARM64 或真实设备 JNI
运行成功。发版前必须分别验证构造/静态与实例调用、nullable 和 jagged arrays、
接口参数、引用释放、Java exception 传播，以及低版本设备的 API guards。

当前 Java 源分析使用 Java 8 source/target + 所选 `android.jar` bootclasspath，
避免误绑定桌面 JDK 的 `java.*` API。需要 Java 9+ syntax 的源文件请先用项目
真实 Android 工具链编译，再输入 `.class` / `.jar`；不启用 annotation
processors，不处理 Kotlin source、AAR 解包、Java callback generation、C#
generic reification、Android nullability annotation contract或自动 ProGuard
配置。开启 R8/minify 的自定义 Java 类型应由项目显式保留所绑定的成员。
