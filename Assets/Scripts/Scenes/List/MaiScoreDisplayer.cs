using MajdataPlay.Scenes.Game;
using MajdataPlay.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

namespace MajdataPlay.Scenes.List
{
    public class MaiScoreDisplayer : MonoBehaviour
    {
        [SerializeField]
        [FormerlySerializedAs("archieveRate")]
        TextMeshProUGUI _archieveRate;

        [SerializeField]
        [FormerlySerializedAs("apbg")]
        GameObject _APbg;

        [SerializeField]
        [FormerlySerializedAs("clearMark")]
        TextMeshProUGUI _clearMark;

        [SerializeField]
        [FormerlySerializedAs("rank")]
        TextMeshProUGUI _rank;

        public void SetScore(ISongDetail songDetail, ChartLevel level)
        {
            var score = ScoreManager.GetScore(songDetail, level);
            if (score.PlayCount == 0)
            {
                _APbg.SetActive(false);
                _archieveRate.enabled = false;
                _rank.text = "";
            }
            else
            {
                var isClassic = MajInstances.GameManager.UserSettings.Judge.Mode == JudgeModeOption.Classic;
                _archieveRate.text = isClassic ? $"{score.Acc.Classic:F2}%" : $"{score.Acc.DX:F4}%";
                _archieveRate.enabled = true;
                _APbg.SetActive(false);
                if (score.ComboState == ComboState.APPlus)
                {
                    _APbg.SetActive(true);
                    _clearMark.text = "AP+";
                }
                else if (score.ComboState == ComboState.AP)
                {
                    _APbg.SetActive(true);
                    _clearMark.text = "AP";
                }
                else if (score.ComboState == ComboState.FCPlus)
                {
                    _APbg.SetActive(true);
                    _clearMark.text = "FC+";
                }
                else if (score.ComboState == ComboState.FC)
                {
                    _APbg.SetActive(true);
                    _clearMark.text = "FC";
                }
                var dxacc = score.Acc.DX;
                var rank = _rank;
                if (dxacc >= 100.5f)
                {
                    rank.text = "SSS+";
                }
                else if (dxacc >= 100f)
                {
                    rank.text = "SSS";
                }
                else if (dxacc >= 99.5f)
                {
                    rank.text = "SS+";
                }
                else if (dxacc >= 99f)
                {
                    rank.text = "SS";
                }
                else if (dxacc >= 98f)
                {
                    rank.text = "S+";
                }
                else if (dxacc >= 97f)
                {
                    rank.text = "S";
                }
                else
                {
                    _rank.text = "";
                }
            }
        }
    }
}
