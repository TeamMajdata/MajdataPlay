using MajdataPlay.Net.Curl.Core.PInvoke;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MajdataPlay.Net.Curl
{
    public class CurlException : Exception
    {
        public CurlCode ErrorCode { get; }
        public CurlException(CurlCode errorCode) : base($"Curl Error {(int)errorCode}: {errorCode}")
        {
            ErrorCode = errorCode;
        }
        public CurlException(CurlCode errorCode, string message) : this(errorCode, message, null) { }
        public CurlException(CurlCode errorCode, string message, Exception innerException) : base(message, innerException)
        {
            ErrorCode = errorCode;
        }
    }
}
