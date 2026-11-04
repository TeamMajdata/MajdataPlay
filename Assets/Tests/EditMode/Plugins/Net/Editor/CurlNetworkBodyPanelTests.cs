#nullable enable
using System.Collections.Generic;
using System.Text;
using MajdataPlay.Net.Editor;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.Net.Editor
{
    /// <summary>
    /// Covers <see cref="CurlNetworkBodyPanel"/>: when it reads a body again, which display mode it really shows and the
    /// text it copies.
    /// </summary>
    public class CurlNetworkBodyPanelTests
    {
        /// <summary>The panel under test.</summary>
        private CurlNetworkBodyPanel _panel = null!;

        /// <summary>Creates the panel.</summary>
        [SetUp]
        public void SetUp()
        {
            _panel = new CurlNetworkBodyPanel();
        }

        /// <summary>Destroys the panel's preview texture.</summary>
        [TearDown]
        public void TearDown()
        {
            _panel.Dispose();
        }

        /// <summary>Checks that the body is read again only when the record or its version changes.</summary>
        [Test]
        public void Update_ReadsBodyOncePerVersion()
        {
            var reads = 0;
            byte[] Read()
            {
                reads++;
                return Encoding.UTF8.GetBytes("v" + reads);
            }

            _panel.Update(1, 1, Read, "text/plain");
            _panel.Update(1, 1, Read, "text/plain");
            Assert.That(reads, Is.EqualTo(1));
            _panel.Update(1, 2, Read, "text/plain");
            _panel.Update(2, 2, Read, "text/plain");

            Assert.That(reads, Is.EqualTo(3));
            Assert.That(Encoding.UTF8.GetString(_panel.Data), Is.EqualTo("v3"));

            _panel.Clear();
            Assert.That(_panel.Data, Is.Empty);
            _panel.Update(2, 2, Read, "text/plain");
            Assert.That(reads, Is.EqualTo(4), "A cleared panel reads the body again.");
        }

        /// <summary>Checks the copied text of JSON in every mode.</summary>
        [Test]
        public void GetText_FormatsJson()
        {
            _panel.Update(1, 1, () => Encoding.UTF8.GetBytes("{\"a\":1}"), "application/json");

            Assert.That(_panel.CanPreview, Is.False);
            Assert.That(_panel.GetText(CurlNetworkBodyMode.Pretty), Is.EqualTo("{\n  \"a\": 1\n}"));
            Assert.That(_panel.GetText(CurlNetworkBodyMode.Raw), Is.EqualTo("{\"a\":1}"));
            Assert.That(_panel.GetText(CurlNetworkBodyMode.Hex), Does.StartWith("00000000  7B 22 61 22"));
            Assert.That(_panel.GetText(CurlNetworkBodyMode.Preview), Is.EqualTo(_panel.GetText(CurlNetworkBodyMode.Pretty)), "Preview falls back to Pretty for non-images.");
        }

        /// <summary>Checks that binary bodies are shown as hex instead of pretty text, and that images can be previewed.</summary>
        [Test]
        public void GetText_UsesHexForBinaryAndImages()
        {
            _panel.Update(1, 1, () => new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png");

            Assert.That(_panel.CanPreview, Is.True);
            Assert.That(_panel.GetText(CurlNetworkBodyMode.Pretty), Does.StartWith("00000000  89 50 4E 47"));
            Assert.That(_panel.GetText(CurlNetworkBodyMode.Preview), Does.StartWith("00000000  89 50 4E 47"));
        }

        /// <summary>Checks that form bodies are recognized and decoded into fields.</summary>
        [Test]
        public void GetFormFields_DecodesFormBody()
        {
            _panel.Update(1, 1, () => Encoding.ASCII.GetBytes("user=maj+data&level=13%2B"), "application/x-www-form-urlencoded; charset=utf-8");

            Assert.That(_panel.IsForm, Is.True);
            Assert.That(_panel.GetFormFields(), Is.EqualTo(new[]
            {
                new KeyValuePair<string, string>("user", "maj data"),
                new KeyValuePair<string, string>("level", "13+"),
            }));
        }
    }
}
