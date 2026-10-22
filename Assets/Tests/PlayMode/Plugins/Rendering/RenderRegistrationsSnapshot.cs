using System;
using System.Collections.Generic;
using MajdataPlay.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Rendering
{
    /// <summary>Saves the static camera capture and render extension registrations, isolates them for a test and restores them on disposal.</summary>
    public sealed class RenderRegistrationsSnapshot : IDisposable
    {
        /// <summary>Stores the capture registrations that existed before the test.</summary>
        private readonly Dictionary<Camera, Action<RenderTargetIdentifier, CommandBuffer>[]> _savedActions;
        /// <summary>Stores the extension factory that existed before the test.</summary>
        private readonly object? _savedFactory;
        /// <summary>Stores the extension version that existed before the test.</summary>
        private readonly int _savedVersion;
        /// <summary>Whether the snapshot was already restored.</summary>
        private bool _disposed;

        /// <summary>Saves the current registrations and clears the extension factory so the test starts without one.</summary>
        public RenderRegistrationsSnapshot()
        {
            _savedActions = new Dictionary<Camera, Action<RenderTargetIdentifier, CommandBuffer>[]>(Actions);
            _savedFactory = PrivateMember.GetStaticField<object?>(typeof(MajCameraRenderExtensions), "_factory");
            _savedVersion = MajCameraRenderExtensions.Version;
            PrivateMember.SetStaticField(typeof(MajCameraRenderExtensions), "_factory", null);
            // A distinct version makes every renderer drop extension instances cached by earlier tests.
            PrivateMember.SetStaticProperty(typeof(MajCameraRenderExtensions), nameof(MajCameraRenderExtensions.Version), _savedVersion + 1000);
        }

        /// <summary>Gets the live capture registration dictionary.</summary>
        private static Dictionary<Camera, Action<RenderTargetIdentifier, CommandBuffer>[]> Actions =>
            PrivateMember.GetStaticField<Dictionary<Camera, Action<RenderTargetIdentifier, CommandBuffer>[]>>(typeof(MajCameraCaptureBridge), "s_actions");

        /// <summary>Restores the saved capture registrations, extension factory and version.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            var actions = Actions;
            actions.Clear();
            foreach (var pair in _savedActions)
            {
                actions[pair.Key] = pair.Value;
            }

            PrivateMember.SetStaticField(typeof(MajCameraRenderExtensions), "_factory", _savedFactory);
            PrivateMember.SetStaticProperty(typeof(MajCameraRenderExtensions), nameof(MajCameraRenderExtensions.Version), _savedVersion);
        }
    }
}
