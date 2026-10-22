using System;
using MajdataPlay.IO;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>Verifies the NPro input report decoding, LED frame encoding and report framing.</summary>
    public class NproProtocolTests
    {
        /// <summary>Checks that every touch bit and button bit lands in the documented slot.</summary>
        [Test]
        public void DecodeInputReport_MapsTouchMaskAndButtons()
        {
            var report = new byte[] { 0x01, 0x00, 0x01, 0x00, 0x06, 0x80, 0b1000_0001, 0b0000_1011 };
            var buttons = new bool[NproProtocol.ButtonCount];
            var sensors = new bool[NproProtocol.SensorCount];

            NproProtocol.DecodeInputReport(report, buttons, sensors);

            Assert.That(sensors[0], Is.True, "A1");
            Assert.That(sensors[16], Is.True, "C1");
            Assert.That(sensors[33], Is.True, "E8");
            Assert.That(CountTrue(sensors), Is.EqualTo(3));
            Assert.That(buttons[0], Is.True, "button 1");
            Assert.That(buttons[7], Is.True, "button 8");
            Assert.That(buttons[9], Is.True, "select P1");
            Assert.That(buttons[10], Is.True, "service");
            Assert.That(buttons[11], Is.False, "select P2");
            Assert.That(buttons[8], Is.True, "test");
            Assert.That(CountTrue(buttons), Is.EqualTo(5));
        }

        /// <summary>Checks that stale destination values are cleared before decoding.</summary>
        [Test]
        public void DecodeInputReport_ClearsDestinations()
        {
            var buttons = new bool[NproProtocol.ButtonCount];
            var sensors = new bool[NproProtocol.SensorCount];
            Array.Fill(buttons, true);
            Array.Fill(sensors, true);

            NproProtocol.DecodeInputReport(new byte[8], buttons, sensors);

            Assert.That(CountTrue(buttons), Is.EqualTo(0));
            Assert.That(CountTrue(sensors), Is.EqualTo(0));
        }

        /// <summary>Checks that every undersized argument is rejected.</summary>
        [Test]
        public void DecodeInputReport_ShortArguments_Throw()
        {
            Assert.Throws<ArgumentException>(() => NproProtocol.DecodeInputReport(new byte[7], new bool[12], new bool[35]));
            Assert.Throws<ArgumentException>(() => NproProtocol.DecodeInputReport(new byte[8], new bool[11], new bool[35]));
            Assert.Throws<ArgumentException>(() => NproProtocol.DecodeInputReport(new byte[8], new bool[12], new bool[34]));
        }

        /// <summary>Checks the encoding of the update command, which needs no escaping.</summary>
        [Test]
        public void BuildLedUpdateFrame_EncodesTheRefreshCommand()
        {
            var frame = new byte[NproProtocol.MaxLedFrameLength];

            var length = NproProtocol.BuildLedUpdateFrame(frame);

            Assert.That(frame.AsSpan(0, length).ToArray(), Is.EqualTo(new byte[] { 0xE0, 0x11, 0x01, 0x01, 0x3C, 0x4F }));
        }

        /// <summary>Checks the encoding of a colour command including the brightness factor and checksum.</summary>
        [Test]
        public void BuildLedColorFrame_EncodesIndexColourAndChecksum()
        {
            var frame = new byte[NproProtocol.MaxLedFrameLength];

            var length = NproProtocol.BuildLedColorFrame(frame, 2, new Color(1f, 0.5f, 0f), 0.5f);

            Assert.That(frame.AsSpan(0, length).ToArray(), Is.EqualTo(new byte[] { 0xE0, 0x11, 0x01, 0x05, 0x31, 0x02, 0x7F, 0x3F, 0x00, 0x08 }));
        }

        /// <summary>Checks that sync and escape bytes inside the frame are escaped.</summary>
        [Test]
        public void BuildLedFrame_EscapesSyncAndEscapeBytes()
        {
            var frame = new byte[NproProtocol.MaxLedFrameLength];

            var length = NproProtocol.BuildLedFrame(frame, 0x31, new byte[] { 0x00, 0xE0, 0xD0, 0x00 });

            var expectedChecksum = unchecked((byte)(0x11 + 0x01 + 0x05 + 0x31 + 0xE0 + 0xD0));
            Assert.That(frame.AsSpan(0, length).ToArray(), Is.EqualTo(new byte[]
            {
                0xE0, 0x11, 0x01, 0x05, 0x31, 0x00, 0xD0, 0xDF, 0xD0, 0xCF, 0x00, expectedChecksum,
            }));
        }

        /// <summary>Checks that the framer returns whole reports only and keeps a partial tail.</summary>
        [Test]
        public void Framer_SplitsChunksIntoReports()
        {
            var framer = new NproReportFramer();
            var report = new byte[NproReportFramer.ReportLength];

            framer.Append(new byte[] { 1, 2, 3, 4, 5 });
            Assert.That(framer.TryReadReport(report), Is.False);

            framer.Append(new byte[] { 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 });
            Assert.That(framer.TryReadReport(report), Is.True);
            Assert.That(report, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
            Assert.That(framer.TryReadReport(report), Is.True);
            Assert.That(report, Is.EqualTo(new byte[] { 9, 10, 11, 12, 13, 14, 15, 16 }));
            Assert.That(framer.TryReadReport(report), Is.False);
        }

        /// <summary>Checks that clearing drops a partial report.</summary>
        [Test]
        public void Framer_Clear_DropsPartialReport()
        {
            var framer = new NproReportFramer();
            var report = new byte[NproReportFramer.ReportLength];
            framer.Append(new byte[] { 1, 2, 3 });

            framer.Clear();
            framer.Append(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 });

            Assert.That(framer.TryReadReport(report), Is.True);
            Assert.That(report, Is.EqualTo(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 }));
        }

        /// <summary>Checks that oversized chunks are rejected and a maximal chunk fits after a partial report.</summary>
        [Test]
        public void Framer_ChunkLimits()
        {
            var framer = new NproReportFramer();
            Assert.Throws<ArgumentOutOfRangeException>(() => framer.Append(new byte[NproReportFramer.MaxChunkLength + 1]));

            framer.Append(new byte[NproReportFramer.ReportLength - 1]);
            Assert.DoesNotThrow(() => framer.Append(new byte[NproReportFramer.MaxChunkLength]));
            var report = new byte[NproReportFramer.ReportLength];
            var count = 0;
            while (framer.TryReadReport(report))
            {
                count++;
            }
            Assert.That(count, Is.EqualTo((NproReportFramer.MaxChunkLength + NproReportFramer.ReportLength - 1) / NproReportFramer.ReportLength));
        }

        /// <summary>Counts the set flags.</summary>
        /// <param name="values">The flags.</param>
        /// <returns>The number of true values.</returns>
        private static int CountTrue(bool[] values)
        {
            var count = 0;
            foreach (var value in values)
            {
                if (value)
                {
                    count++;
                }
            }
            return count;
        }
    }
}
