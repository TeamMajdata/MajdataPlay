#nullable enable
global using UniTask = System.Threading.Tasks.Task;
using System;
using System.Threading.Tasks;

namespace UnityEngine
{
    /// <summary>Tracks requested native resource destruction without loading Unity.</summary>
    public class Object
    {
        /// <summary>Gets the number of destruction requests received by this resource.</summary>
        public int DestroyCalls { get; private set; }

        /// <summary>Gets or sets a destruction failure injected by a test case.</summary>
        public static Exception? DestroyFailure { get; set; }

        /// <summary>Records a resource destruction request and optionally injects a native failure.</summary>
        /// <param name="value">The owned resource being released.</param>
        public static void Destroy(Object value)
        {
            value.DestroyCalls++;
            if (DestroyFailure != null)
            {
                throw DestroyFailure;
            }
        }
    }

    /// <summary>Provides only component lookup used during manager initialization.</summary>
    public class MonoBehaviour : Object
    {
        /// <summary>Gets or sets the component returned by this fixture.</summary>
        public object? TestComponent { get; set; }

        /// <summary>Returns the fixture's explicitly assigned component.</summary>
        /// <typeparam name="T">The requested component type.</typeparam>
        /// <returns>The configured component.</returns>
        public T GetComponent<T>() where T : class
        {
            return (T)(TestComponent ?? throw new InvalidOperationException("No fixture component was assigned."));
        }
    }

    /// <summary>Models a borrowed material or a separately owned cloned material.</summary>
    public sealed class Material : Object
    {
        /// <summary>Creates a fixture's borrowed source material.</summary>
        public Material()
        {
        }

        /// <summary>Creates an independent owned copy of a borrowed material.</summary>
        /// <param name="source">The borrowed source that remains owned by the fixture.</param>
        public Material(Material source)
        {
            ArgumentNullException.ThrowIfNull(source);
        }

        /// <summary>Accepts brightness updates without executing a shader.</summary>
        /// <param name="name">The production shader property name.</param>
        /// <param name="value">The bounded brightness.</param>
        public void SetFloat(string name, float value)
        {
        }
    }

    /// <summary>Supplies dimensions required to compile background layout paths.</summary>
    public class Texture : Object
    {
        /// <summary>Gets a representative width.</summary>
        public int width { get; } = 1080;

        /// <summary>Gets a representative height.</summary>
        public int height { get; } = 1080;
    }

    /// <summary>Supplies the borrowed cover texture.</summary>
    public sealed class Sprite : Object
    {
        /// <summary>Gets the fixture texture.</summary>
        public Texture texture { get; } = new Texture();
    }

    /// <summary>Retains scale values without Unity transform behavior.</summary>
    public sealed class Transform
    {
        /// <summary>Gets or sets the local scale.</summary>
        public Vector3 localScale { get; set; }
    }

    /// <summary>Retains size values without Unity layout behavior.</summary>
    public sealed class RectTransform
    {
        /// <summary>Gets or sets the local size.</summary>
        public Vector2 sizeDelta { get; set; }
    }

    /// <summary>Provides the value constructor referenced by production layout code.</summary>
    /// <param name="X">The horizontal value.</param>
    /// <param name="Y">The vertical value.</param>
    public readonly record struct Vector2(float X, float Y);

    /// <summary>Provides the value constructor referenced by production layout code.</summary>
    /// <param name="X">The horizontal scale.</param>
    /// <param name="Y">The vertical scale.</param>
    /// <param name="Z">The depth scale.</param>
    public readonly record struct Vector3(float X, float Y, float Z);

    /// <summary>Marks a serialized field without running a Unity importer.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute
    {
    }

    /// <summary>Accepts the production component requirement.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class RequireComponent : Attribute
    {
        /// <summary>Accepts the required fixture component type.</summary>
        /// <param name="type">The required component type.</param>
        public RequireComponent(Type type)
        {
        }
    }
}

namespace UnityEngine.UI
{
    /// <summary>Stores observable UI material references.</summary>
    public abstract class Graphic : UnityEngine.Object
    {
        /// <summary>Gets or sets the material referenced by the graphic.</summary>
        public UnityEngine.Material material { get; set; } = null!;

        /// <summary>Gets or sets whether the graphic is visible.</summary>
        public bool enabled { get; set; }

        /// <summary>Gets the local transform data.</summary>
        public UnityEngine.Transform transform { get; } = new UnityEngine.Transform();
    }

    /// <summary>Stores the cover graphic data consumed by Awake.</summary>
    public sealed class Image : Graphic
    {
        /// <summary>Gets or sets the borrowed cover sprite.</summary>
        public UnityEngine.Sprite? sprite { get; set; }

        /// <summary>Gets the fixture rectangle.</summary>
        public UnityEngine.RectTransform rectTransform { get; } = new UnityEngine.RectTransform();
    }

    /// <summary>Stores the video graphic data consumed by Awake.</summary>
    public sealed class RawImage : Graphic
    {
        /// <summary>Gets or sets the borrowed decoder texture.</summary>
        public UnityEngine.Texture? texture { get; set; }
    }
}

namespace MajdataPlay
{
    /// <summary>Provides only the production base lifecycle and fallback resource required by BGManager.</summary>
    public abstract class MajBehaviour : UnityEngine.MonoBehaviour
    {
        /// <summary>Gets the fixture's fallback cover resources.</summary>
        protected TestRuntime RuntimeDatabase { get; } = new TestRuntime();

        /// <summary>Supplies a no-op base Awake for manager lifecycle tests.</summary>
        protected virtual void Awake()
        {
        }
    }

    /// <summary>Contains the fallback sprite group required by initialization.</summary>
    public sealed class TestRuntime
    {
        /// <summary>Gets the fallback sprite group.</summary>
        public TestSprites Sprite { get; } = new TestSprites();
    }

    /// <summary>Supplies a fallback cover without loading project assets.</summary>
    public sealed class TestSprites
    {
        /// <summary>Gets the fallback cover sprite.</summary>
        public UnityEngine.Sprite EmptySongCover { get; } = new UnityEngine.Sprite();
    }
}

namespace MajdataPlay.FFmpeg
{
    /// <summary>Supplies the state checked by the production manager.</summary>
    public enum VideoPlaybackState
    {
        /// <summary>The fixture is idle.</summary>
        Idle,
        /// <summary>The fixture has reached its end.</summary>
        Ended
    }

    /// <summary>Observes decoder shutdown and subscriptions without native FFmpeg libraries.</summary>
    public sealed class FFmpegVideoPlayer : UnityEngine.Object
    {
        /// <summary>Receives production texture subscription changes.</summary>
        public event Action<FFmpegVideoPlayer, UnityEngine.Texture?>? TextureChanged;

        /// <summary>Receives production error subscription changes.</summary>
        public event Action<FFmpegVideoPlayer, string>? ErrorReceived;

        /// <summary>Gets the number of installed texture callbacks.</summary>
        public int TextureSubscribers => TextureChanged?.GetInvocationList().Length ?? 0;

        /// <summary>Gets the number of installed error callbacks.</summary>
        public int ErrorSubscribers => ErrorReceived?.GetInvocationList().Length ?? 0;

        /// <summary>Gets the shutdown invocation count.</summary>
        public int CloseCalls { get; private set; }

        /// <summary>Gets or sets a decoder shutdown failure.</summary>
        public Exception? CloseFailure { get; set; }

        /// <summary>Gets or sets the loop preference.</summary>
        public bool Loop { get; set; }

        /// <summary>Gets or sets the playback position.</summary>
        public double TimeSeconds { get; set; }

        /// <summary>Gets the media length used by production accessors.</summary>
        public double LengthSeconds { get; } = 10;

        /// <summary>Gets the fixture's idle state.</summary>
        public VideoPlaybackState State { get; } = VideoPlaybackState.Idle;

        /// <summary>Gets whether the fixture is prepared.</summary>
        public bool IsPrepared { get; } = true;

        /// <summary>Gets whether the fixture allows seeking.</summary>
        public bool IsSeekable { get; } = true;

        /// <summary>Gets or sets the playback speed.</summary>
        public float PlaybackRate { get; set; }

        /// <summary>Gets the fixture's borrowed decoder texture.</summary>
        public UnityEngine.Texture? Texture { get; }

        /// <summary>Records shutdown and injects failures at the ownership boundary.</summary>
        public void Close()
        {
            CloseCalls++;
            if (CloseFailure != null)
            {
                throw CloseFailure;
            }
        }

        /// <summary>Compiles the unused production pause branch without a decoder.</summary>
        public void Pause()
        {
        }

        /// <summary>Compiles the unused production stop branch without a decoder.</summary>
        public void Stop()
        {
        }

        /// <summary>Compiles the unused production play branch without a decoder.</summary>
        public void Play()
        {
        }

        /// <summary>Supplies an already completed task for the unused load branch.</summary>
        /// <param name="path">The requested media path.</param>
        /// <returns>A completed managed task.</returns>
        public Task PreloadAsync(string path)
        {
            return Task.CompletedTask;
        }
    }
}

namespace MajdataPlay.Diagnostics
{
    /// <summary>Supplies unused error logging methods without project runtime services.</summary>
    public static class MajDebug
    {
        /// <summary>Accepts the production error text.</summary>
        /// <param name="message">The diagnostic message.</param>
        public static void LogError(string message)
        {
        }

        /// <summary>Accepts the production exception.</summary>
        /// <param name="exception">The diagnostic failure.</param>
        public static void LogException(Exception exception)
        {
        }
    }
}

namespace MajdataPlay.Extensions
{
    /// <summary>Provides the unused localized error lookup.</summary>
    public static class StringExtensions
    {
        /// <summary>Returns a key unchanged for managed tests.</summary>
        /// <param name="key">The production localization key.</param>
        /// <returns>The original key.</returns>
        public static string i18n(this string key)
        {
            return key;
        }
    }
}

namespace MajdataPlay.Numerics
{
    /// <summary>Provides the production brightness clamping signature.</summary>
    public static class FloatExtensions
    {
        /// <summary>Clamps one managed numeric value.</summary>
        /// <param name="value">The value being bounded.</param>
        /// <param name="min">The lower bound.</param>
        /// <param name="max">The upper bound.</param>
        /// <returns>The bounded value.</returns>
        public static float Clamp(this float value, float min, float max)
        {
            return Math.Clamp(value, min, max);
        }
    }
}

namespace Cysharp.Threading.Tasks
{
    /// <summary>Supplies the timeout clock selector required by the unused load branch.</summary>
    public enum DelayType
    {
        /// <summary>Uses wall-clock time in production.</summary>
        Realtime
    }

    /// <summary>Adapts the unused asynchronous branch to managed Tasks for source compilation.</summary>
    public static class TaskExtensions
    {
        /// <summary>Returns the fixture task without a Unity player loop.</summary>
        /// <param name="task">The task to expose.</param>
        /// <returns>The same task.</returns>
        public static Task AsUniTask(this Task task)
        {
            return task;
        }

        /// <summary>Compiles the production timeout expression without simulating asynchronous playback.</summary>
        /// <param name="task">The task to expose.</param>
        /// <param name="timeout">The unused timeout interval.</param>
        /// <param name="delayType">The unused production clock selector.</param>
        /// <returns>The same task.</returns>
        public static Task Timeout(this Task task, TimeSpan timeout, DelayType delayType)
        {
            return task;
        }
    }
}
