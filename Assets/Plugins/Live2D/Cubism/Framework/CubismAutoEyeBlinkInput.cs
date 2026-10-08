/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using UnityEngine;


namespace Live2D.Cubism.Framework
{
    /// <summary>
    /// Automatic mouth movement.
    /// </summary>
    public sealed class CubismAutoEyeBlinkInput : MonoBehaviour
    {
        /// <summary>
        /// Mean time between eye blinks in seconds.
        /// </summary>
        [SerializeField, Range(1f, 10f)]
        public float Mean = 2.5f;

        /// <summary>
        /// Maximum deviation from <see cref="Mean"/> in seconds.
        /// </summary>
        [SerializeField, Range(0.5f, 5f)]
        public float MaximumDeviation = 2f;

        /// <summary>
        /// Timescale.
        /// </summary>
        [SerializeField, Range(1f, 20f)]
        public float Timescale = 10f;

        /// <summary>
        /// Target controller.
        /// </summary>
        private CubismEyeBlinkController Controller { get; set; }

        /// <summary>
        /// Control over whether output should be evaluated.
        /// </summary>
        private Phase CurrentPhase { get; set; }

        /// <summary>
        /// Used for switching from <see cref="Phase.ClosingEyes"/> to <see cref="Phase.OpeningEyes"/> and back to <see cref="Phase.Idling"/>.
        /// </summary>
        private float LastValue { get; set; }

        /// <summary>
        /// Totalized delta time [s].
        /// </summary>
        private float UserTimeSeconds { get; set; }

        /// <summary>
        /// Time when the current state started [sec].
        /// </summary>
        private float StateStartTimeSeconds { get; set; }

        /// <summary>
        /// Duration of eyelid closing motion [sec]
        /// </summary>
        private float ClosingSeconds { get; set; }

        /// <summary>
        /// Duration of eyelid closed state [sec]
        /// </summary>
        private float ClosedSeconds { get; set; }

        /// <summary>
        /// Duration of eyelid opening motion [sec]
        /// </summary>
        private float OpeningSeconds { get; set; }

        /// <summary>
        /// Next blinking time.
        /// </summary>
        private float NextBlinkingTime { get; set; }

        /// <summary>
        /// Resets the input.
        /// </summary>
        public void Reset()
        {
            CurrentPhase = Phase.Idling;
            NextBlinkingTime = Mean + Random.Range(-MaximumDeviation, MaximumDeviation);
        }

        /// <summary>
        /// Calculate the value when the eyes are closed.
        /// </summary>
        /// <returns>Eye closing value.</returns>
        private float UpdateEyeBlinkClosing()
        {
            var t = (UserTimeSeconds - StateStartTimeSeconds) / ClosingSeconds;

            if (t >= 1.0f)
            {
                CurrentPhase = Phase.ClosedEyes;
                StateStartTimeSeconds = UserTimeSeconds;
            }

            var value = 1.0f - t;

            return value;
        }

        /// <summary>
        /// Calculate the value when the eyes are closed.
        /// </summary>
        /// <returns>Eye closed value.</returns>
        private float UpdateEyeBlinkClosed()
        {
            var t = (UserTimeSeconds - StateStartTimeSeconds) / ClosedSeconds;

            if (t >= 1.0f)
            {
                CurrentPhase = Phase.OpeningEyes;
                StateStartTimeSeconds = UserTimeSeconds;
            }

            var value = 0.0f;

            return value;
        }

        /// <summary>
        /// Calculate the value when the eyes are opening.
        /// </summary>
        /// <returns>Eye opening value.</returns>
        private float UpdateEyeBlinkOpening()
        {
            var t = (UserTimeSeconds - StateStartTimeSeconds) / OpeningSeconds;

            if (t >= 1.0f)
            {
                t = 1.0f;
                CurrentPhase = Phase.Idling;
                NextBlinkingTime = Mean + Random.Range(-MaximumDeviation, MaximumDeviation);
            }

            var value = t;

            return value;
        }

        /// <summary>
        /// Calculate the value when the eyes are opened.
        /// </summary>
        /// <returns>Eye opened value.</returns>
        private float UpdateEyeBlinkIdling()
        {
            NextBlinkingTime -= Time.deltaTime;

            if (NextBlinkingTime < 0.0f)
            {
                CurrentPhase = Phase.ClosingEyes;
                StateStartTimeSeconds = UserTimeSeconds;
            }

            var value = 1.0f;

            return value;
        }



        /// <summary>
        /// Update eye blink.
        /// </summary>
        private void UpdateEyeBlink()
        {
            UserTimeSeconds += (Time.deltaTime * Timescale);
            var value = 0.0f;

            switch (CurrentPhase)
            {
                case Phase.ClosingEyes:
                {
                    value = UpdateEyeBlinkClosing();
                    break;
                }
                case Phase.ClosedEyes:
                {
                    value = UpdateEyeBlinkClosed();
                    break;
                }
                case Phase.OpeningEyes:
                {
                    value = UpdateEyeBlinkOpening();
                    break;
                }
                case Phase.Idling:
                {
                    value = UpdateEyeBlinkIdling();
                    break;
                }
            }

            Controller.EyeOpening = value;
        }

        /// <summary>
        /// Set the details of the blinking motion.
        /// </summary>
        /// <param name="closing">Duration of eyelid closing motion [sec].</param>
        /// <param name="closed">Duration of eyelid closed state [sec].</param>
        /// <param name="opening">Duration of eyelid opening motion [sec].</param>
        public void SetBlinkingSettings(float closing, float closed, float opening)
        {
            ClosingSeconds = closing;
            ClosedSeconds = closed;
            OpeningSeconds = opening;
        }

        #region Unity Event Handling

        /// <summary>
        /// Called by Unity. Initializes input.
        /// </summary>
        private void Start()
        {
            Controller = GetComponent<CubismEyeBlinkController>();
            CurrentPhase = Phase.Idling;
            NextBlinkingTime = Mean + Random.Range(-MaximumDeviation, MaximumDeviation);
            SetBlinkingSettings(1.0f, 0.5f, 1.5f);
        }


        /// <summary>
        /// Called by Unity. Updates controller.
        /// </summary>
        /// <remarks>
        /// Make sure this method is called after any animations are evaluated.
        /// </remarks>
        private void LateUpdate()
        {
            // Fail silently.
            if (Controller == null)
            {
                return;
            }

            UpdateEyeBlink();
        }

        #endregion

        /// <summary>
        /// Internal states.
        /// </summary>
        private enum Phase
        {
            /// <summary>
            /// Idle state.
            /// </summary>
            Idling,

            /// <summary>
            /// State when closing eyes.
            /// </summary>
            ClosingEyes,

            /// <summary>
            /// State when closed eyes.
            /// </summary>
            ClosedEyes,

            /// <summary>
            /// State when opening eyes.
            /// </summary>
            OpeningEyes
        }
    }
}
