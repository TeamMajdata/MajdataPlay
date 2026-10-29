using Cysharp.Threading.Tasks;
using MajdataPlay.IO;
using MajdataPlay.Net;
using MajSimai;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
#nullable enable
namespace MajdataPlay
{
    public interface ISongDetail
    {
        string Title { get; }
        string Artist { get; }
        string Description { get; }
        ReadOnlySpan<string> Designers { get; }
        ReadOnlySpan<string> Levels { get; }
        ReadOnlySpan<string> Tags { get; }
        ChartStorageLocation Location { get; }
        DateTime Timestamp { get; }
        string Hash { get; }
        bool IsOnline => Location == ChartStorageLocation.Online;

        bool IsVideoLoaded { get; }
        bool IsCoverLoaded { get; }
        bool IsCompressedCoverLoaded { get; }
        bool IsAudioTrackLoaded { get; }
        bool IsPreviewAudioTrackLoaded { get; }
        bool IsMaidataLoaded { get; }

        ValueTask PreloadAsync(INetProgress? progress = null, CancellationToken token = default);
        ValueTask<string> GetVideoPathAsync(INetProgress? progress = null, CancellationToken token = default);
        ValueTask<Sprite> GetCoverAsync(bool isCompressed, INetProgress? progress = null, CancellationToken token = default);
        ValueTask<AudioSampleWrap> GetAudioTrackAsync(INetProgress? progress = null, CancellationToken token = default);
        ValueTask<AudioSampleWrap> GetPreviewAudioTrackAsync(INetProgress? progress = null, CancellationToken token = default);
        ValueTask<SimaiFile> GetMaidataAsync(bool ignoreCache = false, INetProgress? progress = null, CancellationToken token = default);
    }
}
