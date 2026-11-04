using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace MajdataPlay.Scenes.Game
{
    internal sealed class TouchHoldEffectDisplayer : TapEffectDisplayer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected override bool PlayResult(in NoteJudgeResult judgeResult)
        {
            bool canPlay;
            if (judgeResult.IsMine && judgeResult.Grade is not (JudgeGrade.Miss or JudgeGrade.TooFast))
            {
                return false;
            }
            else if (judgeResult.IsBreak)
            {
                canPlay = NoteEffectManager.CheckJudgeDisplaySetting(MajEnv.UserSettings.Judge.BreakJudgeType, judgeResult);
            }
            else
            {
                canPlay = NoteEffectManager.CheckJudgeDisplaySetting(MajEnv.UserSettings.Judge.TouchJudgeType, judgeResult);
            }
            if (!canPlay)
            {
                return false;
            }
            JudgeTextDisplayer.Play(judgeResult);
            return true;
        }
    }
}
