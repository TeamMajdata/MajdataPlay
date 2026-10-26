using UnityEngine;

namespace MajdataPlay.Rendering
{
    /// <summary>
    /// 按实际比例计算场景尺寸，保留 Auto 上采样并支持超采样缩小。
    /// 这些计算不修改 Camera/Screen 的输出分辨率，也不改变游戏坐标或判定时钟。
    /// </summary>
    internal static class MajRenderScale
    {
        private static int _maxTextureSize;
        private static int MaxTextureSize
        {
            get
            {
                // 首次准备相机附件时查询；Asset 的 OnValidate 只执行纯数值 Clamp。
                if (_maxTextureSize == 0) _maxTextureSize = Mathf.Max(1, SystemInfo.maxTextureSize);
                return _maxTextureSize;
            }
        }

        internal static float Clamp(float scale)
        {
            return float.IsNaN(scale) || float.IsInfinity(scale) ? 1f : Mathf.Clamp(scale, 0.1f, 2f);
        }

        internal static float LimitToTextureSize(float scale, int outputWidth, int outputHeight)
        {
            // 超采样附件不能超过硬件单边尺寸上限；同比例限制宽高，保留相机投影和输出尺寸。
            // 这里只防止超规格申请，不承诺大尺寸 MSAA 一定有足够显存，分配失败仍走现有兼容回退。
            int largestDimension = Mathf.Max(1, Mathf.Max(outputWidth, outputHeight));
            return Mathf.Min(scale, (float)MaxTextureSize / largestDimension);
        }

        internal static float Effective(float scale)
        {
            // 游戏按 5% 精确调节；不能沿用旧 URP 的 100% 附近吸附，
            // 否则 105% 的浮点差值略小于 .05 时会被错误吞成 100%。
            return Clamp(scale);
        }

        internal static int ScaledDimension(int outputSize, float effectiveScale)
        {
            // 与 URP 相同：向下取整。奇数分辨率的 50% 不会向上多分配一行/列。
            // 再夹一次整数结果，防止上限比例的浮点舍入多申请一个像素。
            return Mathf.Clamp((int)(outputSize * effectiveScale), 1, MaxTextureSize);
        }

        internal static FilterMode AutoFilter(int width, int height, float effectiveScale)
        {
            // 超采样是缩小而非整数倍放大，必须滤波，不能退成 Point 丢掉额外样本。
            if (effectiveScale > 1f) return FilterMode.Bilinear;
            float pixelScale = 1f / effectiveScale;
            // 整数倍放大并且宽高整除时用 Point；75% 等非整数倍放大用 Bilinear。
            // 保留 URP 对浮点数“小数部分接近零”的判定，而不是把接近整数的值四舍五入。
            if (!Mathf.Approximately(pixelScale - Mathf.Floor(pixelScale), 0f))
                return FilterMode.Bilinear;
            float widthScale = width / pixelScale;
            float heightScale = height / pixelScale;
            return Mathf.Approximately(widthScale - Mathf.Floor(widthScale), 0f)
                && Mathf.Approximately(heightScale - Mathf.Floor(heightScale), 0f)
                ? FilterMode.Point : FilterMode.Bilinear;
        }

        internal static Vector4 MipBias(int sceneWidth, int outputWidth)
        {
            // 原尺寸及超采样不施加负偏置，避免为了更细的附件额外放大全局纹理带宽。
            if (sceneWidth >= outputWidth) return new Vector4(0f, 1f, 0f, 0f);
            float bias = Mathf.Min(Mathf.Log((float)sceneWidth / Mathf.Max(1, outputWidth), 2f), 0f);
            return new Vector4(bias, Mathf.Pow(2f, bias), 0f, 0f);
        }
    }
}
