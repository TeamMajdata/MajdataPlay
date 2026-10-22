#nullable enable
using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.Net
{
    /// <summary>Asserts on asynchronous outcomes without blocking the Unity main thread.</summary>
    public static class NetAsyncAssert
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

        /// <summary>Awaits a task for at most the given time and asserts that it finished.</summary>
        /// <param name="task">The task to wait for.</param>
        /// <param name="timeoutMs">The maximum wait in milliseconds.</param>
        /// <param name="what">A description of the awaited operation for the failure message.</param>
        /// <returns>A task completing once the awaited task finished.</returns>
        /// <exception cref="AssertionException">The task did not finish in time.</exception>
        public static async Task CompletesWithin(Task task, int timeoutMs, string what)
        {
            var winner = await Task.WhenAny(task, Task.Delay(timeoutMs));
            Assert.That(winner, Is.SameAs(task), $"{what} did not finish within {timeoutMs} ms.");
        }
    }
}
