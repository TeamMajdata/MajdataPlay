using System;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.Game.Notes.Slide;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode.Doubles
{
    /// <summary>
    /// A minimal concrete slide that exposes the protected judgment, bar, star and queue helpers of
    /// <see cref="SlideBase"/>; tests fill its bars, stars and judge queues directly.
    /// </summary>
    internal sealed class ProbeSlide : SlideBase
    {
        /// <summary>Gets the number of times <see cref="Init"/> ran.</summary>
        public int InitCount { get; private set; }

        /// <summary>Gets the number of times the skin was requested.</summary>
        public int LoadSkinCount { get; private set; }

        /// <summary>Gets or sets the stored judgment result.</summary>
        public JudgeGrade Result
        {
            get => JudgeResult;
            set => JudgeResult = value;
        }

        /// <summary>Gets or sets the stored judgment offset in milliseconds.</summary>
        public float Diff
        {
            get => JudgeDiff;
            set => JudgeDiff = value;
        }

        /// <summary>Gets or sets a value indicating whether the slide is judged.</summary>
        public bool Judged
        {
            get => IsJudged;
            set => IsJudged = value;
        }

        /// <summary>Gets or sets the answer timing in seconds.</summary>
        public float AnswerTiming
        {
            get => JudgeTiming;
            set => JudgeTiming = value;
        }

        /// <summary>Gets or sets the time the slide waits after its judgment before it ends.</summary>
        public float WaitTime
        {
            get => LastWaitTimeSec;
            set => LastWaitTimeSec = value;
        }

        /// <summary>Gets or sets a value indicating whether sensor checks are enabled.</summary>
        public bool Checkable
        {
            get => IsCheckable;
            set => IsCheckable = value;
        }

        /// <summary>Gets a value indicating whether the slide sound has been requested.</summary>
        public bool SoundPlayed => IsSoundPlayed;

        /// <summary>Gets the fade-in completion flag.</summary>
        public uint FadeInFlag => SlideBarFadeInFlag;

        /// <summary>Gets the index of the last hidden bar plus one.</summary>
        public int HiddenBarCount => LastHiddenSlideBarIndex;

        /// <summary>Gets the remaining time until the arrive timing.</summary>
        public float RemainingTime => GetRemainingTime();

        /// <summary>Gets the time from the arrive timing to the current frame.</summary>
        public float TimeSpanToArrive => GetTimeSpanToArriveTiming();

        /// <summary>Gets the slide result object, if any.</summary>
        public SlideOK? SlideResult => SlideOK;

        /// <summary>Marks the slide as initialized.</summary>
        public override void Init()
        {
            InitCount++;
            State = NoteStatus.Inited;
        }

        /// <summary>Sets the slide state.</summary>
        /// <param name="state">The new state.</param>
        public void SetState(NoteStatus state)
        {
            State = state;
        }

        /// <summary>Adds an arrow bar.</summary>
        /// <param name="renderer">The bar renderer.</param>
        public void AddBar(SpriteRenderer renderer)
        {
            SlideBars.Add(renderer.gameObject);
            SlideBarRenderers.Add(renderer);
            SlideBarTransforms.Add(renderer.transform);
        }

        /// <summary>Stores a star at a star slot.</summary>
        /// <param name="index">The star slot from 0 to 2.</param>
        /// <param name="renderer">The star renderer.</param>
        public void SetStar(int index, SpriteRenderer renderer)
        {
            Stars.Span[index] = renderer.gameObject;
            StarRenderers.Span[index] = renderer;
            StarTransforms.Span[index] = renderer.transform;
        }

        /// <summary>Gets the star stored at a star slot.</summary>
        /// <param name="index">The star slot from 0 to 2.</param>
        /// <returns>The star object, or null.</returns>
        public GameObject? GetStar(int index)
        {
            return Stars.Span[index];
        }

        /// <summary>Replaces one of the three judge queues.</summary>
        /// <param name="index">The queue index from 0 to 2.</param>
        /// <param name="queue">The queue.</param>
        public void SetQueue(int index, Memory<SlideArea> queue)
        {
            JudgeQueues[index] = queue;
        }

        /// <summary>Sets the slide result object.</summary>
        /// <param name="slideOK">The slide result object or null.</param>
        public void SetSlideOK(SlideOK? slideOK)
        {
            SlideOK = slideOK;
        }

        /// <summary>Sets the fade-in timings.</summary>
        /// <param name="start">The time the fade-in starts.</param>
        /// <param name="cutoff">The time the partial fade-in reaches its maximum alpha.</param>
        /// <param name="completed">The time the bars become fully opaque.</param>
        /// <param name="duration">The partial fade-in duration.</param>
        /// <param name="maxAlpha">The alpha reached by the partial fade-in.</param>
        public void SetFadeIn(float start, float cutoff, float completed, float duration, float maxAlpha)
        {
            FadeInTiming = start;
            FadeInCutoffTiming = cutoff;
            FadeInCompletedTiming = completed;
            FadeInDurationTimeSec = duration;
            FadeInMaxAlpha = maxAlpha;
        }

        /// <summary>Runs the deluxe slide judgment.</summary>
        /// <param name="currentSec">The finish time in seconds.</param>
        public void RunJudge(float currentSec)
        {
            Judge(currentSec);
        }

        /// <summary>Runs the classic slide judgment.</summary>
        /// <param name="currentSec">The finish time in seconds.</param>
        public void RunJudgeClassic(float currentSec)
        {
            JudgeClassic(currentSec);
        }

        /// <summary>Merges subdivided perfect grades into a plain perfect.</summary>
        /// <param name="grade">The grade.</param>
        /// <returns>The corrected grade.</returns>
        public JudgeGrade Correct(JudgeGrade grade)
        {
            JudgeGradeCorrection(ref grade);
            return grade;
        }

        /// <summary>Converts a grade with the active judge style.</summary>
        /// <param name="grade">The grade.</param>
        /// <returns>The converted grade.</returns>
        public JudgeGrade Convert(JudgeGrade grade)
        {
            ConvertJudgeGrade(ref grade);
            return grade;
        }

        /// <summary>Hides the bars up to a progress index.</summary>
        /// <param name="endIndex">The progress index.</param>
        public void RunHideBar(int endIndex)
        {
            HideBar(endIndex);
        }

        /// <summary>Hides every bar.</summary>
        public void RunHideAllBar()
        {
            HideAllBar();
        }

        /// <summary>Sets the alpha of every bar.</summary>
        /// <param name="alpha">The alpha.</param>
        public void RunSetSlideBarAlpha(float alpha)
        {
            SetSlideBarAlpha(alpha);
        }

        /// <summary>Sets the material of every bar.</summary>
        /// <param name="material">The material.</param>
        public void RunSetSlideBarMaterial(Material material)
        {
            SetSlideBarMaterial(material);
        }

        /// <summary>Sets the alpha and material of every bar.</summary>
        /// <param name="alpha">The alpha.</param>
        /// <param name="material">The material.</param>
        public void RunSetSlideBarAlphaAndMaterial(float alpha, Material material)
        {
            SetSlideBarAlphaAndMaterial(alpha, material);
        }

        /// <summary>Advances the bar fade-in.</summary>
        public void RunSlideBarFadeIn()
        {
            SlideBarFadeIn();
        }

        /// <summary>Runs the too-late judgment.</summary>
        public void RunTooLateJudge()
        {
            TooLateJudge();
        }

        /// <summary>Ends the slide.</summary>
        public void RunEnd()
        {
            End();
        }

        /// <summary>Runs the mine check.</summary>
        public void RunMineCheck()
        {
            MineCheck();
        }

        /// <summary>Runs one sensor check over the judge queues.</summary>
        /// <returns>True when any judge area was triggered.</returns>
        public bool RunSensorCheck()
        {
            return SensorCheck().IsAnyAreaTriggered;
        }

        /// <summary>Requests the slide sound.</summary>
        public void RunPlaySfx()
        {
            PlaySFX();
        }

        /// <summary>Requests the judgment sound.</summary>
        /// <param name="grade">The grade.</param>
        /// <param name="isBreak">Whether the slide is a break.</param>
        public void RunPlayJudgeSfx(JudgeGrade grade, bool isBreak)
        {
            PlayJudgeSFX(new NoteJudgeResult()
            {
                Grade = grade,
                IsBreak = isBreak
            });
        }

        /// <summary>Checks whether the slide result may be shown for a grade.</summary>
        /// <param name="grade">The grade.</param>
        /// <returns>True when a slide result exists and the display setting allows the grade.</returns>
        public bool RunPlaySlideOK(JudgeGrade grade)
        {
            return PlaySlideOK(new NoteJudgeResult()
            {
                Grade = grade
            });
        }

        /// <summary>Forgets every star.</summary>
        public void RunDestroyStars()
        {
            DestroyStars();
        }

        /// <summary>Shows or hides every star.</summary>
        /// <param name="state">Whether the stars are visible.</param>
        public void RunSetStarActive(bool state)
        {
            SetStarActive(state);
        }

        /// <summary>Counts the skin request.</summary>
        protected override void LoadSkin()
        {
            LoadSkinCount++;
        }
    }
}
