#nullable enable
#if UNITY_ANDROID || UNITY_EDITOR
using Cysharp.Threading.Tasks;
using MajdataPlay.Diagnostics;
using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.SceneManagement;

namespace MajdataPlay
{
    /// <summary>Defers the first scene until legacy storage permissions and environment initialization complete.</summary>
    internal sealed class AndroidStorageStartup : MonoBehaviour
    {
        /// <summary>Lists the legacy permissions in the order in which they are requested.</summary>
        private static readonly string[] s_permissions =
        {
            Permission.ExternalStorageRead,
            Permission.ExternalStorageWrite,
        };

        /// <summary>Initializes directories, logging, settings, and the remaining environment before scene activation.</summary>
        private Action _initialize = null!;

        /// <summary>Requests the permissions without allowing the first scene to start in the meantime.</summary>
        private Func<CancellationToken, UniTask<bool>> _requestPermissions = null!;

        /// <summary>Identifies the first scene to reload with its original activation and execution order.</summary>
        private string _scenePath = string.Empty;

        /// <summary>Determines whether this device and application can use legacy external storage.</summary>
        /// <param name="sdkVersion">The Android SDK level of the device.</param>
        /// <param name="targetSdkVersion">The target SDK level of the installed application.</param>
        /// <returns>Whether the legacy storage permission branch applies.</returns>
        internal static bool SupportsLegacyStorage(int sdkVersion, int targetSdkVersion)
        {
            return sdkVersion < 30 || targetSdkVersion < 30;
        }

        /// <summary>Checks whether both legacy external storage permissions are already granted.</summary>
        /// <returns>Whether the application can read and write legacy external storage.</returns>
        internal static bool HasPermissions()
        {
            return Permission.HasUserAuthorizedPermission(Permission.ExternalStorageRead)
                && Permission.HasUserAuthorizedPermission(Permission.ExternalStorageWrite);
        }

        /// <summary>Suspends the first scene before Awake and creates a persistent permission bootstrap.</summary>
        /// <param name="initialize">Initializes the environment after permission requests finish.</param>
        /// <param name="requestPermissions">Requests storage access and reports the final permission state.</param>
        /// <exception cref="InvalidOperationException">The first scene has no path to reload.</exception>
        internal static void Begin(Action initialize, Func<CancellationToken, UniTask<bool>> requestPermissions)
        {
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                throw new InvalidOperationException("The first scene must have a path for storage initialization.");
            }

            // BeforeSceneLoad exposes the first scene's objects before their scene handles become valid.
            // Preserve objects already created in a different valid scene, such as persistent library runners.
            var transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var transform in transforms)
            {
                var root = transform.gameObject;
                if (transform.parent is not null || !root.activeSelf || (root.scene.IsValid() && root.scene != scene))
                {
                    continue;
                }
                root.SetActive(false);
            }

            var bootstrap = new GameObject(nameof(AndroidStorageStartup));
            DontDestroyOnLoad(bootstrap);
            var startup = bootstrap.AddComponent<AndroidStorageStartup>();
            startup._initialize = initialize;
            startup._requestPermissions = requestPermissions;
            startup._scenePath = scene.path;
        }

        /// <summary>Requests legacy storage access using Unity's permission callbacks.</summary>
        /// <param name="cancellationToken">Cancels startup when the bootstrap is destroyed or the application exits.</param>
        /// <returns>Whether both legacy storage permissions are granted after all requested responses.</returns>
        /// <exception cref="OperationCanceledException">Startup was cancelled.</exception>
        internal static UniTask<bool> RequestPermissionsAsync(CancellationToken cancellationToken)
        {
            return RequestPermissionsAsync(Permission.HasUserAuthorizedPermission, RequestPermissionAsync, cancellationToken);
        }

        /// <summary>Waits for each missing permission before deciding whether shared storage can be used.</summary>
        /// <param name="hasPermission">Queries the current grant for a permission.</param>
        /// <param name="requestPermission">Requests one permission and completes after its response.</param>
        /// <param name="cancellationToken">Cancels the pending permission wait and prevents further requests.</param>
        /// <returns>Whether both permissions remain granted when the requests finish.</returns>
        /// <exception cref="OperationCanceledException">Startup was cancelled.</exception>
        internal static async UniTask<bool> RequestPermissionsAsync(Func<string, bool> hasPermission,
            Func<string, CancellationToken, UniTask<bool>> requestPermission, CancellationToken cancellationToken)
        {
            foreach (var permission in s_permissions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (hasPermission(permission))
                {
                    continue;
                }
                var granted = await requestPermission(permission, cancellationToken).AttachExternalCancellation(cancellationToken);
                await UniTask.SwitchToMainThread(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!granted)
                {
                    return false;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return hasPermission(Permission.ExternalStorageRead) && hasPermission(Permission.ExternalStorageWrite);
        }

        /// <summary>Waits for a grant, refusal, or dismissal instead of immediately rechecking a pending dialog.</summary>
        /// <param name="permission">The Android permission requested from the user.</param>
        /// <param name="cancellationToken">Cancels the wait and detaches its callbacks.</param>
        /// <returns>Whether the permission was granted.</returns>
        /// <exception cref="OperationCanceledException">Startup was cancelled.</exception>
        private static async UniTask<bool> RequestPermissionAsync(string permission, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completion = new UniTaskCompletionSource<bool>();
            var callbacks = new PermissionCallbacks();
            /// <summary>Completes this request when its permission is granted.</summary>
            /// <param name="name">The permission reported by Unity.</param>
            void OnGranted(string name)
            {
                if (name == permission)
                {
                    completion.TrySetResult(true);
                }
            }
            /// <summary>Completes this request when its permission is refused or dismissed.</summary>
            /// <param name="name">The permission reported by Unity.</param>
            void OnDenied(string name)
            {
                if (name == permission)
                {
                    completion.TrySetResult(false);
                }
            }
            callbacks.PermissionGranted += OnGranted;
            callbacks.PermissionDenied += OnDenied;
            callbacks.PermissionRequestDismissed += OnDenied;
            // Unity also forwards permanent refusals to PermissionDenied when its obsolete event is not subscribed.
            try
            {
                MajDebug.LogInfo(nameof(AndroidStorageStartup), $"Requesting legacy storage permission: {permission}");
                Permission.RequestUserPermission(permission, callbacks);
                return await completion.Task.AttachExternalCancellation(cancellationToken);
            }
            finally
            {
                callbacks.PermissionGranted -= OnGranted;
                callbacks.PermissionDenied -= OnDenied;
                callbacks.PermissionRequestDismissed -= OnDenied;
            }
        }

        private void Start()
        {
            InitializeAsync().Forget(OnInitializationFailed);
        }

        /// <summary>Initializes the environment after the permission response, then reloads the suspended first scene.</summary>
        /// <returns>An operation that completes once the initialized scene has loaded.</returns>
        /// <exception cref="OperationCanceledException">The bootstrap was destroyed or the application is exiting.</exception>
        /// <exception cref="Exception">Environment initialization or scene loading failed.</exception>
        private async UniTask InitializeAsync()
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
                this.GetCancellationTokenOnDestroy(), Application.exitCancellationToken);
            var cancellationToken = lifetime.Token;
            var granted = await _requestPermissions(cancellationToken);
            await UniTask.SwitchToMainThread(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (granted)
            {
                MajDebug.LogInfo(nameof(AndroidStorageStartup), "Legacy storage permission requests completed.");
            }
            else
            {
                MajDebug.LogWarning(nameof(AndroidStorageStartup), "Legacy storage permission was denied or dismissed; using application storage.");
            }
            _initialize();
            // Reload instead of enabling roots individually, preserving Awake ordering and serialized inactive objects.
            await SceneManager.LoadSceneAsync(_scenePath, LoadSceneMode.Single).ToUniTask(cancellationToken: cancellationToken);
            Destroy(gameObject);
        }

        /// <summary>Reports startup failures without starting scene services with a partial environment.</summary>
        /// <param name="exception">The observed initialization failure or lifetime cancellation.</param>
        private static void OnInitializationFailed(Exception exception)
        {
            if (exception is OperationCanceledException)
            {
                return;
            }
            MajDebug.LogError(nameof(AndroidStorageStartup), "Failed to initialize Android storage before loading the first scene.");
            MajDebug.LogException(nameof(AndroidStorageStartup), exception);
        }
    }
}
#endif
