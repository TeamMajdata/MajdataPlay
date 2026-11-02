using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MajdataPlay.Net.Curl.Core.PInvoke
{
    /// <summary>
    /// libcurl CURLINFO enumeration.
    /// Corresponds to curl_easy_getinfo() CURLINFO_* definitions.
    /// </summary>
    internal enum CurlInfo
    {
        None = 0,

        // ============================================================
        // STRING (0x100000)
        // ============================================================

        /// <summary>
        /// Last used effective URL.
        /// char**
        /// </summary>
        EffectiveUrl = 0x100001,

        /// <summary>
        /// Content-Type from response header.
        /// char**
        /// </summary>
        ContentType = 0x100012,

        /// <summary>
        /// Private data pointer set by <see cref="CurlOption.Private"/>.
        /// char**
        /// </summary>
        Private = 0x100015,

        /// <summary>
        /// Primary IP address.
        /// char**
        /// </summary>
        PrimaryIp = 0x100020,

        /// <summary>
        /// Local IP address.
        /// char**
        /// </summary>
        LocalIp = 0x100029,

        /// <summary>
        /// FTP entry path.
        /// char**
        /// </summary>
        FtpEntryPath = 0x10002A,

        /// <summary>
        /// Redirect URL.
        /// char**
        /// </summary>
        RedirectUrl = 0x10001F,

        /// <summary>
        /// TLS session information.
        /// </summary>
        TlsSession = 0x400030,


        // ============================================================
        // LONG (0x200000)
        // ============================================================

        /// <summary>
        /// HTTP response code.
        /// long*
        /// </summary>
        ResponseCode = 0x200002,

        /// <summary>
        /// Total time spent resolving host.
        /// </summary>
        HeaderSize = 0x20000B,

        /// <summary>
        /// Request size.
        /// </summary>
        RequestSize = 0x20000C,

        /// <summary>
        /// SSL verification result.
        /// </summary>
        SslVerifyResult = 0x20000D,

        /// <summary>
        /// Remote file timestamp.
        /// </summary>
        FileTime = 0x20000E,

        /// <summary>
        /// Number of redirects.
        /// </summary>
        RedirectCount = 0x200014,

        /// <summary>
        /// Number of connections created.
        /// </summary>
        NumConnects = 0x20001A,

        /// <summary>
        /// Number of authentication attempts.
        /// </summary>
        HttpAuthAvail = 0x20001B,

        /// <summary>
        /// Proxy authentication availability.
        /// </summary>
        ProxyAuthAvail = 0x20001C,

        /// <summary>
        /// OS errno.
        /// </summary>
        OsErrno = 0x200019,

        /// <summary>
        /// SSL engines count.
        /// </summary>
        SslEngines = 0x40001F,


        /// <summary>
        /// HTTP version used.
        /// </summary>
        HttpVersion = 0x20002E,

        /// <summary>
        /// Destination port of the most recent connection.
        /// long*
        /// </summary>
        PrimaryPort = 0x200028,

        /// <summary>
        /// Filetime is valid.
        /// </summary>
        FileTimeT = 0x60000E,


        // ============================================================
        // DOUBLE (0x300000)
        // ============================================================

        /// <summary>
        /// Total transfer time.
        /// double*
        /// </summary>
        TotalTime = 0x300003,

        /// <summary>
        /// DNS lookup time.
        /// </summary>
        NameLookupTime = 0x300004,

        /// <summary>
        /// Connection establishment time.
        /// </summary>
        ConnectTime = 0x300005,

        /// <summary>
        /// Time before transfer starts.
        /// </summary>
        PreTransferTime = 0x300006,

        /// <summary>
        /// Upload size.
        /// </summary>
        SizeUpload = 0x300007,

        /// <summary>
        /// Download size.
        /// </summary>
        SizeDownload = 0x300008,

        /// <summary>
        /// Download speed.
        /// </summary>
        SpeedDownload = 0x300009,

        /// <summary>
        /// Upload speed.
        /// </summary>
        SpeedUpload = 0x30000A,

        /// <summary>
        /// Content length download.
        /// </summary>
        ContentLengthDownload = 0x30000F,

        /// <summary>
        /// Content length upload.
        /// </summary>
        ContentLengthUpload = 0x300010,


        // ============================================================
        // OFF_T (0x600000)
        // ============================================================

        /// <summary>
        /// Upload size as curl_off_t.
        /// </summary>
        SizeUploadT = 0x600007,

        /// <summary>
        /// Download size as curl_off_t.
        /// </summary>
        SizeDownloadT = 0x600008,

        /// <summary>
        /// Download speed as curl_off_t.
        /// </summary>
        SpeedDownloadT = 0x600009,

        /// <summary>
        /// Upload speed as curl_off_t.
        /// </summary>
        SpeedUploadT = 0x60000A,

        /// <summary>
        /// File time as curl_off_t.
        /// </summary>
        FileTimeT_Off = 0x60000E,

        /// <summary>
        /// Content length download as curl_off_t.
        /// </summary>
        ContentLengthDownloadT = 0x60000F,

        /// <summary>
        /// Content length upload as curl_off_t.
        /// </summary>
        ContentLengthUploadT = 0x600010,

        /// <summary>
        /// Start transfer time in microseconds.
        /// </summary>
        StartTransferTimeT = 0x600036,

        /// <summary>
        /// Total transfer time in microseconds, including name resolving, connecting and every redirect.
        /// </summary>
        TotalTimeT = 0x600032,

        /// <summary>
        /// Time in microseconds from the start until name resolving completed.
        /// </summary>
        NameLookupTimeT = 0x600033,

        /// <summary>
        /// Time in microseconds from the start until the connection to the remote host or proxy completed.
        /// </summary>
        ConnectTimeT = 0x600034,

        /// <summary>
        /// Time in microseconds from the start until the file transfer was just about to begin.
        /// </summary>
        PreTransferTimeT = 0x600035,

        /// <summary>
        /// Time in microseconds taken by every redirect step before the final transfer started.
        /// </summary>
        RedirectTimeT = 0x600037,

        /// <summary>
        /// Time in microseconds from the start until the SSL/TLS handshake completed.
        /// </summary>
        AppConnectTimeT = 0x600038,

        /// <summary>
        /// Time in microseconds from the start until the last request byte was sent (libcurl 8.10.0 and later).
        /// </summary>
        PostTransferTimeT = 0x600043,

        /// <summary>
        /// Identifier of the last connection used by the transfer, unique within its connection pool, or -1
        /// (libcurl 8.2.0 and later).
        /// </summary>
        ConnId = 0x600040,


        // ============================================================
        // SOCKET
        // ============================================================

        /// <summary>
        /// Active socket.
        /// curl_socket_t*
        /// </summary>
        Activesocket = 0x50001E,


        // ============================================================
        // Misc
        // ============================================================

        /// <summary>
        /// CURLINFO_LASTONE marker.
        /// </summary>
        LastOne = 0x600037
    }
}
