using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MajdataPlay.Behaviours.UI
{
    [RequireComponent(typeof(Camera))]
    internal class PreviewOnlyCamera : MajComponent
    {
        Camera _camera;
        protected override void Awake()
        {
            base.Awake();
            _camera = GetComponent<Camera>();
            Destroy(GameObject);
        }
    }
}
