using Cysharp.Threading.Tasks;
using MajdataPlay.IO;
using MajdataPlay.Net;
using MajdataPlay.Scenes.Result.Components.Indicators;
using Nito.AsyncEx;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;
#nullable enable
namespace MajdataPlay.Scenes.Result.Components
{
    public class OnlineInteractionSender : MonoBehaviour
    {
        [SerializeField]
        [FormerlySerializedAs("scoreUploadIndicator")]
        ScoreUploadIndicator _scoreUploadIndicator;

        OnlineSongDetail? _onlineDetail;
        MaiScore? _score;

        bool _isLocalOrGuest = false;
        bool _isInited = false;
        bool _isScorePosted = false;

        readonly AsyncLock _sendScoreLock = new();
        readonly CancellationTokenSource _cts = new();

        public void Init(ISongDetail song, MaiScore score)
        {
            if (song is not OnlineSongDetail onlineDetail)
            {
                _isLocalOrGuest = true;
                return;
            }
                
            var serverInfo = onlineDetail.ServerInfo;
            if (serverInfo is null || serverInfo.RuntimeConfig.AuthMethod == NetAuthMethodOption.None)
            {
                _isLocalOrGuest = true;
                return;
            }
            _isInited = true;
            _score = score;
            _onlineDetail = onlineDetail;
        }
        void Update()
        {
            if(!_isInited || _onlineDetail is null || _isLocalOrGuest)
            {
                return;
            }
            if (!_isScorePosted && !MajInstances.GameManager.Settings.Mod.IsAnyModActive() && 
                (InputManager.IsSensorPressedInThisFrame(SensorArea.D4) ||
                InputManager.IsSensorPressedInThisFrame(SensorArea.A3))
                )
            {
                _ = SendScoreAsync();
            }
        }
        private void OnDestroy()
        {
            _cts.Cancel();
        }

        public async Task SendScoreAsync(CancellationToken token = default)
        {
            if(!_isInited || _onlineDetail is null || _score is null || _isScorePosted || _isLocalOrGuest)
            {
                return;
            }
            var cts = CancellationTokenSource.CreateLinkedTokenSource(token, _cts.Token);
            token = cts.Token;
            await using (UniTask.ReturnToCurrentSynchronizationContext())
            {
                using (await _sendScoreLock.LockAsync(token))
                {
                    if(_isScorePosted)
                    {
                        return;
                    }
                    await UniTask.SwitchToMainThread();
                    _scoreUploadIndicator.SetUploading();
                    _scoreUploadIndicator.SetText("MAJTEXT_SCORE_SENDING".i18n());
                    await UniTask.SwitchToThreadPool();
                    var rsp = await Online.PostScoreAsync(_onlineDetail, _score, token);
                    await UniTask.SwitchToMainThread();

                    if (rsp.IsSuccessfully)
                    {
                        _scoreUploadIndicator.SetText("MAJTEXT_SCORE_SENDED".i18n());
                        _scoreUploadIndicator.SetSuccess();
                        _isScorePosted = true;
                    }
                    else
                    {
                        _scoreUploadIndicator.SetError();
                        if(rsp.StatusCode is HttpStatusCode.Unauthorized)
                        {
                            _scoreUploadIndicator.SetText("MAJTEXT_LOGIN_SESSION_EXPIRED".i18n());
                        }
                        else
                        {
                            _scoreUploadIndicator.SetText("MAJTEXT_SCORE_FAILED".i18n());
                        }
                    }
                }
            }
        }
    }
}
