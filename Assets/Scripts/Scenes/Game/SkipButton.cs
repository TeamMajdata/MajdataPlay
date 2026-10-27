using Cysharp.Threading.Tasks;
using MajdataPlay.IO;
using System.Threading;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Scenes.Game
{
    public class SkipButton : MonoBehaviour
    {
        private static readonly SensorArea[] s_defaultSensorArea =
        {
            SensorArea.B4,
            SensorArea.B5,
            SensorArea.E5,
        };

        private SensorArea[] _boundSensorAreas = new SensorArea[s_defaultSensorArea.Length];
        private bool _isBound = false;
        private bool _isTriggered = false;
        private CancellationTokenSource? _bindCts;

        private void Awake()
        {
            var rotationOffset = (int)MajEnv.Settings.Display.GameplayScreenRotationAngle * 2;
            for (var i = 0; i < s_defaultSensorArea.Length; i++)
            {
                _boundSensorAreas[i] = s_defaultSensorArea[i].Diff(rotationOffset);
            }
        }
        private void OnAreaDown(object? sender, InputEventArgs args)
        {
            if (_isTriggered)
            {
                return;
            }
            _isTriggered = true;
            Majdata<GamePlayManager>.Instance!.EndGame().Forget();
        }
        private void OnEnable()
        {
            _bindCts?.Cancel();
            _bindCts?.Dispose();
            _bindCts = new CancellationTokenSource();
            DelayBind(_bindCts.Token).Forget();
        }
        private async UniTaskVoid DelayBind(CancellationToken token)
        {
            if (_isBound)
            {
                return;
            }
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
        private void OnDestroy()
        {
            OnDisable();
        }
        private void OnDisable()
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
