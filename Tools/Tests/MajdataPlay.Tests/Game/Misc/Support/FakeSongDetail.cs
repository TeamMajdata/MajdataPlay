using System;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.IO;
using MajdataPlay.Net;
using MajSimai;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Support
{
    /// <summary>
    /// An in-memory song whose metadata is supplied by the test, used to drive collection, sorting and score logic
    /// without chart files or media.
    /// </summary>
    internal sealed class FakeSongDetail : ISongDetail
    {
        /// <summary>The seven designer names, one per chart level.</summary>
        private readonly string[] _designers;

        /// <summary>The seven level labels, one per chart level.</summary>
        private readonly string[] _levels;

        /// <summary>Creates a song with the given identity and metadata.</summary>
        /// <param name="hash">The chart hash identifying the song.</param>
        /// <param name="title">The song title.</param>
        /// <param name="artist">The song artist.</param>
        /// <param name="designer">The designer name used for every level.</param>
        /// <param name="level">The level label used for every level.</param>
        /// <param name="description">The description searched by keyword filters.</param>
        /// <param name="location">Whether the song is local or online.</param>
        /// <param name="timestamp">The song timestamp used by time ordering.</param>
        public FakeSongDetail(string hash,
                              string title = "",
                              string artist = "",
                              string designer = "",
                              string level = "",
                              string description = "",
                              ChartStorageLocation location = ChartStorageLocation.Local,
                              DateTime timestamp = default)
        {
            Hash = hash;
            Title = title;
            Artist = artist;
            Description = description;
            Location = location;
            Timestamp = timestamp;
            _designers = new string[7];
            _levels = new string[7];
            for (var i = 0; i < 7; i++)
            {
                _designers[i] = designer;
                _levels[i] = level;
            }
        }

        /// <summary>Gets the song title.</summary>
        public string Title { get; }

        /// <summary>Gets the song artist.</summary>
        public string Artist { get; }

        /// <summary>Gets the song description.</summary>
        public string Description { get; }

        /// <summary>Gets the designer of each level.</summary>
        public ReadOnlySpan<string> Designers => _designers;

        /// <summary>Gets the label of each level.</summary>
        public ReadOnlySpan<string> Levels => _levels;

        /// <summary>Gets whether the song is local or online.</summary>
        public ChartStorageLocation Location { get; }

        /// <summary>Gets the song timestamp.</summary>
        public DateTime Timestamp { get; }

        /// <summary>Gets the chart hash.</summary>
        public string Hash { get; }

        /// <summary>Gets a value indicating that no video is loaded.</summary>
        public bool IsVideoLoaded => false;

        /// <summary>Gets a value indicating that no cover is loaded.</summary>
        public bool IsCoverLoaded => false;

        /// <summary>Gets a value indicating that no compressed cover is loaded.</summary>
        public bool IsCompressedCoverLoaded => false;

        /// <summary>Gets a value indicating that no audio track is loaded.</summary>
        public bool IsAudioTrackLoaded => false;

        /// <summary>Gets a value indicating that no preview track is loaded.</summary>
        public bool IsPreviewAudioTrackLoaded => false;

        /// <summary>Gets a value indicating that no chart is loaded.</summary>
        public bool IsMaidataLoaded => false;

        /// <summary>Sets the label of one level, so tests can build songs with different difficulties.</summary>
        /// <param name="index">The level index.</param>
        /// <param name="value">The new label.</param>
        public void SetLevel(int index, string value)
        {
            _levels[index] = value;
        }

        /// <summary>Sets the designer of one level, so tests can build songs with different designers.</summary>
        /// <param name="index">The level index.</param>
        /// <param name="value">The new designer.</param>
        public void SetDesigner(int index, string value)
        {
            _designers[index] = value;
        }

        /// <summary>Completes immediately because the fake has nothing to preload.</summary>
        /// <param name="progress">Ignored progress receiver.</param>
        /// <param name="token">Ignored cancellation token.</param>
        /// <returns>A completed operation.</returns>
        public ValueTask PreloadAsync(INetProgress? progress = null, CancellationToken token = default)
        {
            return default;
        }

        /// <summary>Returns an empty video path because the fake has no video.</summary>
        /// <param name="progress">Ignored progress receiver.</param>
        /// <param name="token">Ignored cancellation token.</param>
        /// <returns>An empty path.</returns>
        public ValueTask<string> GetVideoPathAsync(INetProgress? progress = null, CancellationToken token = default)
        {
            return new ValueTask<string>(string.Empty);
        }

        /// <summary>Throws because the fake has no cover.</summary>
        /// <param name="isCompressed">Ignored cover kind.</param>
        /// <param name="progress">Ignored progress receiver.</param>
        /// <param name="token">Ignored cancellation token.</param>
        /// <returns>Never returns.</returns>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public ValueTask<Sprite> GetCoverAsync(bool isCompressed, INetProgress? progress = null, CancellationToken token = default)
        {
            throw new NotSupportedException();
        }

        /// <summary>Throws because the fake has no audio track.</summary>
        /// <param name="progress">Ignored progress receiver.</param>
        /// <param name="token">Ignored cancellation token.</param>
        /// <returns>Never returns.</returns>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public ValueTask<AudioSampleWrap> GetAudioTrackAsync(INetProgress? progress = null, CancellationToken token = default)
        {
            throw new NotSupportedException();
        }

        /// <summary>Throws because the fake has no preview track.</summary>
        /// <param name="progress">Ignored progress receiver.</param>
        /// <param name="token">Ignored cancellation token.</param>
        /// <returns>Never returns.</returns>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public ValueTask<AudioSampleWrap> GetPreviewAudioTrackAsync(INetProgress? progress = null, CancellationToken token = default)
        {
            throw new NotSupportedException();
        }

        /// <summary>Throws because the fake has no chart.</summary>
        /// <param name="ignoreCache">Ignored cache flag.</param>
        /// <param name="progress">Ignored progress receiver.</param>
        /// <param name="token">Ignored cancellation token.</param>
        /// <returns>Never returns.</returns>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public ValueTask<SimaiFile> GetMaidataAsync(bool ignoreCache = false, INetProgress? progress = null, CancellationToken token = default)
        {
            throw new NotSupportedException();
        }

        /// <summary>Returns the hash so assertion messages identify the song.</summary>
        /// <returns>The chart hash.</returns>
        public override string ToString()
        {
            return Hash;
        }
    }
}
