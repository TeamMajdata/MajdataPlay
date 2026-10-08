#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace MajdataPlay.SourceGenerators.AndroidJava
{
    /// <summary>Runs the Java source-file helper and parses its versioned, entity-free XML protocol.</summary>
    internal static class JavaApiExtractor
    {
        /// <summary>Extracts all requested types with one process for a resolved configuration.</summary>
        /// <param name="configuration">The validated extraction inputs.</param>
        /// <param name="binaryNames">The requested Java binary names.</param>
        /// <param name="cancellationToken">The compilation cancellation token.</param>
        /// <returns>The freshly extracted metadata indexed by exact Java binary name.</returns>
        /// <exception cref="GeneratorException">The helper fails, times out, or emits invalid metadata.</exception>
        /// <exception cref="OperationCanceledException">Compilation has been canceled.</exception>
        internal static IReadOnlyDictionary<string, JavaApiType> Extract(JavaApiConfiguration configuration,
            IEnumerable<string> binaryNames, CancellationToken cancellationToken)
        {
            var names = binaryNames.Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            var arguments = new List<string> { configuration.ExtractorPath, "--android-jar", configuration.AndroidJar };
            AddArguments(arguments, "--source", configuration.Sources);
            AddArguments(arguments, "--classpath", configuration.ClassPath);
            AddArguments(arguments, "--documentation", configuration.DocumentationPaths);
            AddArguments(arguments, "--type", names);
            arguments.Add("--include-inherited");
            arguments.Add(configuration.IncludeInheritedMembers ? "true" : "false");
            var output = Run(configuration, arguments, cancellationToken);
            var metadata = Parse(output, cancellationToken);
            foreach (var name in names)
            {
                if (!metadata.ContainsKey(name))
                {
                    throw new GeneratorException(GeneratorDiagnostics.InvalidMetadata,
                        "Java helper omitted requested type '" + name + "'. No cached metadata is used.");
                }
            }
            return metadata;
        }

        /// <summary>Parses the helper's version-one protocol with DTDs and external entities disabled.</summary>
        /// <param name="xml">The complete, successful helper stdout.</param>
        /// <param name="cancellationToken">The compilation cancellation token.</param>
        /// <returns>The parsed Java types.</returns>
        /// <exception cref="GeneratorException">The XML or metadata is invalid.</exception>
        /// <exception cref="OperationCanceledException">Compilation has been canceled.</exception>
        internal static IReadOnlyDictionary<string, JavaApiType> Parse(string xml, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            XDocument document;
            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = 32L * 1024 * 1024,
                    MaxCharactersFromEntities = 1024
                };
                using (var input = new StringReader(xml))
                using (var reader = XmlReader.Create(input, settings))
                {
                    document = XDocument.Load(reader, LoadOptions.None);
                }
            }
            catch (XmlException exception)
            {
                throw Invalid("Java helper emitted invalid XML: " + exception.Message);
            }
            var root = document.Root;
            if (root == null || root.Name != "java-api" || (string?)root.Attribute("version") != "1")
            {
                throw Invalid("Expected <java-api version=\"1\"> from the Java helper.");
            }
            var result = new Dictionary<string, JavaApiType>(StringComparer.Ordinal);
            foreach (var element in root.Elements())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (element.Name != "type")
                {
                    throw Invalid("Unexpected Java API element '" + element.Name + "'.");
                }
                var type = new JavaApiType
                {
                    Name = Required(element, "name"),
                    IsInterface = Boolean(element, "interface"),
                    IsAbstract = Boolean(element, "abstract"),
                    IsFinal = Boolean(element, "final")
                };
                ValidateBinaryName(type.Name);
                ReadDocumentation(element, type);
                if (result.ContainsKey(type.Name))
                {
                    throw Invalid("Java helper emitted duplicate type '" + type.Name + "'.");
                }
                foreach (var member in element.Elements())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (member.Name == "field")
                    {
                        type.Fields.Add(ReadField(member, type.Name));
                    }
                    else if (member.Name == "method")
                    {
                        type.Methods.Add(ReadMethod(member, type.Name));
                    }
                    else if (member.Name == "nestedType")
                    {
                        var nested = Required(member, "name");
                        ValidateBinaryName(nested);
                        type.NestedTypes.Add(nested);
                    }
                    else
                    {
                        throw Invalid("Unexpected member element '" + member.Name + "' in '" + type.Name + "'.");
                    }
                }
                result.Add(type.Name, type);
            }
            return result;
        }

        /// <summary>Quotes one argument using ProcessStartInfo's portable argument parsing rules.</summary>
        /// <param name="argument">The exact argument to pass, including spaces or trailing backslashes.</param>
        /// <returns>The escaped, double-quoted command-line argument.</returns>
        /// <exception cref="GeneratorException">The argument contains NUL or a line break.</exception>
        internal static string QuoteArgument(string argument)
        {
            if (argument.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0)
            {
                throw JavaApiConfiguration.Invalid("A Java helper argument contains NUL or a line break.");
            }
            var result = new StringBuilder("\"");
            var backslashes = 0;
            foreach (var character in argument)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (character == '"')
                {
                    result.Append('\\', (backslashes * 2) + 1);
                    result.Append('"');
                }
                else
                {
                    result.Append('\\', backslashes);
                    result.Append(character);
                }
                backslashes = 0;
            }
            result.Append('\\', backslashes * 2);
            result.Append('"');
            return result.ToString();
        }

        /// <summary>Runs Java directly, concurrently draining bounded stdout and stderr without a shell.</summary>
        /// <param name="configuration">The process configuration and timeout.</param>
        /// <param name="arguments">The individual helper arguments.</param>
        /// <param name="cancellationToken">The compilation cancellation token.</param>
        /// <returns>Fresh stdout only if the helper exits successfully.</returns>
        /// <exception cref="GeneratorException">The process cannot run or exits unsuccessfully.</exception>
        /// <exception cref="OperationCanceledException">Compilation has been canceled.</exception>
        private static string Run(JavaApiConfiguration configuration, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = new ProcessStartInfo
            {
                FileName = configuration.JavaExecutable,
                Arguments = string.Join(" ", arguments.Select(QuoteArgument)),
                WorkingDirectory = configuration.ProjectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false, true),
                StandardErrorEncoding = new UTF8Encoding(false, false)
            };
            using (var process = new Process { StartInfo = start })
            {
                Task<string>? stdout = null;
                Task<string>? stderr = null;
                var started = false;
                try
                {
                    started = process.Start();
                    if (!started)
                    {
                        throw Failed("The Java helper process did not start.");
                    }
                    stdout = ReadBoundedAsync(process.StandardOutput, 32 * 1024 * 1024, "stdout");
                    stderr = ReadBoundedAsync(process.StandardError, 1024 * 1024, "stderr");
                    var watch = Stopwatch.StartNew();
                    while (!process.WaitForExit(50))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ThrowReadFailure(stdout, stderr);
                        if (watch.ElapsedMilliseconds >= configuration.TimeoutMilliseconds)
                        {
                            throw Failed("Java API extraction exceeded " +
                                (configuration.TimeoutMilliseconds / 1000).ToString(CultureInfo.InvariantCulture) +
                                " seconds. Adjust MAJDATA_JAVA_EXTRACTOR_TIMEOUT_SECONDS if necessary.");
                        }
                    }
                    while (!stdout.IsCompleted || !stderr.IsCompleted)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ThrowReadFailure(stdout, stderr);
                        if (watch.ElapsedMilliseconds >= configuration.TimeoutMilliseconds)
                        {
                            throw Failed("Java helper output did not close before the extraction timeout.");
                        }
                        Thread.Sleep(10);
                    }
                    var output = stdout.GetAwaiter().GetResult();
                    var errors = stderr.GetAwaiter().GetResult();
                    cancellationToken.ThrowIfCancellationRequested();
                    if (process.ExitCode != 0)
                    {
                        throw Failed("Java API helper exited with code " + process.ExitCode.ToString(CultureInfo.InvariantCulture) +
                            ": " + LimitDiagnostic(errors.Length == 0 ? output : errors));
                    }
                    if (string.IsNullOrWhiteSpace(output))
                    {
                        throw Failed("Java API helper returned no metadata. " + LimitDiagnostic(errors));
                    }
                    return output;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (GeneratorException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw Failed("Cannot run Java API helper '" + configuration.ExtractorPath + "' with '" +
                        configuration.JavaExecutable + "': " + exception.Message);
                }
                finally
                {
                    if (started)
                    {
                        StopProcess(process);
                    }
                    Observe(stdout);
                    Observe(stderr);
                }
            }
        }

        /// <summary>Reads a redirected stream asynchronously with a bounded memory budget.</summary>
        /// <param name="reader">The redirected process stream reader.</param>
        /// <param name="limit">The maximum accepted character count.</param>
        /// <param name="streamName">The stream name used in diagnostics.</param>
        /// <returns>The complete stream content.</returns>
        /// <exception cref="GeneratorException">The helper exceeds the output limit.</exception>
        /// <exception cref="IOException">The redirected stream cannot be read.</exception>
        private static async Task<string> ReadBoundedAsync(StreamReader reader, int limit, string streamName)
        {
            var buffer = new char[4096];
            var output = new StringBuilder();
            while (true)
            {
                var count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (count == 0)
                {
                    return output.ToString();
                }
                if (count > limit - output.Length)
                {
                    throw Failed("Java helper " + streamName + " exceeds its safe output limit.");
                }
                output.Append(buffer, 0, count);
            }
        }

        /// <summary>Surfaces read failures before blocked process pipes can deadlock extraction.</summary>
        /// <param name="stdout">The stdout read task.</param>
        /// <param name="stderr">The stderr read task.</param>
        /// <exception cref="Exception">One of the process stream reads has failed.</exception>
        private static void ThrowReadFailure(Task<string> stdout, Task<string> stderr)
        {
            if (stdout.IsFaulted)
            {
                stdout.GetAwaiter().GetResult();
            }
            if (stderr.IsFaulted)
            {
                stderr.GetAwaiter().GetResult();
            }
        }

        /// <summary>Terminates an active helper on cancellation or timeout without hiding the original failure.</summary>
        /// <param name="process">The helper process.</param>
        private static void StopProcess(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(2000);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception || exception is NotSupportedException)
            {
                // Preserve the original extraction diagnostic if the process has already exited.
            }
        }

        /// <summary>Observes eventual read-task failures after process teardown.</summary>
        /// <param name="task">A redirected stream read task, if it was started.</param>
        private static void Observe(Task<string>? task)
        {
            if (task == null)
            {
                return;
            }
            task.ContinueWith(completed =>
            {
                var ignored = completed.Exception;
                GC.KeepAlive(ignored);
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        /// <summary>Appends repeated flag/value pairs to a helper command.</summary>
        /// <param name="arguments">The command argument list.</param>
        /// <param name="flag">The helper flag.</param>
        /// <param name="values">The ordered values.</param>
        private static void AddArguments(List<string> arguments, string flag, IEnumerable<string> values)
        {
            foreach (var value in values)
            {
                arguments.Add(flag);
                arguments.Add(value);
            }
        }

        /// <summary>Reads and validates one public Java field.</summary>
        /// <param name="element">The protocol field element.</param>
        /// <param name="owner">The requested type's binary name.</param>
        /// <returns>The validated field metadata.</returns>
        /// <exception cref="GeneratorException">The field metadata is invalid.</exception>
        private static JavaApiField ReadField(XElement element, string owner)
        {
            var field = new JavaApiField
            {
                Name = Required(element, "name"),
                Descriptor = Required(element, "descriptor"),
                DeclaringType = (string?)element.Attribute("declaringType") ?? owner,
                IsStatic = Boolean(element, "static"),
                IsFinal = Boolean(element, "final"),
                ConstantKind = (string?)element.Attribute("constantKind") ?? string.Empty,
                ConstantValue = ReadConstantValue(element)
            };
            ValidateBinaryName(field.DeclaringType);
            JavaDescriptors.ParseField(field.Descriptor);
            ReadDocumentation(element, field);
            return field;
        }

        /// <summary>Decodes the helper's lossless constant extension for XML-inexpressible Java Strings.</summary>
        /// <param name="element">The field element containing optional constant attributes.</param>
        /// <returns>The original constant text, or null when no compile-time constant exists.</returns>
        /// <exception cref="GeneratorException">The encoding or encoded UTF-16 value is invalid.</exception>
        private static string? ReadConstantValue(XElement element)
        {
            var value = (string?)element.Attribute("constantValue");
            var encoding = (string?)element.Attribute("constantEncoding");
            if (encoding == null)
            {
                return value;
            }
            if (encoding != "base64-utf16be" || value == null || (string?)element.Attribute("descriptor") != "Ljava/lang/String;")
            {
                throw Invalid("Unsupported Java constant encoding '" + encoding + "'.");
            }
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(value);
            }
            catch (FormatException)
            {
                throw Invalid("A Java String constant has malformed base64-utf16be data.");
            }
            if ((bytes.Length & 1) != 0)
            {
                throw Invalid("A Java String constant has truncated UTF-16 data.");
            }
            var characters = new char[bytes.Length / 2];
            for (var index = 0; index < characters.Length; index++)
            {
                characters[index] = (char)((bytes[index * 2] << 8) | bytes[(index * 2) + 1]);
            }
            return new string(characters);
        }

        /// <summary>Reads and validates one method or constructor.</summary>
        /// <param name="element">The protocol method element.</param>
        /// <param name="owner">The requested type's binary name.</param>
        /// <returns>The validated method metadata.</returns>
        /// <exception cref="GeneratorException">The method metadata is invalid.</exception>
        private static JavaApiMethod ReadMethod(XElement element, string owner)
        {
            var method = new JavaApiMethod
            {
                Name = Required(element, "name"),
                Descriptor = Required(element, "descriptor"),
                DeclaringType = (string?)element.Attribute("declaringType") ?? owner,
                IsStatic = Boolean(element, "static"),
                Returns = (string?)element.Attribute("returns") ?? string.Empty
            };
            ValidateBinaryName(method.DeclaringType);
            var signature = JavaDescriptors.ParseMethod(method.Descriptor);
            if (method.Name == "<clinit>" || (method.Name == "<init>" && (signature.ReturnType.Code != 'V' || method.IsStatic)))
            {
                throw Invalid("Invalid public Java method '" + method.Name + method.Descriptor + "'.");
            }
            ReadDocumentation(element, method);
            foreach (var child in element.Elements())
            {
                if (child.Name == "parameter")
                {
                    method.Parameters.Add(new JavaApiParameter
                    {
                        Name = (string?)child.Attribute("name") ?? string.Empty,
                        Summary = (string?)child.Attribute("summary") ?? string.Empty
                    });
                }
                else if (child.Name == "exception")
                {
                    var name = Required(child, "type");
                    ValidateBinaryName(name);
                    method.Exceptions.Add(new JavaApiException
                    {
                        Type = name,
                        Summary = (string?)child.Attribute("summary") ?? string.Empty
                    });
                }
                else
                {
                    throw Invalid("Unexpected method element '" + child.Name + "'.");
                }
            }
            if (method.Parameters.Count > signature.Parameters.Count)
            {
                throw Invalid("Java method '" + method.Name + method.Descriptor + "' has more parameter names than descriptor parameters.");
            }
            return method;
        }

        /// <summary>Reads documentation common to every protocol declaration.</summary>
        /// <param name="element">The declaration element.</param>
        /// <param name="documentation">The documentation metadata to populate.</param>
        /// <exception cref="GeneratorException">The deprecation flag is invalid.</exception>
        private static void ReadDocumentation(XElement element, JavaDocumentation documentation)
        {
            documentation.Summary = (string?)element.Attribute("summary") ?? string.Empty;
            documentation.DocumentationUrl = (string?)element.Attribute("documentationUrl") ?? string.Empty;
            documentation.Deprecated = Boolean(element, "deprecated");
        }

        /// <summary>Reads a required, nonempty protocol attribute.</summary>
        /// <param name="element">The element containing the attribute.</param>
        /// <param name="name">The required attribute name.</param>
        /// <returns>The attribute value.</returns>
        /// <exception cref="GeneratorException">The attribute is missing or empty.</exception>
        private static string Required(XElement element, string name)
        {
            var value = (string?)element.Attribute(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw Invalid("Java helper element '" + element.Name + "' is missing attribute '" + name + "'.");
            }
            return value!;
        }

        /// <summary>Reads a boolean protocol attribute, defaulting an absent flag to false.</summary>
        /// <param name="element">The protocol element.</param>
        /// <param name="name">The boolean attribute name.</param>
        /// <returns>The parsed flag.</returns>
        /// <exception cref="GeneratorException">The flag is not true or false.</exception>
        private static bool Boolean(XElement element, string name)
        {
            var value = (string?)element.Attribute(name);
            if (value == null)
            {
                return false;
            }
            if (value == "true")
            {
                return true;
            }
            if (value == "false")
            {
                return false;
            }
            throw Invalid("Invalid boolean '" + value + "' for Java helper attribute '" + name + "'.");
        }

        /// <summary>Validates a declared Java binary name.</summary>
        /// <param name="name">The name to validate.</param>
        /// <exception cref="GeneratorException">The name is invalid.</exception>
        private static void ValidateBinaryName(string name)
        {
            if (!JavaDescriptors.IsBinaryName(name))
            {
                throw Invalid("Invalid Java binary name '" + name + "'.");
            }
        }

        /// <summary>Bounds external process text included in a compiler diagnostic.</summary>
        /// <param name="message">The process output.</param>
        /// <returns>A diagnostic-sized output excerpt.</returns>
        private static string LimitDiagnostic(string message)
        {
            var trimmed = message.Trim();
            return trimmed.Length <= 4096 ? trimmed : trimmed.Substring(0, 4096) + " [truncated]";
        }

        /// <summary>Creates an extraction process failure.</summary>
        /// <param name="message">The failure description.</param>
        /// <returns>The reportable extraction failure.</returns>
        private static GeneratorException Failed(string message)
        {
            return new GeneratorException(GeneratorDiagnostics.ExtractionFailed, message);
        }

        /// <summary>Creates a helper protocol validation failure.</summary>
        /// <param name="message">The protocol validation description.</param>
        /// <returns>The reportable metadata failure.</returns>
        private static GeneratorException Invalid(string message)
        {
            return new GeneratorException(GeneratorDiagnostics.InvalidMetadata, message);
        }
    }
}
