#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace UnityEngine
{
    /// <summary>Models the destroyed-object null semantics used by the production selection code.</summary>
    internal class Object
    {
        /// <summary>Stores the instance identity supplied by each fixture.</summary>
        private readonly int _instanceId;

        /// <summary>Stores the diagnostic name while the object remains alive.</summary>
        private string _name;

        /// <summary>Gets or sets whether native object destruction has occurred.</summary>
        public bool Destroyed { get; set; }

        /// <summary>Gets or sets the live object's name.</summary>
        public string name
        {
            get
            {
                if (Destroyed)
                {
                    throw new InvalidOperationException("A destroyed Unity object's name was accessed.");
                }
                return _name;
            }
            set
            {
                _name = value;
            }
        }

        /// <summary>Creates a fixture object with a stable instance identity.</summary>
        /// <param name="objectName">The loaded asset name.</param>
        /// <param name="instanceId">The deterministic identity used for duplicate-name ordering.</param>
        protected Object(string objectName, int instanceId)
        {
            _name = objectName;
            _instanceId = instanceId;
        }

        /// <summary>Gets the fixture's stable instance identity.</summary>
        /// <returns>The identity supplied by the fixture.</returns>
        public int GetInstanceID()
        {
            return _instanceId;
        }

        /// <summary>Reports whether a managed reference still represents a live native object.</summary>
        /// <param name="value">The managed Unity object reference.</param>
        /// <returns>True only for a live, non-null reference.</returns>
        public static implicit operator bool(Object? value)
        {
            return value is not null && !value.Destroyed;
        }

        /// <summary>Compares live identities while treating destroyed objects as null.</summary>
        /// <param name="left">The left reference.</param>
        /// <param name="right">The right reference.</param>
        /// <returns>Whether both references denote the same live identity or Unity null.</returns>
        public static bool operator ==(Object? left, Object? right)
        {
            var leftNull = left is null || left.Destroyed;
            var rightNull = right is null || right.Destroyed;
            if (leftNull || rightNull)
            {
                return leftNull && rightNull;
            }
            return ReferenceEquals(left, right);
        }

        /// <summary>Negates Unity object equality.</summary>
        /// <param name="left">The left reference.</param>
        /// <param name="right">The right reference.</param>
        /// <returns>Whether the references do not compare equal.</returns>
        public static bool operator !=(Object? left, Object? right)
        {
            return !(left == right);
        }

        /// <summary>Compares this fixture's managed identity.</summary>
        /// <param name="value">The candidate object.</param>
        /// <returns>Whether the exact managed instance was supplied.</returns>
        public override bool Equals(object? value)
        {
            return ReferenceEquals(this, value);
        }

        /// <summary>Gets a stable hash for the fixture identity.</summary>
        /// <returns>The fixture's numeric identity.</returns>
        public override int GetHashCode()
        {
            return _instanceId;
        }
    }

    /// <summary>Models loaded shader metadata and the original global warmup entry point.</summary>
    internal sealed class Shader : Object
    {
        /// <summary>Gets or sets whether this shader is supported by the simulated device.</summary>
        public bool isSupported { get; set; } = true;

        /// <summary>Gets or sets the active subshader's pass count.</summary>
        public int passCount { get; set; } = 1;

        /// <summary>Counts original global warmup calls.</summary>
        public static int GlobalWarmupCalls { get; set; }

        /// <summary>Runs assertions or simulates a native-boundary managed failure.</summary>
        public static Action? BeforeGlobalWarmup { get; set; }

        /// <summary>Creates a named shader fixture.</summary>
        /// <param name="shaderName">The loaded shader name.</param>
        /// <param name="instanceId">The stable fixture identity.</param>
        public Shader(string shaderName, int instanceId) : base(shaderName, instanceId)
        {
        }

        /// <summary>Observes entry into the original bulk native warmup call.</summary>
        public static void WarmupAllShaders()
        {
            GlobalWarmupCalls++;
            BeforeGlobalWarmup?.Invoke();
        }
    }

    /// <summary>Returns the shader fixtures without loading new assets or constructing materials.</summary>
    internal static class Resources
    {
        /// <summary>Gets or sets the loaded object snapshot, including optional destroyed references.</summary>
        public static Object[] LoadedObjects { get; set; } = Array.Empty<Object>();

        /// <summary>Counts loaded object enumeration requests.</summary>
        public static int EnumerationCalls { get; set; }

        /// <summary>Returns all fixture objects assignable to the requested Unity type.</summary>
        /// <typeparam name="T">The requested Unity object type.</typeparam>
        /// <returns>A fresh snapshot retaining fixture object identities.</returns>
        public static T[] FindObjectsOfTypeAll<T>() where T : Object
        {
            EnumerationCalls++;
            return LoadedObjects.OfType<T>().ToArray();
        }
    }

    /// <summary>Provides the read-only build identity and per-case writable storage root.</summary>
    internal static class Application
    {
        /// <summary>Gets or sets the current fixture's persistent data directory.</summary>
        public static string persistentDataPath { get; set; } = string.Empty;

        /// <summary>Gets the simulated application version.</summary>
        public static string version => "warmup-validation";

        /// <summary>Gets the simulated build identity written into each session.</summary>
        public static string buildGUID => "validation-build-guid";

        /// <summary>Gets the pinned Unity version represented by these stubs.</summary>
        public static string unityVersion => "6000.3.17f1";

        /// <summary>Gets the simulated Android platform identity.</summary>
        public static RuntimePlatform platform => RuntimePlatform.Android;
    }

    /// <summary>Names the only simulated runtime platform.</summary>
    internal enum RuntimePlatform
    {
        /// <summary>Represents an Android Player.</summary>
        Android
    }

    /// <summary>Provides stable backend metadata without creating a graphics device.</summary>
    internal static class SystemInfo
    {
        /// <summary>Gets the backend identity used by diagnostic sessions.</summary>
        public static Rendering.GraphicsDeviceType graphicsDeviceType => Rendering.GraphicsDeviceType.Vulkan;

        /// <summary>Gets the simulated device name.</summary>
        public static string graphicsDeviceName => "Stub Vulkan device";

        /// <summary>Gets the simulated device vendor.</summary>
        public static string graphicsDeviceVendor => "Validation";

        /// <summary>Gets the simulated graphics driver version.</summary>
        public static string graphicsDeviceVersion => "Stub Vulkan 1.0";

        /// <summary>Gets the simulated model name.</summary>
        public static string deviceModel => "Managed validation fixture";

        /// <summary>Gets the simulated operating system.</summary>
        public static string operatingSystem => "Managed validation host";
    }

    /// <summary>Approximates the public-field JSON configuration used by this diagnostic helper.</summary>
    internal static class JsonUtility
    {
        /// <summary>Matches the public-field and exact-name mapping expected by the configuration.</summary>
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = false
        };

        /// <summary>Reads a configuration fixture using public serializable fields.</summary>
        /// <param name="json">The JSON fixture.</param>
        /// <param name="target">The initialized object whose explicitly supplied fields should be overwritten.</param>
        public static void FromJsonOverwrite(string json, object target)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new ArgumentException("A configuration JSON object is required.", nameof(json));
                }
                foreach (var field in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (document.RootElement.TryGetProperty(field.Name, out var value))
                    {
                        field.SetValue(target, value.Deserialize(field.FieldType, Options));
                    }
                }
            }
            catch (JsonException exception)
            {
                throw new ArgumentException("The fixture contains malformed JSON.", nameof(json), exception);
            }
        }

        /// <summary>Serializes public configuration fields without changing their initializers.</summary>
        /// <param name="value">The production options object.</param>
        /// <param name="prettyPrint">Whether to indent the generated JSON fixture.</param>
        /// <returns>The serialized public-field representation.</returns>
        public static string ToJson(object value, bool prettyPrint)
        {
            var options = new JsonSerializerOptions(Options) { WriteIndented = prettyPrint };
            return JsonSerializer.Serialize(value, value.GetType(), options);
        }
    }

    /// <summary>Names the logger severity values used by synchronous diagnostics.</summary>
    internal enum LogType
    {
        /// <summary>Represents an informational message.</summary>
        Log,
        /// <summary>Represents a recoverable diagnostic warning.</summary>
        Warning,
        /// <summary>Represents a diagnostic error.</summary>
        Error
    }

    /// <summary>Names the synchronous logger option used to suppress unnecessary stacks.</summary>
    internal enum LogOption
    {
        /// <summary>Disables stack traces for individual marker lines.</summary>
        NoStacktrace
    }

    /// <summary>Captures synchronous logger markers for assertions.</summary>
    internal static class Debug
    {
        /// <summary>Reports that the regression target represents a Release build.</summary>
        public static bool isDebugBuild => false;

        /// <summary>Collects fully formatted log messages in call order.</summary>
        public static List<string> Messages { get; } = new List<string>();

        /// <summary>Formats one synchronous Unity logger message.</summary>
        /// <param name="logType">The marker severity.</param>
        /// <param name="logOptions">The requested stack trace behavior.</param>
        /// <param name="context">The optional Unity object context.</param>
        /// <param name="format">The composite format string.</param>
        /// <param name="args">The format arguments.</param>
        public static void LogFormat(LogType logType, LogOption logOptions, Object? context, string format, params object[] args)
        {
            Messages.Add(string.Format(CultureInfo.InvariantCulture, format, args));
        }

        /// <summary>Captures the fallback diagnostic for a journal failure.</summary>
        /// <param name="message">The fallback error context.</param>
        public static void LogError(object message)
        {
            Messages.Add(message.ToString() ?? string.Empty);
        }
    }
}

namespace UnityEngine.Rendering
{
    /// <summary>Names the only graphics backend represented by this fixture.</summary>
    internal enum GraphicsDeviceType
    {
        /// <summary>Represents the Vulkan backend under investigation.</summary>
        Vulkan
    }

    /// <summary>Names vertex channels accepted by the linked production warmup setup.</summary>
    internal enum VertexAttribute
    {
        /// <summary>Represents object-space positions.</summary>
        Position,
        /// <summary>Represents vertex normals.</summary>
        Normal,
        /// <summary>Represents vertex tangents.</summary>
        Tangent,
        /// <summary>Represents vertex colors.</summary>
        Color,
        /// <summary>Represents the first texture coordinate channel.</summary>
        TexCoord0,
        /// <summary>Represents the second texture coordinate channel.</summary>
        TexCoord1
    }

    /// <summary>Names the numeric channel format needed by this diagnostic setup.</summary>
    internal enum VertexAttributeFormat
    {
        /// <summary>Represents one 32-bit floating point component.</summary>
        Float32
    }

    /// <summary>Retains the diagnostic vertex declaration for native-boundary assertions.</summary>
    internal readonly struct VertexAttributeDescriptor
    {
        /// <summary>Gets the vertex channel semantic.</summary>
        public VertexAttribute attribute { get; }

        /// <summary>Gets the numeric component format.</summary>
        public VertexAttributeFormat format { get; }

        /// <summary>Gets the component count.</summary>
        public int dimension { get; }

        /// <summary>Gets the vertex stream index.</summary>
        public int stream { get; }

        /// <summary>Creates a diagnostic vertex channel description.</summary>
        /// <param name="attribute">The vertex channel semantic.</param>
        /// <param name="format">The component format.</param>
        /// <param name="dimension">The channel's component count.</param>
        /// <param name="stream">The vertex stream index.</param>
        public VertexAttributeDescriptor(VertexAttribute attribute, VertexAttributeFormat format = VertexAttributeFormat.Float32, int dimension = 3, int stream = 0)
        {
            this.attribute = attribute;
            this.format = format;
            this.dimension = dimension;
            this.stream = stream;
        }
    }
}

namespace UnityEngine.Experimental.Rendering
{
    /// <summary>Models the real API's exposed vertex declaration field.</summary>
    internal struct ShaderWarmupSetup
    {
        /// <summary>Gets or sets the vertex layout used by one shader prewarm request.</summary>
        public UnityEngine.Rendering.VertexAttributeDescriptor[] vdecl;
    }

    /// <summary>Observes per-shader native calls without touching graphics state.</summary>
    internal static class ShaderWarmup
    {
        /// <summary>Collects attempted shader calls, including those whose observer throws.</summary>
        public static List<UnityEngine.Shader> Calls { get; } = new List<UnityEngine.Shader>();

        /// <summary>Runs assertions immediately on entering the native API boundary.</summary>
        public static Action<UnityEngine.Shader, ShaderWarmupSetup>? BeforeWarmup { get; set; }

        /// <summary>Records the attempted call and invokes the configured observer synchronously.</summary>
        /// <param name="shader">The selected loaded shader.</param>
        /// <param name="setup">The production vertex declaration.</param>
        public static void WarmupShader(UnityEngine.Shader shader, ShaderWarmupSetup setup)
        {
            Calls.Add(shader);
            BeforeWarmup?.Invoke(shader, setup);
        }
    }
}
