#nullable enable
using MajdataPlay.IO;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataPlay.Tests.RecorderLifecycleValidation
{
    /// <summary>Stores only the files and failures needed by the recorder lifecycle cases.</summary>
    internal static class TestStorage
    {
        /// <summary>Retains file contents across stream closure and simulated moves.</summary>
        internal static readonly ConcurrentDictionary<string, TestStream> Files = new ConcurrentDictionary<string, TestStream>();
        /// <summary>Identifies the common in-memory storage backend.</summary>
        internal static readonly object Provider = new object();
        /// <summary>Injects a temporary path creation error.</summary>
        internal static Exception? PathFailure;
        /// <summary>Injects a PCM write error on the drain thread.</summary>
        internal static Exception? AudioWriteFailure;
        /// <summary>Injects an output directory move error.</summary>
        internal static Exception? MoveFailure;
        /// <summary>Injects a muxing error.</summary>
        internal static Exception? MuxFailure;
        /// <summary>Counts mux attempts.</summary>
        internal static int MuxCalls;

        /// <summary>Clears per-case state only after all previous workers have finished.</summary>
        internal static void Reset()
        {
            Files.Clear();
            PathFailure = null;
            AudioWriteFailure = null;
            MoveFailure = null;
            MuxFailure = null;
            MuxCalls = 0;
            MajdataPlay.Diagnostics.MajDebug.Messages.Clear();
        }

        /// <summary>Creates one observable output stream.</summary>
        /// <param name="path">The in-memory absolute path.</param>
        /// <returns>The new writable stream.</returns>
        internal static Stream OpenWrite(string path)
        {
            var stream = new TestStream(path);
            Files[path] = stream;
            return stream;
        }

        /// <summary>Moves a closed file without accessing the host filesystem.</summary>
        /// <param name="source">The existing file path.</param>
        /// <param name="directory">The output directory.</param>
        /// <param name="name">The output file name.</param>
        internal static void Move(string source, string directory, string name)
        {
            if (MoveFailure != null)
            {
                throw MoveFailure;
            }
            if (!Files.TryRemove(source, out var stream))
            {
                throw new FileNotFoundException("Missing fixture file", source);
            }
            Files[Path.Combine(directory, name)] = stream;
        }
    }

    /// <summary>Retains bytes after closing and can fail PCM writes.</summary>
    internal sealed class TestStream : MemoryStream
    {
        /// <summary>Retains the path selecting injected PCM failures.</summary>
        private readonly string _path;
        /// <summary>Gets whether the owner has closed this stream.</summary>
        internal bool Closed { get; private set; }

        /// <summary>Creates a stream for one fixture file.</summary>
        /// <param name="path">The fixture path.</param>
        internal TestStream(string path)
        {
            _path = path;
        }

        /// <summary>Writes PCM bytes or raises the configured disk error.</summary>
        /// <param name="buffer">The bytes to append.</param>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (_path.EndsWith(".audio.pcm", StringComparison.Ordinal) && TestStorage.AudioWriteFailure != null)
            {
                throw TestStorage.AudioWriteFailure;
            }
            base.Write(buffer);
        }

        /// <summary>Records stream closure while retaining the managed byte snapshot.</summary>
        /// <param name="disposing">Whether managed resources are being closed.</param>
        protected override void Dispose(bool disposing)
        {
            Closed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>Supplies finite PCM data and controllable source failures.</summary>
    internal sealed class TestAudioRecorder : IAudioRecorder
    {
        /// <summary>Counts source start attempts.</summary>
        internal int StartCalls;
        /// <summary>Counts source stop attempts.</summary>
        internal int StopCalls;
        /// <summary>Counts consumer drain passes.</summary>
        private int _readCalls;
        /// <summary>Injects an audio consumer failure.</summary>
        internal Exception? ReadFailure;
        /// <summary>Injects a source shutdown failure.</summary>
        internal Exception? StopFailure;
        /// <summary>Completes after the drain thread has called Read.</summary>
        internal TaskCompletionSource<bool> ReadReached { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>Completes when the production owner disposes this audio source.</summary>
        internal TaskCompletionSource<bool> Disposed { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <inheritdoc/>
        public int SampleRate => 100;
        /// <inheritdoc/>
        public int Channels => 2;
        /// <inheritdoc/>
        public bool IsRecording { get; private set; }
        /// <inheritdoc/>
        public long DroppedFrames => 0;

        /// <inheritdoc/>
        public void Start()
        {
            StartCalls++;
            Interlocked.Exchange(ref _readCalls, 0);
            IsRecording = true;
        }

        /// <inheritdoc/>
        public void Stop()
        {
            StopCalls++;
            IsRecording = false;
            if (StopFailure != null)
            {
                throw StopFailure;
            }
        }

        /// <inheritdoc/>
        public int Read(Span<float> destination)
        {
            var count = Interlocked.Increment(ref _readCalls);
            ReadReached.TrySetResult(true);
            if (ReadFailure != null)
            {
                throw ReadFailure;
            }
            if (count == 1)
            {
                destination[0] = 0.25f;
                destination[1] = -0.25f;
                return 2;
            }
            return 0;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            IsRecording = false;
            Disposed.TrySetResult(true);
        }
    }
}

namespace MajdataPlay.IO.Storage
{
    /// <summary>Supplies the exact file facade called by the production recorder.</summary>
    public static class FileSystem
    {
        /// <summary>Provides in-memory file operations.</summary>
        public static class File
        {
            /// <summary>Creates an observable output stream.</summary>
            /// <param name="path">The absolute fixture path.</param>
            /// <returns>The writable stream.</returns>
            public static Stream OpenWrite(string path)
            {
                return MajdataPlay.Tests.RecorderLifecycleValidation.TestStorage.OpenWrite(path);
            }

            /// <summary>Reads a closed file's retained bytes.</summary>
            /// <param name="path">The existing fixture path.</param>
            /// <returns>A read-only byte stream.</returns>
            public static Stream OpenRead(string path)
            {
                return new MemoryStream(MajdataPlay.Tests.RecorderLifecycleValidation.TestStorage.Files[path].ToArray(), false);
            }

            /// <summary>Returns a file handle without opening an OS resource.</summary>
            /// <param name="path">The fixture path.</param>
            /// <returns>The fixture file handle.</returns>
            public static StorageFile Open(string path)
            {
                return new StorageFile(path);
            }

            /// <summary>Moves a fixture file into the output directory.</summary>
            /// <param name="source">The source path.</param>
            /// <param name="directory">The output directory path.</param>
            /// <param name="name">The output name.</param>
            public static void Move(string source, string directory, string name)
            {
                MajdataPlay.Tests.RecorderLifecycleValidation.TestStorage.Move(source, directory, name);
            }
        }
    }

    /// <summary>Supplies an output directory and backend identity.</summary>
    public sealed class StorageDirectory
    {
        /// <summary>Gets the absolute fixture directory.</summary>
        public string Location { get; }
        /// <summary>Gets the shared backend identity.</summary>
        public object FileSystem => MajdataPlay.Tests.RecorderLifecycleValidation.TestStorage.Provider;

        /// <summary>Creates a fixture directory.</summary>
        /// <param name="location">The absolute fixture directory.</param>
        public StorageDirectory(string location)
        {
            Location = location;
        }
    }

    /// <summary>Supplies only the handle operations used by recording file ownership.</summary>
    public sealed class StorageFile
    {
        /// <summary>Gets the fixture file path.</summary>
        public string Location { get; }
        /// <summary>Gets the shared backend identity.</summary>
        public object FileSystem => MajdataPlay.Tests.RecorderLifecycleValidation.TestStorage.Provider;
        /// <summary>Gets whether this fixture file still exists.</summary>
        public bool Exists => MajdataPlay.Tests.RecorderLifecycleValidation.TestStorage.Files.ContainsKey(Location);

        /// <summary>Creates a fixture file handle.</summary>
        /// <param name="location">The absolute fixture file path.</param>
        public StorageFile(string location)
        {
            Location = location;
        }

        /// <summary>Deletes the fixture file entry.</summary>
        public void Delete()
        {
            MajdataPlay.Tests.RecorderLifecycleValidation.TestStorage.Files.TryRemove(Location, out _);
        }

        /// <summary>Compiles the recorder's cross-provider branch without host file access.</summary>
        /// <param name="directory">The destination directory.</param>
        /// <param name="name">The output file name.</param>
        public void CopyTo(StorageDirectory directory, string name)
        {
            var bytes = MajdataPlay.Tests.RecorderLifecycleValidation.TestStorage.Files[Location].ToArray();
            using (var stream = MajdataPlay.Tests.RecorderLifecycleValidation.TestStorage.OpenWrite(Path.Combine(directory.Location, name)))
            {
                stream.Write(bytes);
            }
        }
    }
}

namespace MajdataPlay
{
    /// <summary>Supplies in-memory output locations without initializing the game.</summary>
    public static class MajEnv
    {
        /// <summary>Gets the temporary directory or injects an initialization failure.</summary>
        public static IO.Storage.StorageDirectory TempDirectory
        {
            get
            {
                if (Tests.RecorderLifecycleValidation.TestStorage.PathFailure != null)
                {
                    throw Tests.RecorderLifecycleValidation.TestStorage.PathFailure;
                }
                return new IO.Storage.StorageDirectory("/fixture/temp");
            }
        }

        /// <summary>Gets the output directory.</summary>
        public static IO.Storage.StorageDirectory RecordOutputsDirectory => new IO.Storage.StorageDirectory("/fixture/output");
    }
}

namespace MajdataPlay.Diagnostics
{
    /// <summary>Records lifecycle messages for recovery-path assertions.</summary>
    public static class MajDebug
    {
        /// <summary>Retains observed messages without Unity logging.</summary>
        internal static readonly List<string> Messages = new List<string>();

        /// <summary>Records informational lifecycle messages.</summary>
        /// <param name="tag">The production logging category.</param>
        /// <param name="message">The message text.</param>
        public static void LogInfo(string tag, string message)
        {
            Messages.Add(message);
        }

        /// <summary>Records lifecycle warnings.</summary>
        /// <param name="tag">The production logging category.</param>
        /// <param name="message">The message text.</param>
        public static void LogWarning(string tag, string message)
        {
            Messages.Add(message);
        }

        /// <summary>Records failures and recoverable source paths.</summary>
        /// <param name="tag">The production logging category.</param>
        /// <param name="message">The message text.</param>
        public static void LogError(string tag, string message)
        {
            Messages.Add(message);
        }
    }
}

namespace MajdataPlay.FFmpeg
{
    /// <summary>Supplies container values consumed by the production recorder.</summary>
    public enum MediaContainerFormat
    {
        /// <summary>Uses the MP4 container.</summary>
        MP4,
        /// <summary>Uses the Matroska container.</summary>
        Matroska
    }

    /// <summary>Supplies camera capture states consumed by the production recorder.</summary>
    public enum CameraCaptureState
    {
        /// <summary>No recording is active.</summary>
        Idle,
        /// <summary>Frames are being captured.</summary>
        Recording,
        /// <summary>The capturer has stopped.</summary>
        Stopped,
        /// <summary>The capturer failed.</summary>
        Error
    }

    /// <summary>Models capture timing and finalization without Unity or a GPU.</summary>
    public sealed class FFmpegCameraCapturer
    {
        /// <summary>Signals the same start event used by the production audio synchronizer.</summary>
        public event Action<FFmpegCameraCapturer>? Started;
        /// <summary>Gets or sets a start error.</summary>
        internal Exception? StartFailure { get; set; }
        /// <summary>Gets or sets a finalization error.</summary>
        internal Exception? StopFailure { get; set; }
        /// <summary>Gets or sets a gate retaining pending camera finalization.</summary>
        internal TaskCompletionSource<bool>? StopGate { get; set; }
        /// <summary>Gets the number of source stop attempts.</summary>
        internal int StopCalls { get; private set; }
        /// <summary>Gets whether a new session may begin.</summary>
        public bool CanStartRecording => !IsRecording;
        /// <summary>Gets whether the fixture is recording.</summary>
        public bool IsRecording { get; private set; }
        /// <summary>Gets the current fixture state.</summary>
        public CameraCaptureState State { get; private set; }
        /// <summary>Gets a representative capture width.</summary>
        public int Width => 1920;
        /// <summary>Gets a representative capture height.</summary>
        public int Height => 1080;
        /// <summary>Gets a representative capture frame rate.</summary>
        public int FrameRate => 60;
        /// <summary>Gets the fixture encoder name.</summary>
        public string EncoderName => "fixture";

        /// <summary>Creates the source video and starts the production audio subscriber.</summary>
        /// <param name="path">The temporary video path.</param>
        /// <returns>A completed task or the configured start failure.</returns>
        public Task StartRecordingAsync(string path)
        {
            if (StartFailure != null)
            {
                return Task.FromException(StartFailure);
            }
            using (var video = IO.Storage.FileSystem.File.OpenWrite(path))
            {
                video.WriteByte(0x76);
            }
            IsRecording = true;
            State = CameraCaptureState.Recording;
            Started?.Invoke(this);
            return Task.CompletedTask;
        }

        /// <summary>Stops the fixture source and waits for its optional pending work.</summary>
        /// <returns>The finalization task, which may fail after the gate is released.</returns>
        public async Task StopRecordingAsync()
        {
            StopCalls++;
            IsRecording = false;
            State = CameraCaptureState.Stopped;
            if (StopGate != null)
            {
                await StopGate.Task;
            }
            if (StopFailure != null)
            {
                throw StopFailure;
            }
        }
    }

    /// <summary>Supplies controllable mux success or failure without native media processing.</summary>
    public static class FFmpegMediaMuxer
    {
        /// <summary>Writes fixture combined output or raises an injected mux failure.</summary>
        /// <param name="videoPath">The retained source video path.</param>
        /// <param name="pcm">The completed PCM stream.</param>
        /// <param name="sampleRate">The audio sample rate.</param>
        /// <param name="channels">The audio channel count.</param>
        /// <param name="output">The combined output stream.</param>
        /// <param name="container">The chosen container.</param>
        /// <returns>The fixture codec name.</returns>
        public static string Mux(string videoPath, Stream pcm, int sampleRate, int channels, Stream output, MediaContainerFormat container)
        {
            Interlocked.Increment(ref Tests.RecorderLifecycleValidation.TestStorage.MuxCalls);
            if (Tests.RecorderLifecycleValidation.TestStorage.MuxFailure != null)
            {
                throw Tests.RecorderLifecycleValidation.TestStorage.MuxFailure;
            }
            output.WriteByte(0x6d);
            pcm.CopyTo(output);
            return "fixture-audio";
        }
    }
}
