#nullable enable
using System;
using System.Net;
using System.Net.Http;
using MajdataPlay.Net;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.Net
{
    /// <summary>Covers every constructor of the HTTP exception and its diagnostic text.</summary>
    public class HttpExceptionTests
    {
        /// <summary>The URL used by every exception.</summary>
        private const string Url = "https://example.invalid/api";

        /// <summary>Checks that a message-only exception keeps its message and has no status code.</summary>
        [Test]
        public void MessageConstructor_KeepsMessage()
        {
            var exception = new HttpException(Url, HttpErrorCode.InvalidRequest, "bad request");

            Assert.That(exception, Is.InstanceOf<HttpRequestException>());
            Assert.That(exception.ErrorCode, Is.EqualTo(HttpErrorCode.InvalidRequest));
            Assert.That(exception.OriginalUrl, Is.EqualTo(Url));
            Assert.That(exception.StatusCode, Is.Null);
            Assert.That(exception.Message, Is.EqualTo("bad request"));
            Assert.That(exception.ToString(), Is.EqualTo("bad request"));
        }

        /// <summary>Checks that the inner exception is kept.</summary>
        [Test]
        public void InnerExceptionConstructor_KeepsInnerException()
        {
            var inner = new InvalidOperationException("inner");

            var exception = new HttpException(Url, HttpErrorCode.Unreachable, "outer", inner);

            Assert.That(exception.InnerException, Is.SameAs(inner));
            Assert.That(exception.Message, Is.EqualTo("outer"));
            Assert.That(exception.ToString(), Is.EqualTo("outer"));
        }

        /// <summary>Checks that the code-only constructor describes the error code and URL.</summary>
        [Test]
        public void CodeConstructor_DescribesCodeAndUrl()
        {
            var exception = new HttpException(Url, HttpErrorCode.Timeout);

            Assert.That(exception.StatusCode, Is.Null);
            Assert.That(exception.ToString(), Does.Contain("ErrorCode: Timeout"));
            Assert.That(exception.ToString(), Does.Contain("Uri: " + Url));
        }

        /// <summary>Checks that a status code is described only when present.</summary>
        [Test]
        public void StatusConstructor_DescribesStatusWhenPresent()
        {
            var withStatus = new HttpException(Url, HttpErrorCode.Unsuccessful, HttpStatusCode.NotFound);
            var withoutStatus = new HttpException(Url, HttpErrorCode.Unsuccessful, (HttpStatusCode?)null);

            Assert.That(withStatus.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(withStatus.ToString(), Does.Contain("StatusCode: NotFound"));
            Assert.That(withoutStatus.StatusCode, Is.Null);
            Assert.That(withoutStatus.ToString(), Does.Not.Contain("StatusCode"));
            Assert.That(withoutStatus.ToString(), Does.Contain("ErrorCode: Unsuccessful"));
        }

        /// <summary>Checks that the status-and-message constructor keeps the message and describes the status.</summary>
        [Test]
        public void StatusMessageConstructor_KeepsMessageAndDescribesStatus()
        {
            var withStatus = new HttpException(Url, HttpErrorCode.Unsuccessful, HttpStatusCode.BadGateway, "gateway");
            var withoutStatus = new HttpException(Url, HttpErrorCode.Canceled, null, "cancelled");

            Assert.That(withStatus.Message, Is.EqualTo("gateway"));
            Assert.That(withStatus.ToString(), Does.Contain("StatusCode: BadGateway"));
            Assert.That(withoutStatus.Message, Is.EqualTo("cancelled"));
            Assert.That(withoutStatus.ToString(), Does.Not.Contain("StatusCode"));
        }

        /// <summary>Checks that the full constructor keeps message, inner exception and status.</summary>
        [Test]
        public void FullConstructor_KeepsEverything()
        {
            var inner = new TimeoutException();

            var withStatus = new HttpException(Url, HttpErrorCode.IntegrityCheckFailed, HttpStatusCode.OK, "hash", inner);
            var withoutStatus = new HttpException(Url, HttpErrorCode.NotSupported, null, "unsupported", inner);

            Assert.That(withStatus.InnerException, Is.SameAs(inner));
            Assert.That(withStatus.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(withStatus.ToString(), Does.Contain("ErrorCode: IntegrityCheckFailed"));
            Assert.That(withStatus.ToString(), Does.Contain("StatusCode: OK"));
            Assert.That(withoutStatus.ToString(), Does.Not.Contain("StatusCode"));
            Assert.That(withoutStatus.ErrorCode, Is.EqualTo(HttpErrorCode.NotSupported));
        }
    }
}
