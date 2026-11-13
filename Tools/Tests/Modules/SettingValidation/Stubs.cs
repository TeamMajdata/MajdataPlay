// These narrow test doubles exercise production control flow outside Unity.
// They intentionally do not claim to validate TMP rendering or Unity's player loop.
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class SerializeField : Attribute { }
    public class Object
    {
        public static readonly List<GameObject> Objects = new();
        public static int InstantiateCount;
        public static T FindAnyObjectByType<T>() where T : Component => Objects.SelectMany(x => x.Components).OfType<T>().First();
        public static GameObject Instantiate(GameObject source, Transform parent)
        {
            InstantiateCount++;
            var clone = source.Clone();
            clone.transform.SetParent(parent, false);
            return clone;
        }
        public static T Instantiate<T>(T source, Transform parent) where T : Component => Instantiate(source.gameObject, parent).GetComponent<T>();
        public static void Destroy(Object value)
        {
            if (value is GameObject go)
            {
                go.SetActive(false);
                foreach (var child in go.transform.Children.ToArray()) Destroy(child.gameObject);
                foreach (var component in go.Components.ToArray()) Invoke(component, "OnDestroy");
                Objects.Remove(go);
            }
        }
        internal static void Invoke(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(target, null);
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public bool isActiveAndEnabled => gameObject.activeInHierarchy;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public void GetComponentsInChildren<T>(bool includeInactive, List<T> results) where T : Component
        {
            results.Clear();
            results.AddRange(gameObject.Descendants().Where(x => includeInactive || x.activeInHierarchy).SelectMany(x => x.Components).OfType<T>());
        }
    }
    public class MonoBehaviour : Component
    {
        /// <summary>Provides an uncancelled lifetime for this harness, which has no Unity player loop.</summary>
        public CancellationToken destroyCancellationToken => CancellationToken.None;
    }
    public class Canvas : Component { }
    /// <summary>Stores arrow opacity without modelling Unity Canvas composition.</summary>
    public class CanvasGroup : Component
    {
        /// <summary>Gets or sets the opacity supplied by production navigation callbacks.</summary>
        public float alpha { get; set; } = 1f;
    }
    public class CanvasRenderer : Component
    {
        Color _color = Color.white;
        public void SetColor(Color value) => _color = value;
        public Color GetColor() => _color;
    }
    public class GameObject : Object
    {
        internal static readonly HashSet<GameObject> TransitionRoots = new();
        internal readonly List<Component> Components = new();
        public string name;
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent?.gameObject.activeInHierarchy ?? true);
        public Transform transform;
        public Func<GameObject> Clone;
        public GameObject(string name = "Object", params Type[] types)
        {
            this.name = name;
            transform = types.Contains(typeof(RectTransform)) ? new RectTransform() : new Transform();
            transform.gameObject = this;
            Components.Add(transform);
            Objects.Add(this);
        }
        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            Components.Add(component);
            if (component is TMPro.TextMeshProUGUI) AddComponent<CanvasRenderer>();
            return component;
        }
        public T GetComponent<T>() where T : Component => Components.OfType<T>().First();
        public bool TryGetComponent<T>(out T value) where T : Component
        {
            value = Components.OfType<T>().FirstOrDefault();
            return value != null;
        }
        public void SetActive(bool active)
        {
            var subtree = Descendants().ToArray();
            var before = subtree.Select(x => x.activeInHierarchy).ToArray();
            activeSelf = active;
            TransitionRoots.Add(this);
            try
            {
                for (var i = 0; i < subtree.Length; i++)
                {
                    if (before[i] == subtree[i].activeInHierarchy) continue;
                    foreach (var component in subtree[i].Components.ToArray())
                        Invoke(component, subtree[i].activeInHierarchy ? "OnEnable" : "OnDisable");
                }
            }
            finally { TransitionRoots.Remove(this); }
        }
        internal IEnumerable<GameObject> Descendants()
        {
            yield return this;
            foreach (var child in transform.Children.ToArray())
                foreach (var value in child.gameObject.Descendants()) yield return value;
        }
    }
    public class Transform : Component
    {
        public Transform parent;
        public readonly List<Transform> Children = new();
        public Vector3 localPosition, localScale;
        public void SetParent(Transform value, bool worldPositionStays)
        {
            if (IsTransitioning(this) || IsTransitioning(value))
                throw new InvalidOperationException("Reparenting during hierarchy activation/deactivation is not allowed.");
            parent?.Children.Remove(this);
            parent = value;
            value?.Children.Add(this);
        }
        static bool IsTransitioning(Transform value)
        {
            for (var current = value; current != null; current = current.parent)
                if (GameObject.TransitionRoots.Contains(current.gameObject)) return true;
            return false;
        }
    }
    public class RectTransform : Transform { }
    public readonly record struct Vector3(float x, float y, float z)
    {
        public static Vector3 one => new(1, 1, 1);
        public static Vector3 operator *(Vector3 v, float s) => new(v.x * s, v.y * s, v.z * s);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => new(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
    }
    public readonly record struct Color(float r, float g, float b, float a)
    {
        public static Color white => new(1, 1, 1, 1);
        public static Color green => new(0, 1, 0, 1);
        public static Color red => new(1, 0, 0, 1);
        public static Color blue => new(0, 0, 1, 1);
        public static Color Lerp(Color a, Color b, float t) => new(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
    }
    public static class Mathf
    {
        public static float Clamp01(float x) => Math.Clamp(x, 0, 1);
        public static float Clamp(float x, float min, float max) => Math.Clamp(x, min, max);
        /// <summary>Interpolates a scalar for production category motion callbacks.</summary>
        /// <param name="a">The starting value.</param>
        /// <param name="b">The ending value.</param>
        /// <param name="t">The interpolation amount.</param>
        /// <returns>The interpolated value with the amount clamped to zero through one.</returns>
        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * Clamp01(t);
        }
        public static int FloorToInt(float x) => (int)Math.Floor(x);
        public static int CeilToInt(float x) => (int)Math.Ceiling(x);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Abs(float x) => Math.Abs(x);
        public static int RoundToInt(float x) => (int)Math.Round(x);
    }
    public static class Application { public static int targetFrameRate; }
    public static class QualitySettings
    {
        public static int vSyncCount;
    }
}
namespace UnityEngine.Serialization
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FormerlySerializedAsAttribute : Attribute { public FormerlySerializedAsAttribute(string name) { } }
}
namespace UnityEngine.Scripting { public class PreserveAttribute : Attribute { } }
namespace Newtonsoft.Json
{
    // Attribute presence is checked here; JSON serialization is verified separately with real Newtonsoft.
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class JsonIgnoreAttribute : Attribute { }
    /// <summary>Represents the JSON-name annotation; real JSON behavior is validated with Newtonsoft separately.</summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class JsonPropertyAttribute : Attribute
    {
        /// <summary>Accepts the serialized property name used by the production model.</summary>
        /// <param name="name">The JSON property name.</param>
        public JsonPropertyAttribute(string name)
        {
        }
    }
}
namespace MajdataPlay.Recording
{
    /// <summary>Isolates settings-menu close callbacks from the recording backend.</summary>
    internal static class Recorder
    {
        /// <summary>Accepts the production settings-change notification without starting a recorder.</summary>
        internal static void OnSettingsChanged()
        {
        }
    }
}
namespace TMPro
{
    public enum AtlasPopulationMode { Static, Dynamic }
    public sealed class TMP_Character { }
    public sealed class TMP_TextInfo { }
    public sealed class TMP_FontAsset
    {
        public readonly Dictionary<uint, TMP_Character> characterLookupTable = new();
        public List<TMP_FontAsset> fallbackFontAssetTable = new();
        public AtlasPopulationMode atlasPopulationMode = AtlasPopulationMode.Dynamic;
        public bool getFontFeatures = true;
        public bool TryAddCharacters(uint[] values, out uint[] missing, bool includeFontFeatures)
        {
            foreach (var value in values) characterLookupTable[value] = new();
            missing = Array.Empty<uint>();
            return true;
        }
    }
    public static class TMP_Settings
    {
        public static int missingGlyphCharacter;
        public static bool getFontFeaturesAtRuntime = true;
        public static readonly List<TMP_FontAsset> fallbackFontAssets = new();
        public static TMP_FontAsset defaultFontAsset;
    }
    public class TextMeshProUGUI : UnityEngine.Component
    {
        string _text = string.Empty;
        public static int TotalWrites;
        public int Writes;
        public string text { get => _text; set { _text = value; Writes++; TotalWrites++; } }
        public UnityEngine.Color color;
        public TMP_FontAsset font;
        public event Action<TMP_TextInfo> OnPreRenderText;
    }
}
namespace LitMotion
{
    public enum Ease { OutQuad, Linear }
    public static class MotionScheduler { public static readonly object PostLateUpdate = new(); }
    public sealed class Motion
    {
        public float From, To;
        public bool Active = true;
        public Action<float> Bind;
        public Action Complete;
    }
    public readonly struct MotionHandle
    {
        readonly Motion _motion;
        public MotionHandle(Motion motion) => _motion = motion;
        public bool TryCancel() { if (_motion == null) return false; var active = _motion.Active; _motion.Active = false; return active; }
    }
    public sealed class MotionBuilder
    {
        readonly Motion _motion;
        public MotionBuilder(float from, float to) => _motion = new Motion { From = from, To = to };
        public MotionBuilder WithScheduler(object scheduler) => this;
        public MotionBuilder WithEase(Ease ease) => this;
        public MotionBuilder WithOnComplete(Action action) { _motion.Complete = action; return this; }
        public MotionHandle Bind(Action<float> callback) { _motion.Bind = callback; LMotion.Motions.Add(_motion); return new(_motion); }
        public MotionHandle Bind<T>(T state, Action<float, T> callback) where T : class => Bind(value => callback(value, state));
    }
    public static class LMotion
    {
        public static readonly List<Motion> Motions = new();
        public static MotionBuilder Create(float from, float to, float duration) => new(from, to);
        public static void Step(float fraction)
        {
            foreach (var motion in Motions.Where(x => x.Active).ToArray())
            {
                motion.Bind(motion.From + (motion.To - motion.From) * fraction);
                if (fraction >= 1) { motion.Active = false; motion.Complete?.Invoke(); }
            }
            Motions.RemoveAll(x => !x.Active);
        }
    }
}
#nullable enable
namespace Cysharp.Threading.Tasks
{
    /// <summary>Adapts completed framework tasks to the production async signature without simulating Unity frames.</summary>
    [AsyncMethodBuilder(typeof(UniTaskMethodBuilder))]
    public readonly struct UniTask
    {
        /// <summary>Stores the completed or cancelled operation represented by this test adapter.</summary>
        private readonly Task? _task;

        /// <summary>Wraps a framework task returned by the test async builder.</summary>
        /// <param name="task">The operation to expose to production callers.</param>
        internal UniTask(Task task)
        {
            _task = task;
        }

        /// <summary>Completes a frame delay immediately while respecting an already cancelled token.</summary>
        /// <param name="frames">The production delay length; frame advancement is outside this harness.</param>
        /// <param name="cancellationToken">The cancellation state supplied by production initialization.</param>
        /// <returns>A completed or cancelled operation.</returns>
        public static UniTask DelayFrame(int frames, CancellationToken cancellationToken = default)
        {
            return new UniTask(cancellationToken.IsCancellationRequested
                ? Task.FromCanceled(cancellationToken) : Task.CompletedTask);
        }

        /// <summary>Exposes the framework awaiter to production async methods.</summary>
        /// <returns>The awaiter for this operation, or a completed default operation.</returns>
        public TaskAwaiter GetAwaiter()
        {
            return (_task ?? Task.CompletedTask).GetAwaiter();
        }

        /// <summary>Observes test failures while treating lifecycle cancellation as normal completion.</summary>
        public void Forget()
        {
            var task = _task ?? Task.CompletedTask;
            if (!task.IsCanceled)
            {
                task.GetAwaiter().GetResult();
            }
        }
    }

    /// <summary>Forwards the production task-like method signature to the framework async builder.</summary>
    public struct UniTaskMethodBuilder
    {
        /// <summary>Runs the actual C# state machine for this standalone adapter.</summary>
        private AsyncTaskMethodBuilder _builder;

        /// <summary>Creates a framework-backed builder.</summary>
        /// <returns>A builder ready to start its async state machine.</returns>
        public static UniTaskMethodBuilder Create()
        {
            return new UniTaskMethodBuilder { _builder = AsyncTaskMethodBuilder.Create() };
        }

        /// <summary>Gets the operation returned to the production caller.</summary>
        public UniTask Task => new UniTask(_builder.Task);

        /// <summary>Starts the generated state machine.</summary>
        /// <typeparam name="T">The compiler-generated state machine type.</typeparam>
        /// <param name="state">The state machine to start.</param>
        public void Start<T>(ref T state) where T : IAsyncStateMachine
        {
            _builder.Start(ref state);
        }

        /// <summary>Associates the generated state machine with the framework builder.</summary>
        /// <param name="state">The state machine being associated.</param>
        public void SetStateMachine(IAsyncStateMachine state)
        {
            _builder.SetStateMachine(state);
        }

        /// <summary>Completes the operation successfully.</summary>
        public void SetResult()
        {
            _builder.SetResult();
        }

        /// <summary>Records a failure for the caller's Forget observation.</summary>
        /// <param name="error">The exception raised by production initialization.</param>
        public void SetException(Exception error)
        {
            _builder.SetException(error);
        }

        /// <summary>Schedules a safe awaiter continuation through the framework builder.</summary>
        /// <typeparam name="TA">The awaiter type.</typeparam>
        /// <typeparam name="TS">The state machine type.</typeparam>
        /// <param name="awaiter">The operation being awaited.</param>
        /// <param name="state">The state machine to continue.</param>
        public void AwaitOnCompleted<TA, TS>(ref TA awaiter, ref TS state)
            where TA : INotifyCompletion where TS : IAsyncStateMachine
        {
            _builder.AwaitOnCompleted(ref awaiter, ref state);
        }

        /// <summary>Schedules a critical awaiter continuation through the framework builder.</summary>
        /// <typeparam name="TA">The awaiter type.</typeparam>
        /// <typeparam name="TS">The state machine type.</typeparam>
        /// <param name="awaiter">The operation being awaited.</param>
        /// <param name="state">The state machine to continue.</param>
        public void AwaitUnsafeOnCompleted<TA, TS>(ref TA awaiter, ref TS state)
            where TA : ICriticalNotifyCompletion where TS : IAsyncStateMachine
        {
            _builder.AwaitUnsafeOnCompleted(ref awaiter, ref state);
        }
    }
}
#nullable restore
namespace Cysharp.Text
{
    public readonly struct Utf16PreparedFormat<T>
    {
        readonly string _format;
        public Utf16PreparedFormat(string format) => _format = format;
        public string Format(T value) => string.Format(_format, value);
    }
    public readonly struct Utf16PreparedFormat<T, U>
    {
        readonly string _format;
        public Utf16PreparedFormat(string format) => _format = format;
        public string Format(T first, U second) => string.Format(_format, first, second);
    }
    public static class ZString
    {
        public static Utf16PreparedFormat<T> PrepareUtf16<T>(string format) => new(format);
        public static Utf16PreparedFormat<T, U> PrepareUtf16<T, U>(string format) => new(format);
    }
}
namespace MajdataPlay.i18n
{
    public sealed class Language
    {
        readonly string _name;
        public readonly string Code;
        Language(string name, string code) { _name = name; Code = code; }
        public static readonly Language English = new("English", "en-US"), Chinese = new("Chinese", "zh-CN"),
            TraditionalChinese = new("TraditionalChinese", "zh-TW"), Japanese = new("Japanese", "ja-JP");
        public static readonly Language[] All = { English, Chinese, TraditionalChinese, Japanese };
        public override string ToString() => _name;
    }
    public static class Localization
    {
        public static Language Current = Language.English;
        public static event EventHandler<Language> OnLanguageChanged;
        public static int Subscribers => OnLanguageChanged?.GetInvocationList().Length ?? 0;
        public static void Change(Language language) { Current = language; OnLanguageChanged?.Invoke(null, language); }
        /// <summary>Uses real rendering translations while retaining the existing narrow general-label behavior.</summary>
        public static bool TryFindLocalizedText(string key, out string text)
        {
            if (Translations.IsRenderingKey(key) || key.Contains("_OPTION_", StringComparison.Ordinal))
            {
                return Translations.Tryi18n(key, out text);
            }
            text = Translations.i18n(key);
            return true;
        }

        /// <summary>Accepts missing-key diagnostics without introducing a production logging backend.</summary>
        public static void ReportMissingTranslation(string key, string fallbackKeys = null, bool isNumericValue = false)
        {
        }
    }
}
namespace MajdataPlay.Extensions { public static class Placeholder { } }
namespace MajdataPlay.Numerics { public static class Integers { public static int Clamp(this int x, int min, int max) => Math.Clamp(x, min, max); } }
namespace MajdataPlay.Editor { [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)] public sealed class ReadOnlyFieldAttribute : Attribute { } }
namespace MajdataPlay.Collections
{
    public static class Enumerables
    {
        public static IEnumerable<(int, T)> WithIndex<T>(this IEnumerable<T> source) => source.Select((item, index) => (index, item));
        public static int FindIndex<T>(this T[] source, Predicate<T> predicate) => Array.FindIndex(source, predicate);
    }
}
namespace MajdataPlay.Diagnostics { public static class MajDebug { public static void LogWarning(string message) { } public static void LogError(string message) { } public static void LogWarning(string tag, string message) { } public static void LogError(string tag, string message) { } } }
namespace MajdataPlay { public static class MajTimeline { public static float DeltaTime = 1f / 60; } }
namespace MajdataPlay.IO
{
    public enum SensorArea { A1, A8, E4, B4, B3, E6, B5, B6, B1, B8 }
    public enum ButtonZone { A1, A2, A3, A4, A5, A6, A7, A8 }
    public enum SwitchStatus { On }
    public static class InputManager
    {
        public const float UI_CLICK_ANIMATION_DURATION_SEC = .1f;
        public static float TouchButtonRingEdge;
        public static bool PositivePressed, NegativePressed;
        public static readonly HashSet<ButtonZone> PressedButtons = new();
        public static readonly HashSet<SensorArea> PressedSensors = new();
        public static readonly HashSet<ButtonZone> CompletedButtons = new();
        public static readonly HashSet<SensorArea> CompletedSensors = new();
        public static readonly HashSet<ButtonZone> PressedThisFrameButtons = new();
        public static readonly HashSet<SensorArea> PressedThisFrameSensors = new();
        public static bool CheckSensorStatusInThisFrame(SensorArea area, SwitchStatus status) => PressedSensors.Contains(area) ||
            (area == SensorArea.E4 ? PositivePressed : area == SensorArea.E6 && NegativePressed);
        public static bool CheckButtonStatusInThisFrame(ButtonZone zone, SwitchStatus status) => PressedButtons.Contains(zone);
        public static bool IsButtonClickCompletedInThisFrame(ButtonZone zone) => CompletedButtons.Contains(zone);
        public static bool IsSensorClickCompletedInThisFrame(SensorArea area) => CompletedSensors.Contains(area);
        public static bool IsButtonPressedInThisFrame(ButtonZone zone) => PressedThisFrameButtons.Contains(zone);
        public static bool IsSensorPressedInThisFrame(SensorArea area) => PressedThisFrameSensors.Contains(area);
        public static void ClearFrame()
        {
            PositivePressed = NegativePressed = false;
            PressedButtons.Clear();
            PressedSensors.Clear();
            CompletedButtons.Clear();
            CompletedSensors.Clear();
            PressedThisFrameButtons.Clear();
            PressedThisFrameSensors.Clear();
        }
    }
    public static class CabinetLed { public static void SetAllLight(UnityEngine.Color color) { } public static void SetButtonLight(UnityEngine.Color color, int index) { } }
    public class AudioManager { public int VolumeReads, Previews; public void ReadVolumeFromSettings() => VolumeReads++; public void PlaySFX(string name) => Previews++; }
}
namespace MajdataPlay.Settings.OptionEnumerators
{
    // DisplayOptions names these production enumerators; the setting UI falls back to a read-only value for them here.
    public sealed class LanguageEnumerator { }
    public sealed class SkinEnumerator { }
}
namespace MajdataPlay.Settings.Runtime
{
    public class SettingConfig { public string SelectedMenu = "Game", SelectedOption = ""; public bool IgnoreChartSettingPage = true; }
    public class RuntimeConfig { public SettingConfig Setting = new(); public ListConfig List = new(); }
    public class ListConfig { public string SelectedSongId = ""; }
}
namespace MajdataPlay.Settings
{
    public enum OffsetUnitOption { Frame, Second }
    public class UserSetting { public GameOptions Game { get; } = new(); public JudgeOptions Judge { get; } = new(); public DisplayOptions Display { get; } = new(); public VolumeOptions Volume { get; } = new(); public TouchOptions Touch { get; } = new(); }
    public class SystemSetting { public CommonOptions Common { get; } = new(); public RenderingOptions Rendering { get; } = new(); [HideInSettingUI] public AudioOptions Audio { get; } = new(); public DebugOptions Debug { get; } = new(); }
    public class AudioOptions { public bool ForceMono { get; set; } }
    public class ChartSetting { [ChartDiscoveryProbe] public int ChartSpeed { get; set; } }
    public class GameOptions { public float SlideFadeInOffset { get; set; } }
    public class JudgeOptions { public float AudioOffset { get; set; } public float JudgeOffset { get; set; } public float AnswerOffset { get; set; } public float TouchPanelOffset { get; set; } public OffsetUnitOption OffsetUnit { get; set; } }
    public class VolumeOptions { public float Answer { get; set; } public float Tap { get; set; } public float Ex { get; set; } public float Break { get; set; } public float Slide { get; set; } public float Touch { get; set; } public float Hanabi { get; set; } public float Voice { get; set; } }
    public static class ChartSettingStorage { public static object GetSetting(object song) => new GameOptions(); public static void ConvertUnitToSecond() { } public static void ConvertUnitToFrame() { } }
}
namespace MajdataPlay.Scenes.Setting
{
    /// <summary>Isolates category text and animation rendering from production menu-state checks.</summary>
    public class MenuTitleDisplayer : UnityEngine.MonoBehaviour
    {
        /// <summary>Provides the font used by production menu text preparation.</summary>
        public TMPro.TMP_FontAsset Font;

        /// <summary>Accepts the production title provider without rendering a category title.</summary>
        /// <param name="title">The localized title provider.</param>
        public void Initialize(Func<string> title)
        {
        }

        /// <summary>Accepts production category visibility without rendering a hierarchy.</summary>
        /// <param name="value">The visibility requested by the menu.</param>
        public void SetVisible(bool value)
        {
        }

        /// <summary>Accepts the category's relative cursor position without modelling UI geometry.</summary>
        /// <param name="value">The relative category position.</param>
        public void SetDistance(float value)
        {
        }

        /// <summary>Leaves visual arrow animation outside this standalone menu-state harness.</summary>
        /// <param name="progress">The production animation clock, which this stub does not render.</param>
        /// <param name="initialArrowAlpha">The opacity retained by this visual stub.</param>
        /// <returns>The existing opacity without simulating the production fade profile.</returns>
        internal static float GetNavigationAlpha(float progress, float initialArrowAlpha)
        {
            return initialArrowAlpha;
        }
    }
}
namespace MajdataPlay
{
    public enum RunningMode { View, Normal }
    public static class MajEnv
    {
        public const float FRAME_LENGTH_SEC = 1f / 60;
        public static Settings.UserSetting UserSettings = new();
        public static Settings.SystemSetting SystemSettings = new();
        public static Settings.Runtime.RuntimeConfig RuntimeConfig = new();
        public static RunningMode Mode;
    }
    public static class MajInstances { public static IO.AudioManager AudioManager = new(); public static SceneSwitcher SceneSwitcher = new(); }
    /// <summary>Does not select a chart while validating standalone settings menus.</summary>
    internal static class SongStorage
    {
        /// <summary>Returns no selected chart; chart storage is outside this fixture.</summary>
        internal static object FindSelectedSong()
        {
            return null;
        }
    }
    public sealed class SceneSwitcher
    {
        public readonly List<string> SwitchedScenes = new();
        public void SwitchScene(string scene, bool value = true) => SwitchedScenes.Add(scene);
        public void FadeOut() { }
    }
    public static class GameManager
    {
        public static int RenderingApplies;
        public static int SaveRequests;
        public static object LastSaveRequester;
        public static void ApplyRenderingSettings() => RenderingApplies++;
        public static void RequestSave(object value) { SaveRequests++; LastSaveRequester = value; }
    }
    public static class Translations
    {
        static readonly Dictionary<string, Dictionary<string, string>> _renderingTranslations = new();
        // Keep old stub behavior for unrelated tests; rendering uses the four actual project tables.
        public static bool IsRenderingKey(string value) => value == "MAJSETTING_CATEGORY_Rendering" ||
            new[] { "RenderQuality", "AntiAliasing", "TextureQuality", "UIOutlineMode", "UIBackdropStyle", "RenderScale", "OutputMode", "CompatibilityMode", "SystemResolutionScaling", "FPSLimit", "VSync", "SystemScalingStatus", "DisplayBufferResolution", "SceneRenderResolution", "CurrentOutputPath", "SensorPosition", "SensorStyle", "SensorOpacity", "DisplaySensor" }
                .Any(property => value == "MAJSETTING_PROPERTY_" + property || value.StartsWith("MAJSETTING_PROPERTY_" + property + "_", StringComparison.Ordinal));
        public static string LookupRendering(string value, global::MajdataPlay.i18n.Language language)
        {
            if (!_renderingTranslations.TryGetValue(language.Code, out var table))
            {
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Languages", language.Code + ".json")));
                table = json.RootElement.GetProperty("Translations").EnumerateObject()
                    .Where(property => IsRenderingKey(property.Name)).ToDictionary(property => property.Name, property => property.Value.GetString());
                _renderingTranslations.Add(language.Code, table);
            }
            return table.TryGetValue(value, out var text) ? text : null;
        }
        public static string i18n(this string value) => IsRenderingKey(value)
            ? LookupRendering(value, global::MajdataPlay.i18n.Localization.Current) ?? value
            : $"{(global::MajdataPlay.i18n.Localization.Current)}:{value}";
        public static bool Tryi18n(this string value, out string text)
        {
            if (IsRenderingKey(value))
            {
                text = LookupRendering(value, global::MajdataPlay.i18n.Localization.Current);
                return text != null;
            }
            // Only bool values are translated so numeric assertions remain culture neutral.
            if (value.EndsWith("_True") || value.EndsWith("_False")) { text = value.i18n(); return true; }
            text = string.Empty;
            return false;
        }
    }
}

namespace MajdataPlay.Rendering { public static class MajAndroidDisplayResolution { public static MajSystemScalingStatus Status => MajSystemScalingStatus.Unsupported; } }
