using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Runs SDK discovery and independent source generation from the CLI.</summary>
    internal static class Program
    {
        /// <summary>Executes a generation command, reporting actionable failures to standard error.</summary>
        /// <param name="arguments">The command-line arguments.</param>
        /// <returns>Zero on success or help, two for invalid options, one for generation errors, or 130 on cancellation.</returns>
        internal static async Task<int> Main(string[] arguments)
        {
            if (arguments.Length > 0 && (arguments[0] == "--project" || arguments[0] == "--build-wrapper"))
            {
                return await ProjectWrapperGeneration.RunAsync(arguments).ConfigureAwait(false);
            }

            GeneratorOptions? options;
            try
            {
                options = CliOptions.Parse(arguments);
            }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 2;
            }

            if (options == null)
            {
                Console.WriteLine(CliOptions.Usage);
                return 0;
            }

            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancelHandler = (sender, args) =>
            {
                args.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;
            try
            {
                Console.WriteLine("Scanning Java API metadata...");
                var api = JavaApiScanner.Scan(options, cancellation.Token);
                if (api.PlatformJar != null)
                {
                    Console.WriteLine("Android platform: " + api.PlatformJar);
                }

                cancellation.Token.ThrowIfCancellationRequested();
                if (options.Documentation != DocumentationMode.None)
                {
                    Console.WriteLine("Enriching official English documentation (" + options.Documentation + ", requests: " + options.DocumentationJobs + ")...");
                    var selected = WrapperEmitter.GetSelectedTypes(api, options);
                    var report = await OfficialDocumentationProvider.EnrichAsync(api, selected, options, cancellation.Token).ConfigureAwait(false);
                    Console.WriteLine("Official documentation: " + report.DocumentedTypes + "/" + report.RequestedTypes + " types, " + report.DocumentedMembers + " members, " + report.RestoredParameters + " parameter names; " + report.CachedPages + " cache hits, " + report.DownloadedPages + " downloads.");
                }
                Console.WriteLine("Generating wrappers with " + options.Jobs + " CPU workers...");
                var files = new Dictionary<string, string>(StringComparer.Ordinal);
                AddFiles(files, RuntimeEmitter.Generate(options.RootNamespace));
                AddFiles(files, WrapperEmitter.Generate(api, options, cancellation.Token));
                foreach (var warning in api.Warnings)
                {
                    Console.Error.WriteLine("Warning: " + warning);
                }

                cancellation.Token.ThrowIfCancellationRequested();
                GeneratedOutput.Write(options, api.PlatformJar, files, cancellation.Token);
                Console.WriteLine("Generated " + files.Count + " C# files in " + options.OutputDirectory);
                return 0;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("Generation canceled.");
                return 130;
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is InvalidOperationException || exception is NotSupportedException)
            {
                Console.Error.WriteLine("Generation failed: " + exception.Message);
                return 1;
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
            }
        }

        /// <summary>Merges one output set while rejecting path collisions between emitters.</summary>
        /// <param name="destination">The combined generated sources.</param>
        /// <param name="sources">The new relative paths and C# contents.</param>
        /// <exception cref="InvalidOperationException">Two generated sources use the same path.</exception>
        private static void AddFiles(Dictionary<string, string> destination, IReadOnlyDictionary<string, string> sources)
        {
            foreach (var source in sources)
            {
                if (!destination.TryAdd(source.Key, source.Value))
                {
                    throw new InvalidOperationException("Generated path collision: " + source.Key);
                }
            }
        }
    }
}
