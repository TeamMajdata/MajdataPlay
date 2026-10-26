using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.Tools.AndroidJavaWrapperGenerator;

#nullable enable

namespace MajdataPlay.Tests.AndroidJavaWrapperGeneratorValidation
{
    /// <summary>Checks official-page extraction, provenance, cache behavior, and restored source names.</summary>
    internal static partial class Program
    {
        /// <summary>Validates documentation without accessing the network.</summary>
        /// <param name="settings">The standard and Unity compiler references.</param>
        /// <param name="workspace">The owned scratch directory.</param>
        private static void VerifyDocumentation(ValidationSettings settings, string workspace)
        {
            var androidHtml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "DocumentationFixtures", "android-sample.html"));
            var oracleHtml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "DocumentationFixtures", "oracle-integer.html"));
            var api = DocumentationApi();
            var sample = api.Types["android.testing.Sample"];
            var integer = api.Types["java.lang.Integer"];
            var android = OfficialDocumentationParser.Parse(sample, androidHtml, OfficialDocumentationProvider.AndroidUrl(sample.Name));
            Require(android.Methods.ContainsKey("convert(Ljava/lang/String;I)I"), "Android documentation matches descriptor-exact overloads");
            Require(android.Methods["convert(Ljava/lang/String;I)I"].ParameterNames.SequenceEqual(new[] { "source", "radix" }), "Android source parameter names are restored in declaration order");
            Require(android.Methods["convert(II)I"].ParameterNames[0] == "value", "Same-arity overloads do not borrow another method's parameters");
            Require(android.Methods["echo([Ljava/lang/String;)[Ljava/lang/String;"].ParameterNames[0] == "values", "Varargs documentation matches JVM arrays");
            Require(android.Fields["LIMIT"].Summary.Contains("<allowed>", StringComparison.Ordinal), "HTML entities are decoded as documentation text");
            Require(!android.Type.Summary.Contains("Navigation", StringComparison.Ordinal), "Navigation is excluded from API descriptions");
            var oracle = OfficialDocumentationParser.Parse(integer, oracleHtml, OfficialDocumentationProvider.OracleUrl(integer.Name));
            Require(oracle.Methods["parseInt(Ljava/lang/String;I)I"].ParameterNames.SequenceEqual(new[] { "s", "radix" }), "Oracle Javadoc restores BCL parameter names");
            Require(oracle.Methods["parseInt(Ljava/lang/String;I)I"].Returns.Contains("numeric result", StringComparison.Ordinal), "Oracle return descriptions are retained");
            Require(oracle.Methods["parseInt(Ljava/lang/String;I)I"].Remarks.Contains("NumberFormatException", StringComparison.Ordinal), "Java exception descriptions are preserved without inventing CLR exceptions");

            var options = new GeneratorOptions { NoSdk = true, OutputDirectory = Path.Combine(workspace, "documented"), RootNamespace = "Documented.CustomRoot", DocumentationCache = Path.Combine(workspace, "docs-cache"), DocumentationJobs = 2, Jobs = 4 };
            using var handler = new DocumentationHandler(androidHtml, oracleHtml);
            using var client = new HttpClient(handler);
            var report = OfficialDocumentationProvider.EnrichAsync(api, new[] { sample, integer }, options, CancellationToken.None, client).GetAwaiter().GetResult();
            Require(report.DocumentedTypes == 2 && report.RestoredParameters >= 8, "Official enrichment covers SDK and BCL types");
            Require(handler.MaximumActive >= 2, "Official-page retrieval uses bounded concurrent requests");
            Require(handler.MaximumActive <= options.DocumentationJobs, "Documentation concurrency stays within its configured bound");
            var generated = Generate(api, options);
            var sampleSource = generated.Values.Single(text => text.Contains("partial class Sample :", StringComparison.Ordinal));
            Require(sampleSource.Contains("string? source, int radix", StringComparison.Ordinal), "Generated C# signatures use restored Java parameter names");
            Require(sampleSource.Contains("&lt;text&gt; &amp; symbols", StringComparison.Ordinal), "Official descriptions are XML-escaped in param tags");
            Require(sampleSource.Contains("<seealso href=", StringComparison.Ordinal), "Generated XML identifies its official source URL");
            Require(sampleSource.Contains("Converts the supplied source", StringComparison.Ordinal), "Official English descriptions replace generic binding summaries");
            CompileGenerated(settings, generated, false, false);

            var requests = handler.Requests;
            var offlineApi = DocumentationApi();
            options.Documentation = DocumentationMode.Offline;
            var offline = OfficialDocumentationProvider.EnrichAsync(offlineApi, offlineApi.Types.Values.ToArray(), options, CancellationToken.None, client).GetAwaiter().GetResult();
            Require(handler.Requests == requests && offline.CachedPages >= 2, "Offline enrichment reads parsed caches without any HTTP calls");
            Require(offlineApi.Types["java.lang.Integer"].Methods.Single(method => method.Descriptor == "(Ljava/lang/String;I)I").ParameterNames[0] == "s", "Cached pages preserve restored parameter names");
            Require(Generate(offlineApi, options).All(item => generated[item.Key] == item.Value), "Online and offline cached output are byte-identical");
            options.Documentation = DocumentationMode.None;
            var disabled = OfficialDocumentationProvider.EnrichAsync(DocumentationApi(), new[] { sample }, options, CancellationToken.None, client).GetAwaiter().GetResult();
            Require(disabled.RequestedTypes == 0 && handler.Requests == requests, "Disabled enrichment performs no network work");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Expect<OperationCanceledException>(() => OfficialDocumentationProvider.EnrichAsync(api, new[] { sample }, options, cancellation.Token, client).GetAwaiter().GetResult(), "Documentation observes cancellation before network access");
        }

        /// <summary>Validates real official-page snapshots fetched separately through the approved network path.</summary>
        /// <param name="settings">The downloaded sample directory and compiler references.</param>
        /// <param name="api">The selected actual Android SDK metadata.</param>
        /// <param name="workspace">The exclusively owned validation directory.</param>
        private static void VerifyOfficialSamples(ValidationSettings settings, JavaApi api, string workspace)
        {
            var androidHtml = File.ReadAllText(Path.Combine(settings.OfficialSamples!, "android-uri.html"));
            var oracleHtml = File.ReadAllText(Path.Combine(settings.OfficialSamples!, "oracle-integer.html"));
            var uri = api.Types["android.net.Uri"];
            var integer = api.Types["java.lang.Integer"];
            var android = OfficialDocumentationParser.Parse(uri, androidHtml, OfficialDocumentationProvider.AndroidUrl(uri.Name));
            Require(android.Methods["parse(Ljava/lang/String;)Landroid/net/Uri;"].ParameterNames[0] == "uriString", "Real Android Uri.parse restores uriString");
            Require(android.Methods["getQueryParameter(Ljava/lang/String;)Ljava/lang/String;"].ParameterNames[0] == "key", "Real Android query API restores key");
            var oracle = OfficialDocumentationParser.Parse(integer, oracleHtml, OfficialDocumentationProvider.OracleUrl(integer.Name));
            Require(oracle.Methods["parseInt(Ljava/lang/String;I)I"].ParameterNames.SequenceEqual(new[] { "s", "radix" }), "Real Oracle Integer.parseInt restores s and radix");
            using var handler = new DocumentationHandler(androidHtml, oracleHtml);
            using var client = new HttpClient(handler);
            var options = new GeneratorOptions { OutputDirectory = Path.Combine(workspace, "official"), DocumentationCache = Path.Combine(workspace, "official-cache"), DocumentationJobs = 2 };
            var report = OfficialDocumentationProvider.EnrichAsync(api, new[] { uri, integer }, options, CancellationToken.None, client).GetAwaiter().GetResult();
            Require(report.DocumentedTypes == 2 && report.RestoredParameters > 20, "Actual SDK API is enriched from independently downloaded official pages");
            Require(uri.Documentation!.SourceUrl.StartsWith("https://developer.android.com/", StringComparison.Ordinal), "Android type documentation retains official provenance");
            Require(integer.Documentation!.SourceUrl.StartsWith("https://docs.oracle.com/", StringComparison.Ordinal), "BCL fallback retains Oracle provenance");
            var documentedApi = new JavaApi();
            documentedApi.Types.Add(uri.Name, uri);
            documentedApi.Types.Add(integer.Name, integer);
            options.Jobs = 4;
            var documentedOutput = Generate(documentedApi, options);
            CompileGenerated(settings, documentedOutput, false, false);
            CompileGenerated(settings, documentedOutput, true, false);
            Console.WriteLine("Official snapshots: " + report.DocumentedMembers + " matched members, " + report.RestoredParameters + " restored parameters.");
        }

        /// <summary>Builds descriptor-exact SDK/BCL declarations for HTML matching fixtures.</summary>
        /// <returns>The isolated metadata graph.</returns>
        private static JavaApi DocumentationApi()
        {
            var api = new JavaApi();
            var sample = new JavaType { Name = "android.testing.Sample", SuperName = "java.lang.Object", IsPublic = true, AccessFlags = 0x0001 };
            sample.Fields.Add(new JavaField { Name = "LIMIT", Descriptor = "I", AccessFlags = 0x0019 });
            sample.Methods.Add(new JavaMethod { Name = "convert", Descriptor = "(Ljava/lang/String;I)I", AccessFlags = 0x0009 });
            sample.Methods.Add(new JavaMethod { Name = "convert", Descriptor = "(II)I", AccessFlags = 0x0009 });
            sample.Methods.Add(new JavaMethod { Name = "echo", Descriptor = "([Ljava/lang/String;)[Ljava/lang/String;", AccessFlags = 0x0081 });
            var integer = new JavaType { Name = "java.lang.Integer", SuperName = "java.lang.Object", IsPublic = true, AccessFlags = 0x0001 };
            integer.Fields.Add(new JavaField { Name = "MAX_VALUE", Descriptor = "I", AccessFlags = 0x0019 });
            integer.Methods.Add(new JavaMethod { Name = "parseInt", Descriptor = "(Ljava/lang/String;I)I", AccessFlags = 0x0009 });
            integer.Methods.Add(new JavaMethod { Name = "parseInt", Descriptor = "(Ljava/lang/String;)I", AccessFlags = 0x0009 });
            api.Types.Add(sample.Name, sample);
            api.Types.Add(integer.Name, integer);
            return api;
        }

        /// <summary>Serves owned documentation fixtures while measuring actual request concurrency.</summary>
        private sealed class DocumentationHandler : HttpMessageHandler
        {
            /// <summary>Stores the Android-layout fixture.</summary>
            private readonly string _android;

            /// <summary>Stores the Oracle-layout fixture.</summary>
            private readonly string _oracle;

            /// <summary>Counts requests currently awaiting their fixture response.</summary>
            private int _active;

            /// <summary>Counts all fixture transport requests.</summary>
            internal int Requests;

            /// <summary>Tracks the greatest observed simultaneous request count.</summary>
            internal int MaximumActive;

            /// <summary>Initializes an independent fixture-only HTTP transport.</summary>
            /// <param name="android">The Android reference HTML.</param>
            /// <param name="oracle">The Oracle Javadoc HTML.</param>
            internal DocumentationHandler(string android, string oracle)
            {
                _android = android;
                _oracle = oracle;
            }

            /// <summary>Returns official-layout fixture content without opening a socket.</summary>
            /// <param name="request">The official URL under test.</param>
            /// <param name="cancellationToken">The request cancellation token.</param>
            /// <returns>The fixture or a missing Android BCL page that exercises Oracle fallback.</returns>
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref Requests);
                var active = Interlocked.Increment(ref _active);
                while (true)
                {
                    var maximum = Volatile.Read(ref MaximumActive);
                    if (maximum >= active || Interlocked.CompareExchange(ref MaximumActive, active, maximum) == maximum)
                    {
                        break;
                    }
                }
                try
                {
                    await Task.Delay(40, cancellationToken).ConfigureAwait(false);
                    if (request.RequestUri!.Host == "developer.android.com" && request.RequestUri.AbsolutePath.Contains("java/lang/Integer", StringComparison.Ordinal))
                    {
                        return new HttpResponseMessage(HttpStatusCode.NotFound);
                    }
                    var html = request.RequestUri.Host == "developer.android.com" ? _android : _oracle;
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") };
                }
                finally
                {
                    Interlocked.Decrement(ref _active);
                }
            }
        }
    }
}
