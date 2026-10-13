using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Extracts Java declarations and resolves their erased JVM types without a Java compiler.</summary>
    internal static class JavaSourceParser
    {
        /// <summary>Adds source declarations after dependency bytecode has been loaded.</summary>
        /// <param name="api">The metadata graph providing SDK and archive dependencies.</param>
        /// <param name="paths">The UTF-8 Java source paths to analyze together.</param>
        /// <param name="cancellationToken">The extraction cancellation token.</param>
        /// <param name="jobs">The maximum independent source-reading workers; one runs serially.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The worker count is not positive.</exception>
        /// <exception cref="ArgumentException">A source path is invalid.</exception>
        /// <exception cref="InvalidDataException">A source declaration or resolved reference is invalid.</exception>
        /// <exception cref="IOException">A source cannot be read or its declarations cannot be resolved.</exception>
        /// <exception cref="UnauthorizedAccessException">A source cannot be accessed.</exception>
        /// <exception cref="DecoderFallbackException">A source is not valid UTF-8.</exception>
        /// <exception cref="OperationCanceledException">Extraction is canceled.</exception>
        internal static void AddSources(JavaApi api, IReadOnlyList<string> paths, CancellationToken cancellationToken, int jobs = 1)
        {
            if (api is null)
            {
                throw new ArgumentNullException(nameof(api));
            }
            if (paths is null)
            {
                throw new ArgumentNullException(nameof(paths));
            }
            var declarations = new JavaSourceUnit[paths.Count];
            ParallelWork.For(paths.Count, jobs, index =>
            {
                var path = paths[index];
                var text = File.ReadAllText(path, new UTF8Encoding(false, true));
                cancellationToken.ThrowIfCancellationRequested();
                declarations[index] = JavaSourceDeclarationReader.Read(path, text, cancellationToken);
            }, cancellationToken);
            JavaSourceResolver.AddDeclarations(api, declarations, cancellationToken);
        }
    }
}
