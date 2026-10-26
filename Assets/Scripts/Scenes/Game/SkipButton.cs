using Cysharp.Threading.Tasks;
using MajdataPlay.IO;
using MajdataPlay.Behaviours.UI;
using MajdataPlay.Utils;
using System.Threading;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Scenes.Game
{
    public class SkipButton : MonoBehaviour
    {
        readonly static SensorArea[] DEFAULT_SENSOR_AREAS =
        {
            SensorArea.B4,
            SensorArea.B5,
            SensorArea.E5,
        };

        SensorArea[] _boundSensorAreas = new SensorArea[DEFAULT_SENSOR_AREAS.Length];
        bool _isBound = false;
        bool _isTriggered = false;
        CancellationTokenSource? _bindCts;

        void Awake()
        {
            var rotationOffset = (int)MajEnv.Settings.Display.GameplayScreenRotationAngle * 2;
            for (var i = 0; i < DEFAULT_SENSOR_AREAS.Length; i++)
            {
                _boundSensorAreas[i] = DEFAULT_SENSOR_AREAS[i].Diff(rotationOffset);
            }

            if (TryGetComponent<ButtonAnimation>(out var buttonAnimation))
            {
                buttonAnimation.BindSensor = _boundSensorAreas;
            }
        }
        void OnAreaDown(object? sender,InputEventArgs args)
        {
            if (_isTriggered)
                return;
            _isTriggered = true;
            Majdata<GamePlayManager>.Instance!.EndGame().Forget();
        }
        void OnEnable()
        {
            _bindCts?.Cancel();
            _bindCts?.Dispose();
            _bindCts = new CancellationTokenSource();
            DelayBind(_bindCts.Token).Forget();
        }
        async UniTaskVoid DelayBind(CancellationToken token)
        {
            if (_isBound)
                return;
            // The delay is cancelled by OnDisable, so a button disabled within the second never binds.
            var isCanceled = await UniTask.Delay(1000, cancellationToken: token).SuppressCancellationThrow();
            if (isCanceled || _isBound)
            {
                return;
            }
            _isBound = true;
            foreach (var sensorArea in _boundSensorAreas)
            {
                InputManager.BindSensor(OnAreaDown, sensorArea);
            }
        }
        void OnDestroy()
        {
            OnDisable();
        }
        void OnDisable()
        {
            _bindCts?.Cancel();
            _bindCts?.Dispose();
            _bindCts = null;
            _isBound = false;
            foreach (var sensorArea in _boundSensorAreas)
            {
                InputManager.UnbindSensor(OnAreaDown, sensorArea);
            }
        }
    }
}
