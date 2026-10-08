using System;

#nullable enable

namespace MajdataPlay.Platform.Android
{
    /// <summary>
    /// Reports a Java exception raised by a generated JNI call after clearing the JVM exception state.
    /// </summary>
    /// <remarks>
    /// Unity's <c>AndroidJavaException</c> constructor is internal. Method and field lookup may still
    /// throw that Unity exception; invocation failures expose their Java stack trace through this type.
    /// </remarks>
    public sealed class JavaInvocationException : Exception
    {
        /// <summary>
        /// Gets the original Java stack trace, or an empty string when it could not be recovered.
        /// </summary>
        public string JavaStackTrace { get; }

        /// <summary>
        /// Initializes an invocation failure with its Java message and stack trace.
        /// </summary>
        /// <param name="message">The Java throwable's description.</param>
        /// <param name="javaStackTrace">The original Java stack trace.</param>
        public JavaInvocationException(string message, string javaStackTrace)
            : base(message + Environment.NewLine + javaStackTrace)
        {
            JavaStackTrace = javaStackTrace;
        }
    }
}
