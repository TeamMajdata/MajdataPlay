using MajdataPlay.Utils;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Utils
{
    /// <summary>Covers the mutable box <see cref="Flag{T}"/>.</summary>
    public class FlagTests
    {
        /// <summary>Checks that a new flag holds the default value.</summary>
        [Test]
        public void New_HoldsDefault()
        {
            var flag = new Flag<int>();

            Assert.That(flag.Value, Is.EqualTo(0));
            Assert.That(new Flag<string>().Value, Is.Null);
        }

        /// <summary>Checks that the explicit conversion returns the stored value, including updates through shared references.</summary>
        [Test]
        public void ExplicitConversion_ReturnsCurrentValue()
        {
            var flag = new Flag<bool>();
            var shared = flag;

            shared.Value = true;

            Assert.That((bool)flag, Is.True);
        }
    }
}
