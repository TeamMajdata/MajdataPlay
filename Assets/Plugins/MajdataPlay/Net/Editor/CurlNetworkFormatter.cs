#nullable enable
using MajdataPlay.Net.Curl.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MajdataPlay.Net.Editor
{
    /// <summary>
    /// Formats and parses what the Curl Network window shows: sizes, durations, status texts, cookies, query strings,
    /// bodies, timing phases and cURL commands.
    /// </summary>
    internal static class CurlNetworkFormatter
    {
        /// <summary>
        /// The number of bytes shown per hex dump line.
        /// </summary>
        public const int HexBytesPerLine = 16;

        /// <summary>
        /// The number of leading bytes inspected when guessing whether a body is binary.
        /// </summary>
        private const int BinarySniffLength = 4096;

        /// <summary>
        /// The text shown for a value that does not exist yet.
        /// </summary>
        private const string Missing = "-";

        /// <summary>
        /// Formats a byte count with binary units.
        /// </summary>
        /// <param name="bytes">The byte count.</param>
        /// <returns>The count, such as <c>512 B</c>, <c>1.5 KiB</c> or <c>12.3 MiB</c>.</returns>
        public static string FormatSize(long bytes)
        {
            if (bytes < 1024)
            {
                return $"{bytes.ToString(CultureInfo.InvariantCulture)} B";
            }
            if (bytes < 1024 * 1024)
            {
                return $"{(bytes / 1024d).ToString("0.0", CultureInfo.InvariantCulture)} KiB";
            }
            if (bytes < 1024L * 1024 * 1024)
            {
                return $"{(bytes / (1024d * 1024)).ToString("0.0", CultureInfo.InvariantCulture)} MiB";
            }

            return $"{(bytes / (1024d * 1024 * 1024)).ToString("0.00", CultureInfo.InvariantCulture)} GiB";
        }

        /// <summary>
        /// Formats a duration with a precision that suits its magnitude.
        /// </summary>
        /// <param name="duration">The duration, or <see langword="null"/> when it is not known yet.</param>
        /// <returns>The duration, such as <c>0.42 ms</c>, <c>8.5 ms</c>, <c>120 ms</c>, <c>1.25 s</c> or <c>2 min 5 s</c>; <c>-</c> for <see langword="null"/>.</returns>
        public static string FormatDuration(TimeSpan? duration)
        {
            if (duration is not { } value)
            {
                return Missing;
            }
            var milliseconds = Math.Max(0, value.TotalMilliseconds);
            if (milliseconds < 1)
            {
                return $"{milliseconds.ToString("0.00", CultureInfo.InvariantCulture)} ms";
            }
            if (milliseconds < 10)
            {
                return $"{milliseconds.ToString("0.0", CultureInfo.InvariantCulture)} ms";
            }
            if (milliseconds < 1000)
            {
                return $"{milliseconds.ToString("0", CultureInfo.InvariantCulture)} ms";
            }
            if (milliseconds < 60_000)
            {
                return $"{(milliseconds / 1000).ToString("0.00", CultureInfo.InvariantCulture)} s";
            }
            var totalSeconds = (long)(milliseconds / 1000);

            return $"{totalSeconds / 60} min {totalSeconds % 60} s";
        }

        /// <summary>
        /// Formats the Status column the way browsers do.
        /// </summary>
        /// <param name="snapshot">The request.</param>
        /// <returns>The status code once known, otherwise the lifecycle stage in parentheses.</returns>
        public static string FormatStatus(CurlNetworkSnapshot snapshot)
        {
            switch (snapshot.State)
            {
                case CurlNetworkRequestState.Failed:
                    return "(failed)";
                case CurlNetworkRequestState.Cancelled:
                    return "(canceled)";
            }
            if (snapshot.StatusCode > 0)
            {
                return snapshot.StatusCode.ToString(CultureInfo.InvariantCulture);
            }

            return snapshot.State == CurlNetworkRequestState.Queued ? "(queued)" : "(pending)";
        }

        /// <summary>
        /// Formats the status code with its reason phrase.
        /// </summary>
        /// <param name="snapshot">The request.</param>
        /// <returns>Such as <c>404 Not Found</c>, or the lifecycle stage when no status line arrived.</returns>
        public static string FormatStatusWithReason(CurlNetworkSnapshot snapshot)
        {
            if (snapshot.StatusCode <= 0)
            {
                return FormatStatus(snapshot);
            }
            var code = snapshot.StatusCode.ToString(CultureInfo.InvariantCulture);

            return string.IsNullOrEmpty(snapshot.ReasonPhrase) ? code : $"{code} {snapshot.ReasonPhrase}";
        }

        /// <summary>
        /// Gets whether a request is shown as an error: it failed, was cancelled or got a 4xx/5xx status.
        /// </summary>
        /// <param name="snapshot">The request.</param>
        /// <returns><see langword="true"/> for error rows.</returns>
        public static bool IsError(CurlNetworkSnapshot snapshot)
        {
            return snapshot.State == CurlNetworkRequestState.Failed
                || snapshot.State == CurlNetworkRequestState.Cancelled
                || snapshot.StatusCode >= 400;
        }

        /// <summary>
        /// Gets the short resource type of a <c>Content-Type</c>, as the Type column shows it.
        /// </summary>
        /// <param name="contentType">The header value, or <see langword="null"/>.</param>
        /// <returns>The media subtype (<c>json</c>, <c>png</c>, <c>html</c>...), with <c>text</c> for <c>text/plain</c> and <c>binary</c> for <c>application/octet-stream</c>.</returns>
        public static string GetShortType(string? contentType)
        {
            var mediaType = GetMediaType(contentType);
            if (mediaType.Length == 0)
            {
                return string.Empty;
            }
            var subtype = mediaType.Substring(mediaType.IndexOf('/') + 1);
            var suffix = subtype.LastIndexOf('+');
            if (suffix >= 0)
            {
                subtype = subtype.Substring(suffix + 1);
            }
            switch (subtype)
            {
                case "plain":
                    return "text";
                case "octet-stream":
                    return "binary";
                default:
                    return subtype.StartsWith("x-", StringComparison.Ordinal) ? subtype.Substring(2) : subtype;
            }
        }

        /// <summary>
        /// Gets the media type of a <c>Content-Type</c>, without parameters.
        /// </summary>
        /// <param name="contentType">The header value, or <see langword="null"/>.</param>
        /// <returns>The lower-cased media type, such as <c>application/json</c>, or an empty string.</returns>
        public static string GetMediaType(string? contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
            {
                return string.Empty;
            }
            var semicolon = contentType!.IndexOf(';');
            var mediaType = semicolon >= 0 ? contentType.Substring(0, semicolon) : contentType;

            return mediaType.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Gets whether a request matches the text filter: a case-insensitive substring of its URL, method, status,
        /// type, protocol or remote address.
        /// </summary>
        /// <param name="snapshot">The request.</param>
        /// <param name="filter">The filter text; empty matches everything.</param>
        /// <returns><see langword="true"/> when the request matches.</returns>
        public static bool MatchesFilter(CurlNetworkSnapshot snapshot, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                return true;
            }
            filter = filter.Trim();

            return Contains(snapshot.Url, filter)
                || Contains(snapshot.Method, filter)
                || Contains(FormatStatus(snapshot), filter)
                || Contains(GetShortType(snapshot.ContentType), filter)
                || Contains(snapshot.Protocol, filter)
                || Contains(snapshot.RemoteAddress, filter);
        }

        /// <summary>
        /// Parses a <c>Cookie</c> request header.
        /// </summary>
        /// <param name="header">The header value, such as <c>a=1; b=2</c>, or <see langword="null"/>.</param>
        /// <returns>The cookies in header order.</returns>
        public static List<CurlNetworkCookie> ParseRequestCookies(string? header)
        {
            var cookies = new List<CurlNetworkCookie>();
            if (string.IsNullOrWhiteSpace(header))
            {
                return cookies;
            }
            foreach (var part in header!.Split(';'))
            {
                var pair = part.Trim();
                if (pair.Length == 0)
                {
                    continue;
                }
                SplitPair(pair, '=', out var name, out var value);
                cookies.Add(new CurlNetworkCookie { Name = name, Value = value });
            }

            return cookies;
        }

        /// <summary>
        /// Parses a <c>Set-Cookie</c> response header.
        /// </summary>
        /// <param name="header">The header value, such as <c>sid=1; Path=/; HttpOnly</c>.</param>
        /// <returns>The cookie with its attributes; unknown attributes are ignored.</returns>
        public static CurlNetworkCookie ParseSetCookie(string header)
        {
            var parts = header.Split(';');
            SplitPair(parts[0].Trim(), '=', out var name, out var value);
            var domain = default(string?);
            var path = default(string?);
            var expires = default(string?);
            var maxAge = default(string?);
            var sameSite = default(string?);
            var httpOnly = false;
            var secure = false;
            for (var i = 1; i < parts.Length; i++)
            {
                SplitPair(parts[i].Trim(), '=', out var attribute, out var attributeValue);
                switch (attribute.ToLowerInvariant())
                {
                    case "domain":
                        domain = attributeValue;
                        break;
                    case "path":
                        path = attributeValue;
                        break;
                    case "expires":
                        expires = attributeValue;
                        break;
                    case "max-age":
                        maxAge = attributeValue;
                        break;
                    case "samesite":
                        sameSite = attributeValue;
                        break;
                    case "httponly":
                        httpOnly = true;
                        break;
                    case "secure":
                        secure = true;
                        break;
                }
            }

            return new CurlNetworkCookie
            {
                Name = name,
                Value = value,
                Domain = domain,
                Path = path,
                Expires = expires,
                MaxAge = maxAge,
                SameSite = sameSite,
                HttpOnly = httpOnly,
                Secure = secure,
            };
        }

        /// <summary>
        /// Parses the query string of a URL.
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <returns>The decoded parameters in order; empty when the URL has no query or cannot be parsed.</returns>
        public static List<KeyValuePair<string, string>> ParseQuery(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Query.Length <= 1)
            {
                return new List<KeyValuePair<string, string>>();
            }

            return ParseFormUrlEncoded(uri.Query.Substring(1));
        }

        /// <summary>
        /// Parses <c>application/x-www-form-urlencoded</c> text.
        /// </summary>
        /// <param name="text">The encoded text, such as <c>a=1&amp;b=x+y</c>.</param>
        /// <returns>The decoded name and value pairs in order.</returns>
        public static List<KeyValuePair<string, string>> ParseFormUrlEncoded(string text)
        {
            var pairs = new List<KeyValuePair<string, string>>();
            foreach (var part in text.Split('&'))
            {
                if (part.Length == 0)
                {
                    continue;
                }
                SplitPair(part, '=', out var name, out var value);
                pairs.Add(new KeyValuePair<string, string>(UnescapeForm(name), UnescapeForm(value)));
            }

            return pairs;
        }

        /// <summary>
        /// Formats a header block as it appears on the wire.
        /// </summary>
        /// <param name="firstLine">The request or status line, or <see langword="null"/>.</param>
        /// <param name="headers">The headers.</param>
        /// <returns>The lines joined with line feeds.</returns>
        public static string FormatHeaderBlock(string? firstLine, IReadOnlyList<KeyValuePair<string, string>> headers)
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrEmpty(firstLine))
            {
                builder.Append(firstLine).Append('\n');
            }
            foreach (var header in headers)
            {
                builder.Append(header.Key).Append(": ").Append(header.Value).Append('\n');
            }

            return builder.ToString().TrimEnd('\n');
        }

        /// <summary>
        /// Guesses whether a body is binary from its <c>Content-Type</c> and, when that is inconclusive, from control
        /// characters among its leading bytes.
        /// </summary>
        /// <param name="data">The body bytes.</param>
        /// <param name="contentType">The <c>Content-Type</c>, or <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when the body should be shown as a hex dump.</returns>
        public static bool IsBinary(byte[] data, string? contentType)
        {
            var mediaType = GetMediaType(contentType);
            if (mediaType.StartsWith("text/", StringComparison.Ordinal)
                || mediaType.Contains("json")
                || mediaType.Contains("xml")
                || mediaType.Contains("javascript")
                || mediaType == "application/x-www-form-urlencoded")
            {
                return false;
            }
            if (mediaType.StartsWith("image/", StringComparison.Ordinal)
                || mediaType.StartsWith("audio/", StringComparison.Ordinal)
                || mediaType.StartsWith("video/", StringComparison.Ordinal)
                || mediaType.StartsWith("font/", StringComparison.Ordinal)
                || mediaType == "application/octet-stream"
                || mediaType == "application/zip"
                || mediaType == "application/gzip"
                || mediaType == "application/pdf")
            {
                return true;
            }
            var length = Math.Min(data.Length, BinarySniffLength);
            var controlCount = 0;
            for (var i = 0; i < length; i++)
            {
                var value = data[i];
                if (value == 0)
                {
                    return true;
                }
                if (value < 0x09 || (value > 0x0D && value < 0x20))
                {
                    controlCount++;
                }
            }

            return controlCount > length / 50;
        }

        /// <summary>
        /// Gets whether a body should be pretty-printed as JSON.
        /// </summary>
        /// <param name="contentType">The <c>Content-Type</c>, or <see langword="null"/>.</param>
        /// <param name="text">The decoded body.</param>
        /// <returns><see langword="true"/> for a JSON media type, or text that starts like a JSON object or array.</returns>
        public static bool IsJson(string? contentType, string text)
        {
            if (GetMediaType(contentType).Contains("json"))
            {
                return true;
            }
            var trimmed = text.TrimStart();

            return trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal);
        }

        /// <summary>
        /// Decodes a body with the charset of its <c>Content-Type</c>, or UTF-8.
        /// </summary>
        /// <param name="data">The body bytes.</param>
        /// <param name="contentType">The <c>Content-Type</c>, or <see langword="null"/>.</param>
        /// <returns>The text; invalid sequences become replacement characters and a byte order mark is dropped.</returns>
        public static string DecodeText(byte[] data, string? contentType)
        {
            var encoding = GetCharset(contentType) ?? Encoding.UTF8;
            var text = encoding.GetString(data);

            return text.Length > 0 && text[0] == '\uFEFF' ? text.Substring(1) : text;
        }

        /// <summary>
        /// Re-indents JSON text without parsing it, so truncated or slightly invalid bodies still format.
        /// </summary>
        /// <param name="json">The JSON text.</param>
        /// <returns>The text with one member or element per line, indented by two spaces per level.</returns>
        public static string PrettyPrintJson(string json)
        {
            var builder = new StringBuilder(json.Length + (json.Length / 4));
            var indent = 0;
            var isInString = false;
            var isEscaped = false;
            for (var i = 0; i < json.Length; i++)
            {
                var c = json[i];
                if (isInString)
                {
                    builder.Append(c);
                    if (isEscaped)
                    {
                        isEscaped = false;
                    }
                    else if (c == '\\')
                    {
                        isEscaped = true;
                    }
                    else if (c == '"')
                    {
                        isInString = false;
                    }
                    continue;
                }
                switch (c)
                {
                    case '"':
                        isInString = true;
                        builder.Append(c);
                        break;
                    case '{':
                    case '[':
                        var next = NextNonWhitespace(json, i + 1);
                        if (next < json.Length && json[next] == (c == '{' ? '}' : ']'))
                        {
                            builder.Append(c).Append(json[next]);
                            i = next;
                            break;
                        }
                        builder.Append(c);
                        indent++;
                        AppendNewLine(builder, indent);
                        break;
                    case '}':
                    case ']':
                        indent = Math.Max(0, indent - 1);
                        AppendNewLine(builder, indent);
                        builder.Append(c);
                        break;
                    case ',':
                        builder.Append(c);
                        AppendNewLine(builder, indent);
                        break;
                    case ':':
                        builder.Append(": ");
                        break;
                    default:
                        if (!char.IsWhiteSpace(c))
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Gets the number of hex dump lines of a body.
        /// </summary>
        /// <param name="length">The body length in bytes.</param>
        /// <returns>The number of lines.</returns>
        public static int GetHexLineCount(int length)
        {
            return (length + HexBytesPerLine - 1) / HexBytesPerLine;
        }

        /// <summary>
        /// Formats one hex dump line: the offset, 16 bytes in hex and their printable ASCII characters.
        /// </summary>
        /// <param name="data">The body bytes.</param>
        /// <param name="line">The zero-based line index.</param>
        /// <returns>Such as <c>00000010  48 65 6C 6C 6F ...  |Hello...|</c>.</returns>
        public static string FormatHexLine(byte[] data, int line)
        {
            var offset = line * HexBytesPerLine;
            var count = Math.Max(0, Math.Min(HexBytesPerLine, data.Length - offset));
            var builder = new StringBuilder(80);
            builder.Append(offset.ToString("X8", CultureInfo.InvariantCulture)).Append("  ");
            for (var i = 0; i < HexBytesPerLine; i++)
            {
                if (i == HexBytesPerLine / 2)
                {
                    builder.Append(' ');
                }
                if (i < count)
                {
                    builder.Append(data[offset + i].ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
                }
                else
                {
                    builder.Append("   ");
                }
            }
            builder.Append(" |");
            for (var i = 0; i < count; i++)
            {
                var value = data[offset + i];
                builder.Append(value >= 0x20 && value < 0x7F ? (char)value : '.');
            }
            builder.Append('|');

            return builder.ToString();
        }

        /// <summary>
        /// Splits a request into consecutive phases on its own time line, as the Timing tab draws them.
        /// </summary>
        /// <param name="snapshot">The request.</param>
        /// <param name="now">The current <see cref="System.Diagnostics.Stopwatch"/> timestamp, for a request that has not finished.</param>
        /// <returns>
        /// Queueing, then the libcurl phases that were reached (DNS lookup, initial connection, SSL, request setup,
        /// request sent, waiting for the server) and the content download. Phases of zero length are left out.
        /// </returns>
        public static List<CurlNetworkTimingPhase> GetTimingPhases(CurlNetworkSnapshot snapshot, long now)
        {
            var phases = new List<CurlNetworkTimingPhase>();
            var queueing = snapshot.GetQueueing(now);
            if (queueing > TimeSpan.Zero)
            {
                phases.Add(new CurlNetworkTimingPhase("Queueing", TimeSpan.Zero, queueing));
            }
            // Offsets from the start of the transfer, which is where libcurl's timing points start.
            var cursor = TimeSpan.Zero;
            if (snapshot.Timings is { } timings)
            {
                AddPoint(phases, "DNS Lookup", timings.NameLookup, queueing, ref cursor);
                AddPoint(phases, "Initial connection", timings.Connect, queueing, ref cursor);
                AddPoint(phases, "SSL", timings.AppConnect, queueing, ref cursor);
                AddPoint(phases, "Request setup", timings.PreTransfer, queueing, ref cursor);
                AddPoint(phases, "Request sent", timings.PostTransfer, queueing, ref cursor);
            }
            if (snapshot.Ttfb is { } ttfb)
            {
                AddPoint(phases, "Waiting for server response", ttfb, queueing, ref cursor);
            }
            else if (snapshot.State == CurlNetworkRequestState.Running)
            {
                AddPoint(phases, "Waiting for server response", snapshot.GetTotal(now) - queueing, queueing, ref cursor);
            }
            if (snapshot.GetTransfer(now) is { } transfer && transfer > TimeSpan.Zero)
            {
                phases.Add(new CurlNetworkTimingPhase("Content Download", queueing + cursor, transfer));
            }

            return phases;
        }

        /// <summary>
        /// Builds a bash cURL command that repeats the request.
        /// </summary>
        /// <param name="snapshot">The request.</param>
        /// <param name="requestBody">The captured request body.</param>
        /// <returns>
        /// The command with every request header except <c>Host</c> and <c>Content-Length</c>. The body is included
        /// only when it was captured completely and is text.
        /// </returns>
        public static string BuildCurlCommand(CurlNetworkSnapshot snapshot, byte[] requestBody)
        {
            var builder = new StringBuilder();
            builder.Append("curl ").Append(QuoteShell(snapshot.Url));
            if (string.Equals(snapshot.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(" \\\n  --head");
            }
            else if (!string.Equals(snapshot.Method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(" \\\n  -X ").Append(QuoteShell(snapshot.Method));
            }
            var contentType = default(string?);
            foreach (var header in snapshot.RequestHeaders)
            {
                if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = header.Value;
                }
                builder.Append(" \\\n  -H ").Append(QuoteShell($"{header.Key}: {header.Value}"));
            }
            if (requestBody.Length > 0 && !snapshot.IsRequestBodyTruncated && !IsBinary(requestBody, contentType))
            {
                builder.Append(" \\\n  --data-raw ").Append(QuoteShell(DecodeText(requestBody, contentType)));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Quotes a value for a POSIX shell.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The value in single quotes, with embedded single quotes escaped.</returns>
        public static string QuoteShell(string value)
        {
            return "'" + value.Replace("'", "'\\''") + "'";
        }

        /// <summary>
        /// Adds the phase that ends at a cumulative libcurl timing point, if the point moves the time line forward.
        /// </summary>
        /// <param name="phases">The phases to add to.</param>
        /// <param name="name">The phase name.</param>
        /// <param name="point">The timing point, measured from the start of the transfer.</param>
        /// <param name="queueing">The queueing time that precedes the transfer.</param>
        /// <param name="cursor">The end of the previous phase; moved to <paramref name="point"/>.</param>
        private static void AddPoint(List<CurlNetworkTimingPhase> phases, string name, TimeSpan point, TimeSpan queueing, ref TimeSpan cursor)
        {
            if (point <= cursor)
            {
                return;
            }
            phases.Add(new CurlNetworkTimingPhase(name, queueing + cursor, point - cursor));
            cursor = point;
        }

        /// <summary>
        /// Gets the encoding named by the <c>charset</c> parameter of a <c>Content-Type</c>.
        /// </summary>
        /// <param name="contentType">The header value, or <see langword="null"/>.</param>
        /// <returns>The encoding, or <see langword="null"/> when there is no charset or it is unknown.</returns>
        private static Encoding? GetCharset(string? contentType)
        {
            if (string.IsNullOrEmpty(contentType))
            {
                return null;
            }
            foreach (var parameter in contentType!.Split(';'))
            {
                SplitPair(parameter.Trim(), '=', out var name, out var value);
                if (!string.Equals(name, "charset", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                try
                {
                    return Encoding.GetEncoding(value.Trim('"'));
                }
                catch (ArgumentException)
                {
                    return null;
                }
            }

            return null;
        }

        /// <summary>
        /// Splits text at the first separator.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="separator">The separator.</param>
        /// <param name="name">Receives the trimmed text before the separator, or all of it.</param>
        /// <param name="value">Receives the trimmed text after the separator, or an empty string.</param>
        private static void SplitPair(string text, char separator, out string name, out string value)
        {
            var index = text.IndexOf(separator);
            if (index < 0)
            {
                name = text.Trim();
                value = string.Empty;
                return;
            }
            name = text.Substring(0, index).Trim();
            value = text.Substring(index + 1).Trim();
        }

        /// <summary>
        /// Decodes one form-encoded component, where <c>+</c> stands for a space.
        /// </summary>
        /// <param name="text">The encoded component.</param>
        /// <returns>The decoded text, or the input when it is not valid percent-encoding.</returns>
        private static string UnescapeForm(string text)
        {
            try
            {
                return Uri.UnescapeDataString(text.Replace('+', ' '));
            }
            catch (UriFormatException)
            {
                return text;
            }
        }

        /// <summary>
        /// Gets whether a value contains a filter, ignoring case.
        /// </summary>
        /// <param name="value">The value, or <see langword="null"/>.</param>
        /// <param name="filter">The filter.</param>
        /// <returns><see langword="true"/> when <paramref name="value"/> contains <paramref name="filter"/>.</returns>
        private static bool Contains(string? value, string filter)
        {
            return value is not null && value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Finds the next character that is not white space.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="start">The index to start at.</param>
        /// <returns>The index, or the text length when there is none.</returns>
        private static int NextNonWhitespace(string text, int start)
        {
            var index = start;
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            return index;
        }

        /// <summary>
        /// Appends a line feed and indentation.
        /// </summary>
        /// <param name="builder">The builder.</param>
        /// <param name="indent">The indentation level.</param>
        private static void AppendNewLine(StringBuilder builder, int indent)
        {
            builder.Append('\n').Append(' ', indent * 2);
        }
    }
}
