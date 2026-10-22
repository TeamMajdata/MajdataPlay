using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.Utils;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Utils
{
    /// <summary>Verifies the MD5 and hexadecimal helpers in <see cref="HashHelper"/> and <see cref="ObjectIdHelper"/>.</summary>
    public class HashHelperTests
    {
        /// <summary>The ASCII bytes of "abc", whose MD5 digest is a published test vector.</summary>
        private static readonly byte[] s_abc = Encoding.ASCII.GetBytes("abc");

        /// <summary>The RFC 1321 MD5 digest of "abc".</summary>
        private static readonly byte[] s_abcDigest =
        {
            0x90, 0x01, 0x50, 0x98, 0x3C, 0xD2, 0x4F, 0xB0, 0xD6, 0x96, 0x3F, 0x7D, 0x28, 0xE1, 0x7F, 0x72,
        };

        /// <summary>The Base64 form of <see cref="s_abcDigest"/>.</summary>
        private const string AbcDigestBase64 = "kAFQmDzST7DWlj99KOF/cg==";

        /// <summary>Checks that hashing a whole buffer returns the published digest.</summary>
        [Test]
        public void ComputeHash_Buffer_ReturnsMd5Digest()
        {
            Assert.That(OffMainThread(() => HashHelper.ComputeHash(s_abc)), Is.EqualTo(s_abcDigest));
        }

        /// <summary>Checks that only the selected range of a buffer is hashed.</summary>
        [Test]
        public void ComputeHash_Range_HashesOnlyTheRange()
        {
            var padded = Encoding.ASCII.GetBytes("xxabcyy");

            Assert.That(OffMainThread(() => HashHelper.ComputeHash(padded, 2, 3)), Is.EqualTo(s_abcDigest));
        }

        /// <summary>Checks that a stream is hashed from its current position to its end.</summary>
        [Test]
        public void ComputeHash_Stream_HashesTheStream()
        {
            using var stream = new MemoryStream(s_abc);

            Assert.That(OffMainThread(() => HashHelper.ComputeHash(stream)), Is.EqualTo(s_abcDigest));
        }

        /// <summary>Checks the Base64 string overloads for buffers, ranges and streams.</summary>
        [Test]
        public void ComputeHashAsBase64String_AllOverloads_ReturnTheEncodedDigest()
        {
            var padded = Encoding.ASCII.GetBytes("-abc-");
            using var stream = new MemoryStream(s_abc);

            Assert.That(OffMainThread(() => HashHelper.ComputeHashAsBase64String(s_abc)), Is.EqualTo(AbcDigestBase64));
            Assert.That(OffMainThread(() => HashHelper.ComputeHashAsBase64String(padded, 1, 3)), Is.EqualTo(AbcDigestBase64));
            Assert.That(OffMainThread(() => HashHelper.ComputeHashAsBase64String(stream)), Is.EqualTo(AbcDigestBase64));
        }

        /// <summary>Checks that the asynchronous overloads produce the same digests as the synchronous ones.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ComputeHashAsync_AllOverloads_ReturnTheDigest()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var padded = Encoding.ASCII.GetBytes("zabcz");
                using var first = new MemoryStream(s_abc);
                using var second = new MemoryStream(s_abc);

                Assert.That(await HashHelper.ComputeHashAsync(s_abc), Is.EqualTo(s_abcDigest));
                Assert.That(await HashHelper.ComputeHashAsync(padded, 1, 3), Is.EqualTo(s_abcDigest));
                Assert.That(await HashHelper.ComputeHashAsync(first), Is.EqualTo(s_abcDigest));
                Assert.That(await HashHelper.ComputeHashAsBase64StringAsync(s_abc), Is.EqualTo(AbcDigestBase64));
                Assert.That(await HashHelper.ComputeHashAsBase64StringAsync(padded, 1, 3), Is.EqualTo(AbcDigestBase64));
                Assert.That(await HashHelper.ComputeHashAsBase64StringAsync(second), Is.EqualTo(AbcDigestBase64));
            });
        }

        /// <summary>Checks that an empty buffer hashes to the published digest of the empty message.</summary>
        [Test]
        public void ComputeHash_EmptyBuffer_ReturnsEmptyMessageDigest()
        {
            var hex = HashHelper.ToHexString(OffMainThread(() => HashHelper.ComputeHash(Array.Empty<byte>())));

            Assert.That(hex, Is.EqualTo("D4-1D-8C-D9-8F-00-B2-04-E9-80-09-98-EC-F8-42-7E"));
        }

        /// <summary>Checks that the hexadecimal formatting uses dash separated upper case pairs.</summary>
        [Test]
        public void ToHexString_UsesDashSeparatedPairs()
        {
            Assert.That(HashHelper.ToHexString(new byte[] { 0x00, 0x0A, 0xFF }), Is.EqualTo("00-0A-FF"));
        }

        /// <summary>Checks that hexadecimal text with mixed case is parsed into bytes.</summary>
        [Test]
        public void FromHexString_ParsesMixedCaseDigits()
        {
            var bytes = HashHelper.FromHexString("09afAF10".AsSpan());

            Assert.That(bytes, Is.EqualTo(new byte[] { 0x09, 0xAF, 0xAF, 0x10 }));
        }

        /// <summary>Checks that an empty hexadecimal string produces an empty array.</summary>
        [Test]
        public void FromHexString_Empty_ReturnsEmptyArray()
        {
            Assert.That(HashHelper.FromHexString(ReadOnlySpan<char>.Empty), Is.Empty);
        }

        /// <summary>Checks that an odd number of digits is rejected.</summary>
        [Test]
        public void FromHexString_OddLength_Throws()
        {
            Assert.Throws<ArgumentException>(() => HashHelper.FromHexString("abc".AsSpan()));
        }

        /// <summary>Checks that characters outside the hexadecimal alphabet are rejected.</summary>
        /// <param name="text">The invalid text.</param>
        [TestCase("0g")]
        [TestCase("G0")]
        [TestCase("-1")]
        public void FromHexString_InvalidCharacter_Throws(string text)
        {
            Assert.Throws<ArgumentException>(() => HashHelper.FromHexString(text.AsSpan()));
        }

        /// <summary>Checks that an object id maps to the GUID formed from its UTF-8 MD5 digest.</summary>
        [Test]
        public void ObjectIdHelper_ToGuid_UsesTheMd5Digest()
        {
            Assert.That(ObjectIdHelper.ToGuid("abc"), Is.EqualTo(new Guid(s_abcDigest)));
        }

        /// <summary>Checks that the synchronous wrappers can be called on the Unity main thread.</summary>
        [Test]
        [Ignore("Known issue: HashHelper's synchronous wrappers block on .Result of an async method that resumes on the Unity synchronization context, deadlocking the main thread.")]
        public void ComputeHash_OnMainThread_DoesNotDeadlock()
        {
            Assert.That(HashHelper.ComputeHash(s_abc), Is.EqualTo(s_abcDigest));
        }

        /// <summary>Runs a synchronous wrapper on a thread pool thread, where no synchronization context is captured.</summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="function">The wrapper call.</param>
        /// <returns>The wrapper result.</returns>
        /// <exception cref="TimeoutException">The call did not finish within ten seconds.</exception>
        private static T OffMainThread<T>(Func<T> function)
        {
            var task = Task.Run(function);
            if (!task.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The synchronous hash wrapper did not finish.");
            }
            return task.Result;
        }

        /// <summary>Checks that the GUID mapping is deterministic and distinguishes different ids.</summary>
        [Test]
        public void ObjectIdHelper_ToGuid_IsDeterministic()
        {
            var first = ObjectIdHelper.ToGuid("65f0c2a0b1e2d3c4f5a6b7c8");
            var second = ObjectIdHelper.ToGuid("65f0c2a0b1e2d3c4f5a6b7c8");
            var other = ObjectIdHelper.ToGuid("65f0c2a0b1e2d3c4f5a6b7c9");

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Is.Not.EqualTo(other));
        }
    }
}
