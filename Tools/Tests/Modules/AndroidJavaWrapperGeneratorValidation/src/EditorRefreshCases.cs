using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

#nullable enable

namespace MajdataPlay.Tests.AndroidJavaWrapperGeneratorValidation
{
    /// <summary>Validates the real Editor compilation contract and managed refresh lifecycle.</summary>
    internal static partial class Program
    {
        /// <summary>Compiles the three production Editor files without loading Unity native code.</summary>
        /// <param name="settings">The pinned Unity and .NET Standard reference locations.</param>
        /// <param name="workspace">The exclusively owned managed validation workspace.</param>
        /// <exception cref="IOException">A production source or reference cannot be read.</exception>
        /// <exception cref="InvalidOperationException">An Editor compilation or refresh contract fails.</exception>
        private static void VerifyEditorRefresh(ValidationSettings settings, string workspace)
        {
            var sourceDirectory = FindRefreshEditorDirectory();
            var paths = new[] { "JavaWrapperRefresh.cs", "JavaWrapperGenerationRequest.cs", "JavaWrapperOutput.cs" };
            var sources = paths.ToDictionary(name => Path.Combine(sourceDirectory, name), name => File.ReadAllText(Path.Combine(sourceDirectory, name)), StringComparer.Ordinal);
            Require(sources.Values.All(source => source.Contains("#nullable enable", StringComparison.Ordinal)), "All production Editor refresh sources enable nullable analysis");
            var references = GetRefreshStandardReferences(settings);
            references.Add(MetadataReference.CreateFromFile(Path.Combine(settings.UnityData, "Managed", "UnityEngine", "UnityEngine.CoreModule.dll")));
            references.Add(MetadataReference.CreateFromFile(Path.Combine(settings.UnityData, "Managed", "UnityEngine", "UnityEditor.CoreModule.dll")));
            Console.WriteLine("Compiling production Editor refresh sources as C# 9 / netstandard2.1 against real Unity Core/Editor metadata...");
            foreach (var symbols in new[] { new[] { "UNITY_EDITOR" }, new[] { "UNITY_EDITOR", "UNITY_ANDROID" } })
            {
                var trees = ParseRefreshSources(sources, symbols);
                VerifyRefreshMemberDocumentation(trees);
                CompileRefreshAssembly(trees, references, OutputKind.DynamicallyLinkedLibrary);
            }
            var standardReferences = GetRefreshStandardReferences(settings);
            var doubleTrees = ParseRefreshSources(new Dictionary<string, string> { { "EditorDouble.cs", RefreshEditorDoubleSource } }, Array.Empty<string>());
            var doubleImage = CompileRefreshAssembly(doubleTrees, standardReferences, OutputKind.DynamicallyLinkedLibrary);
            using var doubleStream = new MemoryStream(doubleImage);
            var doubles = AssemblyLoadContext.Default.LoadFromStream(doubleStream);
            standardReferences.Add(MetadataReference.CreateFromImage(doubleImage));
            var image = CompileRefreshAssembly(ParseRefreshSources(sources, new[] { "UNITY_EDITOR" }), standardReferences, OutputKind.DynamicallyLinkedLibrary);
            var controller = new EditorRefreshSandbox(image, doubles, true);
            var project = new RefreshProject(workspace);
            VerifyRefreshRequests(settings, workspace, controller.RequestType, project);
            VerifyRefreshLifecycle(controller, project);
        }

        /// <summary>Locates the production Editor sources from the repository or executing harness.</summary>
        /// <returns>The existing Android Editor source directory.</returns>
        /// <exception cref="DirectoryNotFoundException">Neither starting path belongs to the repository.</exception>
        private static string FindRefreshEditorDirectory()
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                for (DirectoryInfo? directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                {
                    var candidate = Path.Combine(directory.FullName, "Assets", "Plugins", "MajdataPlay", "Platform", "Android", "Editor");
                    if (File.Exists(Path.Combine(candidate, "JavaWrapperRefresh.cs")))
                    {
                        return candidate;
                    }
                }
            }
            throw new DirectoryNotFoundException("Cannot locate the production Android Editor refresh sources.");
        }

        /// <summary>Creates metadata references using only the complete .NET Standard 2.1 pack.</summary>
        /// <param name="settings">The independently supplied reference-pack location.</param>
        /// <returns>The reference pack's deterministically ordered assemblies.</returns>
        private static List<MetadataReference> GetRefreshStandardReferences(ValidationSettings settings)
        {
            return Directory.GetFiles(settings.NetStandardReferences, "*.dll").OrderBy(path => path, StringComparer.Ordinal).Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToList();
        }

        /// <summary>Parses actual sources with the language and documentation settings used by Unity.</summary>
        /// <param name="sources">The actual source paths and contents.</param>
        /// <param name="symbols">The Editor compilation branch symbols.</param>
        /// <returns>The C# 9 syntax trees with XML diagnostic reporting enabled.</returns>
        private static SyntaxTree[] ParseRefreshSources(IReadOnlyDictionary<string, string> sources, string[] symbols)
        {
            var options = new CSharpParseOptions(LanguageVersion.CSharp9, Microsoft.CodeAnalysis.DocumentationMode.Diagnose, preprocessorSymbols: symbols);
            return sources.Select(source => CSharpSyntaxTree.ParseText(source.Value, options, source.Key)).ToArray();
        }

        /// <summary>Requires documented production members independently of their accessibility.</summary>
        /// <param name="trees">The production Editor syntax trees.</param>
        /// <exception cref="InvalidOperationException">A declared member lacks XML documentation.</exception>
        private static void VerifyRefreshMemberDocumentation(IEnumerable<SyntaxTree> trees)
        {
            foreach (var tree in trees)
            {
                foreach (var member in tree.GetRoot().DescendantNodes().OfType<MemberDeclarationSyntax>().Where(member => !(member is BaseNamespaceDeclarationSyntax)))
                {
                    Require(member.GetLeadingTrivia().Any(trivia => trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)), "Editor member has XML documentation at " + member.GetLocation().GetLineSpan());
                }
            }
        }

        /// <summary>Emits managed code while checking language, reference, nullable, and XML diagnostics.</summary>
        /// <param name="trees">The source trees to compile.</param>
        /// <param name="references">Only the intended assembly references.</param>
        /// <param name="kind">The managed library or argument-probe executable output kind.</param>
        /// <returns>The emitted managed assembly image.</returns>
        /// <exception cref="InvalidOperationException">Compilation or documented nullable contracts fail.</exception>
        private static byte[] CompileRefreshAssembly(IEnumerable<SyntaxTree> trees, IEnumerable<MetadataReference> references, OutputKind kind)
        {
            var compilation = CSharpCompilation.Create("EditorRefreshCases" + Guid.NewGuid().ToString("N"), trees, references, new CSharpCompilationOptions(kind, optimizationLevel: OptimizationLevel.Release, nullableContextOptions: NullableContextOptions.Enable));
            using var stream = new MemoryStream();
            using var documentation = new MemoryStream();
            var result = compilation.Emit(stream, xmlDocumentationStream: documentation);
            var failures = result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                || (diagnostic.Severity == DiagnosticSeverity.Warning && (diagnostic.Id.StartsWith("CS86", StringComparison.Ordinal)
                    || diagnostic.Id.StartsWith("CS87", StringComparison.Ordinal) || diagnostic.Id.StartsWith("CS15", StringComparison.Ordinal)
                    || diagnostic.Id == "CS1658"))).ToArray();
            if (!result.Success || failures.Length != 0)
            {
                throw new InvalidOperationException("Editor refresh compilation failed:" + Environment.NewLine + string.Join(Environment.NewLine, failures.Select(diagnostic => diagnostic.ToString())));
            }
            Require(true, "Editor refresh assembly compiles with valid nullable and XML documentation contracts");
            return stream.ToArray();
        }
        /// <summary>Checks import filtering, online arbitration, busy guards, cancellation, and reload restoration.</summary>
        /// <param name="controller">The actual controller compiled against narrow managed Editor doubles.</param>
        /// <param name="project">The scratch project used only to capture managed requests.</param>
        /// <exception cref="InvalidOperationException">A managed Editor lifecycle contract fails.</exception>
        private static void VerifyRefreshLifecycle(EditorRefreshSandbox controller, RefreshProject project)
        {
            controller.SetDoubleProperty("UnityEngine.Application", "dataPath", Path.Combine(project.ProjectRoot, "Assets"));
            controller.SetDoubleProperty("UnityEditor.EditorApplication", "applicationContentsPath", project.UnityData);
            Require(!controller.CanCancel, "An idle refresh has no enabled cancel command");
            var ignored = new[]
            {
                "Assets/Scripts/Example.cs",
                "Assets/Plugins/MajdataPlay/Platform/Android/Runtime/Bindings/Android/Net/Uri.g.cs",
                "Assets/Plugins/Android/Generated.cs",
                "Assets/Plugins/Android/Library.jar.meta",
                "Assets/Plugins/Android/README.md",
                "Assets/Elsewhere/Source.java",
                "Assets/Plugins/AndroidOther/Source.java"
            };
            for (var slot = 0; slot < 4; slot++)
            {
                controller.Import(slot, ignored);
                Require(!controller.CanCancel, "Non-Java and generated C# assets do not queue from import callback slot " + slot);
            }
            foreach (var extension in new[] { ".java", ".class", ".jar", ".aar", ".JAVA", ".CLASS", ".JAR", ".AAR" })
            {
                for (var slot = 0; slot < 4; slot++)
                {
                    controller.ClearQueue();
                    controller.Import(slot, new[] { "Assets/Plugins/Android/nested path/Changed" + extension });
                    Require(controller.CanCancel, "Java import, deletion, or movement queues without requiring the old file: " + extension + " / " + slot);
                    Require(!controller.Offline, "Automatic Java refresh requests official online documentation");
                    controller.AssertNoProcess();
                }
            }
            controller.ClearQueue();
            controller.Call("RegenerateOffline");
            Require(controller.CanCancel && controller.Offline, "Explicit cached-documentation menu queues cached work");
            controller.Call("Regenerate");
            controller.Call("RegenerateOffline");
            controller.Import(0, new[] { "Assets/Plugins/Android/Repeated.java" });
            controller.Import(0, new[] { "Assets/Plugins/Android/Repeated.java" });
            Require(controller.CanCancel && !controller.Offline, "Coalesced cached, online, and repeated import requests preserve the online demand");
            Require(controller.DoubleProperty<int>("UnityEditor.EditorApplication", "UpdateSubscriberCount") == 1, "Coalesced requests retain one Editor update subscription");
            controller.Call("CancelGeneration");
            Require(!controller.CanCancel && controller.Offline, "Cancel clears queued work and its documentation arbitration flags");
            controller.Call("Regenerate");
            controller.Call("RegenerateOffline");
            Require(!controller.Offline, "An online request also wins when cached work is queued afterward");
            foreach (var guard in new[] { "isCompiling", "isUpdating", "isPlayingOrWillChangePlaymode", "isBuildingPlayer" })
            {
                controller.ClearQueue();
                controller.Call("RegenerateOffline");
                var owner = guard == "isBuildingPlayer" ? "UnityEditor.BuildPipeline" : "UnityEditor.EditorApplication";
                controller.SetDoubleProperty(owner, guard, true);
                try
                {
                    controller.CallDouble("UnityEditor.EditorApplication", "Tick");
                    controller.CallDouble("UnityEditor.EditorApplication", "Tick");
                    Require(controller.CanCancel && controller.Offline, "Queued work remains pending while Editor guard " + guard + " is active");
                    controller.AssertNoProcess();
                }
                finally
                {
                    controller.SetDoubleProperty(owner, guard, false);
                }
                controller.Call("CancelGeneration");
            }
            controller.ClearQueue();
            controller.Call("RegenerateOffline");
            controller = controller.Reload();
            Require(controller.CanCancel && controller.Offline, "A queued cached refresh survives a new controller domain");
            Require(!controller.HasSavedFlags, "The restored controller consumes its SessionState keys");
            controller.Import(3, new[] { "Assets/Plugins/Android/MovedOut.jar" });
            controller = controller.Reload();
            Require(controller.CanCancel && !controller.Offline, "A moved-out Java input and its online demand survive domain reload");
            controller.Call("CancelGeneration");
            controller = controller.Reload();
            Require(!controller.CanCancel && controller.Offline, "A canceled queued refresh is not resurrected by domain reload");
            controller.ClearQueue();
            var onlineRequest = CreateRefreshRequest(controller.RequestType, project, false);
            controller.SetField("s_request", onlineRequest);
            controller.Call("RegenerateOffline");
            controller = controller.Reload();
            Require(controller.CanCancel && !controller.Offline, "A captured interrupted online request upgrades a queued cached request on reload");
            controller.ClearQueue();
            controller.SetField("s_request", CreateRefreshRequest(controller.RequestType, project, false));
            controller.Call("CancelGeneration");
            controller.Call("RegenerateOffline");
            controller = controller.Reload();
            Require(controller.CanCancel && controller.Offline, "A new cached request queued after cancellation survives without reviving canceled online work");
            controller.CallDouble("UnityEditor.EditorApplication", "Quit");
            Require(!controller.CanCancel && !controller.HasSavedFlags, "Editor quit clears pending work and saved domain flags");
            controller.AssertNoProcess();
            var fresh = new EditorRefreshSandbox(controller.Image, controller.Doubles, false);
            Require(!fresh.CanCancel, "A fresh controller after Editor quit does not resume abandoned work");
            fresh.AssertNoProcess();
            Console.WriteLine("Managed Editor queue and SessionState lifecycle checks passed; native Unity lifecycle was not exercised.");
        }

        /// <summary>Invokes an actual static method while preserving its original managed exception.</summary>
        /// <param name="type">The actual production or double declaring type.</param>
        /// <param name="name">The static method name.</param>
        /// <param name="arguments">The managed callback or helper arguments.</param>
        /// <returns>The static method result, including null for a void method.</returns>
        /// <exception cref="MissingMethodException">The required method is absent.</exception>
        private static object? CallRefreshMethod(Type type, string name, params object?[] arguments)
        {
            var method = type.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(type.FullName, name);
            return Invoke(method, arguments);
        }

        /// <summary>Reads an actual immutable request property without a production assembly reference.</summary>
        /// <typeparam name="T">The expected managed property type.</typeparam>
        /// <param name="request">The captured actual production request.</param>
        /// <param name="name">The property's declared name.</param>
        /// <returns>The actual managed property value.</returns>
        /// <exception cref="MissingMemberException">The required request property is absent.</exception>
        private static T ReadRefreshProperty<T>(object request, string name)
        {
            var property = request.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMemberException(request.GetType().FullName, name);
            return (T)property.GetValue(request)!;
        }

        /// <summary>Captures an actual production request using only scratch project and Editor paths.</summary>
        /// <param name="requestType">The actual compiled request helper.</param>
        /// <param name="project">The scratch prerequisite locations.</param>
        /// <param name="offline">Whether to request explicit cached documentation.</param>
        /// <returns>The actual immutable generation request.</returns>
        private static object CreateRefreshRequest(Type requestType, RefreshProject project, bool offline)
        {
            return CallRefreshMethod(requestType, "Create", project.ProjectRoot, project.UnityData, project.SdkRoot, offline)!;
        }

        /// <summary>Hosts actual controller statics while sharing SessionState across simulated domains.</summary>
        private sealed class EditorRefreshSandbox
        {
            /// <summary>Gets the actual production controller type in this simulated domain.</summary>
            private Type Controller { get; }

            /// <summary>Gets the production image used to create fresh controller static state.</summary>
            internal byte[] Image { get; }

            /// <summary>Gets the shared narrow Editor double assembly.</summary>
            internal Assembly Doubles { get; }

            /// <summary>Gets the actual request helper in the same controller assembly.</summary>
            internal Type RequestType => Controller.Assembly.GetType("MajdataPlay.Platform.Android.Editor.JavaWrapperGenerationRequest", true)!;

            /// <summary>Gets whether the actual cancel menu is enabled.</summary>
            internal bool CanCancel => (bool)Call("CanCancelGeneration")!;

            /// <summary>Gets the documentation demand retained by the actual queued controller.</summary>
            internal bool Offline => (bool)ReadField("s_offlineDocumentation")!;

            /// <summary>Gets whether the fake Editor still holds either saved refresh key.</summary>
            internal bool HasSavedFlags => (bool)CallDouble("UnityEditor.SessionState", "HasBool", ReadField("PendingRefreshKey"))!
                || (bool)CallDouble("UnityEditor.SessionState", "HasBool", ReadField("OfflineDocumentationKey"))!;

            /// <summary>Loads a fresh controller and runs its actual static lifecycle registration.</summary>
            /// <param name="image">The actual production controller image.</param>
            /// <param name="doubles">The shared fake Editor domain containing SessionState.</param>
            /// <param name="clearSession">Whether to begin with a new Editor session.</param>
            internal EditorRefreshSandbox(byte[] image, Assembly doubles, bool clearSession)
            {
                Image = image;
                Doubles = doubles;
                CallDouble("UnityEditor.EditorApplication", "ClearCallbacks");
                CallDouble("UnityEditor.AssemblyReloadEvents", "ClearCallbacks");
                CallDouble("UnityEditor.Progress", "Reset");
                foreach (var name in new[] { "isCompiling", "isUpdating", "isPlayingOrWillChangePlaymode" })
                {
                    SetDoubleProperty("UnityEditor.EditorApplication", name, false);
                }
                SetDoubleProperty("UnityEditor.BuildPipeline", "isBuildingPlayer", false);
                if (clearSession)
                {
                    CallDouble("UnityEditor.SessionState", "Clear");
                }
                Controller = Assembly.Load(image).GetType("MajdataPlay.Platform.Android.Editor.JavaWrapperRefresh", true)!;
                RuntimeHelpers.RunClassConstructor(Controller.TypeHandle);
                Require(DoubleProperty<int>("UnityEditor.EditorApplication", "UpdateSubscriberCount") == 1
                    && DoubleProperty<int>("UnityEditor.EditorApplication", "QuitSubscriberCount") == 1
                    && DoubleProperty<int>("UnityEditor.AssemblyReloadEvents", "SubscriberCount") == 1, "A fresh controller registers each lifecycle callback once");
            }

            /// <summary>Invokes an actual private menu or lifecycle callback.</summary>
            /// <param name="name">The actual static method name.</param>
            /// <param name="arguments">The managed callback arguments.</param>
            /// <returns>The actual controller callback result.</returns>
            internal object? Call(string name, params object?[] arguments)
            {
                return CallRefreshMethod(Controller, name, arguments);
            }

            /// <summary>Invokes a fake Editor's independently implemented event or state operation.</summary>
            /// <param name="typeName">The fully qualified Editor double type.</param>
            /// <param name="method">The independently implemented double operation.</param>
            /// <param name="arguments">The operation arguments.</param>
            /// <returns>The double operation result.</returns>
            internal object? CallDouble(string typeName, string method, params object?[] arguments)
            {
                return CallRefreshMethod(Doubles.GetType(typeName, true)!, method, arguments);
            }

            /// <summary>Changes one controlled Editor busy state or scratch toolchain path.</summary>
            /// <param name="typeName">The fully qualified Editor double type.</param>
            /// <param name="name">The double property name.</param>
            /// <param name="value">The controlled state or path.</param>
            internal void SetDoubleProperty(string typeName, string name, object value)
            {
                Doubles.GetType(typeName, true)!.GetProperty(name)!.SetValue(null, value);
            }

            /// <summary>Reads an independently recorded Editor observation.</summary>
            /// <typeparam name="T">The expected observation type.</typeparam>
            /// <param name="typeName">The fully qualified Editor double type.</param>
            /// <param name="name">The observation property name.</param>
            /// <returns>The recorded Editor observation.</returns>
            internal T DoubleProperty<T>(string typeName, string name)
            {
                return (T)Doubles.GetType(typeName, true)!.GetProperty(name)!.GetValue(null)!;
            }

            /// <summary>Installs a captured request envelope without creating a live process.</summary>
            /// <param name="name">The actual controller field name.</param>
            /// <param name="value">The actual request envelope to simulate interruption.</param>
            internal void SetField(string name, object value)
            {
                Controller.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);
            }

            /// <summary>Reads actual private state needed to assert queue and process boundaries.</summary>
            /// <param name="name">The actual static field name.</param>
            /// <returns>The actual field value, or null for an empty process or request.</returns>
            private object? ReadField(string name)
            {
                return Controller.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
            }

            /// <summary>Delivers imported, deleted, moved-to, or moved-from paths through the real import callback.</summary>
            /// <param name="slot">Zero for imports, one for deletions, two for destinations, or three for sources.</param>
            /// <param name="paths">The changed asset paths, which need not exist on disk.</param>
            internal void Import(int slot, string[] paths)
            {
                var arguments = new object?[] { Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>() };
                arguments[slot] = paths;
                Call("OnPostprocessAllAssets", arguments);
            }

            /// <summary>Clears work through the real cancel menu and Editor quit callback.</summary>
            internal void ClearQueue()
            {
                Call("CancelGeneration");
                CallDouble("UnityEditor.EditorApplication", "Quit");
                AssertNoProcess();
            }

            /// <summary>Raises the actual reload hook and loads a new controller sharing only Editor session state.</summary>
            /// <returns>The controller with freshly initialized statics and restored queued work.</returns>
            internal EditorRefreshSandbox Reload()
            {
                var pending = CanCancel;
                CallDouble("UnityEditor.AssemblyReloadEvents", "Reload");
                Require(HasSavedFlags, "The registered reload callback saves Editor session flags");
                Require((bool)CallDouble("UnityEditor.SessionState", "GetBool", ReadField("PendingRefreshKey"), !pending)! == pending, "The saved pending flag reflects whether queued work survives reload");
                AssertNoProcess();
                var result = new EditorRefreshSandbox(Image, Doubles, false);
                Require(!ReferenceEquals(Controller, result.Controller), "Domain reload uses distinct actual controller static state");
                return result;
            }

            /// <summary>Requires queue-only validation to avoid build processes and Unity asset publication.</summary>
            /// <exception cref="InvalidOperationException">A queue callback started execution or publication.</exception>
            internal void AssertNoProcess()
            {
                Require(ReadField("s_process") == null && DoubleProperty<int>("UnityEditor.Progress", "StartedCount") == 0
                    && DoubleProperty<int>("UnityEditor.AssetDatabase", "Calls") == 0, "Queue callbacks launch no process and touch no Unity assets");
            }
        }
        /// <summary>Checks managed discovery and actual process argument parsing without executing the CLI.</summary>
        /// <param name="settings">The reference pack used to compile the argument echo process.</param>
        /// <param name="workspace">The exclusively owned scratch directory.</param>
        /// <param name="requestType">The actual production request helper type.</param>
        /// <param name="project">The scratch project and simulated Unity installation.</param>
        /// <exception cref="InvalidOperationException">An immutable request or CLI contract fails.</exception>
        private static void VerifyRefreshRequests(ValidationSettings settings, string workspace, Type requestType, RefreshProject project)
        {
            var before = Directory.GetFileSystemEntries(project.ScratchRoot, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var offline = CreateRefreshRequest(requestType, project, true);
            var online = CreateRefreshRequest(requestType, project, false);
            var actualInputs = ReadRefreshProperty<IReadOnlyList<string>>(offline, "Inputs");
            Require(actualInputs.SequenceEqual(project.ExpectedInputs.OrderBy(path => path, StringComparer.Ordinal)), "Request discovers all supported project and Unity files in deterministic order, including AAR archives");
            Require(((IList<string>)actualInputs).IsReadOnly, "Captured request inputs cannot be changed by a later import");
            Require(Directory.GetFileSystemEntries(project.ScratchRoot, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal).SequenceEqual(before), "Capturing requests creates no build, cache, staging, or Assets output");
            Require(ReadRefreshProperty<bool>(offline, "OfflineDocumentation") && !ReadRefreshProperty<bool>(online, "OfflineDocumentation"), "Explicit cached and ordinary online requests retain their documentation modes");
            Require(ReadRefreshProperty<string>(offline, "WorkingDirectory") == project.ToolDirectory, "The CLI working directory retains its own .NET SDK pin");
            Require(ReadRefreshProperty<string>(offline, "ProjectPath") == Path.Combine(project.ToolDirectory, "AndroidJavaWrapperGenerator.csproj"), "The request builds the independent tool project");
            Require(ReadRefreshProperty<string>(offline, "AssemblyPath") == Path.Combine(project.ToolDirectory, "bin", "Release", "net9.0", "AndroidJavaWrapperGenerator.dll"), "The request invokes the Release .NET 9 CLI assembly");
            var staging = ReadRefreshProperty<string>(offline, "StagingDirectory");
            var stageParent = Path.GetDirectoryName(staging);
            Require(stageParent == Path.Combine(project.ToolDirectory, ".work", "editor-output") && staging != ReadRefreshProperty<string>(online, "StagingDirectory"), "Each request owns a unique direct child of the ignored editor-output directory");
            Require(ReadRefreshProperty<string>(offline, "OutputDirectory") == Path.Combine(project.ProjectRoot, "Tools", "AndroidJavaWrapper", "src"), "Source output targets the independent Tools wrapper project");
            Require(ReadRefreshProperty<string>(offline, "RuntimeAssemblyPath") == Path.Combine(project.ProjectRoot, "Assets", "Plugins", "MajdataPlay", "Platform", "Android", "MajdataPlay.Platform.Android.Runtime.dll"), "Runtime publication targets the requested Unity plugin DLL");
            Require(!Directory.Exists(staging) && !Directory.Exists(ReadRefreshProperty<string>(offline, "OutputDirectory")), "Request capture does not generate Unity Assets output");
            foreach (var extension in new[] { ".java", ".class", ".jar", ".aar", ".JAVA", ".CLASS", ".JAR", ".AAR" })
            {
                Require((bool)CallRefreshMethod(requestType, "IsJavaInput", "deleted path/file" + extension)!, "Removed Java and archive input paths remain recognizable: " + extension);
            }
            foreach (var path in new string?[] { null, string.Empty, "file.cs", "file.jar.meta", "file.java.bak", "file.zip" })
            {
                Require(!(bool)CallRefreshMethod(requestType, "IsJavaInput", path)!, "Unsupported or empty input paths are not generation inputs");
            }
            var probePath = Path.Combine(workspace, "argument echo with spaces.dll");
            var probeTrees = ParseRefreshSources(new Dictionary<string, string> { { "ArgumentEcho.cs", RefreshArgumentEchoSource } }, Array.Empty<string>());
            File.WriteAllBytes(probePath, CompileRefreshAssembly(probeTrees, GetRefreshStandardReferences(settings), OutputKind.ConsoleApplication));
            var quotedSamples = new[] { string.Empty, "path with spaces", "tab\tvalue", "embedded\"quote", "C:\\path with spaces\\", "backslash\\\"before quote", "two\\\\\"slashes", "尾部 path\\", "--literal-option" };
            var quoted = string.Join(" ", quotedSamples.Select(value => (string)CallRefreshMethod(requestType, "QuoteArgument", value)!));
            Require(EchoRefreshArguments(requestType, probePath, quoted).SequenceEqual(quotedSamples), "Real ProcessStartInfo parsing preserves empty, spaced, quoted, Unicode, and trailing-backslash arguments");
            Expect<ArgumentNullException>(() =>
            {
                CallRefreshMethod(requestType, "QuoteArgument", new object?[] { null });
            }, "Quoting rejects a null argument");
            Expect<ArgumentException>(() =>
            {
                CallRefreshMethod(requestType, "QuoteArgument", "invalid\0argument");
            }, "Quoting rejects embedded NUL before process creation");
            var build = EchoRefreshArguments(requestType, probePath, ReadRefreshProperty<string>(offline, "BuildArguments"));
            Require(build.Length > 1 && build[0] == "build" && build[1] == ReadRefreshProperty<string>(offline, "ProjectPath"), "Real build argument parsing preserves the independent project path containing spaces");
            Require(build.Contains("--disable-build-servers", StringComparer.Ordinal)
                && Array.IndexOf(build, "--configuration") >= 0 && build[Array.IndexOf(build, "--configuration") + 1] == "Release", "The Editor build disables build servers and selects Release");
            foreach (var request in new[] { offline, online })
            {
                var arguments = EchoRefreshArguments(requestType, probePath, ReadRefreshProperty<string>(request, "Arguments"));
                Require(arguments.Length > 1 && arguments[0] == ReadRefreshProperty<string>(request, "AssemblyPath"), "Generation directly invokes the DLL with correctly parsed whitespace paths");
                Require(arguments[1] == "--project" && arguments[2] == project.ProjectRoot
                    && arguments[Array.IndexOf(arguments, "--unity-data") + 1] == project.UnityData
                    && arguments[Array.IndexOf(arguments, "--sdk") + 1] == project.SdkRoot
                    && arguments[Array.IndexOf(arguments, "--docs-cache") + 1] == ReadRefreshProperty<string>(request, "DocumentationCacheDirectory"), "Real project pipeline argument parsing preserves project, pinned Unity, SDK, and persistent cache paths");
                Require(!arguments.Contains("--output", StringComparer.Ordinal) && !arguments.Contains("--namespace", StringComparer.Ordinal)
                    && !arguments.Contains("--input", StringComparer.Ordinal), "The Editor invokes the shared project pipeline that owns generation, compilation, and DLL publication");
                Require(arguments.Contains("--offline-docs", StringComparer.Ordinal) == ReadRefreshProperty<bool>(request, "OfflineDocumentation"), "Project pipeline documentation mode agrees with the captured Editor request");
                var jobs = int.Parse(arguments[Array.IndexOf(arguments, "--jobs") + 1]);
                var documentationJobs = int.Parse(arguments[Array.IndexOf(arguments, "--docs-jobs") + 1]);
                Require(!arguments.Contains("--no-sdk", StringComparer.Ordinal) && !arguments.Contains("--api-level", StringComparer.Ordinal)
                    && !arguments.Contains("--include-package", StringComparer.Ordinal)
                    && jobs >= 1 && jobs <= 1024 && documentationJobs >= 1 && documentationJobs <= 32, "Default project requests retain complete SDK/BCL coverage and bounded worker counts");
            }
            Expect<ArgumentNullException>(() =>
            {
                CallRefreshMethod(requestType, "Create", null, project.UnityData, project.SdkRoot, false);
            }, "Request discovery rejects a null project root");
            Expect<ArgumentException>(() =>
            {
                CallRefreshMethod(requestType, "Create", project.ProjectRoot, project.UnityData, " ", false);
            }, "Request discovery rejects an empty configured SDK");
            Expect<DirectoryNotFoundException>(() =>
            {
                CallRefreshMethod(requestType, "Create", project.ProjectRoot, project.UnityData, Path.Combine(project.ScratchRoot, "missing SDK"), false);
            }, "Request discovery reports a missing configured SDK");
            VerifyRefreshMissingPrerequisites(requestType, project);
            Console.WriteLine("Managed refresh request discovery and real process argument round trips passed; the CLI generator was not launched.");
        }

        /// <summary>Exercises missing input prerequisites and the documented Unity archive fallback.</summary>
        /// <param name="requestType">The actual production request helper type.</param>
        /// <param name="project">The exclusively owned scratch prerequisite files.</param>
        private static void VerifyRefreshMissingPrerequisites(Type requestType, RefreshProject project)
        {
            foreach (var required in new[] { Path.Combine(project.ToolDirectory, "AndroidJavaWrapperGenerator.csproj"), Path.Combine(project.ToolDirectory, "global.json") })
            {
                var hidden = required + ".hidden";
                File.Move(required, hidden);
                try
                {
                    Expect<FileNotFoundException>(() =>
                    {
                        CreateRefreshRequest(requestType, project, true);
                    }, "Request discovery reports the missing standalone project or SDK pin");
                }
                finally
                {
                    File.Move(hidden, required);
                }
            }
            var monoHidden = project.MonoJar + ".hidden";
            File.Move(project.MonoJar, monoHidden);
            try
            {
                var fallback = CreateRefreshRequest(requestType, project, true);
                var inputs = ReadRefreshProperty<IReadOnlyList<string>>(fallback, "Inputs");
                Require(inputs.Contains(project.Il2CppJar, StringComparer.Ordinal) && !inputs.Contains(project.MonoJar, StringComparer.Ordinal), "Missing Mono Release classes.jar selects the real IL2CPP Release path");
                var il2CppHidden = project.Il2CppJar + ".hidden";
                File.Move(project.Il2CppJar, il2CppHidden);
                try
                {
                    Expect<FileNotFoundException>(() =>
                    {
                        CreateRefreshRequest(requestType, project, true);
                    }, "Request discovery reports missing Unity Release archives");
                }
                finally
                {
                    File.Move(il2CppHidden, project.Il2CppJar);
                }
            }
            finally
            {
                File.Move(monoHidden, project.MonoJar);
            }
        }

        /// <summary>Runs only an argument echo process through the production quoting contract.</summary>
        /// <param name="requestType">The actual helper providing process argument quoting.</param>
        /// <param name="probePath">The scratch managed argument echo assembly.</param>
        /// <param name="payload">The actual quoted arguments to pass unchanged.</param>
        /// <returns>The arguments received by the real managed child process.</returns>
        /// <exception cref="InvalidOperationException">The managed argument probe fails or returns invalid output.</exception>
        /// <exception cref="TimeoutException">The argument probe does not finish within fifteen seconds.</exception>
        private static string[] EchoRefreshArguments(Type requestType, string probePath, string payload)
        {
            var configuration = Path.Combine(AppContext.BaseDirectory, Assembly.GetExecutingAssembly().GetName().Name + ".runtimeconfig.json");
            var prefix = "exec --runtimeconfig " + (string)CallRefreshMethod(requestType, "QuoteArgument", configuration)!
                + " " + (string)CallRefreshMethod(requestType, "QuoteArgument", probePath)! + " ";
            var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", prefix + payload)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Cannot start the managed argument echo process.");
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15000))
            {
                process.Kill(true);
                throw new TimeoutException("The managed argument echo process did not finish.");
            }
            var errorText = errors.GetAwaiter().GetResult();
            var lines = output.GetAwaiter().GetResult().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Require(process.ExitCode == 0 && errorText.Length == 0 && lines.All(line => line.StartsWith("ARG:", StringComparison.Ordinal)), "The argument echo child exits successfully with only framed managed output");
            return lines.Select(line => Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(4)))).ToArray();
        }

        /// <summary>Creates discovery fixtures with whitespace paths without requiring Java metadata scanning.</summary>
        private sealed class RefreshProject
        {
            /// <summary>Gets the exclusively owned prerequisite root.</summary>
            internal string ScratchRoot { get; }

            /// <summary>Gets the simulated Unity project root containing the independent tool.</summary>
            internal string ProjectRoot { get; }

            /// <summary>Gets the simulated Unity Editor data directory.</summary>
            internal string UnityData { get; }

            /// <summary>Gets the simulated Editor's bundled Android SDK directory.</summary>
            internal string SdkRoot { get; }

            /// <summary>Gets the simulated independent tool directory.</summary>
            internal string ToolDirectory { get; }

            /// <summary>Gets the preferred Unity Mono Release classes archive.</summary>
            internal string MonoJar { get; }

            /// <summary>Gets the Unity IL2CPP Release fallback archive.</summary>
            internal string Il2CppJar { get; }

            /// <summary>Gets the explicitly authored supported input paths expected from discovery.</summary>
            internal IReadOnlyList<string> ExpectedInputs { get; }

            /// <summary>Creates scratch paths and discovery-only input files.</summary>
            /// <param name="workspace">The exclusively owned harness workspace.</param>
            internal RefreshProject(string workspace)
            {
                ScratchRoot = Path.Combine(workspace, "refresh prerequisites with spaces");
                ProjectRoot = Path.Combine(ScratchRoot, "Unity project with spaces");
                UnityData = Path.Combine(ScratchRoot, "Unity Editor with spaces", "Data");
                var android = Path.Combine(UnityData, "PlaybackEngines", "AndroidPlayer");
                SdkRoot = Path.Combine(android, "SDK");
                ToolDirectory = Path.Combine(ProjectRoot, "Tools", "CSharp", "AndroidJavaWrapperGenerator");
                MonoJar = Path.Combine(android, "Variations", "mono", "Release", "Classes", "classes.jar");
                Il2CppJar = Path.Combine(android, "Variations", "il2cpp", "Release", "Classes", "classes.jar");
                var source = Path.Combine(android, "Source");
                var plugin = Path.Combine(ProjectRoot, "Assets", "Plugins", "Android");
                var expected = new[]
                {
                    Path.Combine(source, "com", "unity3d", "player", "Source with spaces.java"),
                    Path.Combine(source, "precompiled", "Source.class"),
                    Path.Combine(source, "libraries", "Source.JAR"),
                    Path.Combine(plugin, "src", "java", "net", "example", "Plugin with spaces.java"),
                    Path.Combine(plugin, "precompiled", "Plugin.CLASS"),
                    Path.Combine(plugin, "archives", "Plugin with spaces.jar"),
                    Path.Combine(plugin, "archives", "Android library with spaces.aar"),
                    MonoJar
                };
                foreach (var path in expected.Concat(new[] { Il2CppJar, Path.Combine(SdkRoot, "platforms", "android-36", "android.jar") }))
                {
                    WriteFixture(path, "Managed request discovery fixture; these bytes are not scanned.\n");
                }
                WriteFixture(Path.Combine(ToolDirectory, "AndroidJavaWrapperGenerator.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
                WriteFixture(Path.Combine(ToolDirectory, "global.json"), "{\"sdk\":{\"version\":\"9.0.100\"}}\n");
                WriteFixture(Path.Combine(plugin, "README.md"), "Ignored non-Java asset.\n");
                WriteFixture(Path.Combine(plugin, "Plugin.java.meta"), "Ignored Unity metadata.\n");
                WriteFixture(Path.Combine(plugin, "Generated.cs"), "Ignored generated C# asset.\n");
                WriteFixture(Path.Combine(ProjectRoot, "Assets", "Elsewhere", "Excluded.java"), "Outside the watched Java plugin root.\n");
                ExpectedInputs = Array.AsReadOnly(expected);
            }

            /// <summary>Writes one discovery-only file beneath this test's scratch prerequisite paths.</summary>
            /// <param name="path">The absolute scratch file path.</param>
            /// <param name="contents">The independently authored discovery fixture contents.</param>
            private static void WriteFixture(string path, string contents)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, contents, new UTF8Encoding(false));
            }
        }

        /// <summary>Provides a managed argument echo executable independent of the production quoting algorithm.</summary>
        private const string RefreshArgumentEchoSource = @"
using System;
using System.Text;

#nullable enable

namespace RefreshArgumentEcho
{
    /// <summary>Reports the arguments received from the operating system's process parser.</summary>
    internal static class Program
    {
        /// <summary>Writes each literal argument as one framed UTF-8 base64 line.</summary>
        /// <param name=""arguments"">The literal arguments passed by ProcessStartInfo.</param>
        /// <returns>Zero after all received arguments have been reported.</returns>
        private static int Main(string[] arguments)
        {
            foreach (var argument in arguments)
            {
                Console.WriteLine(""ARG:"" + Convert.ToBase64String(Encoding.UTF8.GetBytes(argument)));
            }
            return 0;
        }
    }
}
";
        /// <summary>Defines only the Editor surface needed by actual queue and domain callbacks.</summary>
        /// <remarks>Progress execution and asset publication reject accidental calls before any process can start.</remarks>
        private const string RefreshEditorDoubleSource = @"
using System;
using System.Collections.Generic;

#nullable enable

namespace UnityEngine
{
    /// <summary>Supplies only the scratch project path used by request capture.</summary>
    public static class Application
    {
        /// <summary>Gets or sets the controlled scratch Assets directory.</summary>
        public static string dataPath { get; set; } = string.Empty;
    }

    /// <summary>Records controller messages without accessing a native Unity logging service.</summary>
    public static class Debug
    {
        /// <summary>Gets managed messages observed during controller callbacks.</summary>
        public static List<string> Messages { get; } = new List<string>();

        /// <summary>Records an informational controller message.</summary>
        /// <param name=""message"">The informational message.</param>
        public static void Log(object? message)
        {
            Messages.Add(message?.ToString() ?? string.Empty);
        }

        /// <summary>Records a warning controller message.</summary>
        /// <param name=""message"">The warning message.</param>
        public static void LogWarning(object? message)
        {
            Messages.Add(message?.ToString() ?? string.Empty);
        }

        /// <summary>Records an error controller message.</summary>
        /// <param name=""message"">The error message.</param>
        public static void LogError(object? message)
        {
            Messages.Add(message?.ToString() ?? string.Empty);
        }
    }
}

namespace UnityEditor
{
    /// <summary>Allows the actual static initialization annotation to compile.</summary>
    public sealed class InitializeOnLoadAttribute : Attribute
    {
    }

    /// <summary>Allows actual menu declarations to compile without a native Editor menu.</summary>
    public sealed class MenuItem : Attribute
    {
        /// <summary>Accepts the actual menu declaration metadata.</summary>
        /// <param name=""itemName"">The actual menu path.</param>
        /// <param name=""isValidateFunction"">Whether the declared callback validates a menu command.</param>
        public MenuItem(string itemName, bool isValidateFunction = false)
        {
        }
    }

    /// <summary>Provides the inheritance surface for actual importer callback code.</summary>
    public class AssetPostprocessor
    {
    }

    /// <summary>Retains only Editor session booleans across independently loaded controller domains.</summary>
    public static class SessionState
    {
        /// <summary>Stores the independent session state outside controller assemblies.</summary>
        private static readonly Dictionary<string, bool> s_values = new Dictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>Reads a persisted boolean or returns its caller-provided default.</summary>
        /// <param name=""key"">The persisted Editor session key.</param>
        /// <param name=""defaultValue"">The value for an absent key.</param>
        /// <returns>The stored boolean or the supplied default.</returns>
        public static bool GetBool(string key, bool defaultValue)
        {
            return s_values.TryGetValue(key, out var value) ? value : defaultValue;
        }

        /// <summary>Persists a boolean in the fake Editor session.</summary>
        /// <param name=""key"">The persisted Editor session key.</param>
        /// <param name=""value"">The boolean to preserve across a controller reload.</param>
        public static void SetBool(string key, bool value)
        {
            s_values[key] = value;
        }

        /// <summary>Removes a consumed or abandoned Editor session key.</summary>
        /// <param name=""key"">The key to remove.</param>
        public static void EraseBool(string key)
        {
            s_values.Remove(key);
        }

        /// <summary>Reports whether a session key remains stored.</summary>
        /// <param name=""key"">The session key to inspect.</param>
        /// <returns>True when the fake Editor still holds the key.</returns>
        public static bool HasBool(string key)
        {
            return s_values.ContainsKey(key);
        }

        /// <summary>Begins a new fake Editor session.</summary>
        public static void Clear()
        {
            s_values.Clear();
        }
    }

    /// <summary>Supplies controlled Editor busy states and independently delivered lifecycle events.</summary>
    public static class EditorApplication
    {
        /// <summary>Occurs when the managed driver delivers an Editor update.</summary>
        public static event Action? update;

        /// <summary>Occurs when the managed driver delivers Editor quit.</summary>
        public static event Action? quitting;

        /// <summary>Gets or sets the fake script compilation guard.</summary>
        public static bool isCompiling { get; set; }

        /// <summary>Gets or sets the fake asset import guard.</summary>
        public static bool isUpdating { get; set; }

        /// <summary>Gets or sets the fake play mode transition guard.</summary>
        public static bool isPlayingOrWillChangePlaymode { get; set; }

        /// <summary>Gets or sets the simulated Editor's data directory.</summary>
        public static string applicationContentsPath { get; set; } = string.Empty;

        /// <summary>Gets the independently observed update subscription count.</summary>
        public static int UpdateSubscriberCount => update?.GetInvocationList().Length ?? 0;

        /// <summary>Gets the independently observed quit subscription count.</summary>
        public static int QuitSubscriberCount => quitting?.GetInvocationList().Length ?? 0;

        /// <summary>Delivers a managed Editor update to the actual registered controller.</summary>
        public static void Tick()
        {
            update?.Invoke();
        }

        /// <summary>Delivers a managed Editor quit to the actual registered controller.</summary>
        public static void Quit()
        {
            quitting?.Invoke();
        }

        /// <summary>Discards old domain delegates while preserving Editor session state.</summary>
        public static void ClearCallbacks()
        {
            update = null;
            quitting = null;
        }
    }

    /// <summary>Delivers a reload boundary independently of the actual controller implementation.</summary>
    public static class AssemblyReloadEvents
    {
        /// <summary>Occurs before the managed driver replaces the controller's static state.</summary>
        public static event Action? beforeAssemblyReload;

        /// <summary>Gets the independently observed reload subscription count.</summary>
        public static int SubscriberCount => beforeAssemblyReload?.GetInvocationList().Length ?? 0;

        /// <summary>Delivers the registered actual controller reload callback.</summary>
        public static void Reload()
        {
            beforeAssemblyReload?.Invoke();
        }

        /// <summary>Discards delegates belonging to a previous controller domain.</summary>
        public static void ClearCallbacks()
        {
            beforeAssemblyReload = null;
        }
    }

    /// <summary>Supplies the controlled Player build guard used by the real update callback.</summary>
    public static class BuildPipeline
    {
        /// <summary>Gets or sets whether the simulated Editor is building a Player.</summary>
        public static bool isBuildingPlayer { get; set; }
    }

    /// <summary>Rejects any asset operation during queue-only managed verification.</summary>
    public static class AssetDatabase
    {
        /// <summary>Gets the number of forbidden asset operations attempted by controller callbacks.</summary>
        public static int Calls { get; private set; }

        /// <summary>Rejects an unexpected asset refresh suspension.</summary>
        /// <exception cref=""InvalidOperationException"">Publication was attempted in a queue-only test.</exception>
        public static void DisallowAutoRefresh()
        {
            Reject();
        }

        /// <summary>Rejects an unexpected asset refresh resumption.</summary>
        /// <exception cref=""InvalidOperationException"">Publication was attempted in a queue-only test.</exception>
        public static void AllowAutoRefresh()
        {
            Reject();
        }

        /// <summary>Rejects unexpected asset editing.</summary>
        /// <exception cref=""InvalidOperationException"">Publication was attempted in a queue-only test.</exception>
        public static void StartAssetEditing()
        {
            Reject();
        }

        /// <summary>Rejects unexpected completion of asset editing.</summary>
        /// <exception cref=""InvalidOperationException"">Publication was attempted in a queue-only test.</exception>
        public static void StopAssetEditing()
        {
            Reject();
        }

        /// <summary>Rejects an unexpected generated asset refresh.</summary>
        /// <exception cref=""InvalidOperationException"">Publication was attempted in a queue-only test.</exception>
        public static void Refresh()
        {
            Reject();
        }

        /// <summary>Records and rejects attempts to cross the queue-only publication boundary.</summary>
        /// <exception cref=""InvalidOperationException"">A native asset operation would have been required.</exception>
        private static void Reject()
        {
            Calls++;
            throw new InvalidOperationException(""Queue-only managed validation attempted asset publication."");
        }
    }

    /// <summary>Prevents queue-only checks from launching any generator or build process.</summary>
    public static class Progress
    {
        /// <summary>Provides only the progress option used by the actual controller.</summary>
        public enum Options
        {
            /// <summary>Identifies progress without a predicted total duration.</summary>
            Indefinite
        }

        /// <summary>Provides only the terminal progress states used by the actual controller.</summary>
        public enum Status
        {
            /// <summary>Identifies a completed generation.</summary>
            Succeeded,
            /// <summary>Identifies a failed generation.</summary>
            Failed,
            /// <summary>Identifies canceled generation work.</summary>
            Canceled
        }

        /// <summary>Gets the number of forbidden execution attempts.</summary>
        public static int StartedCount { get; private set; }

        /// <summary>Rejects execution before the controller reaches process creation.</summary>
        /// <param name=""name"">The actual progress title.</param>
        /// <param name=""description"">The actual build description.</param>
        /// <param name=""options"">The actual progress options.</param>
        /// <returns>No identifier because execution is forbidden in queue-only tests.</returns>
        /// <exception cref=""InvalidOperationException"">The controller tried to execute a queued generation.</exception>
        public static int Start(string name, string description, Options options)
        {
            StartedCount++;
            throw new InvalidOperationException(""Queue-only validation attempted process execution."");
        }

        /// <summary>Accepts the production cancellation registration signature.</summary>
        /// <param name=""id"">The progress identifier.</param>
        /// <param name=""callback"">The actual controller cancellation callback.</param>
        public static void RegisterCancelCallback(int id, Func<bool> callback)
        {
        }

        /// <summary>Accepts production progress reports without a native progress service.</summary>
        /// <param name=""id"">The progress identifier.</param>
        /// <param name=""fraction"">The reported completion fraction.</param>
        /// <param name=""description"">The reported description.</param>
        public static void Report(int id, float fraction, string description)
        {
        }

        /// <summary>Accepts production progress cleanup without a native progress service.</summary>
        /// <param name=""id"">The progress identifier.</param>
        /// <param name=""status"">The terminal progress status.</param>
        public static void Finish(int id, Status status)
        {
        }

        /// <summary>Begins a new controller domain observation.</summary>
        public static void Reset()
        {
            StartedCount = 0;
        }
    }
}
";
    }
}
