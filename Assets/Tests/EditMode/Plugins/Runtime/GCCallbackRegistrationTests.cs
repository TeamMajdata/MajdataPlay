using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MajdataPlay.Runtime;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Runtime
{
    /// <summary>
    /// Covers <see cref="GCCallbackRegistration"/>. Each test runs against an empty callback list: the registered list is
    /// detached in SetUp and reattached in TearDown, so production callbacks are neither invoked nor lost.
    /// </summary>
    public class GCCallbackRegistrationTests
    {
        /// <summary>The detached head of the production callback list.</summary>
        private object? _savedHead;

        /// <summary>The detached tail of the production callback list.</summary>
        private object? _savedTail;

        /// <summary>Gets the lock that guards the callback list.</summary>
        private static object ListLock => PrivateMember.GetStaticField<object>(typeof(GCCallbackRegistration), "_lock");

        /// <summary>Detaches the production callback list.</summary>
        [SetUp]
        public void SetUp()
        {
            lock (ListLock)
            {
                _savedHead = PrivateMember.GetStaticField<object?>(typeof(GCCallbackRegistration), "_callbackTaskHead");
                _savedTail = PrivateMember.GetStaticField<object?>(typeof(GCCallbackRegistration), "_callbackTaskTail");
                PrivateMember.SetStaticField(typeof(GCCallbackRegistration), "_callbackTaskHead", null);
                PrivateMember.SetStaticField(typeof(GCCallbackRegistration), "_callbackTaskTail", null);
            }
        }

        /// <summary>Reattaches the production callback list and clears the shutdown flag.</summary>
        [TearDown]
        public void TearDown()
        {
            lock (ListLock)
            {
                PrivateMember.SetStaticField(typeof(GCCallbackRegistration), "_callbackTaskHead", _savedHead);
                PrivateMember.SetStaticField(typeof(GCCallbackRegistration), "_callbackTaskTail", _savedTail);
            }
            PrivateMember.SetStaticField(typeof(GCCallbackRegistration), "_isShuttingDown", false);
        }

        /// <summary>Runs one round of the registered callbacks, as the finalizer source does after a collection.</summary>
        private static void RunCallbacks()
        {
            PrivateMember.InvokeStatic(typeof(GCCallbackRegistration), "OnGCCallback");
        }

        /// <summary>Checks that null callbacks are rejected by both overloads.</summary>
        [Test]
        public void Register_NullCallback_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => GCCallbackRegistration.RegisterGCCallback((GCCallback)null!));
            Assert.Throws<ArgumentNullException>(() => GCCallbackRegistration.RegisterGCCallback((GCCallbackWithObject)null!, new object()));
        }

        /// <summary>Checks that running callbacks with nothing registered does nothing.</summary>
        [Test]
        public void OnGCCallback_EmptyList_DoesNothing()
        {
            Assert.DoesNotThrow(RunCallbacks);
        }

        /// <summary>Checks that a callback returning true runs on every round and stays registered.</summary>
        [Test]
        public void Callback_ReturningTrue_StaysRegistered()
        {
            var calls = 0;
            GCCallbackRegistration.RegisterGCCallback(() =>
            {
                calls++;
                return true;
            });

            RunCallbacks();
            RunCallbacks();

            Assert.That(calls, Is.EqualTo(2));
        }

        /// <summary>Checks that a callback returning false runs once and is then removed.</summary>
        [Test]
        public void Callback_ReturningFalse_IsRemoved()
        {
            var calls = 0;
            GCCallbackRegistration.RegisterGCCallback(() =>
            {
                calls++;
                return false;
            });

            RunCallbacks();
            RunCallbacks();

            Assert.That(calls, Is.EqualTo(1));
        }

        /// <summary>Checks that two registered callbacks both run.</summary>
        [Test]
        public void TwoCallbacks_BothRun()
        {
            var first = 0;
            var second = 0;
            GCCallbackRegistration.RegisterGCCallback(() =>
            {
                first++;
                return true;
            });
            GCCallbackRegistration.RegisterGCCallback(() =>
            {
                second++;
                return true;
            });

            RunCallbacks();

            Assert.That(first, Is.EqualTo(1));
            Assert.That(second, Is.EqualTo(1));
        }

        /// <summary>Checks that every one of several registered callbacks runs.</summary>
        [Test]
        public void ThreeCallbacks_AllRun()
        {
            var calls = new int[3];
            for (var i = 0; i < calls.Length; i++)
            {
                var index = i;
                GCCallbackRegistration.RegisterGCCallback(() =>
                {
                    calls[index]++;
                    return true;
                });
            }

            RunCallbacks();

            Assert.That(calls, Is.EqualTo(new[] { 1, 1, 1 }));
        }

        /// <summary>Checks that an object callback receives its live object and is removed once it returns false.</summary>
        [Test]
        public void ObjectCallback_ReceivesObjectUntilItReturnsFalse()
        {
            var target = new object();
            var received = 0;
            GCCallbackRegistration.RegisterGCCallback(o =>
            {
                Assert.That(o, Is.SameAs(target));
                received++;
                return received < 2;
            }, target);

            RunCallbacks();
            RunCallbacks();
            RunCallbacks();

            Assert.That(received, Is.EqualTo(2));
            GC.KeepAlive(target);
        }

        /// <summary>Checks that an object callback whose object has been collected is dropped without running.</summary>
        [Test]
        public void ObjectCallback_CollectedObject_IsDropped()
        {
            var calls = new int[1];
            var weak = RegisterForTemporaryObject(calls);
            for (var i = 0; i < 3 && weak.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            Assume.That(weak.IsAlive, Is.False, "The conservative collector kept the temporary object alive.");

            RunCallbacks();
            RunCallbacks();

            Assert.That(calls[0], Is.EqualTo(0));
        }

        /// <summary>Registers an object callback for an object that becomes unreachable when this method returns.</summary>
        /// <param name="calls">Counts the callback invocations.</param>
        /// <returns>A weak reference to the temporary object.</returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference RegisterForTemporaryObject(int[] calls)
        {
            var temporary = new object();
            GCCallbackRegistration.RegisterGCCallback(_ =>
            {
                calls[0]++;
                return true;
            }, temporary);
            return new WeakReference(temporary);
        }

        /// <summary>Checks how a throwing callback is handled: rethrown in debug builds, swallowed and kept otherwise.</summary>
        [Test]
        public void Callback_Throwing_DependsOnBuildConfiguration()
        {
            var calls = 0;
            GCCallbackRegistration.RegisterGCCallback(() =>
            {
                calls++;
                throw new InvalidOperationException("callback failure");
            });

#if DEBUG
            var exception = Assert.Throws<TargetInvocationException>(RunCallbacks);
            Assert.That(exception!.InnerException, Is.InstanceOf<InvalidOperationException>());
            Assert.That(calls, Is.EqualTo(1));
#else
            RunCallbacks();
            RunCallbacks();
            Assert.That(calls, Is.EqualTo(2));
#endif
        }

        /// <summary>Checks how a throwing object callback is handled: rethrown in debug builds, swallowed and kept otherwise.</summary>
        [Test]
        public void ObjectCallback_Throwing_DependsOnBuildConfiguration()
        {
            var target = new object();
            var calls = 0;
            GCCallbackRegistration.RegisterGCCallback(_ =>
            {
                calls++;
                throw new InvalidOperationException("callback failure");
            }, target);

#if DEBUG
            Assert.Throws<TargetInvocationException>(RunCallbacks);
            Assert.That(calls, Is.EqualTo(1));
#else
            RunCallbacks();
            RunCallbacks();
            Assert.That(calls, Is.EqualTo(2));
#endif
            GC.KeepAlive(target);
        }

        /// <summary>Checks that a real garbage collection runs the registered callbacks through the finalizer source.</summary>
        [Test]
        public void GarbageCollection_RunsCallbacks()
        {
            var called = false;
            GCCallbackRegistration.RegisterGCCallback(() =>
            {
                called = true;
                return false;
            });

            for (var i = 0; i < 3 && !called; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            Assert.That(called, Is.True);
        }

        /// <summary>Checks that the finalizer entry point runs the callbacks and re-registers its source.</summary>
        [Test]
        public void RunCallbackAndReRegister_RunsCallbacks()
        {
            var calls = 0;
            GCCallbackRegistration.RegisterGCCallback(() =>
            {
                calls++;
                return false;
            });

            PrivateMember.InvokeStatic(typeof(GCCallbackRegistration), "RunCallbackAndReRegister", new object());

            Assert.That(calls, Is.EqualTo(1));
        }

        /// <summary>Checks that callbacks stop running once the application reports that it is quitting.</summary>
        [Test]
        public void RunCallbackAndReRegister_AfterQuitting_DoesNothing()
        {
            var calls = 0;
            GCCallbackRegistration.RegisterGCCallback(() =>
            {
                calls++;
                return true;
            });

            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            var type = typeof(GCCallbackRegistration);
            var quitting = type.GetMethod("OnApplicationQuitting", flags)!;
            var run = type.GetMethod("RunCallbackAndReRegister", flags)!;
            var shuttingDown = type.GetField("_isShuttingDown", flags)!;
            var arguments = new object[] { new object() };
            bool flagAfterQuitting;

            // The flag also stops the real finalizer source from re-registering, so it is cleared immediately.
            try
            {
                quitting.Invoke(null, null);
                flagAfterQuitting = (bool)shuttingDown.GetValue(null);
                run.Invoke(null, arguments);
            }
            finally
            {
                shuttingDown.SetValue(null, false);
            }

            Assert.That(flagAfterQuitting, Is.True);
            Assert.That(calls, Is.EqualTo(0));
        }
    }
}
