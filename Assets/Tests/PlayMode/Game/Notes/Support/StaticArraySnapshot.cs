using System;
using MajdataPlay.Tests;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode.Support
{
    /// <summary>
    /// Copies the elements of a static readonly array before a test and writes them back afterwards, for production
    /// caches that keep per-scene state in static arrays.
    /// </summary>
    internal sealed class StaticArraySnapshot
    {
        /// <summary>The live array owned by the production type.</summary>
        private readonly Array _target;

        /// <summary>The copy taken before the test.</summary>
        private readonly Array _copy;

        /// <summary>Captures a copy of a static array field.</summary>
        /// <param name="owner">The type that declares the field.</param>
        /// <param name="fieldName">The field name.</param>
        private StaticArraySnapshot(Type owner, string fieldName)
        {
            _target = PrivateMember.GetStaticField<Array>(owner, fieldName);
            _copy = (Array)_target.Clone();
        }

        /// <summary>Captures a copy of a static array field.</summary>
        /// <param name="owner">The type that declares the field.</param>
        /// <param name="fieldName">The field name.</param>
        /// <returns>The snapshot that restores the elements.</returns>
        /// <exception cref="MissingFieldException">No such field exists.</exception>
        public static StaticArraySnapshot Capture(Type owner, string fieldName)
        {
            return new StaticArraySnapshot(owner, fieldName);
        }

        /// <summary>Writes the captured elements back into the live array.</summary>
        public void Restore()
        {
            Array.Copy(_copy, _target, _copy.Length);
        }
    }
}
