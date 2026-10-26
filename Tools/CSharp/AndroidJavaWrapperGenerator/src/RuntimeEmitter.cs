using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>
    /// Emits the checked-in, self-contained Unity JNI runtime embedded in this executable.
    /// </summary>
    internal static class RuntimeEmitter
    {
        /// <summary>
        /// Identifies the runtime sources and their corresponding embedded template names.
        /// </summary>
        private static readonly string[] s_templateNames =
        {
            "AndroidJni",
            "JavaInvocationException",
            "IJavaObject",
            "JavaObjectReference",
            "JavaWrapperConverter",
            "AndroidJavaObjectExtensions"
        };

        /// <summary>
        /// Expands embedded runtime templates into sources at the output root.
        /// </summary>
        /// <param name="rootNamespace">The validated namespace containing generated runtime support types.</param>
        /// <returns>A read-only map from relative output paths to complete CRLF C# source text.</returns>
        /// <exception cref="ArgumentException">The root namespace is null, empty, or whitespace.</exception>
        /// <exception cref="InvalidOperationException">A required embedded runtime template is missing.</exception>
        /// <exception cref="IOException">An embedded template cannot be read.</exception>
        internal static IReadOnlyDictionary<string, string> Generate(string rootNamespace)
        {
            if (string.IsNullOrWhiteSpace(rootNamespace))
            {
                throw new ArgumentException("A generated root namespace is required.", nameof(rootNamespace));
            }

            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            var assembly = typeof(RuntimeEmitter).Assembly;
            foreach (var name in s_templateNames)
            {
                var resourceName = typeof(RuntimeEmitter).Namespace + ".Templates.Runtime." + name + ".cs.txt";
                using var stream = assembly.GetManifestResourceStream(resourceName)
                    ?? throw new InvalidOperationException("Missing embedded runtime template: " + resourceName);
                using var reader = new StreamReader(stream, Encoding.UTF8, true);
                var source = reader.ReadToEnd().Replace("__ROOT_NAMESPACE__", rootNamespace);
                source = source.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
                sources.Add(name + ".cs", source);
            }

            return new ReadOnlyDictionary<string, string>(sources);
        }
    }
}
