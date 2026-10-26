#nullable enable
using System;
using MajdataPlay.Net;
using Newtonsoft.Json;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.Net
{
    /// <summary>Covers URI combination, transfer progress and the endpoint configuration models.</summary>
    public class NetModelTests
    {
        /// <summary>Checks that a single relative string or URI is resolved against the base like the URI constructor.</summary>
        [Test]
        public void Combine_ResolvesSingleRelativeUri()
        {
            var baseUri = new Uri("https://example.invalid/api/");

            Assert.That(baseUri.Combine("charts").AbsoluteUri, Is.EqualTo("https://example.invalid/api/charts"));
            Assert.That(baseUri.Combine(new Uri("charts", UriKind.Relative)).AbsoluteUri, Is.EqualTo("https://example.invalid/api/charts"));
            Assert.That(new Uri("https://example.invalid/api").Combine("charts").AbsoluteUri, Is.EqualTo("https://example.invalid/charts"));
            Assert.That(baseUri.Combine("/root").AbsoluteUri, Is.EqualTo("https://example.invalid/root"));
        }

        /// <summary>Checks that several relative parts are resolved one after another.</summary>
        [Test]
        public void Combine_ResolvesSeveralParts()
        {
            var baseUri = new Uri("https://example.invalid/");

            var fromStrings = baseUri.Combine("api/", "charts/", "list");
            var fromUris = baseUri.Combine(new Uri("api/", UriKind.Relative), new Uri("list", UriKind.Relative));
            var single = baseUri.Combine(new[] { "only" });
            var singleUri = baseUri.Combine(new[] { new Uri("only", UriKind.Relative) });
            var none = baseUri.Combine(Array.Empty<string>());

            Assert.That(fromStrings.AbsoluteUri, Is.EqualTo("https://example.invalid/api/charts/list"));
            Assert.That(fromUris.AbsoluteUri, Is.EqualTo("https://example.invalid/api/list"));
            Assert.That(single.AbsoluteUri, Is.EqualTo("https://example.invalid/only"));
            Assert.That(singleUri.AbsoluteUri, Is.EqualTo("https://example.invalid/only"));
            Assert.That(none, Is.SameAs(baseUri));
        }

        /// <summary>Checks that progress reports and byte counts are written through the interfaces and read publicly.</summary>
        [Test]
        public void NetProgress_StoresReportsAndCounts()
        {
            var progress = new NetProgress();
            INetProgress writer = progress;

            writer.TotalBytes = 100;
            writer.TransferredBytes = 40;
            writer.Report(0.4f);

            Assert.That(progress.TotalBytes, Is.EqualTo(100));
            Assert.That(progress.TransferredBytes, Is.EqualTo(40));
            Assert.That(writer.TotalBytes, Is.EqualTo(100));
            Assert.That(writer.TransferredBytes, Is.EqualTo(40));
            Assert.That(progress.Percent, Is.EqualTo(0.4f));
        }

        /// <summary>Checks that resetting clears only the reported percentage.</summary>
        [Test]
        public void NetProgress_ResetClearsPercentOnly()
        {
            var progress = new NetProgress();
            INetProgress writer = progress;
            writer.TotalBytes = 10;
            writer.Report(1f);

            progress.Reset();

            Assert.That(progress.Percent, Is.Zero);
            Assert.That(progress.TotalBytes, Is.EqualTo(10));
        }

        /// <summary>Checks the defaults of an API endpoint and its runtime state.</summary>
        [Test]
        public void ApiEndpoint_HasDefaults()
        {
            var endpoint = new ApiEndpoint { Url = new Uri("https://example.invalid/") };

            Assert.That(endpoint.Name, Is.Empty);
            Assert.That(endpoint.Username, Is.Null);
            Assert.That(endpoint.Password, Is.Null);
            Assert.That(endpoint.AutoLogin, Is.False);
            Assert.That(endpoint.RuntimeConfig.Username, Is.Empty);
            Assert.That(endpoint.RuntimeConfig.Nickname, Is.Empty);
            Assert.That(endpoint.RuntimeConfig.IsLoggedIn, Is.False);
            Assert.That(endpoint.RuntimeConfig.AuthMethod, Is.EqualTo(NetAuthMethodOption.None));
            Assert.That(endpoint.RuntimeConfig.AuthUsername, Is.Null);
            Assert.That(endpoint.RuntimeConfig.AuthPassword, Is.Null);
            Assert.That(endpoint.RuntimeConfig.Avatar, Is.Null);
        }

        /// <summary>Checks that the runtime state is never serialized with the endpoint while the settings round-trip.</summary>
        [Test]
        public void ApiEndpoint_SerializesWithoutRuntimeConfig()
        {
            var endpoint = new ApiEndpoint
            {
                Name = "main",
                Url = new Uri("https://example.invalid/api/"),
                Username = "user",
                Password = "secret",
                AutoLogin = true
            };
            endpoint.RuntimeConfig.IsLoggedIn = true;
            endpoint.RuntimeConfig.AuthMethod = NetAuthMethodOption.Cookie;

            var json = JsonConvert.SerializeObject(endpoint);
            var copy = JsonConvert.DeserializeObject<ApiEndpoint>(json)!;

            Assert.That(json, Does.Not.Contain("RuntimeConfig"));
            Assert.That(copy.Name, Is.EqualTo("main"));
            Assert.That(copy.Url, Is.EqualTo(endpoint.Url));
            Assert.That(copy.AutoLogin, Is.True);
            Assert.That(copy.RuntimeConfig.IsLoggedIn, Is.False);
        }

        /// <summary>Checks that runtime state properties are writable.</summary>
        [Test]
        public void ApiRuntimeConfig_StoresState()
        {
            var config = new ApiRuntimeConfig
            {
                Username = "id",
                Nickname = "nick",
                IsLoggedIn = true,
                AuthMethod = NetAuthMethodOption.OAuth,
                AuthUsername = "u",
                AuthPassword = "p"
            };

            Assert.That(config.Username, Is.EqualTo("id"));
            Assert.That(config.Nickname, Is.EqualTo("nick"));
            Assert.That(config.IsLoggedIn, Is.True);
            Assert.That(config.AuthMethod, Is.EqualTo(NetAuthMethodOption.OAuth));
            Assert.That(config.AuthUsername, Is.EqualTo("u"));
            Assert.That(config.AuthPassword, Is.EqualTo("p"));
        }
    }
}
