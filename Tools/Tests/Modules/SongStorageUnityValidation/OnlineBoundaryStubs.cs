#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataPlay.Net
{
    /// <summary>Rejects HTTP sends before opening a socket and counts attempts caught by production retry logic.</summary>
    internal sealed class ValidationHttpHandler : HttpMessageHandler
    {
        /// <summary>Counts every forbidden send, including sends whose exceptions production catches.</summary>
        private static int s_requestCount;
        /// <summary>Gets the number of HTTP requests attempted during this Editor process.</summary>
        public static int RequestCount => Volatile.Read(ref s_requestCount);

        /// <summary>Rejects every request; successful cases must use pre-seeded metadata and media paths.</summary>
        /// <param name="request">The unused request message.</param>
        /// <param name="cancellationToken">The unused request token.</param>
        /// <returns>No response; real networking is forbidden.</returns>
        /// <exception cref="NotSupportedException">Production attempted an HTTP request.</exception>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref s_requestCount);
            throw new NotSupportedException("HTTP requests are outside the cached-metadata harness.");
        }
    }
}

namespace MajdataPlay.Buffers
{
    /// <summary>Compiles the real online download loop without replacing or exercising its algorithm.</summary>
    /// <typeparam name="T">The unused transfer-buffer element type.</typeparam>
    internal static class Pool<T>
    {
        /// <summary>Rejects transfer-buffer allocation if the download loop is accidentally entered.</summary>
        /// <param name="length">The unused requested buffer length.</param>
        /// <param name="clearArrayWhenReturn">The unused clearing flag.</param>
        /// <returns>No buffer; downloads are outside this harness.</returns>
        /// <exception cref="NotSupportedException">Production entered the download transfer loop.</exception>
        public static ValidationPooledBuffer<T> Rent(int length, bool clearArrayWhenReturn = false)
        {
            throw new NotSupportedException("Download transfer buffers are outside the cached-metadata harness.");
        }
    }

    /// <summary>Supplies only the compile-time memory/disposal surface used by the production transfer loop.</summary>
    /// <typeparam name="T">The unused element type.</typeparam>
    internal sealed class ValidationPooledBuffer<T> : IDisposable
    {
        /// <summary>Rejects access instead of supplying a substitute download implementation.</summary>
        /// <returns>No memory; transfer-buffer use is forbidden.</returns>
        /// <exception cref="NotSupportedException">Production attempted a transfer-buffer read.</exception>
        public Memory<T> AsMemory()
        {
            throw new NotSupportedException("Download transfer buffers are outside the cached-metadata harness.");
        }

        /// <summary>Releases no resources because no transfer buffer is ever allocated.</summary>
        public void Dispose()
        {
        }
    }
}
