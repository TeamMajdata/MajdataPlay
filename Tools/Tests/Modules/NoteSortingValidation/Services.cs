#nullable enable
using System;

namespace MajdataPlay.IO
{
    /// <summary>
    /// Supplies unused sensor symbols while the complete production NoteHelper compiles.
    /// </summary>
    internal static class SensorAreaExtensions
    {
        /// <summary>
        /// Rejects sensor mapping, which is outside this isolated sorting validation.
        /// </summary>
        /// <param name="source">The unused sensor area.</param>
        /// <returns>No result is returned.</returns>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        internal static SensorGroup GetGroup(this SensorArea source)
        {
            throw new NotSupportedException("Sensor mapping is outside the sorting validation.");
        }

        /// <summary>
        /// Rejects sensor mapping, which is outside this isolated sorting validation.
        /// </summary>
        /// <param name="source">The unused sensor area.</param>
        /// <returns>No result is returned.</returns>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        internal static int GetIndex(this SensorArea source)
        {
            throw new NotSupportedException("Sensor mapping is outside the sorting validation.");
        }
    }
}
