using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Harness
{
    /// <summary>A behaviour that records the lifecycle messages Unity sends it in Play Mode.</summary>
    public class PlayModeProbe : MonoBehaviour
    {
        /// <summary>Gets a value indicating whether Awake ran.</summary>
        public bool Awakened { get; private set; }

        /// <summary>Gets the number of Update calls received.</summary>
        public int UpdateCount { get; private set; }

        /// <summary>Marks the probe as awakened.</summary>
        private void Awake()
        {
            Awakened = true;
        }

        /// <summary>Counts the frame.</summary>
        private void Update()
        {
            UpdateCount++;
        }
    }
}
