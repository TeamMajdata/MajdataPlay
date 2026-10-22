using System.Collections.Generic;
using MajdataPlay.Scenes.Game.Notes;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode.Support
{
    /// <summary>
    /// Provides the display option combinations and hit results used to check the merged judgment and direction text
    /// (class C text) of the judgment effects.
    /// </summary>
    internal static class ClassCCases
    {
        /// <summary>Gets the option combinations as break flag, break score option and critical perfect option.</summary>
        public static object[] Options => new object[]
        {
            new object[] { false, false, false },
            new object[] { false, false, true },
            new object[] { true, true, false },
            new object[] { true, false, false },
            new object[] { true, false, true }
        };

        /// <summary>Enumerates every hit grade with an offset that matches its direction.</summary>
        /// <returns>The grade, offset and expected sprite name suffix of every hit.</returns>
        public static IEnumerable<(JudgeGrade Grade, float Diff, string Suffix)> Hits()
        {
            for (var grade = JudgeGrade.LateGood; grade <= JudgeGrade.FastGood; grade++)
            {
                if (grade == JudgeGrade.Perfect)
                {
                    yield return (grade, 5f, "_Late");
                    yield return (grade, -5f, "_Fast");
                }
                else if (grade < JudgeGrade.Perfect)
                {
                    yield return (grade, 5f, "_Late");
                }
                else
                {
                    yield return (grade, -5f, "_Fast");
                }
            }
        }
    }
}
