using System;

#nullable enable
namespace MajdataPlay.Tests.Game.EditorTools
{
    /// <summary>A custom option enumerator shape with a literal value array assigned before the text hook.</summary>
    internal sealed class LiteralOptionFixture
    {
        /// <summary>The option values inspected by the analyzer.</summary>
        private string[] OptionValues = Array.Empty<string>();

        /// <summary>Assigns the values and requests their texts.</summary>
        private void InitInternal()
        {
            OptionValues = new[] { "Outer", "Disable", "Inner", "42" };
            InitValueTexts();
        }

        /// <summary>The text hook recognized by the analyzer.</summary>
        private void InitValueTexts()
        {
            GC.KeepAlive(OptionValues);
        }
    }

    /// <summary>A custom option enumerator shape whose values depend on a branch.</summary>
    internal sealed class BranchOptionFixture
    {
        /// <summary>The option values inspected by the analyzer.</summary>
        private object[] OptionValues = Array.Empty<object>();

        /// <summary>Gets or sets which literal array is assigned.</summary>
        public bool UseFirst { get; set; }

        /// <summary>Assigns one of two literal arrays and returns normally.</summary>
        private void InitInternal()
        {
            if (UseFirst)
            {
                OptionValues = new object[] { "BranchA" };
            }
            else
            {
                OptionValues = new object[] { "BranchB" };
            }
        }
    }

    /// <summary>A custom option enumerator shape whose values come from an opaque helper.</summary>
    internal sealed class DynamicOptionFixture
    {
        /// <summary>The option values inspected by the analyzer.</summary>
        private string[] OptionValues = Array.Empty<string>();

        /// <summary>Assigns values from a helper the analyzer does not execute.</summary>
        private void InitInternal()
        {
            OptionValues = CreateValues();
        }

        /// <summary>Creates the values at runtime.</summary>
        /// <returns>The values.</returns>
        private static string[] CreateValues()
        {
            return new[] { "Runtime" };
        }
    }

    /// <summary>A type lacking the option enumerator shape.</summary>
    internal sealed class MissingOptionFixture
    {
        /// <summary>A field with an unrelated name.</summary>
        private string[] Values = Array.Empty<string>();

        /// <summary>Keeps the field referenced.</summary>
        /// <returns>The values.</returns>
        internal string[] Read()
        {
            return Values;
        }
    }

    /// <summary>A custom option enumerator shape that never assigns a value.</summary>
    internal sealed class EmptyOptionFixture
    {
        /// <summary>The option values inspected by the analyzer.</summary>
        private string[] OptionValues = Array.Empty<string>();

        /// <summary>Leaves the values empty.</summary>
        private void InitInternal()
        {
            OptionValues = Array.Empty<string>();
        }
    }
}
