using System;
using MajdataPlay.Utils;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Utils
{
    /// <summary>Verifies that every <see cref="ThrowHelper"/> entry point throws the documented exception.</summary>
    public class ThrowHelperTests
    {
        /// <summary>Checks that the generic overload creates and throws a new exception.</summary>
        [Test]
        public void Throw_Generic_ThrowsNewException()
        {
            Assert.Throws<InvalidOperationException>(() => ThrowHelper.Throw<InvalidOperationException>());
        }

        /// <summary>Checks that the instance overload rethrows the given exception object.</summary>
        [Test]
        public void Throw_Instance_ThrowsTheSameObject()
        {
            var expected = new TimeoutException("boom");

            var actual = Assert.Throws<TimeoutException>(() => ThrowHelper.Throw(expected));

            Assert.That(actual, Is.SameAs(expected));
        }

        /// <summary>Checks the overloads that pretend to return a value.</summary>
        [Test]
        public void Throw_WithResult_Throws()
        {
            var expected = new FormatException("bad");

            Assert.Throws<NotSupportedException>(() => ThrowHelper.Throw<NotSupportedException, int>());
            var actual = Assert.Throws<FormatException>(() => ThrowHelper.Throw<FormatException, string>(expected));
            Assert.That(actual, Is.SameAs(expected));
        }

        /// <summary>Checks the not supported helpers with and without a message.</summary>
        [Test]
        public void NotSupported_ThrowsNotSupportedException()
        {
            Assert.Throws<NotSupportedException>(() => ThrowHelper.NotSupported());
            var withMessage = Assert.Throws<NotSupportedException>(() => ThrowHelper.NotSupported("nope"));
            Assert.That(withMessage.Message, Is.EqualTo("nope"));
        }

        /// <summary>Checks the not implemented helpers with and without a message.</summary>
        [Test]
        public void NotImplemented_ThrowsNotImplementedException()
        {
            Assert.Throws<NotImplementedException>(() => ThrowHelper.NotImplemented());
            var withMessage = Assert.Throws<NotImplementedException>(() => ThrowHelper.NotImplemented("later"));
            Assert.That(withMessage.Message, Is.EqualTo("later"));
        }

        /// <summary>Checks the out of range helpers, including the parameter name and message.</summary>
        [Test]
        public void OutOfRange_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ThrowHelper.OutOfRange());
            var named = Assert.Throws<ArgumentOutOfRangeException>(() => ThrowHelper.OutOfRange("index"));
            var detailed = Assert.Throws<ArgumentOutOfRangeException>(() => ThrowHelper.OutOfRange("count", "too big"));

            Assert.That(named.ParamName, Is.EqualTo("index"));
            Assert.That(detailed.ParamName, Is.EqualTo("count"));
            Assert.That(detailed.Message, Does.StartWith("too big"));
        }

        /// <summary>Checks that the conditional helpers throw only when the condition holds.</summary>
        [Test]
        public void If_Throw_ThrowsOnlyWhenConditionIsTrue()
        {
            var expected = new ArgumentException("arg");

            Assert.DoesNotThrow(() => ThrowHelper.If.Throw<InvalidOperationException>(false));
            Assert.DoesNotThrow(() => ThrowHelper.If.Throw(expected, false));
            Assert.Throws<InvalidOperationException>(() => ThrowHelper.If.Throw<InvalidOperationException>(true));
            var actual = Assert.Throws<ArgumentException>(() => ThrowHelper.If.Throw(expected, true));
            Assert.That(actual, Is.SameAs(expected));
        }

        /// <summary>Checks that the conditional helpers with a result return the default value when they do not throw.</summary>
        [Test]
        public void If_ThrowWithResult_ReturnsDefaultWhenConditionIsFalse()
        {
            var expected = new ArgumentException("arg");

            Assert.That(ThrowHelper.If.Throw<InvalidOperationException, int>(false), Is.EqualTo(0));
            Assert.That(ThrowHelper.If.Throw<ArgumentException, string>(expected, false), Is.Null);
            Assert.Throws<InvalidOperationException>(() => ThrowHelper.If.Throw<InvalidOperationException, int>(true));
            Assert.Throws<ArgumentException>(() => ThrowHelper.If.Throw<ArgumentException, string>(expected, true));
        }
    }
}
