using Cysharp.Text;
using Cysharp.Threading.Tasks;
using MajdataPlay.i18n;
using MajdataPlay.Settings;
using System;
using TMPro;

#nullable enable
namespace MajdataPlay
{
    internal sealed class RuntimeInfoDisplayer : MajComponent
    {
        const string NEW_VERSION_AVAILABLE_I18N_KEY = "MAJTEXT_NEW_VERSION_AVAILABLE";
        const float REFRESH_INTERVAL_SECONDS = 1f;

        FPSMonitor _fpsMonitor = null!;
        TextMeshProUGUI _textDisplayer = null!;
        GameSetting _setting = null!;

        string _versionText = string.Empty;
        string _newVersionAvailableText = string.Empty;
        bool _hasNewVersionAvailable;
        float _refreshTimer = REFRESH_INTERVAL_SECONDS;
        TimeSpan _lastUpdateTiming = TimeSpan.Zero;

        protected override void Awake()
        {
            base.Awake();
            Majdata<RuntimeInfoDisplayer>.SetAsSingleton(this);
            _fpsMonitor = GetComponent<FPSMonitor>();
            _textDisplayer = GetComponent<TextMeshProUGUI>();
            _textDisplayer.enabled = false;
            _lastUpdateTiming = MajTimeline.UnscaledTime;
            Localization.OnLanguageChanged += OnLanguageChanged;
            enabled = false;
        }

        void OnDestroy()
        {
            Localization.OnLanguageChanged -= OnLanguageChanged;
            if (ReferenceEquals(Majdata<RuntimeInfoDisplayer>.Instance, this))
            {
                Majdata<RuntimeInfoDisplayer>.Free();
            }
        }

        internal void Init()
        {
            _setting = MajEnv.Settings;
            _versionText = MajInstances.GameVersion.ToString();
            _textDisplayer.enabled = _setting.Debug.DisplayRuntimeInfo;
            enabled = true;
            CheckForUpdateAsync().Forget();
        }

        void LateUpdate()
        {
            _textDisplayer.enabled = _setting.Debug.DisplayRuntimeInfo;

            var currentTime = MajTimeline.UnscaledTime;
            var delta = currentTime - _lastUpdateTiming;
            _lastUpdateTiming = currentTime;
            if (_refreshTimer > 0)
            {
                _refreshTimer -= (float)delta.TotalSeconds;
                return;
            }

            RefreshText();
            _refreshTimer = REFRESH_INTERVAL_SECONDS;
        }

        void RefreshText()
        {
            var averageFPS = _fpsMonitor.AverageFPS;
            using var sb = ZString.CreateStringBuilder(true);
            sb.AppendFormat("FPS <mspace=0.62em>{0,7:F2}</mspace>   1% ", averageFPS);
            if (_fpsMonitor.TryGetOnePercentLowFPS(out var onePercentLowFPS))
            {
                sb.AppendFormat("<mspace=0.62em>{0,7:F2}</mspace>", onePercentLowFPS);
            }
            else
            {
                sb.Append("<mspace=0.62em>  --.--</mspace>");
            }

            sb.Append("   Ver. ");
            sb.Append(_versionText);
            if (_hasNewVersionAvailable)
            {
                sb.Append("   (");
                sb.Append(_newVersionAvailableText);
                sb.Append(')');
            }

            var text = sb.AsArraySegment();
            _textDisplayer.SetCharArray(text.Array, text.Offset, text.Count);
        }

        void OnLanguageChanged(object? sender, Language language)
        {
            if (!_hasNewVersionAvailable)
            {
                return;
            }

            _newVersionAvailableText = NEW_VERSION_AVAILABLE_I18N_KEY.i18n();
            _refreshTimer = 0;
        }

        async UniTaskVoid CheckForUpdateAsync()
        {
            if (!await VersionChecker.IsNewVersionAvailableAsync())
            {
                return;
            }

            await UniTask.SwitchToMainThread();
            _hasNewVersionAvailable = true;
            _newVersionAvailableText = NEW_VERSION_AVAILABLE_I18N_KEY.i18n();
            _refreshTimer = 0;
        }
    }
}
