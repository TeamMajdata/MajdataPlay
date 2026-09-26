using MajdataPlay.Scenes.Game;
using MajdataPlay.IO;
using MajdataPlay.UnsafeKit;
using MajdataPlay.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Profiling;
using System.Threading;
using MajdataPlay.Net;
using MajdataPlay.Diagnostics;

#nullable enable
namespace MajdataPlay
{
    [DontDestroyOnLoad]
    internal class GameUpdater :  MajComponent
    {
        DummyTouchPanelRenderer _dummyTouchPanelRenderer;
        DummyLedRenderer _dummyLedRenderer;

        MajScenes _lastScene = MajScenes.Init;
        MajScenes _currentScene = MajScenes.Init;
        Task _onlineHeartbeatTask = Task.CompletedTask;
        CancellationTokenSource _heartbeatCts = new();
        TimeSpan _lastExecuteHeartbeatTime = TimeSpan.Zero;
        protected override void Awake()
        {
            base.Awake();
            Majdata<GameUpdater>.SetAsSingleton(this);
        }
        void Start()
        {
            _dummyTouchPanelRenderer = Majdata<DummyTouchPanelRenderer>.Instance!;
            _dummyLedRenderer = Majdata<DummyLedRenderer>.Instance!;
        }

        //void FixedUpdate()
        //{
        //    var gpManager = MajInstanceHelper<GamePlayManager>.Instance;
        //    var noteManager = MajInstanceHelper<NoteManager>.Instance;

        //    gpManager?.OnFixedUpdate();
        //    noteManager?.OnFixedUpdate();
        //    _inputManager.OnFixedUpdate();
        //}
        void Update()
        {
            Profiler.BeginSample("GameUpdater.PreUpdate");
            try
            {
                OnPreUpdate();
            }
            catch(Exception e)
            {
                MajDebug.LogException(e);
            }
            Profiler.EndSample();
            Profiler.BeginSample("GameUpdater.Update");
            try
            {
                OnUpdate();
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
            }
            Profiler.EndSample();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void OnPreUpdate()
        {
            // Time Update
            MajTimeline.OnPreUpdate();
            InputManager.OnPreUpdate();
            _dummyTouchPanelRenderer.OnPreUpdate();
            try
            {
                switch (SceneSwitcher.CurrentScene)
                {
                    case MajScenes.Game:
                        Majdata<GamePlayManager>.Instance?.OnPreUpdate();
                        break;
                }
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void OnUpdate()
        {
            try
            {
                _lastScene = _currentScene;
                _currentScene = SceneSwitcher.CurrentScene;

                if(_currentScene == MajScenes.Game && _lastScene != MajScenes.Game)
                {
                    if(!_onlineHeartbeatTask.IsCompleted)
                    {
                        _heartbeatCts.Cancel();
                        _heartbeatCts = new();
                        MajDebug.LogDebug("Online heartbeat task cancellation has been requested");
                    }
                }
                else if(_currentScene != MajScenes.Game)
                {
                    var currentTime = MajTimeline.UnscaledTime;
                    if(_onlineHeartbeatTask.IsCompleted && (currentTime - _lastExecuteHeartbeatTime).TotalMinutes > 5)
                    {
                        _onlineHeartbeatTask = Online.HeartbeatAsync(_heartbeatCts.Token).AsTask();
                        _lastExecuteHeartbeatTime = currentTime;
                        MajDebug.LogDebug("Online heartbeat requested");
                    }
                }

                switch (SceneSwitcher.CurrentScene)
                {
                    case MajScenes.Game:
                        Majdata<GamePlayManager>.Instance?.OnUpdate();
                        break;
                }
            }
            catch(Exception e)
            {
                MajDebug.LogException(e);
            }
        }
        void LateUpdate()
        {
            Profiler.BeginSample("GameUpdater.LateUpdate");
            try
            {
                switch (SceneSwitcher.CurrentScene)
                {
                    case MajScenes.Game:
                        Majdata<GamePlayManager>.Instance?.OnLateUpdate();
                        break;
                }
                CabinetLed.OnLateUpdate();
#if UNITY_ANDROID && !UNITY_EDITOR
                OniimaiController.UpdateStats();
#endif
                _dummyLedRenderer.OnLateUpdate();
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
            }
            Profiler.EndSample();
        }
    }
}
