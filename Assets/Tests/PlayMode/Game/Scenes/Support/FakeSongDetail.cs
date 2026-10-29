using System;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.IO;
using MajdataPlay.Net;
using MajSimai;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.Support
{
    /// <summary>
    /// A configurable in-memory song used by scene tests instead of charts on disk or on a server.
    /// </summary>
    public sealed class FakeSongDetail : ISongDetail
    {
        /// <summary>The designer names indexed by chart level.</summary>
        private readonly string[] _designers;

        /// <summary>The level labels indexed by chart level.</summary>
        private readonly string[] _levels;

        /// <summary>Creates a song with seven designer and level slots.</summary>
        /// <param name="hash">The unique hash identifying the song.</param>
        /// <param name="title">The song title.</param>
        /// <param name="levels">The level labels; missing slots are empty.</param>
        public FakeSongDetail(string hash, string title = "Title", params string[] levels)
        {
            Hash = hash;
            Title = title;
            _designers = new string[7];
            _levels = new string[7];
            for (var i = 0; i < 7; i++)
            {
                _designers[i] = $"Designer{i}";
                _levels[i] = i < levels.Length ? levels[i] : string.Empty;
            }
        }

        /// <summary>Gets or sets the song title.</summary>
        public string Title { get; set; }

        /// <summary>Gets or sets the artist.</summary>
        public string Artist { get; set; } = "Artist";

        /// <summary>Gets or sets the description.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Gets the designers indexed by chart level.</summary>
        public ReadOnlySpan<string> Designers => _designers;

        /// <summary>Gets the level labels indexed by chart level.</summary>
        public ReadOnlySpan<string> Levels => _levels;

        /// <summary>Gets an empty tag list for the in-memory song.</summary>
        public ReadOnlySpan<string> Tags => ReadOnlySpan<string>.Empty;

        /// <summary>Gets or sets where the song is stored.</summary>
        public ChartStorageLocation Location { get; set; } = ChartStorageLocation.Local;

        /// <summary>Gets or sets the song timestamp.</summary>
        public DateTime Timestamp { get; set; } = new DateTime(2024, 1, 1);

        /// <summary>Gets the song identity, which fakes derive from the hash.</summary>
        public string Id => Hash;

        /// <summary>Gets the unique song hash.</summary>
        public string Hash { get; }

        /// <summary>Gets or sets a value indicating whether the video is loaded.</summary>
        public bool IsVideoLoaded { get; set; }

        /// <summary>Gets or sets a value indicating whether the full size cover is loaded.</summary>
        public bool IsCoverLoaded { get; set; }

        /// <summary>Gets or sets a value indicating whether the compressed cover is loaded, which skips load delays.</summary>
        public bool IsCompressedCoverLoaded { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether the audio track is loaded.</summary>
        public bool IsAudioTrackLoaded { get; set; }

        /// <summary>Gets or sets a value indicating whether the preview audio track is loaded.</summary>
        public bool IsPreviewAudioTrackLoaded { get; set; }

        /// <summary>Gets or sets a value indicating whether the maidata is loaded.</summary>
        public bool IsMaidataLoaded { get; set; }

        /// <summary>Gets or sets the cover returned by <see cref="GetCoverAsync"/>.</summary>
        public Sprite? Cover { get; set; }

        /// <summary>Gets or sets the maidata returned by <see cref="GetMaidataAsync"/>.</summary>
        public SimaiFile? Maidata { get; set; }

        /// <summary>Gets or sets the exception thrown by <see cref="GetMaidataAsync"/>; it takes precedence over <see cref="Maidata"/>.</summary>
        public Exception? MaidataException { get; set; }

        /// <summary>Gets or sets the preview audio returned by <see cref="GetPreviewAudioTrackAsync"/>.</summary>
        public AudioSampleWrap? PreviewAudio { get; set; }

        /// <summary>Gets or sets the audio returned by <see cref="GetAudioTrackAsync"/>.</summary>
        public AudioSampleWrap? AudioTrack { get; set; }

        /// <summary>Gets the number of cover requests.</summary>
        public int CoverRequestCount { get; private set; }

        /// <summary>Gets the number of maidata requests.</summary>
        public int MaidataRequestCount { get; private set; }

        /// <summary>Gets the number of preload requests.</summary>
        public int PreloadCount { get; private set; }

        /// <summary>Sets the designer of one chart level.</summary>
        /// <param name="level">The chart level index.</param>
        /// <param name="designer">The designer name.</param>
        public void SetDesigner(int level, string designer)
        {
            _designers[level] = designer;
        }

        /// <summary>Completes immediately and counts the request.</summary>
        /// <param name="progress">Ignored progress receiver.</param>
        /// <param name="token">Ignored cancellation token.</param>
        /// <returns>A completed task.</returns>
        public ValueTask PreloadAsync(INetProgress? progress = null, CancellationToken token = default)
        {
            PreloadCount++;
            return default;
        }

        /// <summary>Returns an empty video path.</summary>
        /// <param name="progress">Ignored progress receiver.</param>
        /// <param name="token">Ignored cancellation token.</param>
        /// <returns>An empty path.</returns>
        public ValueTask<string> GetVideoPathAsync(INetProgress? progress = null, CancellationToken token = default)
        {
            return new ValueTask<string>(string.Empty);
        }

        /// <summary>Returns the configured cover.</summary>
        /// <param name="isCompressed">Ignored compression flag.</param>
        /// <param name="progress">Ignored progress receiver.</param>
        /// <param name="token">Cancels the request when already cancelled.</param>
        /// <returns>The configured cover sprite.</returns>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        public ValueTask<Sprite> GetCoverAsync(bool isCompressed, INetProgress? progress = null, CancellationToken token = default)
        {
            CoverRequestCount++;
            if (token.IsCancellationRequested)
            {
                return new ValueTask<Sprite>(Task.FromCanceled<Sprite>(token));
            }
            return new ValueTask<Sprite>(Cover!);
        }

        /// <summary>Returns the configured audio track.</summary>
        /// <param name="progress">Ignored progress receiver.</param>
        /// <param name="token">Ignored cancellation token.</param>
        /// <returns>The configured audio track or an empty sample.</returns>
        public ValueTask<AudioSampleWrap> GetAudioTrackAsync(INetProgress? progress = null, CancellationToken token = default)
        {
            return new ValueTask<AudioSampleWrap>(AudioTrack ?? AudioSampleWrap.Empty);
        }

        /// <summary>Returns the configured preview audio track.</summary>
        /// <param name="progress">Ignored progress receiver.</param>
        /// <param name="token">Ignored cancellation token.</param>
        /// <returns>The configured preview audio track or an empty sample.</returns>
        public ValueTask<AudioSampleWrap> GetPreviewAudioTrackAsync(INetProgress? progress = null, CancellationToken token = default)
        {
            return new ValueTask<AudioSampleWrap>(PreviewAudio ?? AudioSampleWrap.Empty);
        }

        /// <summary>Returns the configured maidata or fails with the configured exception.</summary>
        /// <param name="ignoreCache">Ignored cache flag.</param>
        /// <param name="progress">Ignored progress receiver.</param>
        /// <param name="token">Cancels the request when already cancelled.</param>
        /// <returns>The configured maidata.</returns>
        /// <exception cref="InvalidOperationException">No maidata or exception was configured.</exception>
        public ValueTask<SimaiFile> GetMaidataAsync(bool ignoreCache = false, INetProgress? progress = null, CancellationToken token = default)
        {
            MaidataRequestCount++;
            if (token.IsCancellationRequested)
            {
                return new ValueTask<SimaiFile>(Task.FromCanceled<SimaiFile>(token));
            }
            if (MaidataException is not null)
            {
                return new ValueTask<SimaiFile>(Task.FromException<SimaiFile>(MaidataException));
            }
            if (Maidata is null)
            {
                return new ValueTask<SimaiFile>(Task.FromException<SimaiFile>(new InvalidOperationException("No maidata configured.")));
            }
            return new ValueTask<SimaiFile>(Maidata);
        }
    }
}
