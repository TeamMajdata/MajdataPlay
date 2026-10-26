using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Utils;
using MajdataPlay.Scenes.Game.Utils;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Scenes.Game
{
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal sealed class HoldEffectDisplayer: MajComponent
    {
        /// <summary>
        /// Gets the world position or updates its X and Y coordinates while preserving sorting depth.
        /// </summary>
        public Vector3 Position
        {
            get => Transform.position;
            set => NoteHelper.Sorting.SetPosition(Transform, value);
        }
        /// <summary>
        /// Gets the local position or updates its X and Y coordinates while preserving sorting depth.
        /// </summary>
        public Vector3 LocalPosition
        {
            get => Transform.localPosition;
            set => NoteHelper.Sorting.SetLocalPosition(Transform, value);
        }

        Material _material;
        ParticleSystem _particleSystem;
        ParticleSystemRenderer _psRenderer;
        
        bool _isPlaying = false;
        Color _color = WHITE;
        Color _newColor = WHITE;

        static readonly int MATERIAL_COLOR_ID = Shader.PropertyToID("_Color");
        static readonly Color YELLOW = new Color(1f, 227f / 255f, 0f);
        static readonly Color PINK = new Color(1f, 0.70f, 0.94f);
        static readonly Color GREEN = new Color(0.56f, 1f, 0.59f);
        static readonly Color WHITE = new Color(1f, 1f, 1f);
        protected override void Awake()
        {
            base.Awake();
            _particleSystem = GetComponent<ParticleSystem>();
            _psRenderer = GetComponent<ParticleSystemRenderer>();
            _material = _psRenderer.material;
            SetActiveInternal(false);
        }
        public void Reset()
        {
            SetActive(false);
            _isPlaying = false;
        }
        public void Play(in JudgeGrade judgeType)
        {
            if (_isPlaying)
            {
                return;
            }
            _isPlaying = true;

            var newColor = WHITE;
            switch (judgeType)
            {
                case JudgeGrade.LatePerfect3rd:
                case JudgeGrade.FastPerfect3rd:
                case JudgeGrade.LatePerfect2nd:
                case JudgeGrade.FastPerfect2nd:
                case JudgeGrade.Perfect:
                    newColor = YELLOW;
                    break;
                case JudgeGrade.LateGreat:
                case JudgeGrade.LateGreat2nd:
                case JudgeGrade.LateGreat3rd:
                case JudgeGrade.FastGreat3rd:
                case JudgeGrade.FastGreat2nd:
                case JudgeGrade.FastGreat:
                    newColor = PINK;
                    break;
                case JudgeGrade.LateGood:
                case JudgeGrade.FastGood:
                    newColor = GREEN;
                    break;
            }
            if(_color == newColor)
            {
                SetActive(true);
                return;
            }
            SetActive(true);
            _newColor = newColor;
        }
        internal void OnLateUpdate()
        {
            if(!Active)
            {
                return;
            }
            else if(_color == _newColor)
            {
                return;
            }
            _material.SetColor(MATERIAL_COLOR_ID, _newColor);
            _color = _newColor;
        }
        public override void SetActive(bool state)
        {
            if (Active == state)
                return;
            SetActiveInternal(state);
        }
        void SetActiveInternal(bool state)
        {
            Active = state;
            base.SetActive(state);
            switch (state)
            {
                case true:
                    GameObject.layer = MajEnv.DEFAULT_LAYER;
                    break;
                case false:
                    GameObject.layer = MajEnv.HIDDEN_LAYER;
                    break;
            }
        }
    }
}
