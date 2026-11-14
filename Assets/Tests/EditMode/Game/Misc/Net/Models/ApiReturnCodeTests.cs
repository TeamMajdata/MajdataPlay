using System;
using System.Globalization;
using System.Linq;
using MajdataPlay.Net.Models;
using MajdataPlay.Utils;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Net.Models
{
    /// <summary>Pins the backend's numeric business-code contract and preserves optional or unknown status codes.</summary>
    public class ApiReturnCodeTests
    {
        /// <summary>Checks the published number and numeric JSON representation of every backend code.</summary>
        /// <param name="code">The declared business code.</param>
        /// <param name="expected">The number the backend sends on the wire.</param>
        [TestCase(ApiReturnCode.Error, -114514)]
        [TestCase(ApiReturnCode.Success, 114514)]
        [TestCase(ApiReturnCode.InvalidValue, 40000)]
        [TestCase(ApiReturnCode.PayloadRequired, 40001)]
        [TestCase(ApiReturnCode.TooManyElements, 40002)]
        [TestCase(ApiReturnCode.NoSuchItem, 40400)]
        [TestCase(ApiReturnCode.ItemAlreadyExists, 40900)]
        [TestCase(ApiReturnCode.ItemDeleted, 41000)]
        [TestCase(ApiReturnCode.PayloadTooLarge, 41300)]
        [TestCase(ApiReturnCode.FormatNotSupported, 41500)]
        [TestCase(ApiReturnCode.TooManyRequests, 42900)]
        [TestCase(ApiReturnCode.NotLoggedIn, 60000)]
        [TestCase(ApiReturnCode.AlreadyLoggedIn, 60001)]
        [TestCase(ApiReturnCode.PermissionDenied, 60002)]
        [TestCase(ApiReturnCode.InvalidCredentials, 60003)]
        [TestCase(ApiReturnCode.LoginFailedPendingVerification, 60004)]
        [TestCase(ApiReturnCode.LoginFailedUserBanned, 60005)]
        [TestCase(ApiReturnCode.InvalidInviteCode, 60100)]
        [TestCase(ApiReturnCode.PasswordIsEmpty, 60101)]
        [TestCase(ApiReturnCode.InvalidEmailAddress, 60102)]
        [TestCase(ApiReturnCode.UsernameAlreadyExists, 60103)]
        [TestCase(ApiReturnCode.EmailAlreadyExists, 60104)]
        public void BusinessCode_KeepsPublishedNumberAndNumericJson(ApiReturnCode code, int expected)
        {
            var json = expected.ToString(CultureInfo.InvariantCulture);

            Assert.That((int)code, Is.EqualTo(expected));
            Assert.That(Serializer.Json.Serialize(code), Is.EqualTo(json));
            Assert.That(Serializer.Json.Deserialize<ApiReturnCode>(json), Is.EqualTo(code));
        }

        /// <summary>Checks that all backend members are represented and no two codes share a number.</summary>
        [Test]
        public void DeclaredCodes_AreCompleteAndUnique()
        {
            var codes = Enum.GetValues(typeof(ApiReturnCode)).Cast<ApiReturnCode>().ToArray();

            Assert.That(codes.Length, Is.EqualTo(22));
            Assert.That(codes.Distinct().Count(), Is.EqualTo(codes.Length));
        }

        /// <summary>Checks that status bodies preserve current, legacy and unknown numeric codes.</summary>
        /// <param name="code">The numeric status code returned by an endpoint.</param>
        [TestCase(60001)]
        [TestCase(60004)]
        [TestCase(60005)]
        [TestCase(0)]
        [TestCase(11)]
        [TestCase(12)]
        [TestCase(69999)]
        public void NetStatus_PreservesNumericCode(int code)
        {
            var status = Serializer.Json.Deserialize<NetStatus>("{\"code\":" + code.ToString(CultureInfo.InvariantCulture) + "}");

            Assert.That(status.Code, Is.EqualTo(code));
        }

        /// <summary>Checks that a missing code cannot be confused with a legacy successful-session code of zero.</summary>
        /// <param name="json">The response body without a numeric business code.</param>
        [TestCase("{}")]
        [TestCase("{\"code\":null}")]
        public void NetStatus_WithoutCodeKeepsNull(string json)
        {
            var status = Serializer.Json.Deserialize<NetStatus>(json);

            Assert.That(status.Code, Is.Null);
        }
    }
}
