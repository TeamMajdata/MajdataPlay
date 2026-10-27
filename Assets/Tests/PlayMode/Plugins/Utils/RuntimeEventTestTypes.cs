#nullable enable
namespace MajdataPlay.Tests.Plugins.Utils.PlayMode
{
    /// <summary>A value passed by reference to the by-reference test delegates.</summary>
    internal struct RuntimeEventTestPayload
    {
        /// <summary>The payload value.</summary>
        public int Value;
    }

    /// <summary>A delegate with an <c>in</c> parameter.</summary>
    /// <param name="payload">The payload, passed by read-only reference.</param>
    internal delegate void RuntimeEventInHandler(in RuntimeEventTestPayload payload);

    /// <summary>A delegate with a <c>ref</c> struct parameter.</summary>
    /// <param name="payload">The payload, passed by reference.</param>
    internal delegate void RuntimeEventRefHandler(ref RuntimeEventTestPayload payload);

    /// <summary>A delegate with a <c>ref</c> integer parameter.</summary>
    /// <param name="value">The value, passed by reference.</param>
    internal delegate void RuntimeEventRefIntHandler(ref int value);

    /// <summary>A delegate with an <c>out</c> integer parameter.</summary>
    /// <param name="value">The value set by the listener.</param>
    internal delegate void RuntimeEventOutIntHandler(out int value);
}
