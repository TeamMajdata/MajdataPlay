#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>
    /// The business code returned in the <c>code</c> field of an API status object. It is serialized as a number and is
    /// checked separately from the HTTP status code. Request errors use <c>4XXXX</c>, where the first three digits follow
    /// the closest HTTP status code, and account errors use <c>6XXXX</c>.
    /// </summary>
    public enum ApiReturnCode
    {
        /// <summary>
        /// The request failed for a reason that has no more specific code.
        /// </summary>
        Error = -114514,

        /// <summary>
        /// The request succeeded.
        /// </summary>
        Success = 114514,

        /// <summary>
        /// A request parameter, field or body value is missing, malformed or not allowed.
        /// </summary>
        InvalidValue = 40000,

        /// <summary>
        /// The request has no body, file or form data although the API requires one.
        /// </summary>
        PayloadRequired = 40001,

        /// <summary>
        /// The request holds more elements, such as tags, than the API allows.
        /// </summary>
        TooManyElements = 40002,

        /// <summary>
        /// The requested item does not exist or is not visible to the caller.
        /// </summary>
        NoSuchItem = 40400,

        /// <summary>
        /// The requested API endpoint is not supported.
        /// </summary>
        ApiNotSupported = 40499,

        /// <summary>
        /// The item cannot be created because an item with the same identifier already exists.
        /// </summary>
        ItemAlreadyExists = 40900,

        /// <summary>
        /// The item was deleted or removed, so the request cannot apply to it any more.
        /// </summary>
        ItemDeleted = 41000,

        /// <summary>
        /// The request body or uploaded file is larger than the API allows.
        /// </summary>
        PayloadTooLarge = 41300,

        /// <summary>
        /// The request data uses a format the API does not support.
        /// </summary>
        FormatNotSupported = 41500,

        /// <summary>
        /// The caller sent too many requests of this kind within the rate limit window.
        /// </summary>
        TooManyRequests = 42900,

        /// <summary>
        /// The request requires a logged-in user, but no user is logged in.
        /// </summary>
        NotLoggedIn = 60000,

        /// <summary>
        /// The request requires an anonymous caller, but a user is already logged in.
        /// </summary>
        AlreadyLoggedIn = 60001,

        /// <summary>
        /// The logged-in user or session does not have the permission the request requires.
        /// </summary>
        PermissionDenied = 60002,

        /// <summary>
        /// The username, email address or password does not match an account.
        /// </summary>
        InvalidCredentials = 60003,

        /// <summary>
        /// The account exists but has not been activated yet.
        /// </summary>
        LoginFailedPendingVerification = 60004,

        /// <summary>
        /// The account exists but is banned.
        /// </summary>
        LoginFailedUserBanned = 60005,

        /// <summary>
        /// The invite code used for registration is invalid.
        /// </summary>
        InvalidInviteCode = 60100,

        /// <summary>
        /// The password used for registration is empty.
        /// </summary>
        PasswordIsEmpty = 60101,

        /// <summary>
        /// The email address is not a valid address.
        /// </summary>
        InvalidEmailAddress = 60102,

        /// <summary>
        /// Another account already uses the username.
        /// </summary>
        UsernameAlreadyExists = 60103,

        /// <summary>
        /// Another account already uses the email address.
        /// </summary>
        EmailAlreadyExists = 60104,
    }
}
