using System;
using UnityEngine;
using UnityEngine.Rendering;

#nullable enable

namespace MajdataPlay.Rendering
{
    /// <summary>
    /// 可选的相机绘制扩展。接口只依赖引擎，第三方适配程序集反向引用 MRP，避免管线依赖 SDK。
    /// Prepare 在剔除前接管对象，EndCamera 在提交后恢复状态，均按相机配对。
    /// </summary>
    public interface IMajCameraRenderExtension : IDisposable
    {
        bool Prepare(Camera camera);
        bool RequiresIntermediateTarget { get; }

        /// <summary>
        /// 在不透明/Skybox 后、透明队列前绘制。中间路径提供相同尺寸和 MSAA 的颜色、深度附件。
        /// 直接绘制快路径的附件参数为 null，必须使用当前已绑定目标，禁止切换 RT 或 Blit。
        /// </summary>
        void Render(CommandBuffer buffer, Camera camera, RenderTexture? colorTarget,
            RenderTexture? depthTarget, int width, int height);

        void EndCamera();
    }

    /// <summary>注册可选适配器；创建及释放发生在版本变化时，空场景不扫描对象、不分配附件。</summary>
    public static class MajCameraRenderExtensions
    {
        private static Func<IMajCameraRenderExtension>? _factory;
        internal static int Version { get; private set; }

        public static void Register(Func<IMajCameraRenderExtension> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (_factory == factory) return;
            _factory = factory;
            Version++;
        }

        public static void Unregister(Func<IMajCameraRenderExtension> factory)
        {
            if (_factory != factory) return;
            _factory = null;
            Version++;
        }

        internal static IMajCameraRenderExtension? Create() => _factory?.Invoke();
    }
}
