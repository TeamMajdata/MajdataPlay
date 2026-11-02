using Cysharp.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.Net;
using MajdataPlay.Net.Models;
using System;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
#nullable enable
namespace MajdataPlay.Scenes.List
{
    public class OnlineInfoDisplayer : MajComponent
    {
        [SerializeField]
        [FormerlySerializedAs("likeCountDisplayer")]
        TextMeshProUGUI _likeCountDisplayer;

        [SerializeField]
        [FormerlySerializedAs("playCountDisplayer")]
        TextMeshProUGUI _playCountDisplayer;

        [SerializeField]
        [FormerlySerializedAs("commentCountDisplayer")]
        TextMeshProUGUI _commentCountDisplayer;

        [SerializeField]
        [FormerlySerializedAs("commentTextDisplayer")]
        TextMeshProUGUI _commentTextDisplayer;

        [SerializeField]
        [FormerlySerializedAs("commentBox")]
        GameObject _commentBox;

        [SerializeField]
        [FormerlySerializedAs("thumbUpImage")]
        Image _thumbUpImage;

        [SerializeField]
        [FormerlySerializedAs("thumbUpGoldColor")]
        Color _thumbUpGoldColor;

        [SerializeField]
        [FormerlySerializedAs("thumbUpGreenColor")]
        Color _thumbUpGreenColor;

        [SerializeField]
        [FormerlySerializedAs("thumbUpNormalColor")]
        Color _thumbUpNormalColor;


        public void SetSongDetail(ISongDetail songDetail, int loadDelayMS = 0, CancellationToken token = default)
        {
            if (!songDetail.IsOnline)
            {
                GameObject.SetActive(false);
                return;
            }
            GameObject.SetActive(true);
            _likeCountDisplayer.text = "--";
            _playCountDisplayer.text = "--";
            _commentCountDisplayer.text = "--";
            _ = RefreshContentAsync((OnlineSongDetail)songDetail, loadDelayMS, token);
        }
        async ValueTask RefreshContentAsync(OnlineSongDetail detail, int loadDelayMS, CancellationToken token = default)
        {
            await UniTask.SwitchToThreadPool();
            await Task.Delay(loadDelayMS, token);
            var (isSuccessfully1, interact) = await GetOnlineInteractionAsync(detail, token);

            await UniTask.SwitchToMainThread(token);
            if (isSuccessfully1)
            {
                var totalLikes = interact.Likes.Length - interact.DisLikeCount;
                _likeCountDisplayer.text = totalLikes.ToString();
                _playCountDisplayer.text = interact.Plays.ToString();
                _commentCountDisplayer.text = interact.Comments.Length.ToString();

                if (interact.IsLiked)
                {
                    _thumbUpImage.color = _thumbUpGreenColor;
                }
                else if (totalLikes > 5)
                {
                    _thumbUpImage.color = _thumbUpGoldColor;
                }
                else
                {
                    _thumbUpImage.color = _thumbUpNormalColor;
                }

                _commentBox.SetActive(true);
                foreach (var comment in interact.Comments)
                {
                    var text = comment.Sender + $"{"MAJTEXT_SAY".i18n()}\n" + comment.Content + "\n";
                    _commentTextDisplayer.text = text;
                    await UniTask.Delay(5000, cancellationToken: token);
                    token.ThrowIfCancellationRequested();
                }
                _commentBox.SetActive(false);
            }
        }


        async ValueTask<(bool IsSuccessfully, MajNetSongInteract Interact)> GetOnlineInteractionAsync(OnlineSongDetail song, CancellationToken token = default)
        {
            try
            {
                await UniTask.SwitchToThreadPool();
                var interact = await Online.MaiChart.GetInteractAsync(song, token);
                token.ThrowIfCancellationRequested();
                if (interact is null)
                {
                    return (false, default);
                }

                return (true, (MajNetSongInteract)interact);
            }
            catch (Exception ex)
            {
                if (ex is HttpException e)
                {
                    if (e.ErrorCode != HttpErrorCode.Canceled)
                    {
                        MajDebug.LogException(ex);
                    }
                }
                else if (ex is not OperationCanceledException)
                {
                    MajDebug.LogException(ex);
                }
            }
            return (false, default);
        }
    }
}