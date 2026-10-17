using UnityEngine.Scripting;

namespace MajdataPlay.Rendering
{
    /// <summary>主屏幕 Game 相机实际提交的输出路径；它不代表 GPU 画面正确性检测。</summary>
    [Preserve]
    public enum MajRenderOutputStatus
    {
        /// <summary>主屏相机已开始渲染，尚未完成本次提交。</summary>
        Pending,
        /// <summary>场景直接写入相机目标，没有中间颜色复制。</summary>
        Direct,
        /// <summary>实际录制并提交了 MSAA 到屏幕的直接 Resolve。</summary>
        DirectResolve,
        /// <summary>按用户选择，通过中间颜色 RT 和 FinalCopy 输出。</summary>
        IntermediateRequested,
        /// <summary>实际场景尺寸不同于输出尺寸，需要 FinalCopy 缩放。</summary>
        IntermediateScaled,
        /// <summary>原生附件可用，但后端、格式或显示条件不满足直接 Resolve。</summary>
        IntermediateMSAAUnsupported,
        /// <summary>当前相机、附件优化、Gizmos 或扩展条件要求普通中间路径。</summary>
        IntermediateNativeUnavailable,
        /// <summary>附件或最终复制不可用，实际回退为原分辨率直绘。</summary>
        DirectAttachmentFallback,
        /// <summary>系统单采样屏幕请求尚在过渡，实际临时使用兼容直绘。</summary>
        DirectScreenTransition,
        /// <summary>本帧没有有效主屏结果，或剔除、渲染、清理失败或管线已销毁。</summary>
        Unavailable,
        /// <summary>当前屏幕显示区域要求 FinalCopy；原生场景附件及 memoryless 优化仍可保留。</summary>
        IntermediateViewport
    }

    [Preserve]
    public enum MajSystemScalingStatus { Unsupported, Disabled, Active, Unconfirmed, Restoring }

    /// <summary>
    /// 供设置界面读取最近一次主屏幕相机提交结果。保存路径枚举与实际尺寸；尺寸变化时才格式化文本，不发逐帧事件。
    /// Preview、SceneView 和离屏相机不覆盖；本次管线渲染无有效主屏幕相机时保持不可用。
    /// </summary>
    public static class MajRenderDiagnostics
    {
        private static int _displayWidth, _displayHeight, _sceneWidth, _sceneHeight;
        private static string _displayResolution = "--";
        public static string DisplayResolution => OutputStatus is MajRenderOutputStatus.Unavailable or MajRenderOutputStatus.Pending ? "--" : _displayResolution;
        private static string _sceneResolution = "--";
        public static string SceneResolution => OutputStatus is MajRenderOutputStatus.Unavailable or MajRenderOutputStatus.Pending ? "--" : _sceneResolution;

        // 尺寸不变时复用字符串；只记录成功提交的主屏相机，避免每帧格式化。
        internal static void PublishDimensions(int displayWidth, int displayHeight, int sceneWidth, int sceneHeight)
        {
            if (_displayWidth != displayWidth || _displayHeight != displayHeight)
            {
                _displayWidth = displayWidth; _displayHeight = displayHeight;
                _displayResolution = $"{displayWidth} x {displayHeight}";
            }
            if (_sceneWidth != sceneWidth || _sceneHeight != sceneHeight)
            {
                _sceneWidth = sceneWidth; _sceneHeight = sceneHeight;
                _sceneResolution = $"{sceneWidth} x {sceneHeight}";
            }
        }
        private static void ClearDimensions()
        {
            _displayWidth = _displayHeight = _sceneWidth = _sceneHeight = 0;
            _displayResolution = _sceneResolution = "--";
        }
        private static int _frameId = int.MinValue;
        public static MajRenderOutputStatus OutputStatus { get; private set; } = MajRenderOutputStatus.Pending;

        internal static void BeginFrame(int frameId)
        {
            // Editor 同一帧可分开渲染 Game、SceneView、Preview；后两个 context 不抹掉主屏结果。
            // 帧号由管线传入，诊断类本身不查询引擎、不分配，也不向 UI 发变化事件。
            if (_frameId == frameId) return;
            _frameId = frameId;
            OutputStatus = MajRenderOutputStatus.Unavailable;
        }
        internal static void BeginCamera() => OutputStatus = MajRenderOutputStatus.Pending;
        internal static void Publish(MajRenderOutputStatus status)
        {
            OutputStatus = status;
            if (status == MajRenderOutputStatus.Unavailable) ClearDimensions();
        }
        internal static void Invalidate() { OutputStatus = MajRenderOutputStatus.Unavailable; ClearDimensions(); }
    }
}
