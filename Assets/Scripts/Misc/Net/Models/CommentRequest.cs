using UnityEngine.Scripting;
#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>The body of <c>POST maichart/{chartId}/comments</c>, which posts a top-level comment.</summary>
    [Preserve]
    internal readonly struct CommentRequest
    {
        /// <summary>Gets the text of the comment.</summary>
        [Preserve]
        public string Body { get; init; }
    }
}
