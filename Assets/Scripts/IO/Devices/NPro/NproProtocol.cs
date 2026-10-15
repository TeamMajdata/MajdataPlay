#if UNITY_STANDALONE
using System;
using UnityEngine;

namespace MajdataPlay.IO
{
    /// <summary>NPro GAME 管道的输入报文解码与 SEGA 灯板帧编码。</summary>
    internal static class NproProtocol
    {
        /// <summary>输入报告长度：6 字节触摸掩码 + 外键 + 扩展键。</summary>
        internal const int InputReportLength = 8;

        /// <summary>外键槽位数，顺序与 <see cref="ButtonZone"/> 一致。</summary>
        internal const int ButtonCount = 12;

        /// <summary>触摸槽位数；索引 0-33 有效，34 为保留槽位。</summary>
        internal const int SensorCount = 35;

        /// <summary>触摸掩码的位数：A/B/D/E 各 8 个传感器加 C 区 2 个。</summary>
        internal const int ActiveSensorCount = 34;

        /// <summary>灯板帧转义后的最大长度，取自最长的 0x31 单灯区命令。</summary>
        internal const int MaxLedFrameLength = 24;

        const byte Sync = 0xE0;
        const byte Escape = 0xD0;
        const byte DstNodeId = 0x11;
        const byte SrcNodeId = 0x01;
        const byte CmdSetLedGs8Bit = 0x31;
        const byte CmdSetLedGsUpdate = 0x3C;

        /// <summary>解码一份完整的 GAME 管道输入报告。</summary>
        /// <param name="report">长度至少为 <see cref="InputReportLength"/> 的报告数据。</param>
        /// <param name="buttons">接收 <see cref="ButtonCount"/> 个外键状态。</param>
        /// <param name="sensors">接收 <see cref="SensorCount"/> 个触摸槽位。</param>
        /// <exception cref="ArgumentException"><paramref name="report"/>、<paramref name="buttons"/> 或 <paramref name="sensors"/> 长度不足时抛出。</exception>
        internal static void DecodeInputReport(ReadOnlySpan<byte> report, Span<bool> buttons, Span<bool> sensors)
        {
            if (report.Length < InputReportLength)
            {
                throw new ArgumentException($"NPro 输入报告至少需要 {InputReportLength} 字节。", nameof(report));
            }
            if (buttons.Length < ButtonCount)
            {
                throw new ArgumentException($"NPro 外键至少需要 {ButtonCount} 个槽位。", nameof(buttons));
            }
            if (sensors.Length < SensorCount)
            {
                throw new ArgumentException($"NPro 触摸至少需要 {SensorCount} 个槽位。", nameof(sensors));
            }

            buttons.Clear();
            sensors.Clear();

            var touch = 0UL;
            for (var i = 0; i < 6; i++)
            {
                touch |= (ulong)report[i] << (i * 8);
            }
            for (var i = 0; i < ActiveSensorCount; i++)
            {
                sensors[i] = (touch & (1UL << i)) != 0;
            }

            var gameButtons = report[6];
            for (var i = 0; i < 8; i++)
            {
                buttons[i] = (gameButtons & (1 << i)) != 0;
            }

            // 扩展键低 4 位依次是 Select P1、Service、Select P2、Test。
            var extButtons = report[7];
            buttons[9] = (extButtons & 0b0001) != 0;
            buttons[10] = (extButtons & 0b0010) != 0;
            buttons[11] = (extButtons & 0b0100) != 0;
            buttons[8] = (extButtons & 0b1000) != 0;
        }

        /// <summary>把 0x31 单灯区颜色命令编码成转义后的灯板帧。</summary>
        /// <param name="destination">接收转义后帧数据的缓冲，至少 <see cref="MaxLedFrameLength"/> 字节。</param>
        /// <param name="index">灯区索引，0-7。</param>
        /// <param name="color">灯区颜色。</param>
        /// <param name="brightness">亮度系数，在序列化时乘进颜色分量。</param>
        /// <returns>写入 <paramref name="destination"/> 的字节数。</returns>
        internal static int BuildLedColorFrame(Span<byte> destination, int index, Color color, float brightness)
        {
            Span<byte> payload = stackalloc byte[4];
            payload[0] = (byte)index;
            payload[1] = (byte)(color.r * 255 * brightness);
            payload[2] = (byte)(color.g * 255 * brightness);
            payload[3] = (byte)(color.b * 255 * brightness);
            return BuildLedFrame(destination, CmdSetLedGs8Bit, payload);
        }

        /// <summary>把 0x3C 刷新命令编码成转义后的灯板帧；该命令幂等，同时用作灯光保活。</summary>
        /// <param name="destination">接收转义后帧数据的缓冲，至少 <see cref="MaxLedFrameLength"/> 字节。</param>
        /// <returns>写入 <paramref name="destination"/> 的字节数。</returns>
        internal static int BuildLedUpdateFrame(Span<byte> destination) =>
            BuildLedFrame(destination, CmdSetLedGsUpdate, ReadOnlySpan<byte>.Empty);

        /// <summary>把一条 SEGA 灯板命令编码成转义后的帧。</summary>
        /// <param name="destination">接收转义后帧数据的缓冲，至少 <see cref="MaxLedFrameLength"/> 字节。</param>
        /// <param name="command">命令字。</param>
        /// <param name="payload">命令负载，长度不超过 4 字节。</param>
        /// <returns>写入 <paramref name="destination"/> 的字节数。</returns>
        internal static int BuildLedFrame(Span<byte> destination, byte command, ReadOnlySpan<byte> payload)
        {
            // 明文帧：E0 | dst | src | len | cmd | payload | sum，len 含命令字本身。
            Span<byte> plain = stackalloc byte[6 + 4];
            plain[0] = Sync;
            plain[1] = DstNodeId;
            plain[2] = SrcNodeId;
            plain[3] = (byte)(1 + payload.Length);
            plain[4] = command;
            payload.CopyTo(plain.Slice(5));

            var plainLength = 5 + payload.Length;
            byte sum = 0;
            for (var i = 1; i < plainLength; i++)
            {
                sum += plain[i];
            }
            plain[plainLength++] = sum;

            // 帧头原样保留，负载中的同步字与转义符要按协议转义。
            destination[0] = Sync;
            var length = 1;
            for (var i = 1; i < plainLength; i++)
            {
                var value = plain[i];
                if (value == Sync || value == Escape)
                {
                    destination[length++] = Escape;
                    destination[length++] = (byte)(value - 1);
                }
                else
                {
                    destination[length++] = value;
                }
            }
            return length;
        }
    }
}
#endif
