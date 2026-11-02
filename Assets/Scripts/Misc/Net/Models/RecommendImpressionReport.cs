using UnityEngine.Scripting;
#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>The body of <c>POST recommend/impressions</c>, which reports the recommended charts the player has seen.</summary>
    [Preserve]
    internal readonly struct RecommendImpressionReport
    {
        /// <summary>Gets the feed the charts were recommended by, which keeps the impressions of MajdataPlay apart from those of the website.</summary>
        [Preserve]
        public string Source { get; init; }
        /// <summary>Gets the hashes of the charts that were seen.</summary>
        [Preserve]
        public string[] ChartHashes { get; init; }
    }
}
