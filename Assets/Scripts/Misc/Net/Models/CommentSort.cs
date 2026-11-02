#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>
    /// The orders a list of comments can be read in. The server writes the camel case names, which JSON reads without a
    /// converter because enum names are matched ignoring case; requests send them in the query string.
    /// </summary>
    public enum CommentSort
    {
        /// <summary>Comments whose votes are most likely to stay positive first; the default order of the server.</summary>
        Best,
        /// <summary>The highest score first.</summary>
        Top,
        /// <summary>The latest comment first.</summary>
        New,
        /// <summary>The earliest comment first.</summary>
        Old,
        /// <summary>Comments with many votes split close to evenly first.</summary>
        Controversial,
    }
}
