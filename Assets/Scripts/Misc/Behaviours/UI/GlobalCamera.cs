using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Behaviours.UI
{
    [DontDestroyOnLoad]
    [DefaultExecutionOrder(-1000)]
    [RequireComponent(typeof(Camera))]
    public class GlobalCamera : MajComponent
    {
        public Camera Camera
        {
            get => _camera;
        }

        Camera _camera;
        protected override void Awake()
        {
            base.Awake();
            _camera = GetComponent<Camera>();
            Majdata<GlobalCamera>.SetAsSingleton(this);
            SceneSwitcher.MainCamera = _camera;
        }
    }
}
