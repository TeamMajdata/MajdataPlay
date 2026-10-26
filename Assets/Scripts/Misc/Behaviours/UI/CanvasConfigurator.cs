using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Behaviours.UI
{
    [RequireComponent(typeof(Canvas))]
    [DefaultExecutionOrder(200)]
    public class CanvasConfigurator : MonoBehaviour
    {
        Canvas _canvas;

        void Awake()
        {
            _canvas = GetComponent<Canvas>();
        }
        void Start()
        {
            _canvas.worldCamera = SceneSwitcher.MainCamera;
            SceneSwitcher.OnSceneChanged += OnSceneChanged;
        }

        void OnDestroy()
        {
            SceneSwitcher.OnSceneChanged -= OnSceneChanged;
        }

        void OnSceneChanged(object? sender, (MajScenes NewScene, MajScenes OldScene) args)
        {
            _canvas.worldCamera = SceneSwitcher.MainCamera;
        }
    }
}
