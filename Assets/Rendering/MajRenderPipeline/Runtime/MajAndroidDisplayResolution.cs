using UnityEngine;

namespace MajdataPlay.Rendering
{
    /// <summary>
    /// Android 显示缓冲缩放实验。只改变输出缓冲；与 MRP 场景比例独立，最终场景像素尺寸为两者乘积，
    /// 不修改相机、Canvas 或输入坐标。其它平台和 Editor 不执行此实验。
    /// </summary>
    public static class MajAndroidDisplayResolution
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private static int _reducedPercent = 100;
        private static bool _hasBaseline;
        private static bool _reduced;
        private static bool _refreshRequested;
        private static int _baselineWidth;
        private static int _baselineHeight;
        private static int _screenWidth;
        private static int _screenHeight;
        private static int _requestedWidth;
        private static int _requestedHeight;
        private static int _requestedPercent;
        private static int _verifyFrame = -1;
#endif

        /// <summary>以实际缓冲读回确认请求，不把 API 调用成功等同于 GPU/系统免拷贝。</summary>
        public static MajSystemScalingStatus Status
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                var display = Display.main;
                if (!_hasBaseline) return _reduced ? MajSystemScalingStatus.Unconfirmed : MajSystemScalingStatus.Disabled;
                bool matches = display != null && display.renderingWidth == _requestedWidth
                    && display.renderingHeight == _requestedHeight;
                if (_reduced) return matches && !_refreshRequested
                    ? MajSystemScalingStatus.Active : MajSystemScalingStatus.Unconfirmed;
                return matches ? MajSystemScalingStatus.Disabled : MajSystemScalingStatus.Restoring;
#else
                return MajSystemScalingStatus.Unsupported;
#endif
            }
        }

        public static void Configure(int percent)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            percent = Mathf.Clamp(percent, 10, 100);
            if (percent == 100)
            {
                Restore();
                return;
            }
            if (_reduced && _reducedPercent == percent) return;
            _reducedPercent = percent;
            _reduced = true;
            _refreshRequested = true;
            // 启动期可能还没有有效显示缓冲；保留请求，后续 Tick 再捕获。
            if (!TryCaptureBaseline()) return;
            ApplyRequestedSize();
#endif
        }

        public static void Restore()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!_reduced) return;
            _reduced = false;
            if (!_hasBaseline)
            {
                _refreshRequested = false;
                return;
            }
            _refreshRequested = true;
            ApplyRequestedSize();
#endif
        }

        /// <summary>后台恢复可能重建显示缓冲；下一帧读实际尺寸后再决定是否重新请求。</summary>
        public static void RequestRefresh()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_hasBaseline && _reduced) _refreshRequested = true;
#endif
        }

        public static void Tick()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!_hasBaseline && (!_reduced || !TryCaptureBaseline())) return;
            if (_reduced || _refreshRequested) ApplyRequestedSize();
            if (_verifyFrame < 0 || Time.frameCount < _verifyFrame) return;
            _verifyFrame = -1;
            var display = Display.main;
            if (display == null) return;
            // 只在请求后记录一次读回，不把 API 调用成功当作缓冲已生效或底层零复制。
            Debug.Log($"[MRP.DisplayResolution] requested={_requestedPercent}%; size={_requestedWidth}x{_requestedHeight}; "
                + $"actual={display.renderingWidth}x{display.renderingHeight}; screen={Screen.width}x{Screen.height}; "
                + $"native={display.systemWidth}x{display.systemHeight}; "
                + $"confirmed={display.renderingWidth == _requestedWidth && display.renderingHeight == _requestedHeight}; "
                + $"requiresBlit={display.requiresBlitToBackbuffer}");
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static bool TryCaptureBaseline()
        {
            if (_hasBaseline) return true;
            var display = Display.main;
            if (display == null || display.renderingWidth <= 0 || display.renderingHeight <= 0) return false;
            // 保留接管前的实际缓冲，不假定它一定等于面板原生尺寸。
            // 快速来回切档时也保留同一基准，防止异步恢复尚未落地便再次乘 缩放比例。
            _baselineWidth = display.renderingWidth;
            _baselineHeight = display.renderingHeight;
            _hasBaseline = true;
            return true;
        }

        private static void ApplyRequestedSize()
        {
            int screenWidth = Screen.width;
            int screenHeight = Screen.height;
            if (screenWidth <= 0 || screenHeight <= 0) return;
            bool refresh = _refreshRequested;
            if (!refresh && _screenWidth == screenWidth && _screenHeight == screenHeight) return;
            var display = Display.main;
            if (display == null) return;
            _refreshRequested = false;
            _screenWidth = screenWidth;
            _screenHeight = screenHeight;
            // 节能目标以原生显示为基准；恢复仍用接管前缓冲。
            // 两者不能共用，否则已有 Fixed DPI / 系统降分辨率会被再次乘 缩放比例。
            int nativeWidth = display.systemWidth;
            int nativeHeight = display.systemHeight;
            bool hasNativeSize = nativeWidth > 0 && nativeHeight > 0;
            int width = _reduced && hasNativeSize ? nativeWidth : _baselineWidth;
            int height = _reduced && hasNativeSize ? nativeHeight : _baselineHeight;
            if ((width > height) != (screenWidth > screenHeight))
            {
                int previousWidth = width;
                width = height;
                height = previousWidth;
            }
            int percent = _reduced ? _reducedPercent : 100;
            width = Mathf.Max(1, width * percent / 100);
            height = Mathf.Max(1, height * percent / 100);
            // 稳定帧不重复设置；输入尺寸因自身请求而改变也不会再乘一次比例。
            bool sameRequest = width == _requestedWidth && height == _requestedHeight;
            if (sameRequest && (!refresh || (display.renderingWidth == width && display.renderingHeight == height))) return;
            _requestedWidth = width;
            _requestedHeight = height;
            _requestedPercent = percent;
            _verifyFrame = Time.frameCount + 2;
            try
            {
                display.SetRenderingResolution(width, height);
            }
            catch (System.Exception exception) when (exception is UnityException
                || exception is System.ArgumentException || exception is System.NotSupportedException)
            {
                _verifyFrame = -1;
                Debug.LogWarning($"MRP: 显示缓冲 {_requestedPercent}% 请求被拒绝，继续使用设备实际尺寸。{exception.Message}");
            }
        }
#endif

        /// <summary>场景附件使用实际显示缓冲；CPU 布局与触摸仍使用 Unity 自身的屏幕坐标。</summary>
        internal static void GetRenderingSize(out int width, out int height)
        {
            width = Mathf.Max(1, Screen.width);
            height = Mathf.Max(1, Screen.height);
#if UNITY_ANDROID && !UNITY_EDITOR
            var display = Display.main;
            if (display != null && display.renderingWidth > 0 && display.renderingHeight > 0)
            {
                width = display.renderingWidth;
                height = display.renderingHeight;
            }
#endif
        }
    }
}
