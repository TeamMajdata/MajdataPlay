using UnityEngine.Scripting;
#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>The <c>{ code, message }</c> body the server sends with a rejected request, such as a login answered with status 403.</summary>
    [Preserve]
    internal readonly struct NetStatus
    {
        /// <summary>Gets the code of the rejection; null when the body has none, such as a 403 from a proxy or the framework, which must not read as 0.</summary>
        [Preserve]
        public int? Code { get; init; }
        /// <summary>Gets the message of the rejection.</summary>
        [Preserve]
        public string? Message { get; init; }
    }
}
