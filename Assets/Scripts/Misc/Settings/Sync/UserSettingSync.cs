using MajdataPlay.Diagnostics;
using MajdataPlay.Net;
using System;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Settings.Sync
{
    /// <summary>
    /// Keeps the user settings in memory in step with the copy of the logged-in account, and decides which copy a save
    /// writes.
    /// </summary>
    /// <remarks>
    /// Only the first configured endpoint stores user settings, so mirrors of a server never receive them. While an
    /// account owns the settings, settings.json is neither read nor written; leaving the account brings the local copy
    /// back. An upload that fails is logged and retried by the next save, because a failed upload must never block saving.
    /// </remarks>
    internal static class UserSettingSync
    {
        /// <summary>The delay that merges consecutive saves into one upload.</summary>
        internal const int DefaultPushDebounceMilliseconds = 1500;

        /// <summary>The longest wait for pending uploads when the game quits.</summary>
        internal const int QuitFlushTimeoutMilliseconds = 1000;

        /// <summary>The longest wait for pending uploads when the account is left.</summary>
        private static readonly TimeSpan s_detachFlushTimeout = TimeSpan.FromSeconds(1);

        /// <summary>The tag of the log messages of the sync.</summary>
        private const string LOG_TAG = nameof(UserSettingSync);

        /// <summary>Guards the pending upload and the loop that drains it.</summary>
        private static readonly object s_pushGate = new();

        /// <summary>The document waiting to be uploaded, or null when nothing is pending.</summary>
        private static string? s_pendingJson;

        /// <summary>Increases whenever the pending document is replaced, so a stale delay never consumes a newer one.</summary>
        private static int s_pendingVersion;

        /// <summary>The loop draining <see cref="s_pendingJson"/>, or null before the first upload.</summary>
        private static Task? s_pushLoop;

        /// <summary>The API used to exchange the document; replaced by tests.</summary>
        private static IUserSettingApi s_api = OnlineUserSettingApi.Shared;

        /// <summary>Gets or sets the delay that merges consecutive saves into one upload; tests set it to zero.</summary>
        internal static int PushDebounceMilliseconds { get; set; } = DefaultPushDebounceMilliseconds;

        /// <summary>Gets or sets the API used to exchange the document, so tests can answer instead of a server.</summary>
        /// <exception cref="ArgumentNullException">The assigned value is null.</exception>
        internal static IUserSettingApi Api
        {
            get
            {
                return s_api;
            }
            set
            {
                s_api = value ?? throw new ArgumentNullException(nameof(value));
            }
        }

        /// <summary>Makes the copy of the logged-in account the copy the game uses.</summary>
        /// <param name="endpoint">The endpoint the user just logged in on.</param>
        /// <param name="token">The token canceling the exchange.</param>
        /// <returns>
        /// <see cref="UserSettingAttachResult.Skipped"/> when the endpoint does not store user settings,
        /// <see cref="UserSettingAttachResult.Attached"/> when the copy of the account replaced the settings in memory, and
        /// <see cref="UserSettingAttachResult.Degraded"/> when the copy of the account stayed in use.
        /// </returns>
        /// <remarks>
        /// A copy that cannot be fetched falls back to the local settings for this session. A copy that is missing or
        /// unreadable is repaired by uploading the settings in memory, so the account never keeps a broken document.
        /// </remarks>
        internal static async ValueTask<UserSettingAttachResult> AttachAsync(ApiEndpoint endpoint, CancellationToken token = default)
        {
            var owner = MajEnv.GetUserSettingEndpoint();
            if (owner is null || !ReferenceEquals(owner, endpoint))
            {
                // Settings are only exchanged with the first endpoint; the others are mirrors of the same server.
                return UserSettingAttachResult.Skipped;
            }

            MajEnv.SetUserSettingSource(UserSettingSource.Switching, null);

            string? raw;
            try
            {
                raw = await s_api.FetchAsync(owner, token);
            }
            catch (Exception e)
            {
                MajDebug.LogException(LOG_TAG, e);
                raw = null;
            }

            if (raw is null)
            {
                MajDebug.LogWarning(LOG_TAG, "Failed to fetch the user settings of the account, the local settings stay in use for this session.");
                MajEnv.SetUserSettingSource(UserSettingSource.Local, null);
                return UserSettingAttachResult.Degraded;
            }

            var result = MajEnv.ReadUserSettings(raw, out var settings);
            if (result is UserSettingReadResult.Ok && settings is not null)
            {
                MajEnv.ApplyUserSettings(settings, UserSettingSource.Remote, owner);
                return UserSettingAttachResult.Attached;
            }

            MajDebug.LogWarning(LOG_TAG, result is UserSettingReadResult.Empty
                ? "The account holds no user settings, the local settings are uploaded."
                : "The user settings of the account cannot be read, the local settings are uploaded instead.");

            if (await StoreAsync(owner, MajEnv.SerializeUserSettings(), token))
            {
                MajEnv.SetUserSettingSource(UserSettingSource.Remote, owner);
                return UserSettingAttachResult.Repaired;
            }

            MajDebug.LogWarning(LOG_TAG, "Failed to store the user settings of the account, the local settings stay in use for this session.");
            MajEnv.SetUserSettingSource(UserSettingSource.Local, null);
            return UserSettingAttachResult.Degraded;
        }

        /// <summary>Returns to the local settings after the account is left, so the account copy never reaches settings.json.</summary>
        /// <param name="token">The token canceling the last upload.</param>
        /// <returns>A task completing when the local settings are in use again.</returns>
        internal static async ValueTask DetachAsync(CancellationToken token = default)
        {
            MajEnv.SetUserSettingSource(UserSettingSource.Switching, null);
            await FlushAsync(s_detachFlushTimeout, token);
            MajEnv.RestoreLocalUserSettings();
            Reset();
        }

        /// <summary>Queues the user settings for upload after a save; never blocks the caller and never throws.</summary>
        /// <param name="json">The settings document to upload; an empty document is ignored.</param>
        internal static void QueuePush(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return;
            }
            lock (s_pushGate)
            {
                s_pendingJson = json;
                s_pendingVersion++;
                if (s_pushLoop is null || s_pushLoop.IsCompleted)
                {
                    s_pushLoop = Task.Run(PushLoopAsync);
                }
            }
        }

        /// <summary>Uploads the pending settings before the process exits, waiting at most the quit timeout.</summary>
        internal static void FlushOnQuit()
        {
            try
            {
                var task = FlushAsync(TimeSpan.FromMilliseconds(QuitFlushTimeoutMilliseconds)).AsTask();
                if (!task.Wait(QuitFlushTimeoutMilliseconds))
                {
                    MajDebug.LogWarning(LOG_TAG, "The last user settings were not uploaded before the game quit.");
                }
            }
            catch (Exception e)
            {
                MajDebug.LogException(LOG_TAG, e);
            }
        }

        /// <summary>Uploads the pending settings within a time budget, so leaving the account loses no change.</summary>
        /// <param name="timeout">The longest time this call may spend on uploads.</param>
        /// <param name="token">The token canceling the upload.</param>
        /// <returns>A task completing when nothing is pending or the budget is spent.</returns>
        internal static async ValueTask FlushAsync(TimeSpan timeout, CancellationToken token = default)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                string? json;
                lock (s_pushGate)
                {
                    json = s_pendingJson;
                    s_pendingJson = null;
                }
                if (json is null)
                {
                    return;
                }
                var owner = MajEnv.UserSettingOwner;
                if (owner is null || DateTime.UtcNow >= deadline)
                {
                    return;
                }
                if (!await StoreAsync(owner, json, token))
                {
                    MajDebug.LogWarning(LOG_TAG, "Failed to upload the pending user settings.");
                }
            }
        }

        /// <summary>Clears the pending upload and restores the shared API, so a session starts from a clean state.</summary>
        internal static void Reset()
        {
            lock (s_pushGate)
            {
                s_pendingJson = null;
                s_pendingVersion++;
            }
            s_api = OnlineUserSettingApi.Shared;
            PushDebounceMilliseconds = DefaultPushDebounceMilliseconds;
        }

        /// <summary>Drains the pending document, merging saves that arrive while an upload is running.</summary>
        /// <returns>A task completing when nothing is pending; it never faults.</returns>
        private static async Task PushLoopAsync()
        {
            try
            {
                while (true)
                {
                    string? json;
                    int version;
                    lock (s_pushGate)
                    {
                        json = s_pendingJson;
                        version = s_pendingVersion;
                    }
                    if (json is null)
                    {
                        return;
                    }
                    var debounce = PushDebounceMilliseconds;
                    if (debounce > 0)
                    {
                        await Task.Delay(debounce).ConfigureAwait(false);
                    }
                    lock (s_pushGate)
                    {
                        if (version != s_pendingVersion)
                        {
                            // A newer save arrived while waiting, so it gets its own delay and the newer document is sent.
                            continue;
                        }
                        s_pendingJson = null;
                    }
                    var owner = MajEnv.UserSettingOwner;
                    if (owner is null)
                    {
                        return;
                    }
                    if (!await StoreAsync(owner, json, CancellationToken.None).ConfigureAwait(false))
                    {
                        MajDebug.LogWarning(LOG_TAG, "Failed to upload the user settings after a save; the next save uploads them again.");
                    }
                }
            }
            catch (Exception e)
            {
                MajDebug.LogException(LOG_TAG, e);
            }
        }

        /// <summary>Uploads one document, reporting an unexpected failure instead of throwing it to the caller.</summary>
        /// <param name="owner">The endpoint owning the account.</param>
        /// <param name="json">The document to upload.</param>
        /// <param name="token">The token canceling the upload.</param>
        /// <returns>Whether the server accepted the document.</returns>
        private static async ValueTask<bool> StoreAsync(ApiEndpoint owner, string json, CancellationToken token)
        {
            try
            {
                return await s_api.StoreAsync(owner, json, token);
            }
            catch (Exception e)
            {
                MajDebug.LogException(LOG_TAG, e);
                return false;
            }
        }
    }
}
