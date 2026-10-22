#nullable enable
using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.IO
{
    /// <summary>Asserts on asynchronous failures without blocking the Unity main thread.</summary>
    internal static class AsyncAssert
    {
        /// <summary>Awaits an operation and asserts that it fails with an exception of the given type or a subtype.</summary>
        /// <typeparam name="TException">The expected exception type.</typeparam>
        /// <param name="action">The operation to await.</param>
        /// <returns>The caught exception.</returns>
        /// <exception cref="AssertionException">The operation completed or failed with another exception type.</exception>
        public static async Task<TException> ThrowsAsync<TException>(Func<Task> action) where TException : Exception
        {
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                Assert.That(exception, Is.InstanceOf<TException>(), exception.ToString());
                return (TException)exception;
            }
            Assert.Fail($"Expected {typeof(TException).Name}, but the operation completed.");
            throw new InvalidOperationException("Unreachable.");
        }
    }
}
