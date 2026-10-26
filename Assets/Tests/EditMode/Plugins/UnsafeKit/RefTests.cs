using System;
using MajdataPlay.UnsafeKit;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.UnsafeKit
{
    /// <summary>Covers the managed reference wrappers <see cref="Ref{T}"/> and <see cref="ReadOnlyRef{T}"/>.</summary>
    public class RefTests
    {
        /// <summary>Checks that Target reads and writes the referenced variable.</summary>
        [Test]
        public unsafe void Target_AliasesVariable()
        {
            var value = 5;
            var reference = new Ref<int>(ref value);
            try
            {
                reference.Target = 7;

                Assert.That(value, Is.EqualTo(7));
                value = 9;
                Assert.That(reference.Target, Is.EqualTo(9));
                Assert.That((IntPtr)reference.AsPointer(), Is.EqualTo((IntPtr)(&value)));
            }
            finally
            {
                reference.Dispose();
            }
        }

        /// <summary>Checks that a reference-type variable can be referenced and reassigned through Target.</summary>
        [Test]
        public void Target_ReferenceTypeVariable_CanBeReassigned()
        {
            var text = "first";
            var reference = new Ref<string>(ref text);
            try
            {
                reference.Target = "second";

                Assert.That(text, Is.EqualTo("second"));
            }
            finally
            {
                reference.Dispose();
            }
        }

        /// <summary>Checks that disposal clears the pointer and that a second disposal is a no-op.</summary>
        [Test]
        public unsafe void Dispose_ClearsPointerAndIsIdempotent()
        {
            var value = 1;
            var reference = new Ref<int>(ref value);

            reference.Dispose();
            reference.Dispose();

            Assert.That((IntPtr)reference.AsPointer(), Is.EqualTo(IntPtr.Zero));
        }

        /// <summary>Checks that a default reference disposes without a handle.</summary>
        [Test]
        public void Dispose_Default_DoesNothing()
        {
            var reference = default(Ref<int>);

            Assert.DoesNotThrow(() => reference.Dispose());
        }

        /// <summary>Checks that the read-only views created by AsReadOnly and the implicit conversion see writes to the variable.</summary>
        [Test]
        public void AsReadOnly_ObservesVariable()
        {
            var value = 3;
            var reference = new Ref<int>(ref value);
            try
            {
                var viaMethod = reference.AsReadOnly();
                ReadOnlyRef<int> viaConversion = reference;

                value = 4;

                Assert.That(viaMethod.Target, Is.EqualTo(4));
                Assert.That(viaConversion.Target, Is.EqualTo(4));
            }
            finally
            {
                reference.Dispose();
            }
        }

        /// <summary>Checks that a read-only reference created from a variable reads it and releases its handle.</summary>
        [Test]
        public void ReadOnlyRef_FromVariable_ReadsAndDisposes()
        {
            var value = 11;
            var reference = new ReadOnlyRef<int>(ref value);

            value = 12;

            Assert.That(reference.Target, Is.EqualTo(12));
            Assert.DoesNotThrow(() => reference.Dispose());
        }
    }
}
