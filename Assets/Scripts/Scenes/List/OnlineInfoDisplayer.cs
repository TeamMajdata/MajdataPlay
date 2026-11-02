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


        /// <summary>Shows the online counts and comments of a song, or hides the displayer for a local song.</summary>
        /// <param name="songDetail">The selected song.</param>
        /// <param name="loadDelayMS">The delay before the requests, so quickly skipped songs send none.</param>
        /// <param name="token">The token canceling the requests and the comment rotation, for example when another song is selected.</param>
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
            _commentBox.SetActive(false);
            _ = RefreshContentAsync((OnlineSongDetail)songDetail, loadDelayMS, token);
        }
        /// <summary>
        /// Shows the score, plays and comment count of a song, then rotates its live top-level comments in the
        /// <see cref="CommentSort.Best"/> order, five seconds each.
        /// </summary>
        /// <param name="detail">The online song.</param>
        /// <param name="loadDelayMS">The delay before the requests.</param>
        /// <param name="token">The token canceling the requests and the rotation.</param>
        /// <returns>A task that completes when every comment was shown or a request failed.</returns>
        /// <exception cref="OperationCanceledException"><paramref name="token"/> was canceled.</exception>
        async ValueTask RefreshContentAsync(OnlineSongDetail detail, int loadDelayMS, CancellationToken token = default)
        {
            await UniTask.SwitchToThreadPool();
            await Task.Delay(loadDelayMS, token);
            var summary = await RequestAsync(() => Online.MaiChart.GetInteractSummaryAsync(detail, token), token);

            await UniTask.SwitchToMainThread(token);
            if (summary is not MajNetChartInteractSummary counts)
            {
                return;
            }
            _likeCountDisplayer.text = counts.Score.ToString();
            _playCountDisplayer.text = counts.Plays.ToString();
            _commentCountDisplayer.text = counts.Comments.ToString();

            if (counts.MyVote > 0)
            {
                _thumbUpImage.color = _thumbUpGreenColor;
            }
            else if (counts.Score > 5)
            {
                _thumbUpImage.color = _thumbUpGoldColor;
            }
            else
            {
                _thumbUpImage.color = _thumbUpNormalColor;
            }

            if (counts.Comments == 0)
            {
                return;
            }
            var page = await RequestAsync(() => Online.MaiChart.GetCommentsAsync(detail, CommentSort.Best, 1, token), token);
            await UniTask.SwitchToMainThread(token);
            if (page is not MajNetCommentPage comments)
            {
                return;
            }
            _commentBox.SetActive(true);
            foreach (var comment in comments.Items)
            {
                // A deleted comment with replies stays in the thread as a placeholder without author or text.
                if (comment.IsPlaceholder)
                {
                    continue;
                }
                var text = comment.Author + $"{"MAJTEXT_SAY".i18n()}\n" + comment.Body + "\n";
                _commentTextDisplayer.text = text;
                await UniTask.Delay(5000, cancellationToken: token);
                token.ThrowIfCancellationRequested();
            }
            _commentBox.SetActive(false);
        }
        /// <summary>Runs an online request on the thread pool and logs its failures other than cancellation.</summary>
        /// <typeparam name="T">The type of the response.</typeparam>
        /// <param name="request">The request.</param>
        /// <param name="token">The token canceling the request.</param>
        /// <returns>The response, or null when the request failed or was canceled.</returns>
        async ValueTask<T?> RequestAsync<T>(Func<ValueTask<T?>> request, CancellationToken token) where T : struct
        {
            try
            {
                await UniTask.SwitchToThreadPool();
                var response = await request();
                token.ThrowIfCancellationRequested();
                return response;
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
            return null;
        }
    }
}