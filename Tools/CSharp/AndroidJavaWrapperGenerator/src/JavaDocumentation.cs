using System;
using System.Collections.Generic;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Contains plain English descriptions copied from an identified official API page.</summary>
    internal sealed class JavaDocumentation
    {
        /// <summary>Gets or sets the primary member or type description.</summary>
        public string Summary { get; set; } = string.Empty;

        /// <summary>Gets or sets additional API behavior and Java exception notes.</summary>
        public string Remarks { get; set; } = string.Empty;

        /// <summary>Gets or sets the return-value description.</summary>
        public string Returns { get; set; } = string.Empty;

        /// <summary>Gets or sets the official page and optional member anchor used for attribution.</summary>
        public string SourceUrl { get; set; } = string.Empty;

        /// <summary>Gets or sets descriptions keyed by original Java parameter names.</summary>
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Gets or sets original source parameter names in signature order.</summary>
        public List<string> ParameterNames { get; set; } = new List<string>();
    }

    /// <summary>Contains descriptor-exact descriptions parsed from one official type page.</summary>
    internal sealed class JavaDocumentationPage
    {
        /// <summary>Gets or sets the Java binary type identified by the page.</summary>
        public string TypeName { get; set; } = string.Empty;

        /// <summary>Gets or sets the official type-level description.</summary>
        public JavaDocumentation Type { get; set; } = new JavaDocumentation();

        /// <summary>Gets or sets fields keyed by their original Java names.</summary>
        public Dictionary<string, JavaDocumentation> Fields { get; set; } = new Dictionary<string, JavaDocumentation>(StringComparer.Ordinal);

        /// <summary>Gets or sets methods keyed by name followed by their exact JVM descriptor.</summary>
        public Dictionary<string, JavaDocumentation> Methods { get; set; } = new Dictionary<string, JavaDocumentation>(StringComparer.Ordinal);
    }
}
