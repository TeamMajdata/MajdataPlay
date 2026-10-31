using Cysharp.Threading.Tasks;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace MajdataPlay.Scenes.TotalResult
{
    internal class DanSongCoverDisplayer : MajBehaviour
    {
        [SerializeField]
        [FormerlySerializedAs("titleDisplayer")]
        TextMeshProUGUI _titleDisplayer;

        [SerializeField]
        [FormerlySerializedAs("artistDisplayer")]
        TextMeshProUGUI _artistDisplayer;

        [SerializeField]
        [FormerlySerializedAs("achievementDisplayer")]
        TextMeshProUGUI _achievementDisplayer;

        [SerializeField]
        [FormerlySerializedAs("levelDisplayer")]
        TextMeshProUGUI _levelDisplayer;

        [SerializeField]
        [FormerlySerializedAs("coverDisplayer")]
        Image _coverDisplayer;

        [SerializeField]
        [FormerlySerializedAs("levelColorDisplayer")]
        Image _levelColorDisplayer;

        [SerializeField]
        [FormerlySerializedAs("levelRingColorDisplayer")]
        Image _levelRingColorDisplayer;

        [SerializeField]
        [FormerlySerializedAs("loadingIndicator")]
        GameObject _loadingIndicator;

        public void SetSongDetail(ISongDetail songDetail, ChartLevel level, GameResult? result, CancellationToken token = default)
        {
            _levelColorDisplayer.color = RuntimeDatabase.DifficultyColors[(int)level];
            _levelRingColorDisplayer.color = RuntimeDatabase.DifficultyColors[(int)level];

            _titleDisplayer.text = songDetail.Title;
            _artistDisplayer.text = songDetail.Artist;
            var levelText = songDetail.Levels[(int)level];
            if(string.IsNullOrEmpty(levelText))
            {
                _levelDisplayer.text = "-";
            }
            else
            {
                _levelDisplayer.text = levelText;
            }


            if (result is GameResult gameResult)
            {
                if (MajInstances.GameManager.UserSettings.Judge.Mode == JudgeModeOption.Classic)
                {
                    _achievementDisplayer.text = $"{gameResult.Acc.Classic:F2}%";
                }
                else
                {
                    _achievementDisplayer.text = $"{gameResult.Acc.DX:F4}%";
                }
            }
            else
            {
                _achievementDisplayer.text = $"---.----%";
            }
            SetCoverAsync(songDetail, token).Forget();
        }

        async UniTask SetCoverAsync(ISongDetail songDetail, CancellationToken token = default)
        {
            await UniTask.SwitchToMainThread(token);
            _coverDisplayer.sprite = null!;
            _loadingIndicator.SetActive(true);
            var cover = await songDetail.GetCoverAsync(true, token: token);
            await UniTask.SwitchToMainThread(token);
            _coverDisplayer.sprite = cover;
            _loadingIndicator.SetActive(false);
        }
    }
}
