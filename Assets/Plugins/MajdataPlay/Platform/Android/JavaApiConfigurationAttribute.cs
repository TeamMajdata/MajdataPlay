using System;

#nullable enable

namespace MajdataPlay.Platform.Android
{
    /// <summary>
    /// Supplies shared SDK, JDK, input, and documentation settings for Java wrappers in an assembly.
    /// </summary>
    /// <remarks>
    /// Per-class input arrays are appended to these arrays. Explicit per-class scalar settings
    /// override this configuration. Configure every assembly that declares wrapper classes.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public sealed class JavaApiConfigurationAttribute : Attribute
    {
        /// <summary>
        /// Gets or sets shared Java sources, source directories, class files, or JAR archives.
        /// </summary>
        public string[] Sources { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets shared JAR archives and compiled-class roots for dependency resolution.
        /// </summary>
        public string[] ClassPath { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets shared Java source directories or source archives for API documentation.
        /// </summary>
        public string[] DocumentationPaths { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the Android SDK root. When omitted, environment and Unity SDK discovery are used.
        /// </summary>
        public string? AndroidSdkPath { get; set; }

        /// <summary>
        /// Gets or sets the JDK root. When omitted, environment and Unity OpenJDK discovery are used.
        /// </summary>
        public string? JavaHome { get; set; }

        /// <summary>
        /// Gets or sets the Android API level. Zero selects the highest installed platform.
        /// </summary>
        public int ApiLevel { get; set; }

        /// <summary>
        /// Gets or sets whether wrappers include inherited public Java fields and methods by default.
        /// </summary>
        public bool IncludeInheritedMembers { get; set; } = true;
    }
}
