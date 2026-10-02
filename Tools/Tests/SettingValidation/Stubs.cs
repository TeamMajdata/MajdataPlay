// These narrow test doubles exercise production control flow outside Unity.
// They intentionally do not claim to validate TMP rendering or Unity's player loop.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    public class MonoBehaviour : Component { }
    public class Canvas : Component { }
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
        public static int FloorToInt(float x) => (int)Math.Floor(x);
        public static int CeilToInt(float x) => (int)Math.Ceiling(x);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Abs(float x) => Math.Abs(x);
    }
    public static class Application { public static int targetFrameRate; }
    public static class QualitySettings
    {
        public static int vSyncCount;
        public static void SetQualityLevel(int value, bool applyExpensiveChanges)
        {
            vSyncCount = value == 0 ? 0 : 1;
            ((Rendering.Universal.UniversalRenderPipelineAsset)Rendering.GraphicsSettings.currentRenderPipeline).renderScale = value == 0 ? .75f : 1f;
        }
    }
}
namespace UnityEngine.Serialization
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FormerlySerializedAsAttribute : Attribute { public FormerlySerializedAsAttribute(string name) { } }
}
namespace UnityEngine.Scripting { public class PreserveAttribute : Attribute { } }
namespace UnityEngine.Rendering { public static class GraphicsSettings { public static object currentRenderPipeline = new Universal.UniversalRenderPipelineAsset(); } }
namespace UnityEngine.Rendering.Universal { public class UniversalRenderPipelineAsset { public float renderScale = 1; } }
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
    public enum Ease { OutQuad }
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
namespace Cysharp.Threading.Tasks
{
    public static class UniTask { public static Task DelayFrame(int frames) => Task.CompletedTask; }
    [AsyncMethodBuilder(typeof(UniTaskVoidBuilder))]
    public readonly struct UniTaskVoid { public void Forget() { } }
    public struct UniTaskVoidBuilder
    {
        AsyncTaskMethodBuilder _builder;
        public static UniTaskVoidBuilder Create() => new() { _builder = AsyncTaskMethodBuilder.Create() };
        public UniTaskVoid Task => default;
        public void Start<T>(ref T state) where T : IAsyncStateMachine => _builder.Start(ref state);
        public void SetStateMachine(IAsyncStateMachine state) => _builder.SetStateMachine(state);
        public void SetResult() => _builder.SetResult();
        public void SetException(Exception error) => throw error;
        public void AwaitOnCompleted<TA, TS>(ref TA awaiter, ref TS state) where TA : INotifyCompletion where TS : IAsyncStateMachine => _builder.AwaitOnCompleted(ref awaiter, ref state);
        public void AwaitUnsafeOnCompleted<TA, TS>(ref TA awaiter, ref TS state) where TA : ICriticalNotifyCompletion where TS : IAsyncStateMachine => _builder.AwaitUnsafeOnCompleted(ref awaiter, ref state);
    }
}
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
        Language(string name) => _name = name;
        public static readonly Language English = new("English"), Chinese = new("Chinese");
        public override string ToString() => _name;
    }
    public static class Localization
    {
        public static Language Current = Language.English;
        public static event EventHandler<Language> OnLanguageChanged;
        public static int Subscribers => OnLanguageChanged?.GetInvocationList().Length ?? 0;
        public static void Change(Language language) { Current = language; OnLanguageChanged?.Invoke(null, language); }
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
namespace MajdataPlay.Diagnostics { public static class MajDebug { public static void LogWarning(string message) { } public static void LogError(string message) { } } }
namespace MajdataPlay.Utils { public static class MajTimeline { public static float DeltaTime = 1f / 60; } }
namespace MajdataPlay.IO
{
    public enum SensorArea { A1, E4, B4, B3, E6, B5, B6 }
    public enum ButtonZone { A1, A2, A3, A4, A5, A6, A7 }
    public enum SwitchStatus { On }
    public static class InputManager
    {
        public const float UI_CLICK_ANIMATION_DURATION_SEC = .1f;
        public static float TouchButtonRingEdge;
        public static bool PositivePressed, NegativePressed;
        public static bool CheckSensorStatusInThisFrame(SensorArea area, SwitchStatus status) => area == SensorArea.E4 ? PositivePressed : area == SensorArea.E6 && NegativePressed;
        public static bool CheckButtonStatusInThisFrame(ButtonZone zone, SwitchStatus status) => false;
        public static bool IsButtonClickCompletedInThisFrame(ButtonZone zone) => false;
        public static bool IsSensorClickCompletedInThisFrame(SensorArea area) => false;
    }
    public static class CabinetLed { public static void SetAllLight(UnityEngine.Color color) { } public static void SetButtonLight(UnityEngine.Color color, int index) { } }
    public class AudioManager { public int VolumeReads, Previews; public void ReadVolumeFromSettings() => VolumeReads++; public void PlaySFX(string name) => Previews++; }
}
namespace MajdataPlay.Settings.Runtime
{
    public class SettingConfig { public string SelectedMenu = "Game", SelectedOption = ""; public bool IgnoreChartSettingPage = true; }
    public class RuntimeConfig { public SettingConfig Setting = new(); public ListConfig List = new(); }
    public class ListConfig { public string SelectedSongHash = ""; }
}
namespace MajdataPlay.Settings
{
    public enum OffsetUnitOption { Frame, Second }
    public enum RenderQualityOption { Low, High }
    public class GameSetting { public GameOptions Game { get; } = new(); public JudgeOptions Judge { get; } = new(); public DebugOptions Debug { get; } = new(); public AudioOptions Audio { get; } = new(); }
    public class AudioOptions { public SFXVolume Volume { get; } = new(); }
    public class ChartSetting { [ChartDiscoveryProbe] public int ChartSpeed { get; set; } }
    public class GameOptions { public float SlideFadeInOffset { get; set; } }
    public class JudgeOptions { public float AudioOffset { get; set; } public float JudgeOffset { get; set; } public float AnswerOffset { get; set; } public float TouchPanelOffset { get; set; } }
    public class DebugOptions { public OffsetUnitOption OffsetUnit { get; set; } public float DisplayOffset { get; set; } public int MenuOptionIterationSpeed { get; set; } = 45; }
    public class DisplayOptions { public RenderQualityOption RenderQuality { get; set; } = RenderQualityOption.High; public int RenderScale { get; set; } = 100; public bool VSync { get; set; } = true; }
    public class SFXVolume { public float Answer { get; set; } public float Tap { get; set; } public float Ex { get; set; } public float Break { get; set; } public float Slide { get; set; } public float Touch { get; set; } public float Hanabi { get; set; } public float Voice { get; set; } }
    public static class ChartSettingStorage { public static object GetSetting(string hash) => new GameOptions(); public static void ConvertUnitToSecond() { } public static void ConvertUnitToFrame() { } }
}
namespace MajdataPlay.Scenes.Setting
{
    public class MenuTitleDisplayer : UnityEngine.MonoBehaviour { public TMPro.TMP_FontAsset Font; public void Initialize(string key) { } public void SetVisible(bool value) { } public void SetDistance(float value) { } }
}
namespace MajdataPlay
{
    public enum RunningMode { View, Normal }
    public static class MajEnv
    {
        public const float FRAME_LENGTH_SEC = 1f / 60;
        public static Settings.GameSetting Settings = new();
        public static Settings.Runtime.RuntimeConfig RuntimeConfig = new();
        public static RunningMode Mode;
    }
    public static class MajInstances { public static IO.AudioManager AudioManager = new(); public static SceneSwitcher SceneSwitcher = new(); }
    public sealed class SceneSwitcher { public void SwitchScene(string scene, bool value = true) { } public void FadeOut() { } }
    public static class GameManager { public static int RenderScale; public static void ApplyRenderScale(int value) => RenderScale = value; public static void RequestSave(object value) { } }
    public static class Translations
    {
        public static string i18n(this string value) => $"{(global::MajdataPlay.i18n.Localization.Current)}:{value}";
        public static bool Tryi18n(this string value, out string text)
        {
            // Only bool values are translated so numeric assertions remain culture neutral.
            if (value.EndsWith("_True") || value.EndsWith("_False")) { text = value.i18n(); return true; }
            text = string.Empty;
            return false;
        }
    }
}
