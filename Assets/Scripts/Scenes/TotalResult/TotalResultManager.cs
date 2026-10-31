using MajdataPlay.Utils;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MajdataPlay.IO;
using Cysharp.Threading.Tasks;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Settings;
using UnityEngine.Serialization;
using System;

namespace MajdataPlay.Scenes.TotalResult
{
    public class TotalResultManager : MonoBehaviour
    {
        [SerializeField]
        [FormerlySerializedAs("hpDisplayer")]
        GameObject _hpDisplayer;

        [SerializeField]
        [FormerlySerializedAs("hpSlider")]
        Slider _hpSlider;

        [SerializeField]
        [FormerlySerializedAs("hpTextDisplayer")]
        TextMeshProUGUI _hpTextDisplayer;

        [SerializeField]
        [FormerlySerializedAs("totalAchievementDisplayer")]
        TextMeshProUGUI _totalAchievementDisplayer;

        [SerializeField]
        [FormerlySerializedAs("titleDisplayer")]
        TextMeshProUGUI _titleDisplayer;

        [SerializeField]
        [FormerlySerializedAs("songCoverDisplayerListRoot")]
        GameObject _songCoverDisplayerListRoot;

        DanSongCoverDisplayer[] _songCoverDisplayers = Array.Empty<DanSongCoverDisplayer>();

        GameInfo _gameInfo = Majdata<GameInfo>.Instance!;
        bool _isExited = false;
        bool _isInited = false;
        void Awake()
        {
            InputManager.TouchButtonRingEdge = 4.8f;
            CabinetLed.SetAllLight(Color.white);

            var songCoverDisplayerListRoot = _songCoverDisplayerListRoot.transform;
            var displayerCount = songCoverDisplayerListRoot.childCount;
            _songCoverDisplayers = new DanSongCoverDisplayer[displayerCount];

            for (var i = 0; i < displayerCount; i++)
            {
                var gameObject = songCoverDisplayerListRoot.GetChild(i).gameObject;
                _songCoverDisplayers[i] = gameObject.GetComponent<DanSongCoverDisplayer>();
                gameObject.SetActive(false);
            }
        }
        void Start()
        {
            var danInfo = _gameInfo.DanInfo;
            var results = _gameInfo.Results;
            var levels = _gameInfo.Levels;
            var songInfos = _gameInfo.Charts;
            var name = danInfo.Name;
            var isClassic = MajInstances.GameManager.UserSettings.Judge.Mode == JudgeModeOption.Classic;
            var totalAchievementValue = results.Sum(result => isClassic ? result.Acc.Classic : result.Acc.DX);
            if (_gameInfo.IsDanLifeEnabled)
            {
                _hpTextDisplayer.text = $"{_gameInfo.CurrentHP}/<size=75%>{_gameInfo.MaxHP}";
                _hpSlider.value = _gameInfo.CurrentHP / (float)_gameInfo.MaxHP;
            }
            else
            {
                _hpTextDisplayer.text = "  --";
                _hpSlider.value = 0;
                _hpDisplayer.SetActive(false);
            }
            _totalAchievementDisplayer.text = isClassic ? $"{totalAchievementValue:F2}%" : $"{totalAchievementValue:F4}%";
            _titleDisplayer.text = name;

            for (var i = 0; i < results.Length; i++)
            {
                if(i >= _songCoverDisplayers.Length)
                {
                    break;
                }
                var result = default(GameResult?);
                var songDetail = songInfos[i];
                var displayer = _songCoverDisplayers[i];
                if (results[i].SongDetail is null)
                {
                    result = null;
                }
                else
                {
                    result = results[i];
                }
                displayer.SetSongDetail(songDetail,
                    (ChartLevel)danInfo.SongLevels[i],
                    result,
                    gameObject.GetCancellationTokenOnDestroy());
                displayer.gameObject.SetActive(true);
            }

            DelayBind().Forget();
            MajInstances.AudioManager.StopSFX("bgm_result.mp3");
            MajInstances.AudioManager.PlaySFX("bgm_dan.mp3", true);
            if (!_gameInfo.IsDanLifeEnabled || _gameInfo.CurrentHP > 0)
            {
                MajInstances.AudioManager.PlaySFX("challenge_clear.wav");
            }
            else
            {
                MajInstances.AudioManager.PlaySFX("challenge_fail.wav");
            }
        }

        async UniTaskVoid DelayBind()
        {
            await UniTask.Delay(1000);
            CabinetLed.SetButtonLight(Color.green, 3);
            _isInited = true;
        }
        void Update()
        {
            if(_isExited || !_isInited)
            {
                return;
            }

            if(InputManager.IsButtonClickCompletedInThisFrame(ButtonZone.A4))
            {
                MajInstances.AudioManager.StopSFX("bgm_dan.mp3");
                _isExited = true;
                MajInstances.SceneSwitcher.SwitchScene("List", false);
                
            }
        }
        void OnDestroy()
        {
            InputManager.TouchButtonRingEdge = 5.4f;
        }
    }
}
