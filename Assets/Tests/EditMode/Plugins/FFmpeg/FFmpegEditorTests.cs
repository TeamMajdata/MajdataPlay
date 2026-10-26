using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MajdataPlay.FFmpeg;
using MajdataPlay.FFmpeg.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies the Edit Mode behaviour of the FFmpeg player and capturer Inspectors that does not require an IMGUI context.</summary>
    public class FFmpegEditorTests
    {
        /// <summary>Tracks the objects created by the current test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Creates the object tracker.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
        }

        /// <summary>Destroys created objects and editors.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
        }

        /// <summary>Creates a tracked Inspector for a component.</summary>
        /// <typeparam name="T">The expected editor type.</typeparam>
        /// <param name="target">The inspected component.</param>
        /// <returns>The editor.</returns>
        private T CreateEditor<T>(Object target) where T : UnityEditor.Editor
        {
            return (T)_objects.Track(UnityEditor.Editor.CreateEditor(target));
        }

        /// <summary>Checks the bit-rate formatting shared by both Inspectors.</summary>
        /// <param name="editorType">The Inspector type.</param>
        [TestCase(typeof(FFmpegVideoPlayerEditor))]
        [TestCase(typeof(FFmpegCameraCapturerEditor))]
        public void FormatBitRate_UsesReadableUnits(Type editorType)
        {
            string Format(long value)
            {
                return PrivateMember.InvokeStatic<string>(editorType, "FormatBitRate", value);
            }

            Assert.That(Format(0), Is.EqualTo("Unknown"));
            Assert.That(Format(-1), Is.EqualTo("Unknown"));
            Assert.That(Format(999), Is.EqualTo("999 bps"));
            Assert.That(Format(1500), Is.EqualTo($"{1.5:F2} kbps"));
            Assert.That(Format(2_500_000), Is.EqualTo($"{2.5:F2} Mbps"));
        }

        /// <summary>Checks that the player Inspector only repaints continuously in Play Mode.</summary>
        [Test]
        public void PlayerEditor_DoesNotRepaintInEditMode()
        {
            var player = _objects.Add<FFmpegVideoPlayer>();
            var editor = CreateEditor<FFmpegVideoPlayerEditor>(player);

            Assert.That(editor.RequiresConstantRepaint(), Is.False);
        }

        /// <summary>Checks the capturer Inspector's lifecycle bookkeeping and sampling outside Play Mode.</summary>
        [Test]
        public void CapturerEditor_TracksActivationOutsidePlayMode()
        {
            var capturer = _objects.Add<FFmpegCameraCapturer>();
            var editor = CreateEditor<FFmpegCameraCapturerEditor>(capturer);

            Assert.That(PrivateMember.GetField<bool>(editor, "_inspectorActive"), Is.True);
            Assert.That(editor.RequiresConstantRepaint(), Is.False);
            PrivateMember.SetField(editor, "_submittedFrameRate", 12.0);
            PrivateMember.Invoke(editor, "OnEditorUpdate");

            Assert.That(PrivateMember.GetField<double>(editor, "_submittedFrameRate"), Is.EqualTo(0));
            Assert.That(PrivateMember.GetField<bool>(editor, "_hasSample"), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(FFmpegCameraCapturerEditor), "IsLiveSceneCapturer", capturer), Is.False);

            var activation = PrivateMember.GetField<int>(editor, "_activation");
            PrivateMember.Invoke(editor, "OnDisable");
            Assert.That(PrivateMember.GetField<bool>(editor, "_inspectorActive"), Is.False);
            Assert.That(PrivateMember.GetField<int>(editor, "_activation"), Is.EqualTo(activation + 1));
            PrivateMember.Invoke(editor, "OnEditorUpdate");
        }

        /// <summary>Checks the pending-operation helper.</summary>
        [Test]
        public void IsPending_ReportsIncompleteTasks()
        {
            var pending = new TaskCompletionSource<bool>();

            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(FFmpegCameraCapturerEditor), "IsPending", new object?[] { null }), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(FFmpegCameraCapturerEditor), "IsPending", pending.Task), Is.True);
            pending.SetResult(true);
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(FFmpegCameraCapturerEditor), "IsPending", pending.Task), Is.False);
        }

        /// <summary>Checks that pausing an idle capturer from the Inspector clears errors and samples.</summary>
        [Test]
        public void SetPaused_IdleCapturer_ClearsState()
        {
            var capturer = _objects.Add<FFmpegCameraCapturer>();
            var editor = CreateEditor<FFmpegCameraCapturerEditor>(capturer);
            PrivateMember.SetField(editor, "_operationError", "old");

            PrivateMember.Invoke(editor, "SetPaused", capturer, true);
            Assert.That(PrivateMember.GetField<string?>(editor, "_operationError"), Is.Null);
            PrivateMember.SetField(editor, "_operationError", "old");
            PrivateMember.Invoke(editor, "SetPaused", capturer, false);

            Assert.That(PrivateMember.GetField<string?>(editor, "_operationError"), Is.Null);
            Assert.That(capturer.State, Is.EqualTo(CameraCaptureState.Idle));
        }

        /// <summary>Checks that a failed asynchronous control is logged and shown only by the Inspector activation that started it.</summary>
        [Test]
        public void ObserveOperation_ReportsFailureToActiveInspector()
        {
            var capturer = _objects.Add<FFmpegCameraCapturer>();
            var editor = CreateEditor<FFmpegCameraCapturerEditor>(capturer);
            var activation = PrivateMember.GetField<int>(editor, "_activation");
            var reference = new WeakReference<FFmpegCameraCapturerEditor>(editor);
            LogAssert.Expect(LogType.Exception, new Regex("record failed"));
            LogAssert.Expect(LogType.Exception, new Regex("stale failure"));

            var observed = PrivateMember.InvokeStatic<Task>(typeof(FFmpegCameraCapturerEditor), "ObserveOperationAsync",
                Task.FromException(new InvalidOperationException("record failed")), capturer, reference, activation);
            var stale = PrivateMember.InvokeStatic<Task>(typeof(FFmpegCameraCapturerEditor), "ObserveOperationAsync",
                Task.FromException(new InvalidOperationException("stale failure")), capturer, reference, activation + 5);
            var succeeded = PrivateMember.InvokeStatic<Task>(typeof(FFmpegCameraCapturerEditor), "ObserveOperationAsync",
                Task.CompletedTask, capturer, reference, activation);

            Assert.That(observed.IsCompleted && stale.IsCompleted && succeeded.IsCompleted, Is.True);
            Assert.That(PrivateMember.GetField<string?>(editor, "_operationError"), Is.EqualTo("record failed"));
        }
    }
}
