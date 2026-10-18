#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MajdataPlay.Rendering
{
    /// <summary>Registers optional consumers of a camera's resolved color without depending on a recording SDK.</summary>
    /// <remarks>
    /// Register and remove actions on the Unity main thread. Actions append commands after scene color
    /// has been resolved and before final presentation; Screen Space Overlay canvases are excluded.
    /// An action must not retain the source beyond the supplied command stream.
    /// </remarks>
    public static class MajCameraCaptureBridge
    {
        /// <summary>Stores registration-time snapshots so rendering does not allocate an enumerator or copy.</summary>
        private static readonly Dictionary<Camera, Action<RenderTargetIdentifier, CommandBuffer>[]> s_actions =
            new Dictionary<Camera, Action<RenderTargetIdentifier, CommandBuffer>[]>();

        /// <summary>Adds a unique capture action for a camera on the Unity main thread.</summary>
        /// <param name="camera">The live camera whose resolved color is required.</param>
        /// <param name="action">The callback that records capture commands into the renderer's command buffer.</param>
        /// <exception cref="ArgumentNullException">The camera is null or destroyed, or the action is null.</exception>
        public static void AddCaptureAction(Camera camera, Action<RenderTargetIdentifier, CommandBuffer> action)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            var previous = GetCaptureActions(camera);
            if (Array.IndexOf(previous, action) >= 0)
            {
                return;
            }

            var next = new Action<RenderTargetIdentifier, CommandBuffer>[previous.Length + 1];
            Array.Copy(previous, next, previous.Length);
            next[previous.Length] = action;
            s_actions[camera] = next;
        }

        /// <summary>Removes an action on the Unity main thread, including after the camera has been destroyed.</summary>
        /// <param name="camera">The original camera reference; an actual null reference is ignored.</param>
        /// <param name="action">The callback to remove; an unregistered or null callback is ignored.</param>
        public static void RemoveCaptureAction(Camera camera, Action<RenderTargetIdentifier, CommandBuffer> action)
        {
            // Unity's overloaded null comparison hides destroyed objects that still own dictionary entries.
            if (ReferenceEquals(camera, null) || action == null || !s_actions.TryGetValue(camera, out var previous))
            {
                return;
            }

            var index = Array.IndexOf(previous, action);
            if (index < 0)
            {
                return;
            }
            if (previous.Length == 1)
            {
                s_actions.Remove(camera);
                return;
            }

            var next = new Action<RenderTargetIdentifier, CommandBuffer>[previous.Length - 1];
            Array.Copy(previous, 0, next, 0, index);
            Array.Copy(previous, index + 1, next, index, next.Length - index);
            s_actions[camera] = next;
        }

        /// <summary>Gets the cached action snapshot for one camera without allocating during rendering.</summary>
        /// <param name="camera">The camera about to render.</param>
        /// <returns>A read-only-by-contract snapshot, or the shared empty array when no live camera has actions.</returns>
        /// <remarks>The renderer must not modify the returned array. Registration changes affect subsequent lookups only.</remarks>
        internal static Action<RenderTargetIdentifier, CommandBuffer>[] GetCaptureActions(Camera camera)
        {
            if (camera != null && s_actions.TryGetValue(camera, out var actions))
            {
                return actions;
            }
            return Array.Empty<Action<RenderTargetIdentifier, CommandBuffer>>();
        }

        /// <summary>Clears retained camera references when entering a new runtime session, including without domain reload.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Reset()
        {
            s_actions.Clear();
        }
    }
}
