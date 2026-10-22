using MajdataPlay.Settings;
using MajdataPlay.Utils;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Utils
{
    /// <summary>Verifies the left-right and upside-down mirroring of Simai note text in <see cref="SimaiMirror"/>.</summary>
    public class SimaiMirrorTests
    {
        /// <summary>Checks that every button position is mirrored horizontally.</summary>
        [Test]
        public void LeftRightMirror_MapsEveryButtonPosition()
        {
            var result = SimaiMirror.NoteMirrorHandle("1,2,3,4,5,6,7,8", MirrorOption.LRMirror);

            Assert.That(result, Is.EqualTo("8,7,6,5,4,3,2,1"));
        }

        /// <summary>Checks that every button position is mirrored vertically.</summary>
        [Test]
        public void UpsideDownMirror_MapsEveryButtonPosition()
        {
            var result = SimaiMirror.NoteMirrorHandle("1,2,3,4,5,6,7,8", MirrorOption.UDMirror);

            Assert.That(result, Is.EqualTo("4,3,2,1,8,7,6,5"));
        }

        /// <summary>Checks that the off option leaves the text unchanged.</summary>
        [Test]
        public void Off_LeavesTextUnchanged()
        {
            const string Source = "1-5[4:1],2h[2:1],E1,(180){8}3";

            Assert.That(SimaiMirror.NoteMirrorHandle(Source, MirrorOption.Off), Is.EqualTo(Source));
        }

        /// <summary>Checks that an empty chart stays empty.</summary>
        [Test]
        public void EmptyText_ReturnsEmptyText()
        {
            Assert.That(SimaiMirror.NoteMirrorHandle(string.Empty, MirrorOption.LRMirror), Is.Empty);
        }

        /// <summary>Checks that slide shapes swap their direction characters when mirrored horizontally.</summary>
        /// <param name="source">The source note.</param>
        /// <param name="expected">The expected mirrored note.</param>
        [TestCase("1-5", "8-4")]
        [TestCase("1>5", "8<4")]
        [TestCase("1<5", "8>4")]
        [TestCase("1q5", "8p4")]
        [TestCase("1p5", "8q4")]
        [TestCase("1z5", "8s4")]
        [TestCase("1s5", "8z4")]
        public void LeftRightMirror_SwapsSlideDirections(string source, string expected)
        {
            Assert.That(SimaiMirror.NoteMirrorHandle(source, MirrorOption.LRMirror), Is.EqualTo(expected));
        }

        /// <summary>Checks the vertical mirror of slide shapes, which keeps angle brackets but swaps curve characters.</summary>
        /// <param name="source">The source note.</param>
        /// <param name="expected">The expected mirrored note.</param>
        [TestCase("1>5", "4>8")]
        [TestCase("1q5", "4p8")]
        [TestCase("1z5", "4s8")]
        public void UpsideDownMirror_MapsSlides(string source, string expected)
        {
            Assert.That(SimaiMirror.NoteMirrorHandle(source, MirrorOption.UDMirror), Is.EqualTo(expected));
        }

        /// <summary>Checks that timing values inside square brackets are never mirrored.</summary>
        [Test]
        public void BracketContent_IsNotMirrored()
        {
            var result = SimaiMirror.NoteMirrorHandle("1h[4:1],2-6[8:3]", MirrorOption.LRMirror);

            Assert.That(result, Is.EqualTo("8h[4:1],7-3[8:3]"));
        }

        /// <summary>Checks that D and E touch areas use the special horizontal mapping.</summary>
        /// <param name="source">The source touch.</param>
        /// <param name="expected">The expected mirrored touch.</param>
        [TestCase("E1", "E1")]
        [TestCase("E2", "E8")]
        [TestCase("E5", "E5")]
        [TestCase("D3", "D7")]
        [TestCase("D4", "D6")]
        [TestCase("B2", "B7")]
        [TestCase("A1", "A8")]
        [TestCase("C", "C")]
        [TestCase("E9", "E9")]
        [TestCase("Ex", "Ex")]
        public void LeftRightMirror_UsesSpecialTouchMapping(string source, string expected)
        {
            Assert.That(SimaiMirror.NoteMirrorHandle(source, MirrorOption.LRMirror), Is.EqualTo(expected));
        }

        /// <summary>Checks that D and E touch areas use the special vertical mapping.</summary>
        /// <param name="source">The source touch.</param>
        /// <param name="expected">The expected mirrored touch.</param>
        [TestCase("E1", "E5")]
        [TestCase("E3", "E3")]
        [TestCase("D2", "D4")]
        [TestCase("D8", "D6")]
        [TestCase("B1", "B4")]
        public void UpsideDownMirror_UsesSpecialTouchMapping(string source, string expected)
        {
            Assert.That(SimaiMirror.NoteMirrorHandle(source, MirrorOption.UDMirror), Is.EqualTo(expected));
        }

        /// <summary>Checks that BPM, beat and comment-like groups are copied verbatim.</summary>
        [Test]
        public void ParenthesisAndBraceGroups_AreCopiedVerbatim()
        {
            var result = SimaiMirror.NoteMirrorHandle("(120){4}1,2", MirrorOption.LRMirror);

            Assert.That(result, Is.EqualTo("(120){4}8,7"));
        }

        /// <summary>Checks that a complete hi-speed marker is copied verbatim while the following notes are mirrored.</summary>
        [Test]
        public void HiSpeedMarker_IsCopiedVerbatim()
        {
            var result = SimaiMirror.NoteMirrorHandle("<HS*0.5>1,2", MirrorOption.LRMirror);

            Assert.That(result, Is.EqualTo("<HS*0.5>8,7"));
        }

        /// <summary>Checks that whitespace between hi-speed marker characters does not break its detection.</summary>
        [Test]
        public void HiSpeedMarkerWithWhitespace_IsStillRecognized()
        {
            var result = SimaiMirror.NoteMirrorHandle("< HS*2>1", MirrorOption.LRMirror);

            Assert.That(result, Is.EqualTo("< HS*2>8"));
        }

        /// <summary>Checks that an angle bracket that does not start a hi-speed marker is mirrored as a slide.</summary>
        [Test]
        public void IncompleteHiSpeedMarker_IsTreatedAsSlide()
        {
            var result = SimaiMirror.NoteMirrorHandle("1<5,3", MirrorOption.LRMirror);

            Assert.That(result, Is.EqualTo("8>4,6"));
        }

        /// <summary>Checks that whitespace keeps its position in the mirrored text.</summary>
        [Test]
        public void Whitespace_KeepsItsPosition()
        {
            var result = SimaiMirror.NoteMirrorHandle("1 , 2\n3", MirrorOption.LRMirror);

            Assert.That(result, Is.EqualTo("8 , 7\n6"));
        }

        /// <summary>Checks that each separator character ends a part and the remaining parts are mirrored too.</summary>
        [Test]
        public void EachSeparator_EndsAPart()
        {
            var result = SimaiMirror.NoteMirrorHandle("1/2`3,4", MirrorOption.LRMirror);

            Assert.That(result, Is.EqualTo("8/7`6,5"));
        }

        /// <summary>Checks that mirroring twice restores the original chart text.</summary>
        /// <param name="option">The mirror option applied twice.</param>
        [TestCase(MirrorOption.LRMirror)]
        [TestCase(MirrorOption.UDMirror)]
        public void MirroringTwice_RestoresTheOriginal(MirrorOption option)
        {
            const string Source = "(150){8}1-5[4:1],2h[2:1],E2,D3,B4/7,8q2[8:1]";

            var once = SimaiMirror.NoteMirrorHandle(Source, option);
            var twice = SimaiMirror.NoteMirrorHandle(once, option);

            Assert.That(once, Is.Not.EqualTo(Source));
            Assert.That(twice, Is.EqualTo(Source));
        }
    }
}
