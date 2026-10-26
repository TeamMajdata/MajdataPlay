using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MajdataPlay.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>Verifies Android collection selection and cancellation before native warmup starts.</summary>
    public class AndroidGraphicsStateWarmupTests
    {
        /// <summary>The quality level shared by compatible test collections.</summary>
        private const string QualityLevelName = "AndroidTestQuality";

        /// <summary>The native collections owned by the current test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Starts tracking each test's native objects.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
        }

        /// <summary>Destroys the native collections created by the test.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
        }

        /// <summary>Checks that absent resources are rejected without accessing native state.</summary>
        [Test]
        public void IsCompatible_NullCollection_ReturnsFalse()
        {
            Assert.That(AndroidGraphicsStateWarmup.IsCompatible(null, GraphicsDeviceType.OpenGLES3, QualityLevelName), Is.False);
        }

        /// <summary>Checks that a released Unity collection is treated as unavailable.</summary>
        [Test]
        public void IsCompatible_DestroyedCollection_ReturnsFalse()
        {
            var collection = CreateCollection(GraphicsDeviceType.OpenGLES3);
            Object.DestroyImmediate(collection);

            Assert.That(AndroidGraphicsStateWarmup.IsCompatible(collection, GraphicsDeviceType.OpenGLES3, QualityLevelName), Is.False);
        }

        /// <summary>Checks that matching metadata cannot make an empty collection usable.</summary>
        [Test]
        public void IsCompatible_EmptyCollection_ReturnsFalse()
        {
            var collection = CreateCollection(GraphicsDeviceType.OpenGLES3, false);

            Assert.That(AndroidGraphicsStateWarmup.IsCompatible(collection, GraphicsDeviceType.OpenGLES3, QualityLevelName), Is.False);
        }

        /// <summary>Checks that a collection with matching Android metadata is selected.</summary>
        /// <param name="graphicsDeviceType">The Android graphics API recorded in the collection.</param>
        [TestCase(GraphicsDeviceType.OpenGLES3)]
        [TestCase(GraphicsDeviceType.Vulkan)]
        public void IsCompatible_MatchingMetadata_ReturnsTrue(GraphicsDeviceType graphicsDeviceType)
        {
            var collection = CreateCollection(graphicsDeviceType);

            Assert.That(AndroidGraphicsStateWarmup.IsCompatible(collection, graphicsDeviceType, QualityLevelName), Is.True);
        }

        /// <summary>Checks that GLES collections remain usable when tracing records variants without pipeline states.</summary>
        [Test]
        public void IsCompatible_OpenGlesVariantsWithoutGraphicsStates_ReturnsTrue()
        {
            var collection = CreateCollection(GraphicsDeviceType.OpenGLES3);

            Assert.That(collection.variantCount, Is.EqualTo(1));
            Assert.That(collection.totalGraphicsStateCount, Is.Zero);
            Assert.That(AndroidGraphicsStateWarmup.IsCompatible(collection, GraphicsDeviceType.OpenGLES3, QualityLevelName), Is.True);
        }

        /// <summary>Checks that collections from a different platform cannot be selected on Android.</summary>
        /// <param name="runtimePlatform">The incompatible platform recorded in the collection.</param>
        [TestCase(RuntimePlatform.WindowsEditor)]
        [TestCase(RuntimePlatform.WindowsPlayer)]
        [TestCase(RuntimePlatform.IPhonePlayer)]
        public void IsCompatible_DifferentPlatform_ReturnsFalse(RuntimePlatform runtimePlatform)
        {
            var collection = CreateCollection(GraphicsDeviceType.OpenGLES3);
            collection.runtimePlatform = runtimePlatform;

            Assert.That(AndroidGraphicsStateWarmup.IsCompatible(collection, GraphicsDeviceType.OpenGLES3, QualityLevelName), Is.False);
        }

        /// <summary>Checks that Vulkan and GLES collections are never selected interchangeably.</summary>
        /// <param name="collectionApi">The graphics API recorded in the collection.</param>
        /// <param name="activeApi">The graphics API used by the running player.</param>
        [TestCase(GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3)]
        [TestCase(GraphicsDeviceType.OpenGLES3, GraphicsDeviceType.Vulkan)]
        public void IsCompatible_DifferentGraphicsApi_ReturnsFalse(GraphicsDeviceType collectionApi, GraphicsDeviceType activeApi)
        {
            var collection = CreateCollection(collectionApi);

            Assert.That(AndroidGraphicsStateWarmup.IsCompatible(collection, activeApi, QualityLevelName), Is.False);
        }

        /// <summary>Checks that quality names must match exactly, including their casing.</summary>
        /// <param name="activeQuality">The quality name used by the running player.</param>
        [TestCase("DifferentQuality")]
        [TestCase("androidtestquality")]
        [TestCase("")]
        public void IsCompatible_DifferentQuality_ReturnsFalse(string activeQuality)
        {
            var collection = CreateCollection(GraphicsDeviceType.OpenGLES3);

            Assert.That(AndroidGraphicsStateWarmup.IsCompatible(collection, GraphicsDeviceType.OpenGLES3, activeQuality), Is.False);
        }

        /// <summary>Checks cancellation before warmup or the already-complete fast path can run.</summary>
        /// <param name="includeVariant">Whether the collection contains a shader variant.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void WarmUpAsync_PreCancelledToken_ThrowsCancellation(bool includeVariant)
        {
            var collection = CreateCollection(GraphicsDeviceType.OpenGLES3, includeVariant);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
            {
                AndroidGraphicsStateWarmup.WarmUpAsync(collection, cancellation.Token).GetAwaiter().GetResult();
            });
            Assert.That(collection.completedWarmupCount, Is.Zero);
        }

        /// <summary>Checks that empty collections complete without scheduling native warmup jobs.</summary>
        [Test]
        public void WarmUpAsync_EmptyCollection_CompletesImmediately()
        {
            var collection = CreateCollection(GraphicsDeviceType.OpenGLES3, false);
            var warmup = AndroidGraphicsStateWarmup.WarmUpAsync(collection, CancellationToken.None);

            Assert.That(warmup.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            warmup.GetAwaiter().GetResult();
            Assert.That(collection.completedWarmupCount, Is.Zero);
        }

        /// <summary>Creates a native collection with controlled metadata and an optional real shader variant.</summary>
        /// <param name="graphicsDeviceType">The graphics API to record in the collection.</param>
        /// <param name="includeVariant">Whether to add one built-in sprite shader variant.</param>
        /// <returns>A collection tracked for destruction after the test.</returns>
        private GraphicsStateCollection CreateCollection(GraphicsDeviceType graphicsDeviceType, bool includeVariant = true)
        {
            var collection = _objects.Track(new GraphicsStateCollection
            {
                runtimePlatform = RuntimePlatform.Android,
                graphicsDeviceType = graphicsDeviceType,
                qualityLevelName = QualityLevelName
            });
            if (includeVariant)
            {
                var shader = Shader.Find("Sprites/Default");
                Assert.That(shader, Is.Not.Null, "The built-in sprite shader must be available for the collection fixture.");
                Assert.That(collection.AddVariant(shader, new PassIdentifier(0, 0), Array.Empty<LocalKeyword>()), Is.True);
            }
            return collection;
        }
    }
}
