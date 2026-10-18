#nullable enable
using MajdataPlay.IO.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace MajdataPlay.Rendering
{
    /// <summary>Temporarily isolates Android shader warmup crashes without enabling the Profiler.</summary>
    /// <remarks>This probe changes warmup order and API. A returned call does not prove asynchronous GPU work completed.</remarks>
    internal static class ShaderWarmupDiagnostics
    {
        /// <summary>Names the optional configuration in the application's persistent data directory.</summary>
        private const string ConfigFileName = "ShaderWarmupDiagnostics.json";

        /// <summary>Identifies probe messages in Android logcat without changing the application's logger configuration.</summary>
        private const string LogTag = "[ShaderWarmupDiagnostics]";

        /// <summary>Supplies a diagnostic vertex layout; this is not a claim that every game's pipeline state is warmed.</summary>
        private static readonly VertexAttributeDescriptor[] VertexLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4)
        };

        /// <summary>Runs the Android probe, retaining the original warmup on other platforms and in the Editor.</summary>
        /// <remarks>I/O and managed warmup errors propagate so a failed probe cannot silently run unlogged native work.</remarks>
        internal static void Run()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            RunDiagnostic();
#else
            Shader.WarmupAllShaders();
#endif
        }

        /// <summary>Records one independent game-entry session before invoking any native warmup operation.</summary>
        /// <exception cref="IOException">The diagnostic configuration or journal cannot be read or flushed.</exception>
        /// <exception cref="ArgumentException">The diagnostic configuration contains invalid JSON or options.</exception>
        private static void RunDiagnostic()
        {
            var directory = FileSystem.Directory.Create(Path.Combine(Application.persistentDataPath, "Logs", "ShaderWarmup"));
            var session = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + "-" + Guid.NewGuid().ToString("N");
            var path = Path.Combine(directory.Location, session + ".log");
            using var stream = FileSystem.File.OpenWrite(path);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            Mark(writer, $"SESSION version={Quote(Application.version)} build={Quote(Application.buildGUID)} unity={Quote(Application.unityVersion)} api={SystemInfo.graphicsDeviceType} gpu={Quote(SystemInfo.graphicsDeviceName)} development={Debug.isDebugBuild} journal={Quote(path)}");

            try
            {
                var options = LoadOptions();
                var mode = options.Mode?.Trim().ToLowerInvariant();
                if (mode != "individual" && mode != "all" && mode != "skip")
                {
                    throw new ArgumentException("Mode must be individual, all, or skip.");
                }
                if (options.StartIndex < 0 || options.Count < 0)
                {
                    throw new ArgumentException("StartIndex and Count must be non-negative.");
                }
                if (mode != "individual" && (!string.IsNullOrEmpty(options.ShaderName) || options.StartIndex != 0 || options.Count != 0))
                {
                    throw new ArgumentException("ShaderName, StartIndex, and Count are only valid in individual mode.");
                }

                Mark(writer, $"CONFIG mode={mode} shader={Quote(options.ShaderName ?? string.Empty)} start={options.StartIndex} count={options.Count}");
                if (mode == "skip")
                {
                    Mark(writer, "SKIP_WARMUP");
                }
                else if (mode == "all")
                {
                    // Preserve the original API and loaded set: do not enumerate or warm individual shaders first.
                    Mark(writer, "BEGIN_ALL");
                    Shader.WarmupAllShaders();
                    Mark(writer, "RETURN_ALL (native call returned; asynchronous work may remain)");
                }
                else
                {
                    WarmIndividualShaders(writer, options);
                }
                Mark(writer, "COMPLETE (probe returned; this does not certify shader or GPU correctness)");
            }
            catch (Exception exception)
            {
                try
                {
                    Mark(writer, $"MANAGED_EXCEPTION {exception}");
                }
                catch (Exception logException)
                {
                    // Native crashes are not catchable here. A failed disk journal must never hide the first managed error.
                    Debug.LogError($"{LogTag} Journal failure: {logException}");
                }
                throw;
            }
        }

        /// <summary>Reads the editable local probe options, creating documented defaults on first use.</summary>
        /// <returns>The requested diagnostic mode and optional shader selection.</returns>
        /// <exception cref="IOException">Reading or creating the configuration fails.</exception>
        /// <exception cref="ArgumentException">Unity cannot parse the configuration JSON.</exception>
        private static Options LoadOptions()
        {
            var path = Path.Combine(Application.persistentDataPath, ConfigFileName);
            var file = FileSystem.File.Open(path);
            var options = new Options();
            if (file.Exists)
            {
                JsonUtility.FromJsonOverwrite(file.ReadAllText(), options);
            }
            else
            {
                using var stream = FileSystem.File.OpenWrite(path);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.WriteLine(JsonUtility.ToJson(options, true));
                Flush(writer);
            }
            return options;
        }

        /// <summary>Snapshots loaded shaders, journals their stable indices, and warms only the selected entries.</summary>
        /// <param name="writer">The session journal that is flushed before each native operation.</param>
        /// <param name="options">The exact-name filter and index range within the complete sorted snapshot.</param>
        /// <exception cref="ArgumentException">The requested range or shader name does not select a loaded shader.</exception>
        private static void WarmIndividualShaders(StreamWriter writer, Options options)
        {
            Mark(writer, "BEGIN_ENUMERATE");
            var entries = new List<ShaderEntry>();
            foreach (var shader in Resources.FindObjectsOfTypeAll<Shader>())
            {
                if (shader != null)
                {
                    entries.Add(new ShaderEntry(shader));
                }
            }
            entries.Sort((left, right) =>
            {
                var comparison = string.Compare(left.Name, right.Name, StringComparison.Ordinal);
                return comparison != 0 ? comparison : left.InstanceId.CompareTo(right.InstanceId);
            });
            Mark(writer, $"RETURN_ENUMERATE loaded={entries.Count}");
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                Mark(writer, $"INVENTORY index={index} id={entry.InstanceId} shader={Quote(entry.Name)}");
            }

            if (options.StartIndex > entries.Count)
            {
                throw new ArgumentException("StartIndex exceeds the loaded shader count.");
            }
            var end = options.Count == 0
                ? entries.Count
                : (int)Math.Min(entries.Count, (long)options.StartIndex + options.Count);
            var warmed = 0;
            var setup = new ShaderWarmupSetup { vdecl = VertexLayout };
            for (var index = options.StartIndex; index < end; index++)
            {
                var entry = entries[index];
                if (!string.IsNullOrEmpty(options.ShaderName) && !string.Equals(entry.Name, options.ShaderName, StringComparison.Ordinal))
                {
                    continue;
                }
                if (entry.Shader == null)
                {
                    Mark(writer, $"SKIP_DESTROYED index={index} id={entry.InstanceId}");
                    continue;
                }

                // Do not inspect keywordSpace, instantiate materials, alter render targets, or skip unsupported shaders.
                // RETURN identifies CPU call return only. Confirm suspects with isolated cold-process tests.
                var identity = $"index={index} id={entry.InstanceId} shader={Quote(entry.Name)}";
                Mark(writer, "BEGIN_SHADER " + identity);
                ShaderWarmup.WarmupShader(entry.Shader, setup);
                Mark(writer, "RETURN_SHADER " + identity);
                warmed++;
            }
            if (warmed == 0 && (entries.Count != 0 || !string.IsNullOrEmpty(options.ShaderName)))
            {
                throw new ArgumentException("The requested filter/range did not warm any loaded shader. Check INVENTORY in this journal.");
            }
            Mark(writer, $"INDIVIDUAL_RETURNED count={warmed} (Pass/variant unknown; asynchronous work may remain)");
        }

        /// <summary>Writes a durable marker before emitting the same text to Unity's native log.</summary>
        /// <param name="writer">The caller-owned session journal.</param>
        /// <param name="message">The operation marker and its diagnostic context.</param>
        /// <exception cref="IOException">The journal cannot be written or flushed.</exception>
        private static void Mark(StreamWriter writer, string message)
        {
            var line = $"{DateTime.UtcNow:O} {LogTag} {message}";
            writer.WriteLine(line);
            Flush(writer);
            // MajDebug normally queues asynchronously; this temporary crash probe also needs immediate logcat output.
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", line);
        }

        /// <summary>Flushes managed buffers and requests a local disk flush before native code can crash the process.</summary>
        /// <param name="writer">The writer opened through the project's FileSystem abstraction.</param>
        /// <exception cref="IOException">The stream cannot be flushed.</exception>
        private static void Flush(StreamWriter writer)
        {
            writer.Flush();
            if (writer.BaseStream is FileStream file)
            {
                file.Flush(true);
            }
        }

        /// <summary>Escapes text so shader names cannot create ambiguous extra journal lines.</summary>
        /// <param name="value">The text to represent as one quoted journal field.</param>
        /// <returns>The escaped and quoted text.</returns>
        private static string Quote(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        }

        /// <summary>Provides local diagnostic controls without changing saved gameplay or rendering settings.</summary>
        [Serializable]
        private sealed class Options
        {
            /// <summary>Selects individual shader warmup, the original all-shader warmup, or no warmup.</summary>
            public string Mode = "individual";

            /// <summary>Optionally restricts individual mode to an exact, case-sensitive loaded shader name.</summary>
            public string ShaderName = string.Empty;

            /// <summary>Selects the first index in the complete name-sorted inventory.</summary>
            public int StartIndex = 0;

            /// <summary>Limits inventory entries visited; zero visits all remaining entries.</summary>
            public int Count = 0;
        }

        /// <summary>Keeps a loaded shader and its diagnostic identity alive for the current probe.</summary>
        private sealed class ShaderEntry
        {
            /// <summary>Retains the loaded shader without loading or instantiating additional resources.</summary>
            internal readonly Shader Shader;

            /// <summary>Snapshots the shader name before native warmup begins.</summary>
            internal readonly string Name;

            /// <summary>Disambiguates duplicate names within this process; IDs are not stable across launches.</summary>
            internal readonly int InstanceId;

            /// <summary>Snapshots the shader identity without accessing its keyword table.</summary>
            /// <param name="shader">The live shader found in the loaded-resource snapshot.</param>
            internal ShaderEntry(Shader shader)
            {
                Shader = shader;
                Name = shader.name;
                InstanceId = shader.GetInstanceID();
            }
        }
    }
}
