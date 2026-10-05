#nullable enable
using UnityEngine;

namespace MajdataPlay.FFmpeg.Interop
{
    // Close/Destroy can remove the last player before the render thread unregisters
    // its GL texture or finishes a Vulkan fence. This collector outlives that player only
    // while native retirements remain. Driver failures retain, never prematurely
    // delete, registered GL storage.
    /// <summary>Keeps native retirement polling alive until outstanding GL registrations and Vulkan fences complete.</summary>
    internal sealed class HardwareTextureRetirementPump : MonoBehaviour
    {
        /// <summary>References the temporary collector that outlives players with pending native retirements.</summary>
        private static HardwareTextureRetirementPump? s_instance;
        /// <summary>Prevents creation of retirement objects while Unity is shutting down.</summary>
        private static bool s_quitting;
        /// <summary>Resets shutdown state when Unity initializes a new play session.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetQuitting()
        {
            s_quitting = false;
        }

        /// <summary>Creates a hidden persistent collector if native texture retirements still need polling.</summary>
        internal static void Ensure()
        {
            if (s_instance != null || s_quitting)
            {
                return;
            }

            var host = new GameObject("FFmpeg texture retirement")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            DontDestroyOnLoad(host);
            s_instance = host.AddComponent<HardwareTextureRetirementPump>();
        }

        /// <summary>Polls native retirements and destroys the collector once all resources can be released safely.</summary>
        private void Update()
        {
            if (!HardwareVideoPresenter.CollectRetiredTextures())
            {
                return;
            }

            s_instance = null;
            Destroy(gameObject);
        }

        /// <summary>Stops new retirement collectors from being created during shutdown.</summary>
        private void OnApplicationQuit()
        {
            s_quitting = true;
        }

        /// <summary>Clears the singleton reference if Unity destroys the active collector.</summary>
        private void OnDestroy()
        {
            if (s_instance == this)
            {
                s_instance = null;
            }
        }
    }
}
