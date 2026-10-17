using MajdataPlay.Editor;
using MajdataPlay.Settings;
using MajdataPlay.Utils;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#nullable enable
namespace MajdataPlay
{
    [DefaultExecutionOrder(100)]
    public class ScreenPosCameraMover : MonoBehaviour
    {
        const int FLAG_NOT_INIT = 0;
        const int FLAG_INITED = 1;

        Camera _cam;
        int _flag = FLAG_NOT_INIT;
        // 与 CanvasTemplate 的 1080×1920 / Expand 对齐；不读取 MRP 缩放后的场景 RT。
        const float ReferenceWidth = 1080f;
        const float ReferenceHeight = 1920f;
        int _screenWidth;
        int _screenHeight;
        float _screenOrthographicSize;
        float _screenYAdjustment;
        //float _lastMainScreenPos = 1f;

        Transform _transform;
        DisplayOptions? _displayOptions;

        [SerializeField]
        [ReadOnlyField]
        float _baseY;
        [SerializeField]
        [ReadOnlyField]
        float _originalCameraY; // 保存场景的原始相机Y位置
        [SerializeField]
        [ReadOnlyField]
        float _baseOrthographicSize;
        [SerializeField]
        [ReadOnlyField]
        float _lastOffset = float.NaN;
        [SerializeField]
        [ReadOnlyField]
        float _lastScale = float.NaN;

        [SerializeField]
        [ReadOnlyField]
        float _lastMainScreenOffset;
        [SerializeField]
        [ReadOnlyField]
        float _lastMainScreenScale;
        [SerializeField]
        [ReadOnlyField]
        bool _lastTransformDisplay;
        
        
        void Awake()
        {
            _displayOptions = MajEnv.Settings?.Display;
            _transform = transform;
            _cam = GetComponent<Camera>();
            _originalCameraY = _transform.position.y; // 保存场景原始相机位置
            _baseY = 1.5f;
            //获取当前正交视图比例
            _baseOrthographicSize = _cam.orthographicSize;
            _screenOrthographicSize = _baseOrthographicSize;
        }

        bool RefreshScreenLayout()
        {
            int width = Screen.width;
            int height = Screen.height;
            if (width <= 0 || height <= 0 || (width == _screenWidth && height == _screenHeight))
                return false;

            _screenWidth = width;
            _screenHeight = height;
            // Canvas Expand 使用宽高比例中的较小值。长屏 UI 按宽度缩放，
            // 相机也要保持相同的每世界单位像素数，而不能继续固定可见高度。
            float canvasScale = Mathf.Min(width / ReferenceWidth, height / ReferenceHeight);
            _screenOrthographicSize = _baseOrthographicSize * (height / ReferenceHeight) / canvasScale;
            // 主屏 UI 锚定底部。增加可见高度时同步抬高相机，保持世界原点距屏幕底边不变。
            // 手动缩放仍以屏幕中心为锚；此位移不再乘一次 MainScreenScale。
            _screenYAdjustment = _cam.orthographic ? _screenOrthographicSize - _baseOrthographicSize : 0f;
            return true;
        }
        void RestoreOriginal()
        {
            _transform.position = new Vector3(0, _originalCameraY + _screenYAdjustment, -10);
            if (_cam.orthographic)
            {
                _cam.orthographicSize = _screenOrthographicSize;
            }
        }

        void ApplyPosition(float offset, float scale)
        {
            _transform.position = new Vector3(0, _baseY + offset * 2.7f + _screenYAdjustment, -10);
            if (scale > 0 && _cam.orthographic)
            {
                _cam.orthographicSize = _screenOrthographicSize / scale;
            }
        }

        void ApplyTransform()
        {
            if (_lastTransformDisplay)
            {
                var offset = _lastMainScreenOffset;
                var scale = _lastMainScreenScale;
                _lastOffset = offset;
                _lastScale = scale;
                ApplyPosition(offset, scale);
            }
            else
            {
                RestoreOriginal();
            }
        }

        void LateUpdate()
        {
            switch (_flag)
            {
                case FLAG_NOT_INIT:
                    {
                        _displayOptions = MajEnv.Settings?.Display;
                        if (_displayOptions is null)
                        {
                            return;
                        }
                        _flag = FLAG_INITED;
                        
                        _lastTransformDisplay = _displayOptions.MainScreenTransform;
                        _lastMainScreenOffset = _displayOptions.MainScreenOffset;
                        _lastMainScreenScale = _displayOptions.MainScreenScale;
                        //transform.position = new Vector3(0, 1.5f + 2.7f * (MajEnv.Settings?.Display.MainScreenPosition ?? 1f), -10); //Original


                        // 主相机始终使用完整屏幕，不再按长宽比额外平移 viewport。
                        // 用户的显示缩放 / 偏移由相机位置、正交尺寸和 Canvas 调整完成，
                        // 无需改变 rect；100% 渲染分辨率时可继续使用 MSAA 直接 Resolve。
                        // 这会取消旧长屏默认的 .12 / .22 上移，不覆盖用户保存的显示参数。
                        _cam.rect = new Rect(0, 0, 1, 1);
                        RefreshScreenLayout();
                        ApplyTransform();
                    }
                    goto case FLAG_INITED;
                case FLAG_INITED:
                    {
                        bool screenChanged = RefreshScreenLayout();
                        var transformDisplay = _lastTransformDisplay;
                        _lastTransformDisplay = _displayOptions!.MainScreenTransform;
                        _lastMainScreenOffset = _displayOptions!.MainScreenOffset;
                        _lastMainScreenScale = _displayOptions!.MainScreenScale;
                        //如果没开调整显示位置，就直接return
                        if (!_lastTransformDisplay)
                        {
                            if(transformDisplay || screenChanged)
                            {
                                _lastTransformDisplay = false;
                                _lastOffset = float.NaN;
                                _lastScale = float.NaN;
                                RestoreOriginal();
                            }
                            return;
                        }

                        //跑到这就是开了，标记开启了调整显示位置
                        //_lastTransformDisplay = true;

                        var screenOffset = _lastMainScreenOffset;
                        var screenScale = _lastMainScreenScale;

                        if (!screenChanged && screenOffset == _lastOffset && screenScale == _lastScale)
                        {
                            return;
                        }
                        _lastOffset = screenOffset;
                        _lastScale = screenScale;

                        //变换位置
                        ApplyPosition(screenOffset, screenScale);
                    }
                    return;
            }
        }
    }
}
