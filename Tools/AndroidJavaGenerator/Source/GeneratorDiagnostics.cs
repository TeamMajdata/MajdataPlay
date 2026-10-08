#nullable enable

using System;
using Microsoft.CodeAnalysis;

namespace MajdataPlay.SourceGenerators.AndroidJava
{
    /// <summary>
    /// Defines diagnostics emitted by the Java binding generator.
    /// </summary>
    internal static class GeneratorDiagnostics
    {
        /// <summary>Reports a wrapper declaration that cannot be augmented safely.</summary>
        internal static DiagnosticDescriptor InvalidDeclaration { get; } = Create("AJG001", "Invalid Java wrapper declaration", "{0}", DiagnosticSeverity.Error);

        /// <summary>Reports multiple wrappers for the same Java binary name.</summary>
        internal static DiagnosticDescriptor DuplicateMapping { get; } = Create("AJG002", "Duplicate Java type mapping", "{0}", DiagnosticSeverity.Error);

        /// <summary>Reports invalid extraction paths or toolchain settings.</summary>
        internal static DiagnosticDescriptor InvalidConfiguration { get; } = Create("AJG003", "Invalid Java API configuration", "{0}", DiagnosticSeverity.Error);

        /// <summary>Reports unsuccessful or timed-out Java helper execution.</summary>
        internal static DiagnosticDescriptor ExtractionFailed { get; } = Create("AJG004", "Java API extraction failed", "{0}", DiagnosticSeverity.Error);

        /// <summary>Reports unsupported or malformed helper metadata.</summary>
        internal static DiagnosticDescriptor InvalidMetadata { get; } = Create("AJG005", "Invalid Java API metadata", "{0}", DiagnosticSeverity.Error);

        /// <summary>Reports Java overloads that erase to the same C# signature.</summary>
        internal static DiagnosticDescriptor SignatureCollision { get; } = Create("AJG006", "Java overload renamed to preserve its exact descriptor", "{0}", DiagnosticSeverity.Warning);

        /// <summary>Reports deterministic renaming to avoid a generated member conflict.</summary>
        internal static DiagnosticDescriptor RenamedMember { get; } = Create("AJG007", "Java member renamed", "{0}", DiagnosticSeverity.Warning);

        /// <summary>Reports generated members that collide with handwritten members.</summary>
        internal static DiagnosticDescriptor UserMemberCollision { get; } = Create("AJG008", "Java binding conflicts with a user member", "{0}", DiagnosticSeverity.Error);

        /// <summary>Reports an unexpected generator failure without crashing the compiler.</summary>
        internal static DiagnosticDescriptor UnexpectedFailure { get; } = Create("AJG009", "Android Java generator failed", "{0}", DiagnosticSeverity.Error);

        /// <summary>Creates a consistently categorized diagnostic descriptor.</summary>
        /// <param name="id">The stable diagnostic identifier.</param>
        /// <param name="title">The diagnostic title.</param>
        /// <param name="message">The diagnostic message format.</param>
        /// <param name="severity">The diagnostic severity.</param>
        /// <returns>The configured diagnostic descriptor.</returns>
        private static DiagnosticDescriptor Create(string id, string title, string message, DiagnosticSeverity severity)
        {
            return new DiagnosticDescriptor(id, title, message, "AndroidJavaGenerator", severity, true);
        }
    }

    /// <summary>Describes an expected validation or extraction failure.</summary>
    internal sealed class GeneratorException : Exception
    {
        /// <summary>Gets the diagnostic that explains this failure.</summary>
        internal DiagnosticDescriptor Descriptor { get; }

        /// <summary>Creates a failure that can be reported at an attribute location.</summary>
        /// <param name="descriptor">The diagnostic category for the failure.</param>
        /// <param name="message">The actionable failure description.</param>
        internal GeneratorException(DiagnosticDescriptor descriptor, string message)
            : base(message)
        {
            Descriptor = descriptor;
        }
    }
}
