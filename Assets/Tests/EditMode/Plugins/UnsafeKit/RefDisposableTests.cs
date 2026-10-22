using System;
using MajdataPlay.UnsafeKit;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.UnsafeKit
{
    /// <summary>Covers <see cref="RefDisposable"/>, which disposes a disposable variable through a reference.</summary>
    public class RefDisposableTests
    {
        /// <summary>A disposable that counts how often it was disposed.</summary>
        private sealed class CountingDisposable : IDisposable
        {
            /// <summary>Gets the number of Dispose calls.</summary>
            public int DisposeCount { get; private set; }

            /// <summary>Counts the call.</summary>
            public void Dispose()
            {
                DisposeCount++;
            }
        }

        /// <summary>Checks that From keeps a reference to the variable and the requested flag.</summary>
        [Test]
        public void From_ReferencesVariable()
        {
            var first = new CountingDisposable();
            var second = new CountingDisposable();
            var instance = first;

            var disposable = RefDisposable.From(ref instance, true);
            instance = second;

            Assert.That(disposable.Instance, Is.SameAs(second));
            Assert.That(disposable.SetNullWhenDispose, Is.True);
        }

        /// <summary>Checks that disposing with the clear flag disposes the instance and clears the variable.</summary>
        [Test]
        public void Dispose_WithClearFlag_DisposesAndClears()
        {
            var target = new CountingDisposable();
            CountingDisposable? instance = target;

            var disposable = RefDisposable.From(ref instance, true);
            disposable.Dispose();

            Assert.That(target.DisposeCount, Is.EqualTo(1));
            Assert.That(instance, Is.Null);
        }

        /// <summary>Checks that disposing a null variable does nothing.</summary>
        [Test]
        public void Dispose_NullInstance_DoesNothing()
        {
            CountingDisposable? instance = null;

            var disposable = RefDisposable.From(ref instance);
            disposable.Dispose();

            Assert.That(instance, Is.Null);
        }

        /// <summary>Checks that disposing without the clear flag keeps the variable's value.</summary>
        [Test]
        public void Dispose_WithoutClearFlag_KeepsVariable()
        {
            var target = new CountingDisposable();
            var instance = target;

            var disposable = RefDisposable.From(ref instance, false);
            disposable.Dispose();

            Assert.That(target.DisposeCount, Is.EqualTo(1));
            Assert.That(instance, Is.SameAs(target));
        }
    }
}
