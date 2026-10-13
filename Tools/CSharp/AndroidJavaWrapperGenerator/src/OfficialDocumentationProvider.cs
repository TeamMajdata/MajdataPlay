using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Enriches descriptor-exact JVM metadata from cached official English API pages.</summary>
    internal sealed class OfficialDocumentationProvider
    {
        /// <summary>Bounds each downloaded HTML response before parsing navigation-heavy pages.</summary>
        private const int MaxPageBytes = 16 * 1024 * 1024;

        /// <summary>Versions the parsed cache when page extraction rules change.</summary>
        private const int CacheVersion = 1;

        /// <summary>Stores the requested network/cache policy.</summary>
        private readonly GeneratorOptions _options;

        /// <summary>Stores the validated cache directory.</summary>
        private readonly string _cache;

        /// <summary>Stores the injected or locally owned HTTP transport.</summary>
        private readonly HttpClient _client;

        /// <summary>Disables repeatedly unreachable hosts during this enrichment run.</summary>
        private readonly ConcurrentDictionary<string, int> _hostFailures = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Contains parallel counters and bounded failure examples.</summary>
        private readonly DocumentationReport _report = new DocumentationReport();

        /// <summary>Initializes one bounded enrichment session.</summary>
        /// <param name="options">The CLI cache and concurrency policy.</param>
        /// <param name="client">The HTTP transport used only for official documentation.</param>
        private OfficialDocumentationProvider(GeneratorOptions options, HttpClient client)
        {
            _options = options;
            _client = client;
            _cache = Path.GetFullPath(options.DocumentationCache ?? Path.Combine(options.OutputDirectory, ".documentation-cache"));
        }

        /// <summary>Downloads or loads official descriptions for the wrappers selected for output.</summary>
        /// <param name="api">The unmodified JVM graph, enriched only after each page is parsed.</param>
        /// <param name="types">The selected wrappers and their public dependency closure.</param>
        /// <param name="options">The requested official-documentation policy.</param>
        /// <param name="cancellationToken">The generation cancellation token.</param>
        /// <param name="httpClient">An optional injected transport for deterministic fixture validation.</param>
        /// <returns>Coverage, cache, and parameter-name restoration statistics.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The network parallelism is not positive.</exception>
        /// <exception cref="IOException">The cache cannot be read or updated.</exception>
        /// <exception cref="OperationCanceledException">Enrichment was canceled.</exception>
        internal static async Task<DocumentationReport> EnrichAsync(JavaApi api, IReadOnlyList<JavaType> types, GeneratorOptions options, CancellationToken cancellationToken, HttpClient? httpClient = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (options.Documentation == DocumentationMode.None)
            {
                return new DocumentationReport();
            }
            if (options.DocumentationJobs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Documentation parallelism must be positive.");
            }

            using var ownedClient = httpClient == null ? CreateClient(options.DocumentationJobs) : null;
            var provider = new OfficialDocumentationProvider(options, httpClient ?? ownedClient!);
            var selected = types.Where(type => IsOfficialType(type.Name)).OrderBy(type => type.Name, StringComparer.Ordinal).ToArray();
            provider._report.RequestedTypes = selected.Length;
            var results = new JavaDocumentationPage?[selected.Length];
            var failures = new Exception?[selected.Length];
            await Parallel.ForEachAsync(Enumerable.Range(0, selected.Length), new ParallelOptions { MaxDegreeOfParallelism = options.DocumentationJobs, CancellationToken = cancellationToken }, async (index, token) =>
            {
                try
                {
                    results[index] = await provider.LoadTypeAsync(selected[index], token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidDataException)
                {
                    failures[index] = exception;
                }
            }).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            for (var index = 0; index < selected.Length; index++)
            {
                if (failures[index] != null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[index]!).Throw();
                }
                var page = results[index];
                if (page == null || !HasDescriptions(page))
                {
                    provider._report.MissingTypes.Add(selected[index].Name);
                    continue;
                }
                Apply(selected[index], page, provider._report);
            }
            if (provider._report.MissingTypes.Count > 0)
            {
                var samples = string.Join(", ", provider._report.MissingTypes.Take(5));
                api.Warnings.Add("Official documentation unavailable for " + provider._report.MissingTypes.Count + " selected types (" + samples + "). Existing metadata names and binding descriptions are retained. Use --offline-docs with a populated cache, or retry with --refresh-docs.");
            }
            return provider._report;
        }

        /// <summary>Creates a bounded, decompression-aware HTTP transport with English language preferences.</summary>
        /// <param name="jobs">The maximum simultaneous requests per official host.</param>
        /// <returns>A caller-owned transport for official-site reads.</returns>
        private static HttpClient CreateClient(int jobs)
        {
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All, MaxConnectionsPerServer = jobs };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MajdataPlay-AndroidJavaWrapperGenerator/1.0");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            return client;
        }

        /// <summary>Loads Android's platform documentation and fills missing BCL members from Oracle Javadoc.</summary>
        /// <param name="type">The descriptor-exact type being documented.</param>
        /// <param name="cancellationToken">The request cancellation token.</param>
        /// <returns>The merged descriptions, or null when no official page is usable.</returns>
        private async Task<JavaDocumentationPage?> LoadTypeAsync(JavaType type, CancellationToken cancellationToken)
        {
            var primary = await LoadPageAsync(type, AndroidUrl(type.Name), cancellationToken).ConfigureAwait(false);
            if (IsJavaBcl(type.Name) && NeedsFallback(type, primary))
            {
                var fallback = await LoadPageAsync(type, OracleUrl(type.Name), cancellationToken).ConfigureAwait(false);
                if (fallback != null)
                {
                    primary = primary == null ? fallback : Merge(primary, fallback);
                }
            }
            return primary;
        }

        /// <summary>Reads a parsed page cache or downloads and parses one official HTML response.</summary>
        /// <param name="type">The type supplying exact JVM overloads.</param>
        /// <param name="url">The official English page URL.</param>
        /// <param name="cancellationToken">The request cancellation token.</param>
        /// <returns>Descriptor-matched descriptions, or null for unavailable documentation.</returns>
        /// <exception cref="IOException">A local cache read or write fails.</exception>
        private async Task<JavaDocumentationPage?> LoadPageAsync(JavaType type, string url, CancellationToken cancellationToken)
        {
            var fingerprint = Fingerprint(type, url);
            var cachePath = Path.Combine(_cache, fingerprint.Substring(0, 2), fingerprint + ".json");
            if (!_options.RefreshDocumentation || _options.Documentation == DocumentationMode.Offline)
            {
                var cached = await ReadCacheAsync(cachePath, url, _options.Documentation == DocumentationMode.Offline, cancellationToken).ConfigureAwait(false);
                if (cached != null)
                {
                    Interlocked.Increment(ref _report.CachedPages);
                    return cached.Page;
                }
            }
            if (_options.Documentation == DocumentationMode.Offline)
            {
                return null;
            }
            var uri = new Uri(url);
            if (_hostFailures.TryGetValue(uri.Host, out var failures) && failures >= 3)
            {
                return null;
            }
            try
            {
                using var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Gone)
                    {
                        await WriteCacheAsync(cachePath, new DocumentationCacheEntry { Url = url, Page = null }, cancellationToken).ConfigureAwait(false);
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                    {
                        _hostFailures.AddOrUpdate(uri.Host, 1, (key, count) => count + 1);
                    }
                    return null;
                }
                if (response.Content.Headers.ContentLength > MaxPageBytes)
                {
                    return null;
                }
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var buffer = new MemoryStream();
                var chunk = new byte[32768];
                while (true)
                {
                    var read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }
                    if (buffer.Length + read > MaxPageBytes)
                    {
                        return null;
                    }
                    buffer.Write(chunk, 0, read);
                }
                _hostFailures.TryRemove(uri.Host, out _);
                var html = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
                var page = OfficialDocumentationParser.Parse(type, html, url);
                if (!HasDescriptions(page))
                {
                    return null;
                }
                Interlocked.Increment(ref _report.DownloadedPages);
                await WriteCacheAsync(cachePath, new DocumentationCacheEntry { Url = url, Page = page }, cancellationToken).ConfigureAwait(false);
                return page;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _hostFailures.AddOrUpdate(uri.Host, 1, (key, count) => count + 1);
                return null;
            }
            catch (HttpRequestException)
            {
                _hostFailures.AddOrUpdate(uri.Host, 1, (key, count) => count + 1);
                return null;
            }
        }

        /// <summary>Reads an unexpired, versioned, URL-specific parsed documentation entry.</summary>
        /// <param name="path">The cache entry path.</param>
        /// <param name="url">The requested official URL.</param>
        /// <param name="allowExpired">Whether offline use may reuse older matching API descriptions.</param>
        /// <param name="cancellationToken">The cache cancellation token.</param>
        /// <returns>The valid entry, or null for a missing, stale, or malformed cache.</returns>
        private static async Task<DocumentationCacheEntry?> ReadCacheAsync(string path, string url, bool allowExpired, CancellationToken cancellationToken)
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaxPageBytes)
            {
                return null;
            }
            try
            {
                var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                var entry = JsonSerializer.Deserialize<DocumentationCacheEntry>(json);
                if (entry?.Version != CacheVersion || entry.Url != url || (!allowExpired && DateTimeOffset.UtcNow - entry.FetchedAt > TimeSpan.FromDays(entry.Page == null ? 1 : 30)))
                {
                    return null;
                }
                return entry;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Atomically stores parsed descriptions rather than large navigation-heavy HTML pages.</summary>
        /// <param name="path">The versioned content-specific entry path.</param>
        /// <param name="entry">The official page or a short-lived not-found result.</param>
        /// <param name="cancellationToken">The cache cancellation token.</param>
        private static async Task WriteCacheAsync(string path, DocumentationCacheEntry entry, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(entry), cancellationToken).ConfigureAwait(false);
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        /// <summary>Applies documentation in declaration order without changing any JVM descriptors.</summary>
        /// <param name="type">The Java type receiving the official descriptions.</param>
        /// <param name="page">The matched official API page.</param>
        /// <param name="report">The restoration and coverage counters.</param>
        private static void Apply(JavaType type, JavaDocumentationPage page, DocumentationReport report)
        {
            type.Documentation = page.Type;
            report.DocumentedTypes++;
            foreach (var field in type.Fields)
            {
                if (page.Fields.TryGetValue(field.Name, out var documentation))
                {
                    field.Documentation = documentation;
                    report.DocumentedMembers++;
                }
            }
            foreach (var method in type.Methods)
            {
                if (!page.Methods.TryGetValue(method.Name + method.Descriptor, out var documentation))
                {
                    continue;
                }
                method.Documentation = documentation;
                report.DocumentedMembers++;
                if (documentation.ParameterNames.Count == JavaDescriptors.ParseMethod(method.Descriptor).Parameters.Count)
                {
                    method.ParameterNames.Clear();
                    method.ParameterNames.AddRange(documentation.ParameterNames);
                    report.RestoredParameters += documentation.ParameterNames.Count;
                }
            }
        }

        /// <summary>Identifies platform packages published in the official Android reference.</summary>
        /// <param name="name">The JVM binary name.</param>
        /// <returns>Whether official documentation should be attempted for the type.</returns>
        private static bool IsOfficialType(string name)
        {
            return name.StartsWith("android.", StringComparison.Ordinal) || name.StartsWith("dalvik.", StringComparison.Ordinal) || IsJavaBcl(name) || name.StartsWith("org.json.", StringComparison.Ordinal) || name.StartsWith("org.xml.", StringComparison.Ordinal) || name.StartsWith("org.w3c.", StringComparison.Ordinal);
        }

        /// <summary>Identifies Java runtime and standard extension packages.</summary>
        /// <param name="name">The JVM binary name.</param>
        /// <returns>Whether Oracle Javadoc can supplement Android's reference.</returns>
        private static bool IsJavaBcl(string name)
        {
            return name.StartsWith("java.", StringComparison.Ordinal) || name.StartsWith("javax.", StringComparison.Ordinal);
        }

        /// <summary>Builds the official Android canonical nested-type page URL.</summary>
        /// <param name="name">The exact JVM binary name.</param>
        /// <returns>The English reference page URL.</returns>
        internal static string AndroidUrl(string name)
        {
            var packageEnd = name.LastIndexOf('.');
            var package = packageEnd < 0 ? string.Empty : name.Substring(0, packageEnd).Replace('.', '/') + "/";
            return "https://developer.android.com/reference/" + package + name.Substring(packageEnd + 1).Replace('$', '.') + "?hl=en";
        }

        /// <summary>Builds an Oracle JDK 17 page using the standard package's module.</summary>
        /// <param name="name">The JVM binary name in java or javax packages.</param>
        /// <returns>The canonical Oracle Javadoc URL.</returns>
        internal static string OracleUrl(string name)
        {
            var module = "java.base";
            if (name.StartsWith("javax.annotation.processing.", StringComparison.Ordinal) || name.StartsWith("javax.lang.model.", StringComparison.Ordinal) || name.StartsWith("javax.tools.", StringComparison.Ordinal))
            {
                module = "java.compiler";
            }
            else if (name.StartsWith("java.net.http.", StringComparison.Ordinal))
            {
                module = "java.net.http";
            }
            else if (name.StartsWith("java.rmi.", StringComparison.Ordinal) || name.StartsWith("javax.rmi.ssl.", StringComparison.Ordinal))
            {
                module = "java.rmi";
            }
            else if (name.StartsWith("javax.security.sasl.", StringComparison.Ordinal))
            {
                module = "java.security.sasl";
            }
            else if (name.StartsWith("javax.smartcardio.", StringComparison.Ordinal))
            {
                module = "java.smartcardio";
            }
            else if (name.StartsWith("java.sql.", StringComparison.Ordinal) || name.StartsWith("javax.sql.", StringComparison.Ordinal))
            {
                module = "java.sql";
            }
            else if (name.StartsWith("java.util.logging.", StringComparison.Ordinal))
            {
                module = "java.logging";
            }
            else if (name.StartsWith("javax.xml.", StringComparison.Ordinal))
            {
                module = "java.xml";
            }
            else if (name.StartsWith("java.beans.", StringComparison.Ordinal) || name.StartsWith("java.awt.", StringComparison.Ordinal) || name.StartsWith("javax.swing.", StringComparison.Ordinal) || name.StartsWith("javax.imageio.", StringComparison.Ordinal) || name.StartsWith("javax.print.", StringComparison.Ordinal) || name.StartsWith("javax.sound.", StringComparison.Ordinal) || name.StartsWith("javax.accessibility.", StringComparison.Ordinal))
            {
                module = "java.desktop";
            }
            else if (name.StartsWith("java.lang.management.", StringComparison.Ordinal) || name.StartsWith("javax.management.", StringComparison.Ordinal))
            {
                module = "java.management";
            }
            else if (name.StartsWith("javax.naming.", StringComparison.Ordinal))
            {
                module = "java.naming";
            }
            else if (name.StartsWith("javax.script.", StringComparison.Ordinal))
            {
                module = "java.scripting";
            }
            else if (name.StartsWith("javax.security.auth.kerberos.", StringComparison.Ordinal))
            {
                module = "java.security.jgss";
            }
            var packageEnd = name.LastIndexOf('.');
            return "https://docs.oracle.com/en/java/javase/17/docs/api/" + module + "/" + name.Substring(0, packageEnd).Replace('.', '/') + "/" + name.Substring(packageEnd + 1).Replace('$', '.') + ".html";
        }

        /// <summary>Checks whether a page contains any usable API descriptions.</summary>
        /// <param name="page">The parsed page.</param>
        /// <returns>Whether its descriptions can enrich at least one wrapper.</returns>
        private static bool HasDescriptions(JavaDocumentationPage page)
        {
            return page.Type.Summary.Length > 0 || page.Fields.Count > 0 || page.Methods.Count > 0;
        }

        /// <summary>Checks whether BCL members are missing from the platform reference.</summary>
        /// <param name="type">The exact JVM declaration.</param>
        /// <param name="page">The primary official descriptions.</param>
        /// <returns>Whether Oracle should be attempted to fill gaps.</returns>
        private static bool NeedsFallback(JavaType type, JavaDocumentationPage? page)
        {
            return page == null || type.Fields.Any(field => field.IsPublic && !page.Fields.ContainsKey(field.Name)) || type.Methods.Any(method => method.IsPublic && !method.IsBridge && !method.IsSynthetic && !page.Methods.ContainsKey(method.Name + method.Descriptor));
        }

        /// <summary>Fills only missing primary descriptions using the standard BCL documentation.</summary>
        /// <param name="primary">Android's platform-specific descriptions.</param>
        /// <param name="fallback">Oracle's standard Java descriptions.</param>
        /// <returns>The primary page with supplemental missing descriptions.</returns>
        private static JavaDocumentationPage Merge(JavaDocumentationPage primary, JavaDocumentationPage fallback)
        {
            if (primary.Type.Summary.Length == 0)
            {
                primary.Type = fallback.Type;
            }
            foreach (var field in fallback.Fields)
            {
                primary.Fields.TryAdd(field.Key, field.Value);
            }
            foreach (var method in fallback.Methods)
            {
                primary.Methods.TryAdd(method.Key, method.Value);
            }
            return primary;
        }

        /// <summary>Keys parsed descriptions by official URL, parser version, and the exact member contract.</summary>
        /// <param name="type">The JVM declaration used for overload matching.</param>
        /// <param name="url">The official page identity.</param>
        /// <returns>The lowercase SHA-256 cache filename stem.</returns>
        private static string Fingerprint(JavaType type, string url)
        {
            var contract = url + "|" + CacheVersion + "|" + type.Name + "|" + string.Join("|", type.Fields.Where(field => field.IsPublic).Select(field => field.Name + field.Descriptor).OrderBy(value => value, StringComparer.Ordinal)) + "|" + string.Join("|", type.Methods.Where(method => method.IsPublic).Select(method => method.Name + method.Descriptor).OrderBy(value => value, StringComparer.Ordinal));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contract))).ToLowerInvariant();
        }

        /// <summary>Serializes one parsed official page or short-lived not-found result.</summary>
        private sealed class DocumentationCacheEntry
        {
            /// <summary>Initializes a valid cache record for the current parser.</summary>
            public DocumentationCacheEntry()
            {
            }

            /// <summary>Gets or sets the cache parser version.</summary>
            public int Version { get; set; } = CacheVersion;

            /// <summary>Gets or sets the exact official source URL.</summary>
            public string Url { get; set; } = string.Empty;

            /// <summary>Gets or sets the UTC retrieval timestamp.</summary>
            public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.UtcNow;

            /// <summary>Gets or sets the descriptor-exact parsed descriptions, or null for not found.</summary>
            public JavaDocumentationPage? Page { get; set; }
        }
    }

    /// <summary>Reports official documentation coverage and provenance for one generation.</summary>
    internal sealed class DocumentationReport
    {
        /// <summary>Gets or sets the number of selected types with official reference pages.</summary>
        internal int RequestedTypes { get; set; }

        /// <summary>Gets or sets the number of types successfully enriched.</summary>
        internal int DocumentedTypes { get; set; }

        /// <summary>Gets or sets the number of matched fields, methods, and constructors.</summary>
        internal int DocumentedMembers { get; set; }

        /// <summary>Gets or sets the number of restored original Java parameter names.</summary>
        internal int RestoredParameters { get; set; }

        /// <summary>Counts pages successfully downloaded during bounded parallel requests.</summary>
        internal int DownloadedPages;

        /// <summary>Counts valid positive or negative cache entries read during enrichment.</summary>
        internal int CachedPages;

        /// <summary>Gets the selected binary names lacking official page coverage.</summary>
        internal List<string> MissingTypes { get; } = new List<string>();
    }
}
