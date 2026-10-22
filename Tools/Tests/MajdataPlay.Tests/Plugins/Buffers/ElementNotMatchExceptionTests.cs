using System;
using MajdataPlay.Buffers;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Buffers
{
    /// <summary>Covers the constructors of <see cref="ElementNotMatchException{TElement}"/>.</summary>
    public class ElementNotMatchExceptionTests
    {
        /// <summary>Checks that the element-only constructor keeps the element and uses the default message.</summary>
        [Test]
        public void Constructor_ElementOnly_UsesDefaultMessage()
        {
            var exception = new ElementNotMatchException<string>("item");

            Assert.That(exception, Is.InstanceOf<ArgumentException>());
            Assert.That(exception.Element, Is.EqualTo("item"));
            Assert.That(exception.Message, Is.EqualTo("The element do not match"));
        }

        /// <summary>Checks that the message constructor keeps both the element and the message.</summary>
        [Test]
        public void Constructor_WithMessage_KeepsMessage()
        {
            var exception = new ElementNotMatchException<int>(3, "custom");

            Assert.That(exception.Element, Is.EqualTo(3));
            Assert.That(exception.Message, Is.EqualTo("custom"));
        }
    }
}
