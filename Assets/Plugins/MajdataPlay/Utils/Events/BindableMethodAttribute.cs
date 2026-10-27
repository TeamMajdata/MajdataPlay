using System;
using UnityEngine.Scripting;

#nullable enable
namespace MajdataPlay.Utils.Events
{
    /// <summary>
    /// Marks an instance method as a target that a <see cref="RuntimeEvent{TDelegate}"/> binding can call.
    /// </summary>
    /// <remarks>
    /// The Inspector lists only marked methods, and the runtime rejects bindings to unmarked methods, so the Editor and
    /// the Player follow the same rule. Because bound methods are referenced only by name in serialized data, the
    /// attribute derives from <see cref="PreserveAttribute"/> to keep managed code stripping from removing them.
    /// The attribute is not inherited: an override must be marked again, otherwise it hides the marked base method.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class BindableMethodAttribute : PreserveAttribute
    {
    }
}
