using System.Collections;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.Harness.PlayMode
{
    /// <summary>
    /// A minimal behaviour used by the EditMode harness tests. It lives in the PlayMode folder because Unity only
    /// attaches behaviours compiled into runtime assemblies such as MajdataPlay.Tests.Game.PlayMode, never editor
    /// scripts.
    /// </summary>
    public class HarnessProbe : MonoBehaviour
    {
        /// <summary>A private counter written through reflection.</summary>
        private int _counter;

        /// <summary>Gets the label assigned through a private setter.</summary>
        public string Label { get; private set; } = string.Empty;

        /// <summary>Gets a value indicating whether Awake ran.</summary>
        public bool Awakened { get; private set; }

        /// <summary>Marks the probe as awakened.</summary>
        private void Awake()
        {
            Awakened = true;
        }

        /// <summary>Yields three times so the coroutine runner can count its steps.</summary>
        /// <returns>The coroutine.</returns>
        private IEnumerator Start()
        {
            yield return null;
            yield return null;
            yield return null;
        }

        /// <summary>Increments the private counter.</summary>
        private void Increment()
        {
            _counter++;
        }
    }
}
