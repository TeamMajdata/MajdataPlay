using System;
using System.Net;
using Cysharp.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO;
using MajdataPlay.Net;
using LitMotion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Scenes.Result.Components
{
    public sealed class SongFeedbackPanel : MonoBehaviour
    {
        [SerializeField] GameObject _panel = null!;
        [SerializeField] GameObject _songResult = null!;
        [SerializeField] GameObject _scoreIndicators = null!;
        [SerializeField] GameObject _feedbackEntry = null!;
        [SerializeField] TMP_Text _feedbackEntryLabel = null!;
        [SerializeField] CanvasGroup _scoreCanvasGroup = null!;
        [SerializeField] CanvasGroup _scoreIndicatorsCanvasGroup = null!;
        [SerializeField] CanvasGroup _feedbackCanvasGroup = null!;
        [SerializeField] Button _likeButton = null!;
        [SerializeField] Button _dislikeButton = null!;
        [SerializeField] GameObject _likeOutline = null!;
        [SerializeField] GameObject _dislikeOutline = null!;
        [SerializeField] TMP_InputField _commentInput = null!;
        [SerializeField] Button _clearButton = null!;
        [SerializeField] TMP_Text _status = null!;
        [SerializeField] GameObject _backIcon = null!;
        [SerializeField] GameObject _nextIcon = null!;
        [SerializeField] TMP_Text _backLabel = null!;
        [SerializeField] TMP_Text _nextLabel = null!;

        public bool IsOpen => _panel.activeSelf;
        public bool IsAvailable { get; private set; }
        public bool IsFirstPlay { get; private set; }
        public bool IsBusy { get; private set; }

        public const float TRANSITION_DURATION_SEC = 0.4f;

        OnlineSongDetail? _song;
        bool _canSend;
        bool _loaded;
        bool _loading;
        int _vote;
        int _submittedVote;
        bool _voteNeedsRefresh;
        bool _likeHeld;
        bool _dislikeHeld;
        MotionHandle _fade;

        void Awake()
        {
            _clearButton.onClick.AddListener(ClearComment);
            _commentInput.onValueChanged.AddListener(_ => UpdateClearButton());
            _commentInput.richText = false;
        }

        public void Init(ISongDetail song, bool allowFeedback)
        {
            _song = song as OnlineSongDetail;
            IsAvailable = allowFeedback && _song != null
                && string.Equals(_song.ServerInfo.Url.Host, "majdata.net", StringComparison.OrdinalIgnoreCase)
                && _song.ServerInfo.RuntimeConfig.IsLoggedIn
                && _song.ServerInfo.RuntimeConfig.AuthMethod != NetAuthMethodOption.None;
            _canSend = IsAvailable;
            IsFirstPlay = false;
            if (IsAvailable)
            {
                // Ratings belong to the song/chart ID, shared by all its difficulties.
                // Capture this before saving the current result increments PlayCount.
                var history = ScoreManager.GetSongScores(song);
                IsFirstPlay = history.Easy.PlayCount == 0 && history.Basic.PlayCount == 0
                    && history.Advance.PlayCount == 0 && history.Expert.PlayCount == 0
                    && history.Master.PlayCount == 0 && history.ReMaster.PlayCount == 0
                    && history.UTAGE.PlayCount == 0;
            }
            SetVisible(false);
        }

        public async UniTask OpenAsync()
        {
            if (!IsAvailable || IsOpen || IsBusy) return;
            await TransitionToFeedbackAsync();
            if (!_loaded && !_loading)
            {
                LoadInteractionAsync().Forget();
            }
        }

        async UniTask TransitionToFeedbackAsync()
        {
            var token = this.GetCancellationTokenOnDestroy();
            IsBusy = true;
            RefreshControls();
            try
            {
                _fade = LMotion.Create(1f, 0f, TRANSITION_DURATION_SEC / 2)
                    .WithEase(Ease.InOutSine)
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .Bind(SetScoreAlpha);
                await _fade.ToUniTask(token);

                SetVisible(true);
                _feedbackCanvasGroup.alpha = 0;
                _fade = LMotion.Create(0f, 1f, TRANSITION_DURATION_SEC / 2)
                    .WithEase(Ease.InOutSine)
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .Bind(alpha => _feedbackCanvasGroup.alpha = alpha);
                await _fade.ToUniTask(token);
            }
            finally
            {
                _fade.TryCancel();
                IsBusy = false;
                if (this != null) RefreshControls();
            }
        }

#if UNITY_EDITOR
        public UniTask PreviewFeedbackTransitionAsync()
        {
            if (Application.isPlaying || IsBusy) return UniTask.CompletedTask;
            SetVisible(false);
            // Exercise the runtime transition without changing eligibility or contacting the server.
            return TransitionToFeedbackAsync();
        }

        public void ResetFeedbackPreview()
        {
            if (Application.isPlaying) return;
            _fade.TryCancel();
            SetVisible(false);
        }
#endif

        void SetScoreAlpha(float alpha)
        {
            _scoreCanvasGroup.alpha = alpha;
            _scoreIndicatorsCanvasGroup.alpha = alpha;
        }

        public void SetVisible(bool visible)
        {
            _panel.SetActive(visible);
            _songResult.SetActive(!visible);
            _scoreIndicators.SetActive(!visible);
            _feedbackEntry.SetActive(!visible && IsAvailable && !IsFirstPlay);
            _feedbackEntryLabel.text = "MAJTEXT_FEEDBACK_OPEN".i18n();
            SetScoreAlpha(1);
            _feedbackCanvasGroup.alpha = 1;
            _backIcon.SetActive(!visible);
            _backLabel.gameObject.SetActive(visible);
            _backLabel.text = "MAJTEXT_FEEDBACK_BACK".i18n();
            _nextIcon.SetActive(!visible && !IsAvailable);
            _nextLabel.gameObject.SetActive(visible || IsAvailable);
            _nextLabel.text = (visible ? "MAJTEXT_FEEDBACK_SUBMIT" : "MAJTEXT_FEEDBACK_NEXT").i18n();
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                _backLabel.text = "返回";
                _nextLabel.text = visible ? "提交" : "继续";
            }
#endif
            if (!visible) _commentInput.DeactivateInputField();
        }

        async UniTask LoadInteractionAsync()
        {
            _loading = true;
            _status.text = "MAJTEXT_FEEDBACK_LOADING".i18n();
            RefreshControls();
            var token = this.GetCancellationTokenOnDestroy();
            try
            {
                var response = await Online.GetChartInteractAsync(_song!, token, forceRefresh: true);
                await UniTask.SwitchToMainThread(token);
                if (response is not MajNetSongInteract interaction)
                {
                    _status.text = "MAJTEXT_FEEDBACK_LOAD_FAILED".i18n();
                    return;
                }
                _loaded = true;
                _vote = interaction.IsLiked ? 1 : interaction.IsDisLiked ? -1 : 0;
                _submittedVote = _vote;
                _voteNeedsRefresh = false;
                _status.text = "";
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception e)
            {
                await UniTask.SwitchToMainThread();
                if (this != null) _status.text = "MAJTEXT_FEEDBACK_LOAD_FAILED".i18n();
                MajDebug.LogException(e);
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                _loading = false;
                if (this != null) RefreshControls();
            }
        }

        public void SelectVote(int vote)
        {
            if (IsBusy || _loading || !_loaded || !_canSend || (vote != 1 && vote != -1)) return;
            _vote = _vote == vote ? 0 : vote;
            RefreshControls();
        }

        void UpdateVoteSensors(bool likeHeld, bool dislikeHeld)
        {
            var likePressed = likeHeld && !_likeHeld;
            var dislikePressed = dislikeHeld && !_dislikeHeld;
            _likeHeld = likeHeld;
            _dislikeHeld = dislikeHeld;

            // Keep tracking held contacts while hidden/loading so enabling feedback cannot vote.
            if (!IsOpen || IsBusy || _loading) return;
            if (likePressed) SelectVote(1);
            else if (dislikePressed) SelectVote(-1);
        }

        public async UniTask<bool> SubmitAsync()
        {
            if (IsBusy || _loading) return false;
            if (!_canSend) return true;
            if (!_loaded)
            {
                await LoadInteractionAsync();
                return false;
            }
            var vote = _vote;
            var comment = _commentInput.text.Trim();
            if (vote == 0 && _submittedVote == 0 && !_voteNeedsRefresh && comment.Length == 0) return true;

            IsBusy = true;
            _commentInput.DeactivateInputField();
            _status.text = "MAJTEXT_FEEDBACK_SENDING".i18n();
            RefreshControls();
            var token = this.GetCancellationTokenOnDestroy();
            try
            {
                if (_voteNeedsRefresh)
                {
                    // A failed response does not prove that the server rejected the previous vote.
                    // Refresh only the server baseline, preserving the player's current draft.
                    var interaction = await Online.GetChartInteractAsync(_song!, token, forceRefresh: true);
                    await UniTask.SwitchToMainThread(token);
                    if (interaction is not MajNetSongInteract current)
                    {
                        ShowFailure(null);
                        return false;
                    }
                    _submittedVote = current.IsLiked ? 1 : current.IsDisLiked ? -1 : 0;
                    _voteNeedsRefresh = false;
                }
                if (vote != 0 || _submittedVote != 0)
                {
                    _voteNeedsRefresh = true;
                    var response = await (vote switch
                    {
                        1 => Online.PostLikeAsync(_song!, token),
                        -1 => Online.PostDislikeAsync(_song!, token),
                        _ => _submittedVote > 0
                            ? Online.DeleteLikeAsync(_song!, token)
                            : Online.DeleteDislikeAsync(_song!, token)
                    });
                    await UniTask.SwitchToMainThread(token);
                    if (!response.IsSuccessfully)
                    {
                        ShowFailure(response.StatusCode);
                        return false;
                    }
                    _submittedVote = vote;
                    _voteNeedsRefresh = false;
                }
                if (comment.Length != 0)
                {
                    var response = await Online.PostCommentAsync(_song!, comment, token);
                    await UniTask.SwitchToMainThread(token);
                    if (!response.IsSuccessfully)
                    {
                        ShowFailure(response.StatusCode);
                        return false;
                    }
                }
                _commentInput.text = "";
                return true;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
            catch (Exception e)
            {
                await UniTask.SwitchToMainThread();
                if (this != null) ShowFailure(null);
                MajDebug.LogException(e);
                return false;
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                IsBusy = false;
                if (this != null)
                {
                    RefreshControls();
                }
            }
        }

        void ShowFailure(HttpStatusCode? code)
        {
            _status.text = (code == HttpStatusCode.Unauthorized
                ? "MAJTEXT_LOGIN_SESSION_EXPIRED" : "MAJTEXT_FEEDBACK_FAILED").i18n();
        }

        void RefreshControls()
        {
            var editable = _canSend && _loaded && !IsBusy && !_loading;
            _feedbackCanvasGroup.interactable = editable;
            _feedbackCanvasGroup.blocksRaycasts = editable;
            _likeButton.interactable = editable;
            _dislikeButton.interactable = editable;
            _commentInput.interactable = editable;
            _likeOutline.SetActive(_vote == 1);
            _dislikeOutline.SetActive(_vote == -1);
            UpdateClearButton();
        }

        void UpdateClearButton()
        {
            _clearButton.gameObject.SetActive(!string.IsNullOrEmpty(_commentInput.text));
            _clearButton.interactable = _commentInput.interactable;
        }

        public void ClearComment()
        {
            if (!_commentInput.interactable) return;
            _commentInput.text = "";
            FocusComment();
        }

        void FocusComment()
        {
            if (!_commentInput.interactable || _commentInput.isFocused) return;
            _commentInput.Select();
            _commentInput.ActivateInputField();
            _commentInput.MoveTextEnd(false);
        }

        void Update()
        {
            // Mouse and touchscreen presses already enter this same sensor pipeline.
            // Merge the entire dislike region before detecting its press edge.
            UpdateVoteSensors(InputManager.GetSensorState(SensorArea.C).IsPressed,
                InputManager.GetSensorState(SensorArea.B2).IsPressed
                || InputManager.GetSensorState(SensorArea.B3).IsPressed
                || InputManager.GetSensorState(SensorArea.E3).IsPressed);
            if (!IsOpen || IsBusy) return;
            // A3 here is the touch sensor over the input's clear icon, not cabinet button 3.
            if (InputManager.IsSensorPressedInThisFrame(SensorArea.A3)) ClearComment();
            else if (InputManager.IsSensorPressedInThisFrame(SensorArea.B4)
                  || InputManager.IsSensorPressedInThisFrame(SensorArea.B5)
                  || InputManager.IsSensorPressedInThisFrame(SensorArea.E4)
                  || InputManager.IsSensorPressedInThisFrame(SensorArea.E5)
                  || InputManager.IsSensorPressedInThisFrame(SensorArea.E6)
                  || InputManager.IsSensorPressedInThisFrame(SensorArea.D4)) FocusComment();
        }

        void OnDestroy()
        {
            _fade.TryCancel();
        }
    }
}
