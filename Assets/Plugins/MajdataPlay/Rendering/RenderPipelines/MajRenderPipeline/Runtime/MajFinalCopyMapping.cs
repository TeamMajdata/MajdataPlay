using UnityEngine;

namespace MajdataPlay.Rendering
{
    /// <summary>屏幕输出矩形的纯数值映射；不修改 Camera、投影、触摸或场景分辨率。</summary>
    internal static class MajFinalCopyMapping
    {
        // GPU viewport 来自实际附件尺寸；不把它写回 Camera 或触摸坐标。
        internal static Rect GetSurfaceViewport(Rect normalizedRect, int surfaceWidth, int surfaceHeight) =>
            new Rect(normalizedRect.x * surfaceWidth, normalizedRect.y * surfaceHeight,
                normalizedRect.width * surfaceWidth, normalizedRect.height * surfaceHeight);

        internal static Vector4 GetClipScaleBias(Rect pixelRect, int surfaceWidth, int surfaceHeight,
            float projectionYSign)
        {
            float width = Mathf.Max(1, surfaceWidth);
            float height = Mathf.Max(1, surfaceHeight);
            float scaleX = pixelRect.width / width;
            float scaleY = pixelRect.height / height;
            // raw fullscreen shader 绕过相机矩阵，仅平移 Y 需要目标的 GPU 投影符号。
            // 不能翻转整个 positionCS.y，否则会把已正确的源图像一起倒置。
            return new Vector4(scaleX, scaleY, 2f * pixelRect.x / width + scaleX - 1f,
                (2f * pixelRect.y / height + scaleY - 1f) * projectionYSign);
        }

        internal static bool SupportsNativeScreenTarget(Rect rect, int viewportWidth, int viewportHeight,
            int surfaceWidth, int surfaceHeight) => rect.width == 1f && rect.height == 1f
                && viewportWidth == surfaceWidth && viewportHeight == surfaceHeight;

        // 真实显示目标的偏移 MSAA Resolve 尚不能保证与 CameraTarget 直绘采用同一 viewport 约定。
        // 暂仅原点整屏保留直接 Resolve；偏移走 native Resolve-only + Copy，保留 memoryless 优势。
        internal static bool CanResolveViewportToScreen(Rect rect) => rect == new Rect(0f, 0f, 1f, 1f);
    }
}
