using MajdataPlay.Net.Curl.Core.PInvoke;
using MajdataPlay.Net.Curl.Lifecycle;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Net.Curl.Core
{
    public class CurlEasy : CurlHandle
    {
        /// <summary>The size of the error buffer libcurl requires (<c>CURL_ERROR_SIZE</c>).</summary>
        private const int ErrorBufferSize = 256;

        IntPtr _headersList = IntPtr.Zero;
        /// <summary>
        /// The native buffer installed as <c>CURLOPT_ERRORBUFFER</c>. libcurl writes into it during transfers, so it is
        /// freed only after the easy handle has been cleaned up.
        /// </summary>
        private IntPtr _errorBuffer;

        /// <summary>
        /// Creates an easy handle with an error buffer installed.
        /// </summary>
        /// <exception cref="InvalidOperationException">libcurl could not create the handle or install the error buffer.</exception>
        /// <exception cref="OutOfMemoryException">The error buffer could not be allocated.</exception>
        public CurlEasy()
        {
            LibCurlLifecycle.Retain();
            ThisHandle = LibCurl.Easy.Init();
            if (ThisHandle == IntPtr.Zero)
            {
                // The finalizer only releases allocated handles, so the reference must be returned here.
                LibCurlLifecycle.Release();
                GC.SuppressFinalize(this);
                throw new InvalidOperationException("Failed to initialize libcurl.");
            }
            try
            {
                _errorBuffer = Marshal.AllocHGlobal(ErrorBufferSize);
                Marshal.WriteByte(_errorBuffer, 0);
                CheckCurlCode(LibCurl.Easy.SetOption(ThisHandle, CurlOption.ErrorBuffer, _errorBuffer));
            }
            catch
            {
                Dispose();
                throw;
            }
        }
        ~CurlEasy()
        {
            Dispose();
        }

        public void SetOption(CurlOption option, string value)
        {
            ThrowIfDisposed();
            var result = LibCurl.Easy.SetOption(ThisHandle, option, value);
            CheckCurlCode(result);
        }
        public void SetOption(CurlOption option, long value)
        {
            ThrowIfDisposed();
            var result = LibCurl.Easy.SetOption(ThisHandle, option, value);
            CheckCurlCode(result);
        }
        public void SetOption(CurlOption option, IntPtr value)
        {
            ThrowIfDisposed();
            var result = LibCurl.Easy.SetOption(ThisHandle, option, value);
            CheckCurlCode(result);
        }
        public CurlCode Pause(CurlPauseAction action)
        {
            ThrowIfDisposed();
            var result = LibCurl.Easy.Pause(ThisHandle, action);

            return result;
        }
        public CurlCode Perform()
        {
            ThrowIfDisposed();
            var result = LibCurl.Easy.Perform(ThisHandle);

            return result;
        }
        public void SetHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
        {
            ThrowIfDisposed();
            ClearSList();
            foreach (var header in headers)
            {
                var headerString = $"{header.Key}: {string.Join(", ", header.Value)}";
                AppendSList(headerString);
            }
            SetOption(CurlOption.HttpHeader, _headersList);
        }
        public void AppendSList(string text)
        {
            ThrowIfDisposed();
            _headersList = LibCurl.SListAppend(_headersList, text);
        }
        public void ClearSList()
        {
            ThrowIfDisposed();
            if (_headersList != IntPtr.Zero)
            {
                LibCurl.SListFreeAll(_headersList);
                _headersList = IntPtr.Zero;
            }
        }

        public bool TrySetOption(CurlOption option, string value, [NotNullWhen(true)] out CurlCode? result)
        {
            if(!IsAllocated)
            {
                result = default;
                return false;
            }
            result = LibCurl.Easy.SetOption(ThisHandle, option, value);
            return result == CurlCode.Ok;
        }
        public bool TrySetOption(CurlOption option, long value, [NotNullWhen(true)] out CurlCode? result)
        {
            if (!IsAllocated)
            {
                result = default;
                return false;
            }
            result = LibCurl.Easy.SetOption(ThisHandle, option, value);
            return result == CurlCode.Ok;
        }
        public bool TrySetOption(CurlOption option, IntPtr value, [NotNullWhen(true)] out CurlCode? result)
        {
            if (!IsAllocated)
            {
                result = default;
                return false;
            }
            result = LibCurl.Easy.SetOption(ThisHandle, option, value);
            return result == CurlCode.Ok;
        }
        public bool TryPause(CurlPauseAction action, [NotNullWhen(true)] out CurlCode? result)
        {
            if (!IsAllocated)
            {
                result = default;
                return false;
            }
            result = LibCurl.Easy.Pause(ThisHandle, action);
            return result == CurlCode.Ok;
        }
        public bool TryPerform([NotNullWhen(true)] out CurlCode? result)
        {
            if (!IsAllocated)
            {
                result = default;
                return false;
            }
            result = LibCurl.Easy.Perform(ThisHandle);
            return result == CurlCode.Ok;
        }


        /// <summary>
        /// Reads the description libcurl wrote into the error buffer for the last failed transfer or option, such as
        /// <c>Operation too slow. Less than 32768 bytes/sec transferred the last 4 seconds</c>.
        /// </summary>
        /// <remarks>
        /// Call it only while the handle is alive and not used by another thread, such as when the multi handle reports
        /// the transfer as done.
        /// </remarks>
        /// <returns>The description, or <see langword="null"/> when libcurl wrote none or the handle was disposed.</returns>
        internal string? GetErrorDetail()
        {
            var buffer = _errorBuffer;
            if (!IsAllocated || buffer == IntPtr.Zero)
            {
                return null;
            }
            var detail = Marshal.PtrToStringUTF8(buffer)?.TrimEnd('\r', '\n', ' ');
            return string.IsNullOrEmpty(detail) ? null : detail;
        }

        public override void Dispose()
        {
            var handle = Interlocked.Exchange(ref ThisHandle, IntPtr.Zero);
            if (handle != IntPtr.Zero)
            {
                if (_headersList != IntPtr.Zero)
                {
                    LibCurl.SListFreeAll(_headersList);
                    _headersList = IntPtr.Zero;
                }
                LibCurl.Easy.CleanUp(handle);
                // libcurl may write into the buffer until curl_easy_cleanup returns.
                var errorBuffer = Interlocked.Exchange(ref _errorBuffer, IntPtr.Zero);
                if (errorBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(errorBuffer);
                }
                LibCurlLifecycle.Release();
                GC.SuppressFinalize(this);
            }
        }
    }
}
