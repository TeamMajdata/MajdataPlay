#nullable enable
using System;
using MajdataPlay.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;

namespace MajdataPlay.Tests.Plugins.Net
{
    /// <summary>Covers the preconfigured UnityWebRequest factory; requests are created but never sent.</summary>
    public class UnityWebRequestFactoryTests
    {
        /// <summary>The URL used by every request.</summary>
        private const string Url = "http://127.0.0.1:9/resource";

        /// <summary>The user agent configured for each test.</summary>
        private const string FixtureAgent = "Fixture/1";

        /// <summary>The factory timeout before the test.</summary>
        private TimeSpan _timeout;

        /// <summary>The factory user agent before the test.</summary>
        private string _userAgent = string.Empty;

        /// <summary>Captures the static factory settings and applies a two-second timeout and the fixture agent.</summary>
        [SetUp]
        public void SetUp()
        {
            _timeout = UnityWebRequestFactory.Timeout;
            _userAgent = UnityWebRequestFactory.UserAgent;
            UnityWebRequestFactory.Timeout = TimeSpan.FromSeconds(2);
            UnityWebRequestFactory.UserAgent = FixtureAgent;
        }

        /// <summary>Restores the static factory settings.</summary>
        [TearDown]
        public void TearDown()
        {
            UnityWebRequestFactory.Timeout = _timeout;
            UnityWebRequestFactory.UserAgent = _userAgent;
        }

        /// <summary>Checks that the shared timeout and user agent settings are stored.</summary>
        [Test]
        public void Settings_AreStored()
        {
            UnityWebRequestFactory.Timeout = TimeSpan.FromSeconds(7);
            UnityWebRequestFactory.UserAgent = "Agent/2";

            Assert.That(UnityWebRequestFactory.Timeout, Is.EqualTo(TimeSpan.FromSeconds(7)));
            Assert.That(UnityWebRequestFactory.UserAgent, Is.EqualTo("Agent/2"));
        }

        /// <summary>Checks that GET requests from strings and URIs carry the method, timeout and user agent.</summary>
        [Test]
        public void Get_AppliesSettings()
        {
            UnityWebRequestFactory.Timeout = TimeSpan.FromMilliseconds(3500);
            UnityWebRequestFactory.UserAgent = "Tests/1.0";

            using var byString = UnityWebRequestFactory.Get(Url);
            using var byUri = UnityWebRequestFactory.Get(new Uri(Url));

            foreach (var request in new[] { byString, byUri })
            {
                Assert.That(request.method, Is.EqualTo(UnityWebRequest.kHttpVerbGET));
                Assert.That(request.url, Is.EqualTo(Url));
                Assert.That(request.timeout, Is.EqualTo(3));
                Assert.That(request.GetRequestHeader("User-Agent"), Is.EqualTo("Tests/1.0"));
            }
        }

        /// <summary>Checks that HEAD requests from strings and URIs carry the method, timeout and user agent.</summary>
        [Test]
        public void Head_AppliesSettings()
        {
            using var byString = UnityWebRequestFactory.Head(Url);
            using var byUri = UnityWebRequestFactory.Head(new Uri(Url));

            foreach (var request in new[] { byString, byUri })
            {
                Assert.That(request.method, Is.EqualTo(UnityWebRequest.kHttpVerbHEAD));
                Assert.That(request.timeout, Is.EqualTo(2));
                Assert.That(request.GetRequestHeader("User-Agent"), Is.EqualTo(FixtureAgent));
            }
        }

        /// <summary>Checks that POST requests with raw data carry the body, content type and settings.</summary>
        [Test]
        public void Post_WithRawDataAppliesSettings()
        {
            using var byString = UnityWebRequestFactory.Post(Url, "{\"a\":1}", "application/json");
            using var byUri = UnityWebRequestFactory.Post(new Uri(Url), "{\"a\":1}", "application/json");

            foreach (var request in new[] { byString, byUri })
            {
                Assert.That(request.method, Is.EqualTo(UnityWebRequest.kHttpVerbPOST));
                Assert.That(request.uploadHandler.contentType, Is.EqualTo("application/json"));
                Assert.That(request.uploadHandler.data.Length, Is.EqualTo(7));
                Assert.That(request.timeout, Is.EqualTo(2));
                Assert.That(request.GetRequestHeader("User-Agent"), Is.EqualTo(FixtureAgent));
            }
        }

        /// <summary>Checks that POST requests with form data carry the form and settings.</summary>
        [Test]
        public void Post_WithFormAppliesSettings()
        {
            var form = new WWWForm();
            form.AddField("name", "value");

            using var byString = UnityWebRequestFactory.Post(Url, form);
            using var byUri = UnityWebRequestFactory.Post(new Uri(Url), form);

            foreach (var request in new[] { byString, byUri })
            {
                Assert.That(request.method, Is.EqualTo(UnityWebRequest.kHttpVerbPOST));
                Assert.That(request.uploadHandler.data, Is.Not.Empty);
                Assert.That(request.GetRequestHeader("User-Agent"), Is.EqualTo(FixtureAgent));
            }
        }

        /// <summary>Checks that a request that has not completed is neither successful nor accepted by the success check.</summary>
        [Test]
        public void Extensions_RejectUnfinishedRequests()
        {
            using var request = UnityWebRequestFactory.Get(Url);

            Assert.That(request.IsSuccessStatusCode(), Is.False);
            Assert.Throws<InvalidOperationException>(() => request.EnsureSuccessStatusCode());
        }
    }
}
