using System.Collections.Generic;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Configures independent Java API discovery and wrapper output.</summary>
    internal sealed class GeneratorOptions
    {
        /// <summary>Gets or sets the Android SDK root; null enables SDK discovery.</summary>
        internal string? SdkDirectory { get; set; }

        /// <summary>Gets or sets the requested Android API level; null selects the highest installed stable level.</summary>
        internal int? ApiLevel { get; set; }

        /// <summary>Gets or sets whether SDK scanning is disabled for a standalone Java library.</summary>
        internal bool NoSdk { get; set; }

        /// <summary>Gets or sets the runtime namespace, whose Bindings child contains every generated Java package.</summary>
        internal string RootNamespace { get; set; } = "MajdataPlay.Platform.Android";

        /// <summary>Gets or sets the destination directory for generated sources and their manifest.</summary>
        internal string OutputDirectory { get; set; } = string.Empty;

        /// <summary>Gets or sets the bounded worker count used for CPU analysis and generation.</summary>
        internal int Jobs { get; set; } = System.Math.Max(1, System.Environment.ProcessorCount);

        /// <summary>Gets or sets the official-documentation retrieval mode.</summary>
        internal DocumentationMode Documentation { get; set; } = DocumentationMode.Online;

        /// <summary>Gets or sets the parsed-documentation cache directory; null places it under the output directory.</summary>
        internal string? DocumentationCache { get; set; }

        /// <summary>Gets or sets whether existing online documentation cache entries must be refreshed.</summary>
        internal bool RefreshDocumentation { get; set; }

        /// <summary>Gets or sets the bounded number of simultaneous official-site requests.</summary>
        internal int DocumentationJobs { get; set; } = 4;

        /// <summary>Gets additional Java source, class, or archive paths, in precedence order.</summary>
        internal List<string> Inputs { get; } = new List<string>();

        /// <summary>Gets optional Java package prefixes; an empty list exports the full API.</summary>
        internal List<string> IncludePackages { get; } = new List<string>();
    }
}
