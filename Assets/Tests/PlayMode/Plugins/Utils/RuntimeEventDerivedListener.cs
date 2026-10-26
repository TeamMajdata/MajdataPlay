using MajdataPlay.Utils.Events;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Utils.PlayMode
{
    /// <summary>
    /// A listener subclass with a marked override and an unmarked override, for the inheritance rules of
    /// <see cref="RuntimeEventMethods"/>.
    /// </summary>
    internal sealed class RuntimeEventDerivedListener : RuntimeEventListener
    {
        /// <summary>A marked override of <see cref="RuntimeEventListener.OnVirtualValue"/>.</summary>
        /// <param name="value">The event value.</param>
        [BindableMethod]
        public override void OnVirtualValue(int value)
        {
            Calls.Add($"Derived.{nameof(OnVirtualValue)}:{value}");
        }

        /// <summary>An unmarked override that hides the marked <see cref="RuntimeEventListener.OnShadowedValue"/>.</summary>
        /// <param name="value">The event value.</param>
        public override void OnShadowedValue(int value)
        {
            Calls.Add($"Derived.{nameof(OnShadowedValue)}:{value}");
        }
    }
}
