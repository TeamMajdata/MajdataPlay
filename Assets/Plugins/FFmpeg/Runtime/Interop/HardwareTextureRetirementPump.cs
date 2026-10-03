using UnityEngine;

namespace MajdataPlay.Video.Interop
{
    // Close/Destroy can remove the last player before the render thread unregisters
    // its GL texture or finishes a Vulkan fence. This collector outlives that player only
    // while native retirements remain. Driver failures retain, never prematurely
    // delete, registered GL storage.
    internal sealed class HardwareTextureRetirementPump : MonoBehaviour
    {
        static HardwareTextureRetirementPump _instance;
        static bool _quitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetQuitting() { _quitting = false; }

        internal static void Ensure()
        {
            if (_instance != null || _quitting) return;
            var host = new GameObject("FFmpeg texture retirement") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<HardwareTextureRetirementPump>();
        }

        void Update()
        {
            if (!HardwareVideoPresenter.CollectRetiredTextures()) return;
            _instance = null;
            Destroy(gameObject);
        }

        void OnApplicationQuit() { _quitting = true; }
        void OnDestroy() { if (_instance == this) _instance = null; }
    }
}
