using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.Buffers;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Buffers
{
    /// <summary>Covers shared lifetimes, bounded state reuse and concurrent disposal of pooled tokens.</summary>
    public class PooledDisposableTests
    {
        /// <summary>Checks that an uninitialized token is disposed and safe to dispose repeatedly.</summary>
        /// <param name="useConstructor">Whether to use the parameterless value-type constructor.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void Default_IsDisposed(bool useConstructor)
        {
            var token = useConstructor ? new PooledDisposable() : default;

            Assert.That(token.IsDisposed, Is.True);
            Assert.That(token.TryDispose(), Is.False);
            token.Dispose();
            token.Dispose();
            Assert.That(token.IsDisposed, Is.True);
            Assert.Throws<ObjectDisposedException>(() => token.ThrowIfDisposed());
        }

        /// <summary>Checks that separate rentals have independent live states.</summary>
        [Test]
        public void Rent_ReturnsIndependentLifetimes()
        {
            using var first = PooledDisposable.Rent();
            using var second = PooledDisposable.Rent();

            Assert.That(first.IsDisposed, Is.False);
            Assert.That(second.IsDisposed, Is.False);
            first.ThrowIfDisposed();
            first.Dispose();

            Assert.That(first.IsDisposed, Is.True);
            Assert.That(second.IsDisposed, Is.False);
            second.ThrowIfDisposed();
        }

        /// <summary>Checks that disposing a struct copy ends the lifetime observed by every copy.</summary>
        [Test]
        public void Dispose_Copy_DisposesAllCopies()
        {
            using var original = PooledDisposable.Rent();
            var copy = original;
            var anotherCopy = copy;

            copy.Dispose();

            Assert.That(original.IsDisposed, Is.True);
            Assert.That(copy.IsDisposed, Is.True);
            Assert.That(anotherCopy.IsDisposed, Is.True);
            Assert.That(original.TryDispose(), Is.False);
            Assert.That(anotherCopy.TryDispose(), Is.False);
        }

        /// <summary>Checks that distinct struct types can embed the same token to share a lifetime.</summary>
        [Test]
        public void Dispose_DifferentStructTypes_ShareState()
        {
            using var token = PooledDisposable.Rent();
            var owner = new BufferOwner(token);
            var view = new BufferView(token);

            Assert.That(owner.IsDisposed, Is.False);
            Assert.That(view.IsDisposed, Is.False);
            Assert.That(owner.TryDispose(), Is.True);

            Assert.That(view.IsDisposed, Is.True);
            Assert.That(owner.IsDisposed, Is.True);
            Assert.That(token.IsDisposed, Is.True);
        }

        /// <summary>Checks that disposal through a boxed interface updates the original struct's state.</summary>
        [Test]
        public void Dispose_BoxedInterface_DisposesOriginal()
        {
            using var token = PooledDisposable.Rent();
            var boxed = (IDisposable)token;

            boxed.Dispose();
            boxed.Dispose();

            Assert.That(token.IsDisposed, Is.True);
            Assert.That(token.TryDispose(), Is.False);
        }

        /// <summary>Checks that only the first disposal claims responsibility for owner cleanup.</summary>
        [Test]
        public void TryDispose_RepeatedCalls_ReturnsTrueOnlyOnce()
        {
            using var token = PooledDisposable.Rent();

            Assert.That(token.TryDispose(), Is.True);
            Assert.That(token.TryDispose(), Is.False);
            token.Dispose();
            Assert.That(token.TryDispose(), Is.False);
            Assert.That(token.IsDisposed, Is.True);
        }

        /// <summary>Checks the exception's owner name, including the default and an explicit empty name.</summary>
        /// <param name="objectName">The owner name supplied by the caller.</param>
        [TestCase(null)]
        [TestCase("BufferOwner")]
        [TestCase("")]
        public void ThrowIfDisposed_UsesRequestedObjectName(string? objectName)
        {
            using var token = PooledDisposable.Rent();
            token.ThrowIfDisposed(objectName);
            token.Dispose();

            var exception = Assert.Throws<ObjectDisposedException>(() => token.ThrowIfDisposed(objectName));

            Assert.That(exception!.ObjectName, Is.EqualTo(objectName ?? nameof(PooledDisposable)));
        }

        /// <summary>Checks that the default token uses the token type name in its disposal exception.</summary>
        [Test]
        public void ThrowIfDisposed_Default_UsesTokenName()
        {
            var token = default(PooledDisposable);

            var exception = Assert.Throws<ObjectDisposedException>(() => token.ThrowIfDisposed());

            Assert.That(exception!.ObjectName, Is.EqualTo(nameof(PooledDisposable)));
        }

        /// <summary>Checks that an old copy cannot revive or dispose a subsequent lease of the same state.</summary>
        [Test]
        public void Rent_ReusedState_KeepsOldCopiesDisposed()
        {
            using var previous = PooledDisposable.Rent();
            var stale = previous;
            var state = PrivateMember.GetField<object>(previous, "_state");
            previous.Dispose();

            using var current = PooledDisposable.Rent();

            Assert.That(PrivateMember.GetField<object>(current, "_state"), Is.SameAs(state));
            Assert.That(stale.IsDisposed, Is.True);
            Assert.That(stale.TryDispose(), Is.False);
            stale.Dispose();
            Assert.Throws<ObjectDisposedException>(() => stale.ThrowIfDisposed());
            Assert.That(current.IsDisposed, Is.False);

            using var independent = PooledDisposable.Rent();
            Assert.That(PrivateMember.GetField<object>(independent, "_state"), Is.Not.SameAs(state));
            Assert.That(current.TryDispose(), Is.True);
            Assert.That(independent.IsDisposed, Is.False);
        }

        /// <summary>Checks that a burst beyond the pool capacity never shares active states or revives old leases.</summary>
        [Test]
        public void Dispose_BeyondCapacity_KeepsPoolBounded()
        {
            var tokens = new PooledDisposable[257];
            var subsequent = new PooledDisposable[tokens.Length];
            try
            {
                for (var i = 0; i < tokens.Length; i++)
                {
                    tokens[i] = PooledDisposable.Rent();
                }
                foreach (var token in tokens)
                {
                    token.Dispose();
                }

                Assert.That(PrivateMember.GetStaticField<int>(typeof(PooledDisposable), "s_poolCount"), Is.EqualTo(256));

                var activeStates = new HashSet<object>();
                for (var i = 0; i < subsequent.Length; i++)
                {
                    subsequent[i] = PooledDisposable.Rent();
                    Assert.That(activeStates.Add(PrivateMember.GetField<object>(subsequent[i], "_state")), Is.True);
                    Assert.That(tokens[i].IsDisposed, Is.True);
                    Assert.That(tokens[i].TryDispose(), Is.False);
                }
                foreach (var token in subsequent)
                {
                    Assert.That(token.IsDisposed, Is.False);
                }
            }
            finally
            {
                foreach (var token in tokens)
                {
                    token.Dispose();
                }
                foreach (var token in subsequent)
                {
                    token.Dispose();
                }
            }
        }

        /// <summary>Checks that the final version is usable once and then retired instead of wrapping.</summary>
        [Test]
        public void Dispose_FinalVersion_RetiresState()
        {
            // Reserve one pool slot so returning the isolated boundary state always makes it reusable.
            using var reserved = PooledDisposable.Rent();
            using var penultimate = CreateTokenAtVersion(long.MaxValue - 1);
            var state = PrivateMember.GetField<object>(penultimate, "_state");
            Assert.That(penultimate.TryDispose(), Is.True);

            using var final = PooledDisposable.Rent();

            Assert.That(PrivateMember.GetField<object>(final, "_state"), Is.SameAs(state));
            Assert.That(PrivateMember.GetField<long>(final, "_version"), Is.EqualTo(long.MaxValue));
            Assert.That(final.IsDisposed, Is.False);
            Assert.That(penultimate.IsDisposed, Is.True);
            Assert.That(penultimate.TryDispose(), Is.False);
            var pooledBefore = PrivateMember.GetStaticField<int>(typeof(PooledDisposable), "s_poolCount");

            Assert.That(final.TryDispose(), Is.True);

            Assert.That(final.IsDisposed, Is.True);
            Assert.That(final.TryDispose(), Is.False);
            Assert.That(PrivateMember.GetStaticField<int>(typeof(PooledDisposable), "s_poolCount"), Is.EqualTo(pooledBefore));
            using var next = PooledDisposable.Rent();
            Assert.That(PrivateMember.GetField<object>(next, "_state"), Is.Not.SameAs(state));
            Assert.That(next.IsDisposed, Is.False);
        }

        /// <summary>Checks that concurrent copies elect exactly one disposer for each lifetime.</summary>
        [Test]
        public void TryDispose_ConcurrentCopies_HaveOneWinner()
        {
            for (var iteration = 0; iteration < 32; iteration++)
            {
                using var token = PooledDisposable.Rent();
                var winners = 0;
                Parallel.For(0, 128, _ =>
                {
                    var copy = token;
                    if (copy.TryDispose())
                    {
                        Interlocked.Increment(ref winners);
                    }
                });

                Assert.That(winners, Is.EqualTo(1));
                Assert.That(token.IsDisposed, Is.True);
            }
        }

        /// <summary>Checks that concurrent rentals and stale disposal attempts preserve every new lifetime.</summary>
        [Test]
        public void RentAndDispose_Concurrently_KeepLifetimesIndependent()
        {
            var failures = 0;
            Parallel.For(0, 8, _ =>
            {
                var stale = default(PooledDisposable);
                for (var iteration = 0; iteration < 512; iteration++)
                {
                    using var token = PooledDisposable.Rent();
                    if (token.IsDisposed || !stale.IsDisposed || stale.TryDispose())
                    {
                        Interlocked.Increment(ref failures);
                    }
                    stale.Dispose();
                    if (token.IsDisposed || !token.TryDispose() || token.TryDispose())
                    {
                        Interlocked.Increment(ref failures);
                    }
                    stale = token;
                }
            });

            Assert.That(failures, Is.EqualTo(0));
        }

        /// <summary>Creates an isolated live token to exercise version exhaustion without altering shared pool fields.</summary>
        /// <param name="version">The positive version to give the isolated state and its token.</param>
        /// <returns>A token backed by a newly allocated private state.</returns>
        private static PooledDisposable CreateTokenAtVersion(long version)
        {
            var stateType = typeof(PooledDisposable).GetNestedType("State", BindingFlags.NonPublic)!;
            var state = Activator.CreateInstance(stateType, true)!;
            PrivateMember.SetField(state, "_version", version);
            return (PooledDisposable)Activator.CreateInstance(
                typeof(PooledDisposable),
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new object[] { state, version },
                null)!;
        }

        /// <summary>Represents an owner that claims cleanup through a shared disposal token.</summary>
        private readonly struct BufferOwner
        {
            /// <summary>The lifetime shared with related views.</summary>
            private readonly PooledDisposable _lifetime;

            /// <summary>Gets whether the shared lifetime has ended.</summary>
            public bool IsDisposed
            {
                get
                {
                    return _lifetime.IsDisposed;
                }
            }

            /// <summary>Creates an owner with the supplied lifetime.</summary>
            /// <param name="lifetime">The token copied into this owner.</param>
            public BufferOwner(PooledDisposable lifetime)
            {
                _lifetime = lifetime;
            }

            /// <summary>Claims responsibility for ending the shared lifetime.</summary>
            /// <returns>True only when this owner wins disposal.</returns>
            public bool TryDispose()
            {
                return _lifetime.TryDispose();
            }
        }

        /// <summary>Represents a distinct struct type observing its owner's shared lifetime.</summary>
        private readonly struct BufferView
        {
            /// <summary>The lifetime copied from the owner.</summary>
            private readonly PooledDisposable _lifetime;

            /// <summary>Gets whether the shared lifetime has ended.</summary>
            public bool IsDisposed
            {
                get
                {
                    return _lifetime.IsDisposed;
                }
            }

            /// <summary>Creates a view with the supplied lifetime.</summary>
            /// <param name="lifetime">The token copied into this view.</param>
            public BufferView(PooledDisposable lifetime)
            {
                _lifetime = lifetime;
            }
        }
    }
}
