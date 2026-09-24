using MajdataPlay.Diagnostics;
using MajdataPlay.Editor;
using MajdataPlay.IO;
using MajdataPlay.Numerics;
using MajdataPlay.Settings;
using MajdataPlay.Utils;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#nullable enable
namespace MajdataPlay
{
    [DefaultExecutionOrder(150)]
    public class ScreenPosCanvasMover : MonoBehaviour
    {
        const int FLAG_NOT_INIT = 0;
        const int FLAG_INITED = 1;

        const float SCREEN_CANVAS_HEIGHT = 1920;
        const float SCREEN_CANVAS_WIDTH = 1080;

        const float MAIN_DISPLAY_POS_Y = 540;
        const float SUB_COVER_HEIGHT = 390;
        const float SUB_COVER_WIDTH = 1080;
        const float SUB_COVER_POS_Y = 1275;

        const float SUB_COVER_BOTTOM_HEIGHT = 0;
        const float SUB_COVER_BOTTOM_WIDTH = 1080;
        const float SUB_COVER_BOTTOM_POS_Y = 0;

        const float SUB_DISPLAY_ORIGINAL_POS_Y = 735f;
        const float SUB_DISPLAY_HEIGHT = 450f;
        const float MAIN_DISPLAY_HEIGHT = 1080f;

        int _flag = FLAG_NOT_INIT;

        Transform _transform;
        RectTransform _rt;

        // 主屏幕 Y 轴的"零偏移基准位置"（Canvas 坐标系），偏移量以此为基础叠加
        [SerializeField]
        [ReadOnlyField]
        float _basePosY;
        // 预制体自带的原始 Y 位置，关闭 MainScreenTransform 功能时用于完整还原
        [SerializeField]
        [ReadOnlyField]
        float _originalPosY;
        // 父节点的 RectTransform，用于在无 CanvasScaler 时读取父容器高度以计算屏幕中心
        [SerializeField]
        [ReadOnlyField]
        RectTransform? _parentRt;

        [SerializeField]
        [ReadOnlyField]
        float _screenHeight = 1920;
        [SerializeField]
        [ReadOnlyField]
        float _screenWidth = 1080;
        [SerializeField]
        [ReadOnlyField]
        float _canvasScaleFactor = 1f;


        // 脏检测（避免每帧重复计算）
        [SerializeField]
        [ReadOnlyField]
        float _lastMainDisplayOffset = float.NaN;       // 上一帧的主屏幕偏移量
        [SerializeField]
        [ReadOnlyField]
        float _lastMainDisplayScale = float.NaN;        // 上一帧的主屏幕缩放
        [SerializeField]
        [ReadOnlyField]
        float _lastSubDisplayOffset = float.NaN; // 上一帧的副屏幕偏移量
        [SerializeField]
        [ReadOnlyField]
        float _lastSubDisplayScale = float.NaN; // 上一帧的副屏幕偏移量
        [SerializeField]
        [ReadOnlyField]
        bool _lastTransformDisplay;          // 上一帧 MainScreenTransform 开关的状态

        // 屏幕中心缓存，缩放时需要以屏幕中心为锚点补偿位移
        [SerializeField]
        [ReadOnlyField]
        float _cachedScreenCenterY;
        [SerializeField]
        [ReadOnlyField]
        CanvasScaler? _canvasScaler;
        // 跨场景保存真正的原始分辨率
        static Vector2? _originalReferenceResolution;
        // 当前场景启动时记录的基准分辨率
        [SerializeField]
        [ReadOnlyField]
        Vector2 _baseReferenceResolution;

        // 副屏遮罩（Sub_Cover）
        [SerializeField]
        [ReadOnlyField]
        GameObject _subCover;
        [SerializeField]
        [ReadOnlyField]
        Transform _subCoverTransform;
        [SerializeField]
        [ReadOnlyField]
        RectTransform? _subCoverRectTransform;

        [SerializeField]
        [ReadOnlyField]
        GameObject _subCoverBottom;
        [SerializeField]
        [ReadOnlyField]
        Transform _subCoverBottomTransform;
        [SerializeField]
        [ReadOnlyField]
        RectTransform _subCoverBottomRectTransform;

        //副屏幕（Sub_Display）
        [SerializeField]
        [ReadOnlyField]
        RectTransform? _subDisplay;

        DisplayOptions? _displayOptions;


        void Awake()
        {
            _displayOptions = MajEnv.Settings?.Display;
            _rt = GetComponent<RectTransform>();
            _transform = transform;
            _parentRt = _transform.parent as RectTransform;
            // 保存预制体的原始Y位置，用于关闭MainScreenTransform时恢复
            _originalPosY = _rt.anchoredPosition.y;

            _parentRt = _transform.parent as RectTransform;
            // 先查找CanvasScaler，再计算屏幕中心
            var rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
            if (rootCanvas is not null)
            {
                _canvasScaler = rootCanvas.GetComponent<CanvasScaler>();
                if (_canvasScaler is not null)
                {
                    // 只在第一次记录真正的原始分辨率，避免场景切换时读到被修改过的值
                    if (_originalReferenceResolution is null)
                    {
                        _originalReferenceResolution = _canvasScaler.referenceResolution;
                    }
                    _baseReferenceResolution = (Vector2)_originalReferenceResolution;
                }
            }
            // 从Screen尺寸直接计算未缩放Canvas中心（Awake中rect可能尚未初始化）
            if (_canvasScaler is not null)
            {
                var baseScaleFactor = Mathf.Min(
                    Screen.width / _baseReferenceResolution.x,
                    Screen.height / _baseReferenceResolution.y
                );
                _cachedScreenCenterY = Screen.height / baseScaleFactor / 2f;
            }
            else if (_parentRt is not null && _parentRt.rect.height > 0)
            {
                _cachedScreenCenterY = _parentRt.rect.height / 2f;
            }
            else
            {
                _cachedScreenCenterY = _displayOptions?.MainScreenCachedScreenCenterY ?? 960f;
            }
            if (_displayOptions is not null)
            {
                _displayOptions.MainScreenCachedScreenCenterY = _cachedScreenCenterY;
            }

            _basePosY = 810f;
            _rt.anchoredPosition = new Vector2(0, _basePosY);

            var sub = _transform.parent.Find("Sub_Cover");
            if (sub is not null)
            {
                _subCover = sub.gameObject;
                _subCoverTransform = sub;
                _subCoverRectTransform = sub.GetComponent<RectTransform>();
            }
            var subBottom = _transform.parent.Find("Sub_Cover_Bottom");
            if (subBottom is not null)
            {
                _subCoverBottom = subBottom.gameObject;
                _subCoverBottomTransform = subBottom;
                _subCoverBottomRectTransform = subBottom.GetComponent<RectTransform>();
            }
            var subDisplay = _transform.parent.Find("Sub_Display");
            if (subDisplay is not null)
            {
                _subDisplay = subDisplay.GetComponent<RectTransform>();
            }
            _canvasScaleFactor = Mathf.Max(
                Screen.width / _baseReferenceResolution.x,
                Screen.height / _baseReferenceResolution.y);
            var screenRect = _parentRt!.rect;
            MajDebug.LogDebug($"[ScreenPosCanvasMover]Canvas scale factor: {_canvasScaleFactor}");
            _screenHeight = screenRect.height / _canvasScaleFactor;
            _screenWidth = screenRect.width / _canvasScaleFactor;
        }

        void RestoreOriginal()
        {
            // 恢复到预制体的原始状态
            if (_canvasScaler != null)
            {
                _canvasScaler.referenceResolution = _baseReferenceResolution;
            }
            _rt.anchoredPosition = new Vector2(0, _originalPosY);
            _rt.localScale = Vector3.one;
            // 恢复Sub_Display位置
            if (_subDisplay != null)
            {
                _subDisplay.anchoredPosition = new Vector2(_subDisplay.anchoredPosition.x, SUB_DISPLAY_ORIGINAL_POS_Y);
                _subDisplay.localScale = Vector3.one;
            }
            //恢复Sub_Cover位置和大小
            if (_subCoverRectTransform != null)
            {
                _subCoverRectTransform.anchoredPosition = new Vector2(0, SUB_COVER_POS_Y);
                _subCoverRectTransform.sizeDelta = new Vector2(SUB_COVER_WIDTH, SUB_COVER_HEIGHT);
            }
            if (_subCoverBottomRectTransform != null)
            {
                _subCoverBottomRectTransform.anchoredPosition = new Vector2(0, SUB_COVER_BOTTOM_POS_Y);
                _subCoverBottomRectTransform.sizeDelta = new Vector2(SUB_COVER_BOTTOM_WIDTH, SUB_COVER_BOTTOM_HEIGHT);
            }
        }

        void ApplyPosition(float offset, float scale, bool updateCache = false)
        {
            if (updateCache && _canvasScaler == null)
            {
                // localScale路径：parent rect高度不受我们代码影响，可以安全读取
                // CanvasScaler路径：屏幕中心已在Awake中从Screen尺寸计算，运行时不更新
                float parentHeight = _parentRt != null ? _parentRt.rect.height : 0f;
                if (parentHeight > 0)
                {
                    float realCenter = parentHeight / 2f;
                    if (realCenter != _cachedScreenCenterY)
                    {
                        _cachedScreenCenterY = realCenter;
                        _displayOptions!.MainScreenCachedScreenCenterY = realCenter;
                    }
                }
            }

            if (_canvasScaler != null && scale > 0)
            {
                // 通过缩小CanvasScaler的referenceResolution来实现放大效果
                var newRef = new Vector2(
                    _baseReferenceResolution.x / scale,
                    _baseReferenceResolution.y / scale
                );
                //_canvasScaleFactor = Mathf.Max(
                //    Screen.width / newRef.x,
                //    Screen.height / newRef.y);
                _canvasScaler.referenceResolution = newRef;
                float posY = _basePosY - (offset * 270f) + (_cachedScreenCenterY * ((1f / scale) - 1f));
                _rt.anchoredPosition = new Vector2(0, posY);
                _rt.localScale = Vector3.one;
            }
            else
            {
                // 回退到localScale方式（无CanvasScaler时）
                float posYBase = _basePosY - (offset * 270f);
                float scaleCorrection = (_cachedScreenCenterY - posYBase) * (1f - scale);
                _rt.anchoredPosition = new Vector2(0, posYBase + scaleCorrection);
                if (scale > 0)
                {
                    _rt.localScale = new Vector3(scale, scale, 1f);
                }
            }
        }

        void ApplySubDisplayTransform(float offset, float scale)
        {
            if (_subDisplay == null)
            {
                return;
            }
            float newY = SUB_DISPLAY_ORIGINAL_POS_Y + offset;
            _subDisplay.anchoredPosition = new Vector2(_subDisplay.anchoredPosition.x, newY);
            _subDisplay.localScale = new Vector3(scale, scale, 1f);
        }

        void UpdateSubCover()
        {
            if (_subCoverRectTransform != null && _subDisplay != null)
            {
                // 主副屏锚点不同，统一使用父容器坐标，并计入各自的实际尺寸和缩放。
                var mainDisplayTop = _rt.localPosition.y + _rt.rect.yMax * _rt.localScale.y;
                var subDisplayBottom = _subDisplay.localPosition.y + _subDisplay.rect.yMin * _subDisplay.localScale.y;

                var coverHeight = Mathf.Max(0f, subDisplayBottom - mainDisplayTop);
                _subCoverRectTransform.sizeDelta = new Vector2(SUB_COVER_WIDTH, coverHeight);

                var coverPosition = _subCoverRectTransform.localPosition;
                coverPosition.y = mainDisplayTop + coverHeight * _subCoverRectTransform.pivot.y;
                _subCoverRectTransform.localPosition = coverPosition;
            }
            if (_subCoverBottomRectTransform != null)
            {
                var mainDisplayBottom = _rt.anchoredPosition.y - (MAIN_DISPLAY_HEIGHT / 2f);
                var coverHeight = Mathf.Max(0f, mainDisplayBottom);
                var coverCenterY = coverHeight / 2;

                _subCoverBottomRectTransform.anchoredPosition = new Vector2(0, coverCenterY);
                _subCoverBottomRectTransform.sizeDelta = new(SUB_COVER_BOTTOM_WIDTH, coverHeight);
            }
        }

        void ApplyTransform()
        {
            var initTransform = _displayOptions!.MainScreenTransform;
            _lastTransformDisplay = initTransform;
            if (initTransform)
            {
                var offset = _displayOptions!.MainScreenOffset;
                var scale = _displayOptions!.MainScreenScale;
                _lastMainDisplayOffset = offset;
                _lastMainDisplayScale = scale;
                ApplyPosition(offset, scale);

                var subOffset = _displayOptions.SubDisplayOffset;
                var subScale = _displayOptions.SubDisplayScale;
                _lastSubDisplayOffset = subOffset;
                ApplySubDisplayTransform(subOffset * 100f, subScale);

                UpdateSubCover();
            }
            else
            {
                RestoreOriginal();
            }
        }

        void Update()
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
                        ApplyTransform();
                        _flag = FLAG_INITED;
                    }
                    goto case FLAG_INITED;
                case FLAG_INITED:
                    {
                        var transformDisplay = _displayOptions!.MainScreenTransform;

                        if (!transformDisplay)
                        {
                            if (_lastTransformDisplay)
                            {
                                _lastTransformDisplay = false;
                                _lastMainDisplayOffset = float.NaN;
                                _lastMainDisplayScale = float.NaN;
                                _lastSubDisplayOffset = float.NaN;
                                _lastSubDisplayScale = float.NaN;
                                RestoreOriginal();
                            }
                            return;
                        }
                        _lastTransformDisplay = true;

                        var subDisplayOffset = _displayOptions.SubDisplayOffset;
                        var subDisplayScale = _displayOptions.SubDisplayScale;
                        var screenOffset = _displayOptions.MainScreenOffset;
                        var screenScale = _displayOptions.MainScreenScale;

                        var subChanged = subDisplayOffset != _lastSubDisplayOffset || subDisplayScale != _lastSubDisplayScale;
                        var mainChanged = screenOffset != _lastMainDisplayOffset || screenScale != _lastMainDisplayScale;

                        if (!subChanged && !mainChanged)
                        {
                            return;
                        }

                        if (subChanged)
                        {
                            _lastSubDisplayOffset = subDisplayOffset;
                            _lastSubDisplayScale = subDisplayScale;
                            ApplySubDisplayTransform(subDisplayOffset * 100f, subDisplayScale);
                        }

                        if (mainChanged)
                        {
                            _lastMainDisplayOffset = screenOffset;
                            _lastMainDisplayScale = screenScale;
                            ApplyPosition(screenOffset, screenScale, updateCache: true);
                        }
                        UpdateSubCover();
                    }
                    return;
            }
        }
    }
}
