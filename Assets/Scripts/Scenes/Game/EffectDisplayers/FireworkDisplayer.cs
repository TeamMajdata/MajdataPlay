using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Serialization;

namespace MajdataPlay.Scenes.Game
{
    public sealed class FireworkDisplayer : MajComponent
    {
        [SerializeField]
        [FormerlySerializedAs("colorBallRenderer")]
        private SpriteRenderer _colorBallRenderer;

        [SerializeField]
        [FormerlySerializedAs("fireworkRenderer")]
        private SpriteRenderer _fireworkRenderer;

        [SerializeField]
        [FormerlySerializedAs("bigColorBallRenderer")]
        private SpriteRenderer _bigColorBallRenderer;

        [SerializeField]
        [FormerlySerializedAs("animator")]
        private Animator _animator;

        private FireworkRequest _request;
        private float _animRemainingTime = -1f;
        private float _fireworkZPosition = 0f;
        private const float AnimDurationSec = 1.5f;
        private readonly static int FireworkAnimHash = Animator.StringToHash("Fire");

        protected override void Awake()
        {
            base.Awake();
            Majdata<FireworkDisplayer>.Instance = this;
            _fireworkZPosition = Transform.position.z;
        }
        internal void OnLateUpdate()
        {
            if (_request.IsValid)
            {
                Transform.position = _request.Position;
                _request = default;
                _animRemainingTime = AnimDurationSec;
                _animator.SetTrigger(FireworkAnimHash);
                if (!Active)
                {
                    SetActive(true);
                }
            }
            else if (!Active)
            {
                return;
            }
            else if(_animRemainingTime < 0)
            {
                SetActive(false);
                return;
            }
            var deltaTime = MajTimeline.DeltaTime;
            _animator.Update(deltaTime);
            _animRemainingTime -= deltaTime;
        }
        private void OnDestroy()
        {
            Majdata<FireworkDisplayer>.Free();
        }
        public void Play(Vector3 position)
        {
            position.z = _fireworkZPosition;
            _request = new()
            {
                IsValid = true,
                Position = position
            };
        }

        public override void SetActive(bool state)
        {
            base.SetActive(state);
            _fireworkRenderer.enabled = state;
            _colorBallRenderer.enabled = state;
            _bigColorBallRenderer.enabled = state;
        }


        private readonly struct FireworkRequest
        {
            public bool IsValid { get; init; }
            public Vector3 Position { get; init; }
        }
    }
}
