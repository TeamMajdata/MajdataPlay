using System.Collections.Generic;
using MajdataPlay.Utils.Events;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Utils.PlayMode
{
    /// <summary>A non-component bindable target, whose own methods the drawer lists.</summary>
    internal sealed class RuntimeEventListenerAsset : ScriptableObject
    {
        /// <summary>Gets the received calls as <c>Method:value</c> entries in call order.</summary>
        public List<string> Calls { get; } = new();

        /// <summary>A marked method matching <c>Action&lt;int&gt;</c>.</summary>
        /// <param name="value">The event value.</param>
        [BindableMethod]
        public void OnAssetValue(int value)
        {
            Calls.Add($"{nameof(OnAssetValue)}:{value}");
        }
    }
}
