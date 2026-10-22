using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Harness.PlayMode;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Harness
{
    /// <summary>
    /// Verifies that EditMode tests compiled into Assembly-CSharp-Editor reach the main project's Assembly-CSharp and
    /// that the shared helpers behave as the other suites expect.
    /// </summary>
    public class HarnessTests
    {
        /// <summary>Tracks objects created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Creates the object tracker for each test.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
        }

        /// <summary>Destroys every object created by the test.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
        }

        /// <summary>Checks that a main project type is visible and lives in Assembly-CSharp.</summary>
        [Test]
        public void MainProjectTypes_AreReachable()
        {
            Assert.That(typeof(MajEnv).Assembly.GetName().Name, Is.EqualTo("Assembly-CSharp"));
        }

        /// <summary>Checks that private members can be written and read back through <see cref="PrivateMember"/>.</summary>
        [Test]
        public void PrivateMember_RoundTripsFieldsPropertiesAndMethods()
        {
            var probe = _objects.Add<HarnessProbe>();

            PrivateMember.SetField(probe, "_counter", 41);
            PrivateMember.Invoke(probe, "Increment");
            PrivateMember.SetProperty(probe, nameof(HarnessProbe.Label), "probe");

            Assert.That(PrivateMember.GetField<int>(probe, "_counter"), Is.EqualTo(42));
            Assert.That(PrivateMember.GetProperty<string>(probe, nameof(HarnessProbe.Label)), Is.EqualTo("probe"));
        }

        /// <summary>Checks that Unity messages are delivered in EditMode and that missing messages are reported.</summary>
        [Test]
        public void UnityMessages_InvokeDeclaredMessagesOnly()
        {
            var probe = _objects.Add<HarnessProbe>();

            Assert.That(UnityMessages.Send(probe, "Awake"), Is.True);
            Assert.That(UnityMessages.Send(probe, "OnApplicationQuit"), Is.False);
            Assert.That(UnityMessages.RunCoroutine(probe, "Start"), Is.EqualTo(3));
            Assert.That(probe.Awakened, Is.True);
        }

        /// <summary>Checks that tracked objects are destroyed when the tracker is disposed.</summary>
        [Test]
        public void TestObjects_DestroysTrackedObjects()
        {
            var tracker = new TestObjects();
            var gameObject = tracker.Create("Disposable");

            tracker.Dispose();

            Assert.That(gameObject == null, Is.True);
        }

        /// <summary>Checks that the temporary directory exists until disposed.</summary>
        [Test]
        public void TemporaryDirectory_IsCreatedAndDeleted()
        {
            var directory = new TemporaryDirectory();
            var file = directory.WriteText("probe.txt", "content");

            Assert.That(file.ReadAllText(), Is.EqualTo("content"));

            directory.Dispose();

            Assert.That(directory.Directory.Exists, Is.False);
        }
    }
}
