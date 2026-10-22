using System.Text;
using MajdataPlay.IO.Storage;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Support
{
    /// <summary>Writes minimal Simai chart directories into temporary storage.</summary>
    internal static class ChartFixture
    {
        /// <summary>Builds the text of a small, valid maidata file with one Master chart.</summary>
        /// <param name="title">The chart title, which also makes the chart hash unique.</param>
        /// <param name="artist">The artist name.</param>
        /// <param name="level">The Master level label.</param>
        /// <param name="designer">The Master designer name.</param>
        /// <returns>The maidata text.</returns>
        public static string CreateMaidata(string title, string artist = "Artist", string level = "13", string designer = "Designer")
        {
            var builder = new StringBuilder();
            builder.Append("&title=").Append(title).Append('\n');
            builder.Append("&artist=").Append(artist).Append('\n');
            builder.Append("&first=0\n");
            builder.Append("&des_5=").Append(designer).Append('\n');
            builder.Append("&lv_5=").Append(level).Append('\n');
            builder.Append("&inote_5=(120){4}1,2,3,4,\nE\n");
            return builder.ToString();
        }

        /// <summary>Creates a chart directory with the requested files.</summary>
        /// <param name="parent">The directory that receives the chart directory.</param>
        /// <param name="name">The chart directory name.</param>
        /// <param name="title">The chart title written into maidata.txt.</param>
        /// <param name="withMaidata">Whether maidata.txt is written.</param>
        /// <param name="trackName">The track file name, or null for no track.</param>
        /// <param name="coverName">The cover file name, or null for no cover.</param>
        /// <param name="videoName">The video file name, or null for no video.</param>
        /// <returns>The created chart directory.</returns>
        public static StorageDirectory CreateChart(StorageDirectory parent,
                                                   string name,
                                                   string title,
                                                   bool withMaidata = true,
                                                   string? trackName = "track.mp3",
                                                   string? coverName = null,
                                                   string? videoName = null)
        {
            var directory = parent.CreateDirectory(name);
            if (withMaidata)
            {
                directory.CreateFile("maidata.txt").WriteAllText(CreateMaidata(title));
            }
            if (trackName is not null)
            {
                directory.CreateFile(trackName).WriteAllBytes(new byte[] { 1, 2, 3 });
            }
            if (coverName is not null)
            {
                directory.CreateFile(coverName).WriteAllBytes(new byte[] { 4, 5, 6 });
            }
            if (videoName is not null)
            {
                directory.CreateFile(videoName).WriteAllBytes(new byte[] { 7, 8, 9 });
            }
            return directory;
        }
    }
}
