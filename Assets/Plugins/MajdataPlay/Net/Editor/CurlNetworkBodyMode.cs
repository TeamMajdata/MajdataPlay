#nullable enable
namespace MajdataPlay.Net.Editor
{
    /// <summary>
    /// How the details panel shows a body.
    /// </summary>
    internal enum CurlNetworkBodyMode
    {
        /// <summary>
        /// JSON re-indented and form data as a table; other text as it is; binary as a hex dump.
        /// </summary>
        Pretty,

        /// <summary>
        /// The decoded text as it is.
        /// </summary>
        Raw,

        /// <summary>
        /// A hex dump.
        /// </summary>
        Hex,

        /// <summary>
        /// The decoded image, for PNG and JPEG responses.
        /// </summary>
        Preview,
    }
}
