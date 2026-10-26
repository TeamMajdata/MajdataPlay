#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Controls whether official online API descriptions are retrieved or read from cache.</summary>
    internal enum DocumentationMode
    {
        /// <summary>Downloads missing documentation and reuses cached pages.</summary>
        Online,
        /// <summary>Reads the local documentation cache without network access.</summary>
        Offline,
        /// <summary>Leaves extracted names and generic binding documentation unchanged.</summary>
        None
    }
}
