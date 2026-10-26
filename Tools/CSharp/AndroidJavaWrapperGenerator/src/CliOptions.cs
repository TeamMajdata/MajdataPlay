using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Parses the command-line contract without initializing Java or Unity.</summary>
    internal static class CliOptions
    {
        /// <summary>Describes CLI inputs and generation defaults.</summary>
        internal const string Usage = @"AndroidJavaWrapperGenerator — Java APIs to Unity JNI wrappers

Usage:
  AndroidJavaWrapperGenerator --output DIRECTORY [options]
  AndroidJavaWrapperGenerator --project DIRECTORY --unity-data DIRECTORY [project options]
  AndroidJavaWrapperGenerator --build-wrapper DIRECTORY --unity-data DIRECTORY

Options:
  --sdk DIRECTORY          Android SDK root (defaults to Unity/environment discovery).
  --api-level NUMBER       Installed stable API level (defaults to highest installed).
  --input FILE             Add .java, .class, .jar, or .aar input; repeat for multiple inputs.
  --namespace NAME         Runtime root namespace (default: MajdataPlay.Platform.Android).
  --output DIRECTORY       Destination for C# sources and the ownership manifest.
  --include-package NAME   Export a package and subpackages plus API dependencies; repeat.
  --no-sdk                 Scan additional inputs only, without android.jar.
  --jobs NUMBER            CPU analysis/emission workers (default: available CPU count).
  --docs-jobs NUMBER       Simultaneous official-documentation requests (default: 4).
  --docs-cache DIRECTORY   Parsed documentation cache (default: OUTPUT/.documentation-cache).
  --offline-docs           Read cached official documentation without network access.
  --no-docs                Disable official-documentation enrichment.
  --refresh-docs           Refresh online documentation even when it is cached.
  --help, -h               Show this help.

SDK, .class, .jar, and .java metadata are analyzed in pure C#; no JDK tools are invoked.
By default every public type in android.jar is exported, including Android's Java BCL.
Generated code targets C# 9 / netstandard2.1 and requires Unity's Core/AndroidJNI modules.
Runtime support types are directly in ROOT; Java wrappers are under ROOT.Bindings.
Java packages are PascalCase namespace components: android.net -> ROOT.Bindings.Android.Net.
Project mode emits Tools/AndroidJavaWrapper/src and compiles MajdataPlay.Platform.Android.Runtime.dll.
The compiled DLL is installed in Assets/Plugins/MajdataPlay/Platform/Android.
--build-wrapper rebuilds existing sources and handwritten XXX.e.cs extensions without regeneration.
Its optional arguments are --sdk, --api-level, --jobs, and the documentation options above.
";

        /// <summary>Reads arguments and validates independent generation settings.</summary>
        /// <param name="arguments">The process arguments.</param>
        /// <returns>The validated options, or null for a help request.</returns>
        /// <exception cref="ArgumentException">An argument is missing, unknown, or invalid.</exception>
        internal static GeneratorOptions? Parse(string[] arguments)
        {
            var options = new GeneratorOptions();
            var documentationModeSpecified = false;
            if (arguments.Length == 0)
            {
                return null;
            }

            for (var i = 0; i < arguments.Length; i++)
            {
                var argument = arguments[i];
                switch (argument)
                {
                    case "--help":
                    case "-h":
                        return null;
                    case "--no-sdk":
                        options.NoSdk = true;
                        break;
                    case "--jobs":
                        options.Jobs = ReadPositiveInteger(arguments, ref i, 1024);
                        break;
                    case "--docs-jobs":
                        options.DocumentationJobs = ReadPositiveInteger(arguments, ref i, 32);
                        break;
                    case "--docs-cache":
                        options.DocumentationCache = Path.GetFullPath(ReadValue(arguments, ref i));
                        break;
                    case "--offline-docs":
                    case "--no-docs":
                        if (documentationModeSpecified)
                        {
                            throw new ArgumentException("Choose either --offline-docs or --no-docs once.");
                        }
                        documentationModeSpecified = true;
                        options.Documentation = argument == "--offline-docs" ? DocumentationMode.Offline : DocumentationMode.None;
                        break;
                    case "--refresh-docs":
                        options.RefreshDocumentation = true;
                        break;
                    case "--sdk":
                        options.SdkDirectory = Path.GetFullPath(ReadValue(arguments, ref i));
                        break;
                    case "--api-level":
                        var value = ReadValue(arguments, ref i);
                        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var apiLevel) || apiLevel <= 0)
                        {
                            throw new ArgumentException("--api-level requires a positive integer.");
                        }

                        options.ApiLevel = apiLevel;
                        break;
                    case "--namespace":
                        options.RootNamespace = ReadValue(arguments, ref i);
                        break;
                    case "--output":
                        options.OutputDirectory = Path.GetFullPath(ReadValue(arguments, ref i));
                        break;
                    case "--input":
                        options.Inputs.Add(Path.GetFullPath(ReadValue(arguments, ref i)));
                        break;
                    case "--include-package":
                        var package = ReadValue(arguments, ref i);
                        if (!Regex.IsMatch(package, @"^[\p{L}_$][\p{L}\p{N}_$]*(\.[\p{L}_$][\p{L}\p{N}_$]*)*$"))
                        {
                            throw new ArgumentException("--include-package requires a dot-separated Java package name.");
                        }

                        options.IncludePackages.Add(package);
                        break;
                    default:
                        throw new ArgumentException("Unknown option '" + argument + "'. Use --help for usage.");
                }
            }

            Validate(options);
            return options;
        }

        /// <summary>Validates options that do not depend on SDK contents.</summary>
        /// <param name="options">The parsed generator settings.</param>
        /// <exception cref="ArgumentException">Options are inconsistent or the namespace is invalid.</exception>
        private static void Validate(GeneratorOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            {
                throw new ArgumentException("--output is required.");
            }

            if (options.NoSdk && (options.SdkDirectory != null || options.ApiLevel != null))
            {
                throw new ArgumentException("--no-sdk cannot be combined with --sdk or --api-level.");
            }

            if (options.RefreshDocumentation && options.Documentation != DocumentationMode.Online)
            {
                throw new ArgumentException("--refresh-docs requires online documentation mode.");
            }

            if (options.NoSdk && options.Inputs.Count == 0)
            {
                throw new ArgumentException("--no-sdk requires at least one --input.");
            }

            var keywords = " abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while __arglist __makeref __reftype __refvalue ";
            foreach (var segment in options.RootNamespace.Split('.'))
            {
                if (!Regex.IsMatch(segment, @"^[A-Za-z_][A-Za-z0-9_]*$") || keywords.Contains(" " + segment + " ", StringComparison.Ordinal))
                {
                    throw new ArgumentException("--namespace must contain valid, non-keyword C# identifiers.");
                }
            }
        }

        /// <summary>Reads a bounded positive worker count.</summary>
        /// <param name="arguments">The process arguments.</param>
        /// <param name="index">The current option index, advanced to its value.</param>
        /// <param name="maximum">The largest accepted worker count.</param>
        /// <returns>The validated positive integer.</returns>
        /// <exception cref="ArgumentException">The value is missing or outside the permitted range.</exception>
        private static int ReadPositiveInteger(string[] arguments, ref int index, int maximum)
        {
            var option = arguments[index];
            var value = ReadValue(arguments, ref index);
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 1 || count > maximum)
            {
                throw new ArgumentException(option + " requires an integer between 1 and " + maximum + ".");
            }
            return count;
        }

        /// <summary>Reads one required option value and advances the argument cursor.</summary>
        /// <param name="arguments">All process arguments.</param>
        /// <param name="index">The index of the option, updated to its value.</param>
        /// <returns>The nonempty option value.</returns>
        /// <exception cref="ArgumentException">The option has no value.</exception>
        private static string ReadValue(string[] arguments, ref int index)
        {
            var option = arguments[index];
            index++;
            if (index >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index]) || arguments[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException(option + " requires a value.");
            }

            return arguments[index];
        }
    }
}
