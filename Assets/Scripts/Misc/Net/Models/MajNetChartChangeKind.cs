#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>The kinds of change that <c>GET maichart/changes</c> reports; the server writes them as numbers.</summary>
    public enum MajNetChartChangeKind
    {
        /// <summary>The chart was uploaded; its metadata and files are read with the <c>maichart/info</c> API.</summary>
        Added = 0,
        /// <summary>Files or metadata of an existing chart changed.</summary>
        Modified = 1,
        /// <summary>The chart was deleted.</summary>
        Deleted = 2,
    }
}
