using System;
using System.Collections.Generic;
using MajdataPlay.Rendering;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Rendering
{
    /// <summary>Captures the static render diagnostics on creation and restores them on disposal.</summary>
    public sealed class DiagnosticsSnapshot : IDisposable
    {
        /// <summary>Names the private static fields that hold the diagnostics state.</summary>
        private static readonly string[] s_fields =
        {
            "_displayWidth", "_displayHeight", "_sceneWidth", "_sceneHeight", "_displayResolution", "_sceneResolution", "_frameId"
        };

        /// <summary>Stores the captured field values.</summary>
        private readonly Dictionary<string, object?> _values = new Dictionary<string, object?>();
        /// <summary>Stores the captured output status.</summary>
        private readonly MajRenderOutputStatus _status;

        /// <summary>Captures the current diagnostics.</summary>
        public DiagnosticsSnapshot()
        {
            foreach (var name in s_fields)
            {
                _values[name] = PrivateMember.GetStaticField<object?>(typeof(MajRenderDiagnostics), name);
            }

            _status = MajRenderDiagnostics.OutputStatus;
        }

        /// <summary>Restores the captured diagnostics.</summary>
        public void Dispose()
        {
            foreach (var pair in _values)
            {
                PrivateMember.SetStaticField(typeof(MajRenderDiagnostics), pair.Key, pair.Value);
            }

            PrivateMember.SetStaticProperty(typeof(MajRenderDiagnostics), nameof(MajRenderDiagnostics.OutputStatus), _status);
        }
    }
}
