using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.FFmpeg;
using MajdataPlay.IO;
using MajdataPlay.IO.Storage;
using MajdataPlay.Recording;
using MajdataPlay.Settings;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Misc.Support;
using NUnit.Framework;
using UnityEngine.TestTools;
using WebSocketSharp;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Recording
{
    /// <summary>
    /// Covers the managed recording logic: the <see cref="Recorder"/> facade, the lifecycle and file naming of
    /// <see cref="FFmpegRecorder"/> and the OBS message handling of <see cref="OBSRecorder"/>. No capture device, encoder or
    /// OBS connection is used.
    /// </summary>
    public class RecordingTests
    {
        /// <summary>A recorder that records the calls made by <see cref="Recorder"/>.</summary>
        private sealed class FakeRecorder : IRecorder
        {
            /// <summary>Gets or sets whether the fake reports an active recording.</summary>
            public bool IsRecording { get; set; }

            /// <summary>Gets or sets whether the fake reports a connection.</summary>
            public bool IsConnected { get; set; }

            /// <summary>Gets the calls received, in order.</summary>
            public System.Collections.Generic.List<string> Calls { get; } = new();

            /// <summary>Records a synchronous start.</summary>
            public void StartRecord()
            {
                Calls.Add("Start");
            }

            /// <summary>Records an asynchronous start.</summary>
            /// <returns>A completed task.</returns>
            public Task StartRecordAsync()
            {
                Calls.Add("StartAsync");
                return Task.CompletedTask;
            }

            /// <summary>Records a synchronous stop.</summary>
            public void StopRecord()
            {
                Calls.Add("Stop");
            }

            /// <summary>Records an asynchronous stop.</summary>
            /// <returns>A completed task.</returns>
            public Task StopRecordAsync()
            {
                Calls.Add("StopAsync");
                return Task.CompletedTask;
            }

            /// <summary>Records a late update.</summary>
            public void OnLateUpdate()
            {
                Calls.Add("LateUpdate");
            }

            /// <summary>Records the output name.</summary>
            /// <param name="name">The output name.</param>
            public void SetOutputName(string name)
            {
                Calls.Add("Name:" + name);
            }

            /// <summary>Records disposal.</summary>
            public void Dispose()
            {
                Calls.Add("Dispose");
            }
        }

        /// <summary>An audio source that records how the recorder drives it.</summary>
        private sealed class FakeAudioRecorder : IAudioRecorder
        {
            /// <summary>Gets the sample rate.</summary>
            public int SampleRate => 48000;

            /// <summary>Gets the channel count.</summary>
            public int Channels => 2;

            /// <summary>Gets whether capture is running.</summary>
            public bool IsRecording { get; private set; }

            /// <summary>Gets the dropped frame count.</summary>
            public long DroppedFrames => 0;

            /// <summary>Gets the playback delay.</summary>
            public TimeSpan PlaybackDelay => TimeSpan.Zero;

            /// <summary>Gets the number of Stop calls.</summary>
            public int StopCount { get; private set; }

            /// <summary>Gets the number of Dispose calls.</summary>
            public int DisposeCount { get; private set; }

            /// <summary>Starts capture.</summary>
            public void Start()
            {
                IsRecording = true;
            }

            /// <summary>Stops capture.</summary>
            public void Stop()
            {
                IsRecording = false;
                StopCount++;
            }

            /// <summary>Reads no samples.</summary>
            /// <param name="destination">The ignored destination.</param>
            /// <returns>Always zero.</returns>
            public int Read(Span<float> destination)
            {
                return 0;
            }

            /// <summary>Counts disposal.</summary>
            public void Dispose()
            {
                DisposeCount++;
            }
        }

        /// <summary>The temporary storage.</summary>
        private TemporaryDirectory _temporary = null!;

        /// <summary>The scope restoring the application directories.</summary>
        private MajEnvScope _environment = null!;

        /// <summary>The objects created by the test.</summary>
        private TestObjects _objects = null!;

        /// <summary>The recorder installed in the facade before the test.</summary>
        private object? _savedRecorder;

        /// <summary>The facade initialization flag before the test.</summary>
        private bool _savedIsInited;

        /// <summary>Prepares temporary directories and captures the facade state.</summary>
        [SetUp]
        public void SetUp()
        {
            _temporary = new TemporaryDirectory();
            _environment = new MajEnvScope();
            _environment.UseTemporaryLayout(_temporary);
            _objects = new TestObjects();
            _savedRecorder = PrivateMember.GetStaticField<object?>(typeof(Recorder), "s_recorder");
            _savedIsInited = PrivateMember.GetStaticField<bool>(typeof(Recorder), "s_isInited");
        }

        /// <summary>Restores the facade and the directories.</summary>
        [TearDown]
        public void TearDown()
        {
            var handler = (EventHandler<EventArgs?>)Delegate.CreateDelegate(typeof(EventHandler<EventArgs?>),
                typeof(Recorder).GetMethod("OnApplicationQuit", BindingFlags.Static | BindingFlags.NonPublic)!);
            GameManager.OnAppQuit -= handler;
            PrivateMember.SetStaticField(typeof(Recorder), "s_recorder", _savedRecorder);
            PrivateMember.SetStaticField(typeof(Recorder), "s_isInited", _savedIsInited);
            _objects.Dispose();
            _environment.Dispose();
            _temporary.Dispose();
        }

#if UNITY_STANDALONE
        /// <summary>Installs a fake recorder in the facade.</summary>
        /// <param name="connected">Whether the recorder is connected.</param>
        /// <param name="enabled">Whether OBS recording is enabled in the settings.</param>
        /// <returns>The fake recorder.</returns>
        private static FakeRecorder InstallRecorder(bool connected, bool enabled)
        {
            var recorder = new FakeRecorder { IsConnected = connected };
            PrivateMember.SetStaticField(typeof(Recorder), "s_recorder", recorder);
            MajEnv.Settings.Game.RecordMode = enabled ? RecordModeOption.OBSTrigger : RecordModeOption.Disable;
            return recorder;
        }

        /// <summary>Checks that the facade forwards recording calls only when OBS recording is enabled and connected.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RecordHelper_ForwardsWhenEnabledAndConnected()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var recorder = InstallRecorder(true, true);

                Recorder.StartRecord("chart");
                await Recorder.StartRecordAsync("chart2");
                Recorder.StopRecord();
                await Recorder.StopRecordAsync();
                Recorder.OnLateUpdate();
                recorder.IsRecording = true;

                Assert.That(Recorder.IsEnabled, Is.True);
                Assert.That(Recorder.IsConnected, Is.True);
                Assert.That(Recorder.IsRecording, Is.True);
                Assert.That(recorder.Calls, Is.EqualTo(new[] { "Name:chart", "Start", "Name:chart2", "StartAsync", "Stop", "StopAsync", "LateUpdate" }));
            });
        }

        /// <summary>Checks that the facade ignores recording calls while disabled or disconnected.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RecordHelper_IgnoresCallsWhenUnavailable()
        {
            return UniTask.ToCoroutine(async () =>
            {
                foreach (var (connected, enabled) in new[] { (true, false), (false, true) })
                {
                    var recorder = InstallRecorder(connected, enabled);

                    Recorder.StartRecord("chart");
                    await Recorder.StartRecordAsync("chart");
                    Recorder.StopRecord();
                    await Recorder.StopRecordAsync();

                    Assert.That(recorder.Calls, Is.Empty);
                }
                PrivateMember.SetStaticField(typeof(Recorder), "s_recorder", null);
                Assert.That(Recorder.IsRecording, Is.False);
            });
        }

        /// <summary>Checks that an initialized facade stops recording when the application quits.</summary>
        [Test]
        public void RecordHelper_StopsOnQuit()
        {
            var recorder = InstallRecorder(true, true);
            PrivateMember.SetStaticField(typeof(Recorder), "s_isInited", true);

            Recorder.Init();
            PrivateMember.InvokeStatic(typeof(Recorder), "OnApplicationQuit", null, null);

            Assert.That(PrivateMember.GetStaticField<object>(typeof(Recorder), "s_recorder"), Is.SameAs(recorder));
            Assert.That(recorder.Calls, Is.EqualTo(new[] { "Stop" }));

            MajEnv.Settings.Game.RecordMode = RecordModeOption.Disable;
            PrivateMember.InvokeStatic(typeof(Recorder), "OnApplicationQuit", null, null);
            Assert.That(recorder.Calls, Is.EqualTo(new[] { "Stop" }));
        }
#endif

        /// <summary>Creates a recorder over a capturer component and a fake audio source.</summary>
        /// <param name="audio">The created audio source.</param>
        /// <returns>The recorder.</returns>
        private FFmpegRecorder CreateFFmpegRecorder(out FakeAudioRecorder audio)
        {
            var capturer = _objects.Add<FFmpegCameraCapturer>();
            audio = new FakeAudioRecorder();
            return new FFmpegRecorder(capturer, audio, MediaContainerFormat.MP4);
        }

        /// <summary>Checks the constructor argument validation.</summary>
        [Test]
        public void FFmpegRecorder_ValidatesArguments()
        {
            var capturer = _objects.Add<FFmpegCameraCapturer>();

            Assert.Throws<ArgumentNullException>(() => new FFmpegRecorder(null!, new FakeAudioRecorder(), MediaContainerFormat.MP4));
            Assert.Throws<ArgumentNullException>(() => new FFmpegRecorder(capturer, null!, MediaContainerFormat.MP4));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FFmpegRecorder(capturer, new FakeAudioRecorder(), (MediaContainerFormat)99));
        }

        /// <summary>Checks the idle state and that stopping or updating an idle recorder does nothing.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator FFmpegRecorder_IdleOperations()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var recorder = CreateFFmpegRecorder(out var audio);

                recorder.SetOutputName(null!);
                recorder.OnLateUpdate();
                recorder.StopRecord();
                await recorder.StopRecordAsync();

                Assert.That(recorder.IsRecording, Is.False);
                Assert.That(recorder.IsConnected, Is.True);
                Assert.That(audio.StopCount, Is.EqualTo(0));
                recorder.Dispose();
            });
        }

        /// <summary>Checks that a capturer that cannot start leaves no temporary files and returns the recorder to idle.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator FFmpegRecorder_FailedStartCleansUp()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var recorder = CreateFFmpegRecorder(out var audio);
                recorder.SetOutputName("Song: Title");
                var failure = default(Exception);

                try
                {
                    await recorder.StartRecordAsync();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                Assert.That(failure, Is.InstanceOf<InvalidOperationException>());
                Assert.That(recorder.IsRecording, Is.False);
                Assert.That(audio.StopCount, Is.EqualTo(1));
                Assert.That(MajEnv.TempDirectory.EnumerateEntries(), Is.Empty);

                recorder.StartRecord();
                await UniTask.Yield();
                Assert.That(recorder.IsRecording, Is.False);
                recorder.Dispose();
            });
        }

        /// <summary>Checks that disposal releases the audio source once and rejects further use.</summary>
        [Test]
        public void FFmpegRecorder_DisposeReleasesAudio()
        {
            var recorder = CreateFFmpegRecorder(out var audio);

            recorder.Dispose();
            recorder.Dispose();

            Assert.That(audio.DisposeCount, Is.EqualTo(1));
            Assert.That(recorder.IsConnected, Is.False);
            Assert.Throws<ObjectDisposedException>(() => recorder.StartRecord());
            Assert.Throws<ObjectDisposedException>(() => recorder.StartRecordAsync());
            Assert.Throws<ObjectDisposedException>(() => recorder.StopRecord());
            Assert.Throws<ObjectDisposedException>(() => recorder.StopRecordAsync());
            recorder.OnLateUpdate();
        }

        /// <summary>Checks the temporary paths and timestamped output names of a recording.</summary>
        [Test]
        public void RecordingFiles_BuildsNames()
        {
            var filesType = typeof(FFmpegRecorder).GetNestedType("RecordingFiles", BindingFlags.NonPublic)!;

            var named = PrivateMember.InvokeStatic(filesType, "Create", "  a/b:c\\d ", MediaContainerFormat.MP4)!;
            var unnamed = PrivateMember.InvokeStatic(filesType, "Create", " ", MediaContainerFormat.Matroska)!;

            var output = PrivateMember.GetProperty<string>(named, "OutputName");
            Assert.That(output, Does.Match(@"^a_b_c_d_\d{4}-\d{2}-\d{2}_\d{2}_\d{2}_\d{2}\.mp4$"));
            Assert.That(PrivateMember.GetProperty<string>(named, "VideoOnlyOutputName"), Does.EndWith("_video-only.mkv"));
            Assert.That(PrivateMember.GetProperty<string>(named, "VideoPath"), Does.StartWith(MajEnv.TempDirectory.Location).And.EndWith(".video.mkv"));
            Assert.That(PrivateMember.GetProperty<string>(named, "AudioPath"), Does.EndWith(".audio.pcm"));
            Assert.That(PrivateMember.GetProperty<string>(named, "MuxedPath"), Does.EndWith(".muxed.mp4"));
            Assert.That(PrivateMember.GetProperty<string>(unnamed, "OutputName"), Does.Match(@"^\d{4}-\d{2}-\d{2}_\d{2}_\d{2}_\d{2}\.mkv$"));
            Assert.That(PrivateMember.GetProperty<string>(unnamed, "MuxedPath"), Does.EndWith(".muxed.mkv"));
        }

        /// <summary>Checks that finished files are moved into the output directory and temporary files are deleted.</summary>
        [Test]
        public void FFmpegRecorder_FileHelpers()
        {
            var source = MajEnv.TempDirectory.CreateFile("capture.mp4");
            source.WriteAllText("video");

            PrivateMember.InvokeStatic(typeof(FFmpegRecorder), "MoveToOutputDirectory", source.Location, "final.mp4");
            var leftover = MajEnv.TempDirectory.CreateFile("leftover.pcm");
            PrivateMember.InvokeStatic(typeof(FFmpegRecorder), "DeleteTemporaryFile", leftover.Location);
            PrivateMember.InvokeStatic(typeof(FFmpegRecorder), "DeleteTemporaryFile", leftover.Location);

            Assert.That(MajEnv.RecordOutputsDirectory.FindFile("final.mp4")!.ReadAllText(), Is.EqualTo("video"));
            Assert.That(source.Exists, Is.False);
            Assert.That(leftover.Exists, Is.False);
        }

        /// <summary>Creates a text WebSocket message.</summary>
        /// <param name="json">The message text.</param>
        /// <returns>The message event arguments.</returns>
        private static MessageEventArgs CreateMessage(string json)
        {
            var constructor = typeof(MessageEventArgs).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Single(candidate =>
                {
                    var parameters = candidate.GetParameters();
                    return parameters.Length == 2 && parameters[0].ParameterType.IsEnum && parameters[1].ParameterType == typeof(byte[]);
                });
            var opcode = Enum.ToObject(constructor.GetParameters()[0].ParameterType, 1);
            return (MessageEventArgs)constructor.Invoke(new object[] { opcode, Encoding.UTF8.GetBytes(json) });
        }

        /// <summary>Checks that OBS identification and record responses update the recorder state.</summary>
        [Test]
        public void ObsRecorder_HandlesResponses()
        {
            var recorder = (OBSRecorder)FormatterServices.GetUninitializedObject(typeof(OBSRecorder));

            PrivateMember.Invoke(recorder, "OnMessageReceived", null, CreateMessage("{\"op\":2,\"d\":{\"negotiatedRpcVersion\":1}}"));
            Assert.That(recorder.IsConnected, Is.True);

            PrivateMember.Invoke(recorder, "OnMessageReceived", null, CreateMessage("{\"op\":7,\"d\":{\"requestType\":\"StartRecord\",\"requestStatus\":{\"result\":false}}}"));
            Assert.That(recorder.IsRecording, Is.False);

            PrivateMember.Invoke(recorder, "OnMessageReceived", null, CreateMessage("{\"op\":7,\"d\":{\"requestType\":\"StartRecord\",\"requestStatus\":{\"result\":true}}}"));
            Assert.That(recorder.IsRecording, Is.True);

            PrivateMember.Invoke(recorder, "OnMessageReceived", null, CreateMessage("{\"op\":7,\"d\":{\"requestType\":\"StopRecord\",\"requestStatus\":{\"result\":false}}}"));
            Assert.That(recorder.IsRecording, Is.True);

            PrivateMember.Invoke(recorder, "OnMessageReceived", null, CreateMessage("{\"op\":7,\"d\":{\"requestType\":\"StopRecord\",\"requestStatus\":{\"result\":true},\"responseData\":{\"outputPath\":\"C:/obs/out.mp4\"}}}"));
            Assert.That(recorder.IsRecording, Is.False);
            Assert.That(PrivateMember.GetField<string>(recorder, "_obsOutPath"), Is.EqualTo("C:/obs/out.mp4"));

            PrivateMember.Invoke(recorder, "OnMessageReceived", null, CreateMessage("{\"op\":0}"));
            PrivateMember.Invoke(recorder, "OnMessageReceived", null, CreateMessage("not json"));
            Assert.That(recorder.IsConnected, Is.True);
        }

        /// <summary>Checks that a stopped OBS recording is moved into the output directory under the chart name.</summary>
        [Test]
        public void ObsRecorder_MovesFinishedRecording()
        {
            var recorder = (OBSRecorder)FormatterServices.GetUninitializedObject(typeof(OBSRecorder));
            var recording = MajEnv.TempDirectory.CreateFile("obs.mp4");
            recording.WriteAllText("obs video");
            recorder.SetOutputName("Chart");
            recorder.OnLateUpdate();
            PrivateMember.SetField(recorder, "_obsOutPath", recording.Location);

            PrivateMember.Invoke(recorder, "OnMessageReceived", null, CreateMessage(
                "{\"op\":5,\"d\":{\"eventType\":\"RecordStateChanged\",\"eventData\":{\"outputState\":\"OBS_WEBSOCKET_OUTPUT_STOPPED\"}}}"));

            var moved = MajEnv.RecordOutputsDirectory.EnumerateEntries().Single();
            Assert.That(moved.Name, Does.Match(@"^Chart_\d{4}-\d{2}-\d{2}_\d{2}_\d{2}_\d{2}\.mp4$"));
            Assert.That(recording.Exists, Is.False);

            PrivateMember.Invoke(recorder, "OnMessageReceived", null, CreateMessage(
                "{\"op\":5,\"d\":{\"eventType\":\"RecordStateChanged\",\"eventData\":{\"outputState\":\"OBS_WEBSOCKET_OUTPUT_STARTED\"}}}"));
            Assert.That(MajEnv.RecordOutputsDirectory.EnumerateEntries().Count(), Is.EqualTo(1));
        }

        /// <summary>Checks the message model helpers and the recorder exception.</summary>
        [Test]
        public void ObsMessageModel_SerializesAndException()
        {
            var message = new OBSRecorder.ReceivedMessage { op = 7, d = new OBSRecorder.DData { requestType = "StartRecord" } };

            Assert.That(message.ToString(), Does.Contain("StartRecord"));
            Assert.That(new OBSRecorderException(), Is.InstanceOf<Exception>());
            Assert.That(Enum.GetNames(typeof(BuiltInRecorder)), Does.Contain("OBS"));
            Assert.That(Enum.GetNames(typeof(RecordEncoder)), Is.EqualTo(new[] { "H264", "HEVC", "VP9", "AV1" }));
        }
    }
}
