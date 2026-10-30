using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.Android;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Base
{
    /// <summary>Verifies that Android storage startup waits for the final permission decisions.</summary>
    public class AndroidStorageStartupTests
    {
        /// <summary>Checks the device and target SDK combinations that permit legacy external storage.</summary>
        /// <param name="sdkVersion">The Android SDK level of the device.</param>
        /// <param name="targetSdkVersion">The target SDK level declared by the installed application.</param>
        /// <param name="expected">Whether legacy storage access should be selected.</param>
        [TestCase(29, 29, true)]
        [TestCase(29, 36, true)]
        [TestCase(30, 29, true)]
        [TestCase(36, 29, true)]
        [TestCase(30, 30, false)]
        [TestCase(36, 36, false)]
        public void SupportsLegacyStorage_RespectsDeviceAndTargetSdk(int sdkVersion, int targetSdkVersion, bool expected)
        {
            Assert.That(AndroidStorageStartup.SupportsLegacyStorage(sdkVersion, targetSdkVersion), Is.EqualTo(expected));
        }

        /// <summary>Checks that read and write requests wait for their callbacks and run sequentially.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RequestPermissionsAsync_WaitsForReadAndWriteResponses()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var permissions = new PermissionStub();
                var operation = AndroidStorageStartup.RequestPermissionsAsync(
                    permissions.HasPermission, permissions.RequestPermissionAsync, CancellationToken.None);

                Assert.That(permissions.RequestedPermissions, Is.EqualTo(new[] { Permission.ExternalStorageRead }));
                Assert.That(operation.Status, Is.EqualTo(UniTaskStatus.Pending));

                await UniTask.Yield();

                Assert.That(operation.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(permissions.RequestedPermissions, Is.EqualTo(new[] { Permission.ExternalStorageRead }));
                permissions.CompletePermission(Permission.ExternalStorageRead, true);

                Assert.That(permissions.RequestedPermissions,
                    Is.EqualTo(new[] { Permission.ExternalStorageRead, Permission.ExternalStorageWrite }));
                Assert.That(operation.Status, Is.EqualTo(UniTaskStatus.Pending));

                await UniTask.Yield();

                Assert.That(operation.Status, Is.EqualTo(UniTaskStatus.Pending));
                permissions.CompletePermission(Permission.ExternalStorageWrite, true);

                Assert.That(await operation, Is.True);
            });
        }

        /// <summary>Checks that background permission responses resume Android queries and requests on the main thread.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RequestPermissionsAsync_BackgroundResponses_ResumeOnMainThread()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var mainThreadId = Thread.CurrentThread.ManagedThreadId;
                var readResponseThreadId = 0;
                var writeResponseThreadId = 0;
                var permissions = new PermissionStub();
                var operation = AndroidStorageStartup.RequestPermissionsAsync(
                    permissions.HasPermission, permissions.RequestPermissionAsync, CancellationToken.None);

                await Task.Run(() =>
                {
                    readResponseThreadId = Thread.CurrentThread.ManagedThreadId;
                    permissions.CompletePermission(Permission.ExternalStorageRead, true);
                });
                await UniTask.SwitchToMainThread();
                await UniTask.Yield();

                Assert.That(readResponseThreadId, Is.Not.EqualTo(mainThreadId));
                Assert.That(permissions.RequestedPermissions,
                    Is.EqualTo(new[] { Permission.ExternalStorageRead, Permission.ExternalStorageWrite }));
                Assert.That(permissions.RequestThreadIds, Is.All.EqualTo(mainThreadId));
                Assert.That(operation.Status, Is.EqualTo(UniTaskStatus.Pending));

                await Task.Run(() =>
                {
                    writeResponseThreadId = Thread.CurrentThread.ManagedThreadId;
                    permissions.CompletePermission(Permission.ExternalStorageWrite, true);
                });
                await UniTask.SwitchToMainThread();

                Assert.That(await operation, Is.True);
                Assert.That(writeResponseThreadId, Is.Not.EqualTo(mainThreadId));
                Assert.That(permissions.QueryThreadIds, Is.All.EqualTo(mainThreadId));
                Assert.That(permissions.RequestThreadIds, Is.All.EqualTo(mainThreadId));
            });
        }

        /// <summary>Checks that existing read and write permissions avoid opening permission dialogs.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RequestPermissionsAsync_AlreadyGranted_DoesNotRequest()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var permissions = new PermissionStub(Permission.ExternalStorageRead, Permission.ExternalStorageWrite);

                var granted = await AndroidStorageStartup.RequestPermissionsAsync(
                    permissions.HasPermission, permissions.RequestPermissionAsync, CancellationToken.None);

                Assert.That(granted, Is.True);
                Assert.That(permissions.RequestedPermissions, Is.Empty);
            });
        }

        /// <summary>Checks that an existing read permission leaves only the write permission to request.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RequestPermissionsAsync_ReadGranted_RequestsOnlyWrite()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var permissions = new PermissionStub(Permission.ExternalStorageRead);
                var operation = AndroidStorageStartup.RequestPermissionsAsync(
                    permissions.HasPermission, permissions.RequestPermissionAsync, CancellationToken.None);

                Assert.That(permissions.RequestedPermissions, Is.EqualTo(new[] { Permission.ExternalStorageWrite }));
                Assert.That(operation.Status, Is.EqualTo(UniTaskStatus.Pending));
                permissions.CompletePermission(Permission.ExternalStorageWrite, true);

                Assert.That(await operation, Is.True);
            });
        }

        /// <summary>Checks that an existing write permission leaves only the read permission to request.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RequestPermissionsAsync_WriteGranted_RequestsOnlyRead()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var permissions = new PermissionStub(Permission.ExternalStorageWrite);
                var operation = AndroidStorageStartup.RequestPermissionsAsync(
                    permissions.HasPermission, permissions.RequestPermissionAsync, CancellationToken.None);

                Assert.That(permissions.RequestedPermissions, Is.EqualTo(new[] { Permission.ExternalStorageRead }));
                permissions.CompletePermission(Permission.ExternalStorageRead, true);

                Assert.That(await operation, Is.True);
            });
        }

        /// <summary>Checks that denying read access stops startup without requesting write access.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RequestPermissionsAsync_ReadDenied_DoesNotRequestWrite()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var permissions = new PermissionStub();
                var operation = AndroidStorageStartup.RequestPermissionsAsync(
                    permissions.HasPermission, permissions.RequestPermissionAsync, CancellationToken.None);
                permissions.CompletePermission(Permission.ExternalStorageRead, false);

                Assert.That(await operation, Is.False);
                Assert.That(permissions.RequestedPermissions, Is.EqualTo(new[] { Permission.ExternalStorageRead }));
            });
        }

        /// <summary>Checks that denying write access rejects the external storage directory.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RequestPermissionsAsync_WriteDenied_ReturnsFalse()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var permissions = new PermissionStub();
                var operation = AndroidStorageStartup.RequestPermissionsAsync(
                    permissions.HasPermission, permissions.RequestPermissionAsync, CancellationToken.None);
                permissions.CompletePermission(Permission.ExternalStorageRead, true);
                permissions.CompletePermission(Permission.ExternalStorageWrite, false);

                Assert.That(await operation, Is.False);
                Assert.That(permissions.RequestedPermissions,
                    Is.EqualTo(new[] { Permission.ExternalStorageRead, Permission.ExternalStorageWrite }));
            });
        }

        /// <summary>Checks that a successful callback cannot replace querying the final permission state.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RequestPermissionsAsync_GrantCallbackWithoutPermission_ReturnsFalse()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var permissions = new PermissionStub(Permission.ExternalStorageRead);
                var operation = AndroidStorageStartup.RequestPermissionsAsync(
                    permissions.HasPermission, permissions.RequestPermissionAsync, CancellationToken.None);
                permissions.CompletePermission(Permission.ExternalStorageWrite, true, updatePermission: false);

                Assert.That(await operation, Is.False);
            });
        }

        /// <summary>Checks that the final query also detects read permission revoked while write access is pending.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RequestPermissionsAsync_ReadRevokedBeforeWriteResponse_ReturnsFalse()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var permissions = new PermissionStub(Permission.ExternalStorageRead);
                var operation = AndroidStorageStartup.RequestPermissionsAsync(
                    permissions.HasPermission, permissions.RequestPermissionAsync, CancellationToken.None);
                permissions.RevokePermission(Permission.ExternalStorageRead);
                permissions.CompletePermission(Permission.ExternalStorageWrite, true);

                Assert.That(await operation, Is.False);
            });
        }

        /// <summary>Checks that cancellation before startup prevents permission requests.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RequestPermissionsAsync_AlreadyCanceled_DoesNotRequest()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var source = new CancellationTokenSource();
                source.Cancel();
                var permissions = new PermissionStub();
                var operation = AndroidStorageStartup.RequestPermissionsAsync(
                    permissions.HasPermission, permissions.RequestPermissionAsync, source.Token);

                Assert.That(operation.Status, Is.EqualTo(UniTaskStatus.Canceled));
                await AssertCanceledAsync(operation);

                Assert.That(permissions.RequestedPermissions, Is.Empty);
            });
        }

        /// <summary>Checks that preexisting permission grants do not hide a canceled startup.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RequestPermissionsAsync_AlreadyCanceledAndGranted_ThrowsCancellation()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var source = new CancellationTokenSource();
                source.Cancel();
                var permissions = new PermissionStub(Permission.ExternalStorageRead, Permission.ExternalStorageWrite);
                var operation = AndroidStorageStartup.RequestPermissionsAsync(
                    permissions.HasPermission, permissions.RequestPermissionAsync, source.Token);

                await AssertCanceledAsync(operation);

                Assert.That(permissions.RequestedPermissions, Is.Empty);
            });
        }

        /// <summary>Checks that cancellation while read access is pending stops the write request despite a late grant.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RequestPermissionsAsync_CanceledDuringRead_IgnoresLateGrant()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var source = new CancellationTokenSource();
                var permissions = new PermissionStub();
                var operation = AndroidStorageStartup.RequestPermissionsAsync(
                    permissions.HasPermission, permissions.RequestPermissionAsync, source.Token);

                Assert.That(permissions.LastRequestCancellationToken, Is.EqualTo(source.Token));
                source.Cancel();

                Assert.That(operation.Status, Is.EqualTo(UniTaskStatus.Canceled));
                permissions.CompletePermission(Permission.ExternalStorageRead, true);
                await AssertCanceledAsync(operation);

                Assert.That(permissions.RequestedPermissions, Is.EqualTo(new[] { Permission.ExternalStorageRead }));
            });
        }

        /// <summary>Checks that cancellation while write access is pending cannot finish as a successful authorization.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RequestPermissionsAsync_CanceledDuringWrite_DoesNotReportGranted()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var source = new CancellationTokenSource();
                var permissions = new PermissionStub(Permission.ExternalStorageRead);
                var operation = AndroidStorageStartup.RequestPermissionsAsync(
                    permissions.HasPermission, permissions.RequestPermissionAsync, source.Token);
                source.Cancel();

                Assert.That(operation.Status, Is.EqualTo(UniTaskStatus.Canceled));
                permissions.CompletePermission(Permission.ExternalStorageWrite, true);
                await AssertCanceledAsync(operation);
            });
        }

        /// <summary>Checks that an operation throws cancellation instead of returning a permission decision.</summary>
        /// <param name="operation">The pending or canceled permission operation.</param>
        /// <returns>The asynchronous assertion operation.</returns>
        private static async UniTask AssertCanceledAsync(UniTask<bool> operation)
        {
            try
            {
                await operation;
                Assert.Fail("Canceled storage startup must throw OperationCanceledException.");
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>Controls permission queries and callback completion without requesting Android permissions.</summary>
        private sealed class PermissionStub
        {
            /// <summary>The permission names currently granted by the simulated Android permission service.</summary>
            private readonly HashSet<string> _grantedPermissions;

            /// <summary>The permission responses that have not yet been delivered.</summary>
            private readonly Dictionary<string, UniTaskCompletionSource<bool>> _pendingRequests = new();

            /// <summary>The permission names requested in order.</summary>
            private readonly List<string> _requestedPermissions = new();

            /// <summary>The managed thread IDs used for permission queries in invocation order.</summary>
            private readonly List<int> _queryThreadIds = new();

            /// <summary>The managed thread IDs used for permission requests in invocation order.</summary>
            private readonly List<int> _requestThreadIds = new();

            /// <summary>Gets the permission names requested in order.</summary>
            public IReadOnlyList<string> RequestedPermissions => _requestedPermissions;

            /// <summary>Gets the managed thread IDs used for permission queries in invocation order.</summary>
            public IReadOnlyList<int> QueryThreadIds => _queryThreadIds;

            /// <summary>Gets the managed thread IDs used for permission requests in invocation order.</summary>
            public IReadOnlyList<int> RequestThreadIds => _requestThreadIds;

            /// <summary>Gets the cancellation token forwarded to the latest permission request.</summary>
            public CancellationToken LastRequestCancellationToken { get; private set; }

            /// <summary>Creates a controllable permission service with the specified initial grants.</summary>
            /// <param name="grantedPermissions">The permission names granted before startup.</param>
            public PermissionStub(params string[] grantedPermissions)
            {
                _grantedPermissions = new HashSet<string>(grantedPermissions);
            }

            /// <summary>Checks whether a permission is currently granted.</summary>
            /// <param name="permission">The Android permission name to query.</param>
            /// <returns>Whether the simulated permission service currently grants the permission.</returns>
            public bool HasPermission(string permission)
            {
                _queryThreadIds.Add(Thread.CurrentThread.ManagedThreadId);
                return _grantedPermissions.Contains(permission);
            }

            /// <summary>Records a request and waits until the test explicitly delivers its response.</summary>
            /// <param name="permission">The Android permission name to request.</param>
            /// <param name="cancellationToken">The token forwarded by storage startup.</param>
            /// <returns>The response supplied by the test; cancellation is deliberately handled by production startup.</returns>
            public UniTask<bool> RequestPermissionAsync(string permission, CancellationToken cancellationToken)
            {
                var response = new UniTaskCompletionSource<bool>();
                _pendingRequests.Add(permission, response);
                _requestedPermissions.Add(permission);
                _requestThreadIds.Add(Thread.CurrentThread.ManagedThreadId);
                LastRequestCancellationToken = cancellationToken;
                return response.Task;
            }

            /// <summary>Updates the queried permission state and delivers the pending permission callback.</summary>
            /// <param name="permission">The Android permission whose response should be delivered.</param>
            /// <param name="granted">Whether the simulated callback reports success.</param>
            /// <param name="updatePermission">Whether a successful callback also updates the queried permission state.</param>
            public void CompletePermission(string permission, bool granted, bool updatePermission = true)
            {
                if (granted && updatePermission)
                {
                    _grantedPermissions.Add(permission);
                }
                var response = _pendingRequests[permission];
                _pendingRequests.Remove(permission);
                // Publish all simulated permission state before releasing the startup continuation.
                response.TrySetResult(granted);
            }

            /// <summary>Revokes an existing permission without delivering a pending request response.</summary>
            /// <param name="permission">The Android permission name to revoke.</param>
            public void RevokePermission(string permission)
            {
                _grantedPermissions.Remove(permission);
            }
        }
    }
}
