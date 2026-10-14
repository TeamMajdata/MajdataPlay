using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

#nullable enable

namespace MajdataPlay.Tests.AndroidJavaWrapperGeneratorValidation
{
    /// <summary>Checks production consumers against the complete replacement Java binding API.</summary>
    internal static partial class Program
    {
        /// <summary>Compiles migrated Android consumers and the device validation script against the independently built wrapper DLL.</summary>
        /// <param name="settings">The pinned Unity modules and standard reference pack.</param>
        /// <param name="projectRoot">The production repository containing the consumers and Java device probe.</param>
        /// <param name="libraryBytes">The actual compiled wrapper DLL, including handwritten partial extensions.</param>
        /// <exception cref="IOException">A production source or reference assembly cannot be read.</exception>
        /// <exception cref="InvalidOperationException">A consumer no longer compiles against the generated API.</exception>
        private static void VerifyMigratedConsumers(ValidationSettings settings, string projectRoot, byte[] libraryBytes)
        {
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            var androidDirectory = Path.Combine(projectRoot, "Assets", "Plugins", "MajdataPlay", "Platform", "Android");
            var sourcePaths = new List<string>
            {
                Path.Combine(androidDirectory, "AndroidRuntime.cs"),
                Path.Combine(androidDirectory, "IO", "KeyCode.cs"),
                Path.Combine(projectRoot, "Tools", "Tests", "AndroidStorageDeviceValidation", "AndroidStorageDeviceValidation.cs")
            };
            sourcePaths.AddRange(Directory.GetFiles(Path.Combine(androidDirectory, "Storage"), "*.cs"));
            sourcePaths.AddRange(Directory.GetFiles(Path.Combine(projectRoot, "Assets", "Plugins", "MajdataPlay", "IO", "Storage"), "*.cs"));
            foreach (var path in sourcePaths.OrderBy(path => path, StringComparer.Ordinal))
            {
                sources.Add(path, File.ReadAllText(path));
            }
            sources.Add("ConsumerDependencies.cs", ConsumerDependencySource);

            var references = GetRefreshStandardReferences(settings);
            references.Add(MetadataReference.CreateFromImage(libraryBytes));
            foreach (var module in new[] { "CoreModule", "AndroidJNIModule", "IMGUIModule", "JSONSerializeModule" })
            {
                references.Add(MetadataReference.CreateFromFile(Path.Combine(settings.UnityData, "Managed", "UnityEngine", "UnityEngine." + module + ".dll")));
            }
            foreach (var symbols in new[] { new[] { "UNITY_EDITOR" }, new[] { "UNITY_EDITOR", "UNITY_ANDROID" }, new[] { "UNITY_ANDROID" } })
            {
                var trees = ParseRefreshSources(sources, symbols);
                var compilation = CSharpCompilation.Create("MigratedConsumers" + Guid.NewGuid().ToString("N"), trees, references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
                var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
                if (errors.Length != 0)
                {
                    throw new InvalidOperationException("Migrated consumers fail for " + string.Join(", ", symbols) + ":"
                        + Environment.NewLine + string.Join(Environment.NewLine, errors.Take(80).Select(diagnostic => diagnostic.ToString())));
                }
                Require(true, "Production Android consumers and device validation compile against the independent wrapper DLL: " + string.Join(", ", symbols));
            }
            VerifyStorageProtocolConstants(projectRoot, androidDirectory);
        }

        /// <summary>Checks managed compile-time storage constants against the independent Java bridge protocol.</summary>
        /// <param name="projectRoot">The repository containing the production Java bridge.</param>
        /// <param name="androidDirectory">The production managed Android directory.</param>
        /// <exception cref="InvalidOperationException">The managed protocol differs from the Java source.</exception>
        private static void VerifyStorageProtocolConstants(string projectRoot, string androidDirectory)
        {
            var javaSource = File.ReadAllText(Path.Combine(projectRoot, "Assets", "Plugins", "Android", "src", "java", "net", "majdata", "majdataplay", "StorageAccess.java"));
            var managedSource = File.ReadAllText(Path.Combine(androidDirectory, "Storage", "AndroidDocumentBridge.cs"));
            var names = new Dictionary<string, string>
            {
                { "SUCCESS", "Success" },
                { "INVALID_ARGUMENT", "InvalidArgument" },
                { "NOT_FOUND", "NotFound" },
                { "DIRECTORY_NOT_FOUND", "DirectoryNotFound" },
                { "ACCESS_DENIED", "AccessDenied" },
                { "UNSUPPORTED", "Unsupported" },
                { "MAX_TRANSFER_SIZE", "MaxTransferSize" }
            };
            foreach (var name in names)
            {
                var javaValue = Regex.Match(javaSource, @"public static final int " + name.Key + @"\s*=\s*(\d+);");
                var managedValue = Regex.Match(managedSource, @"const int " + name.Value + @"\s*=\s*(\d+);");
                Require(javaValue.Success && managedValue.Success && javaValue.Groups[1].Value == managedValue.Groups[1].Value,
                    "Managed storage protocol matches production Java: " + name.Key);
            }
        }

        /// <summary>Substitutes only unrelated logging and keyboard initialization during consumer compilation.</summary>
        private const string ConsumerDependencySource = @"
#nullable enable
namespace MajdataPlay.Diagnostics
{
    /// <summary>Provides compilation-only diagnostic entry points.</summary>
    public static class MajDebug
    {
        /// <summary>Accepts one diagnostic message.</summary>
        /// <param name=""message"">The diagnostic text.</param>
        public static void LogInfo(string message)
        {
        }
        /// <summary>Accepts a tagged diagnostic value.</summary>
        /// <typeparam name=""T"">The diagnostic value type.</typeparam>
        /// <param name=""tag"">The component supplying the diagnostic value.</param>
        /// <param name=""obj"">The diagnostic value.</param>
        public static void LogInfo<T>(string tag, T obj)
        {
        }
        /// <summary>Accepts one failure message.</summary>
        /// <param name=""message"">The diagnostic text.</param>
        public static void LogError(string message)
        {
        }
    }
}
namespace MajdataPlay.Platform.Android.IO
{
    /// <summary>Replaces unrelated keyboard registration during compilation.</summary>
    public static class AndroidKeyboard
    {
        /// <summary>Provides the initialization entry point used by AndroidRuntime.</summary>
        internal static void Init()
        {
        }

        /// <summary>Provides the production key-state query used by the device harness.</summary>
        /// <param name=""keyCode"">The Android key whose pressed state is queried.</param>
        /// <returns>False because this compilation-only substitute receives no device callbacks.</returns>
        public static bool IsPressed(KeyCode keyCode)
        {
            return false;
        }
    }
}";
    }
}
