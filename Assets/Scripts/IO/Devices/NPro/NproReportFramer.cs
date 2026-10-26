#if UNITY_STANDALONE
using System;

namespace MajdataPlay.IO
{
    /// <summary>
    /// 把 GAME 管道的 bulk 字节流按固定长度的输入报告重组。
    /// bulk 是字节流，一次读取可能拿到多份报告，也可能因为读取边界拿到半份报告。
    /// </summary>
    internal sealed class NproReportFramer
    {
        /// <summary>单份输入报告的长度。</summary>
        internal const int ReportLength = NproProtocol.InputReportLength;

        /// <summary>单次追加的最大长度；与 <c>UsbDevice</c> 的读缓冲保持一致。</summary>
        internal const int MaxChunkLength = 64;

        /// <summary>缓冲容量：一次追加加上一份报告，因此只要追加不超过 <see cref="MaxChunkLength"/> 就不会溢出。</summary>
        const int Capacity = MaxChunkLength + ReportLength - 1;

        readonly byte[] _pending = new byte[Capacity];
        int _count;

        /// <summary>追加一段字节流。</summary>
        /// <param name="chunk">长度不超过 <see cref="MaxChunkLength"/> 的字节流。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="chunk"/> 超过 <see cref="MaxChunkLength"/> 时抛出。</exception>
        internal void Append(ReadOnlySpan<byte> chunk)
        {
            if (chunk.Length > MaxChunkLength)
            {
                throw new ArgumentOutOfRangeException(nameof(chunk), chunk.Length, $"NPro 单次追加不能超过 {MaxChunkLength} 字节。");
            }
            chunk.CopyTo(_pending.AsSpan(_count));
            _count += chunk.Length;
        }

        /// <summary>取出一份完整的输入报告。</summary>
        /// <param name="report">接收报告数据的跨度，长度至少为 <see cref="ReportLength"/>。</param>
        /// <returns>取到完整报告时返回 true；数据不足时返回 false。</returns>
        internal bool TryReadReport(Span<byte> report)
        {
            if (_count < ReportLength)
            {
                return false;
            }
            _pending.AsSpan(0, ReportLength).CopyTo(report);
            _pending.AsSpan(ReportLength, _count - ReportLength).CopyTo(_pending);
            _count -= ReportLength;
            return true;
        }

        /// <summary>丢弃缓冲中的半份报告；断开连接时调用，避免重连后残留的数据错位。</summary>
        internal void Clear() => _count = 0;
    }
}
#endif
