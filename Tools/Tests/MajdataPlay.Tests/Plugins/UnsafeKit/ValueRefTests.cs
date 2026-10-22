using System;
using MajdataPlay.UnsafeKit;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.UnsafeKit
{
    /// <summary>Covers the pointer-backed reference wrappers <see cref="ValueRef{T}"/> and <see cref="ReadOnlyValueRef{T}"/>.</summary>
    public class ValueRefTests
    {
        /// <summary>Checks that a reference built from a variable aliases it.</summary>
        [Test]
        public unsafe void FromVariable_AliasesVariable()
        {
            var value = 2.5f;
            var reference = new ValueRef<float>(ref value);

            reference.Target = 4f;

            Assert.That(value, Is.EqualTo(4f));
            Assert.That((IntPtr)reference.AsPointer(), Is.EqualTo((IntPtr)(&value)));
        }

        /// <summary>Checks that a reference built from a pointer aliases the pointed-to element.</summary>
        [Test]
        public unsafe void FromPointer_AliasesElement()
        {
            var buffer = stackalloc int[3];
            buffer[1] = 8;
            var reference = new ValueRef<int>(buffer + 1);

            reference.Target++;

            Assert.That(buffer[1], Is.EqualTo(9));
            Assert.That((IntPtr)reference.AsPointer(), Is.EqualTo((IntPtr)(buffer + 1)));
        }

        /// <summary>Checks the read-only views produced from a variable, a pointer, AsReadOnly and the implicit conversion.</summary>
        [Test]
        public unsafe void ReadOnlyViews_ObserveWrites()
        {
            var value = 10L;
            var reference = new ValueRef<long>(ref value);
            var fromMethod = reference.AsReadOnly();
            ReadOnlyValueRef<long> fromConversion = reference;
            var fromVariable = new ReadOnlyValueRef<long>(ref value);
            var fromPointer = new ReadOnlyValueRef<long>(&value);

            value = 20;

            Assert.That(fromMethod.Target, Is.EqualTo(20));
            Assert.That(fromConversion.Target, Is.EqualTo(20));
            Assert.That(fromVariable.Target, Is.EqualTo(20));
            Assert.That(fromPointer.Target, Is.EqualTo(20));
        }
    }
}
