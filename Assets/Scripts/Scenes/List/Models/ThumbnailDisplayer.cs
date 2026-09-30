using Cysharp.Threading.Tasks;
using MajdataPlay.Drawing;
using MajdataPlay.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace MajdataPlay.Scenes.List.Models
{
    public class ThumbnailDisplayer : CoverSmallDisplayer
    {
        [SerializeField]
        [FormerlySerializedAs("coverDisplayer")]
        Image _coverDisplayer;

        [SerializeField]
        [FormerlySerializedAs("loadingComponent")]
        GameObject _loadingComponent;

        CancellationTokenSource _cts = new();
        public void SetActive(bool state)
        {
            gameObject.SetActive(state);
        }
        public void SetSongDetail(ISongDetail detail, int loadDelayMS = 0)
        {
            if (!_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }
            _cts = new();
            ListManager.AllBackgroundTasks.Add(SetCoverAsync(detail, loadDelayMS, _cts.Token)
                                               .Register($"[{nameof(ThumbnailDisplayer)}]Load song thumbnail"));
        }

        async Task SetCoverAsync(ISongDetail songDetail, int loadDelayMS, CancellationToken token = default)
        {
            await UniTask.SwitchToMainThread();
            _loadingComponent.SetActive(true);
            _coverDisplayer.sprite = null!;
            if (!songDetail.IsCompressedCoverLoaded)
            {
                await Task.Delay(loadDelayMS, token);
            }
            var cover = await songDetail.GetCoverAsync(true, token: token);
            await UniTask.SwitchToMainThread();
            if(token.IsCancellationRequested)
            {
                return;
            }
            _loadingComponent.SetActive(false);
            _coverDisplayer.sprite = cover;
        }
    }
}
