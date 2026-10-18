#nullable enable
using MajdataPlay.Diagnostics;
using MajdataPlay.FFmpeg;
using MajdataPlay.IO;
using MajdataPlay.IO.Storage;
using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataPlay.Recording
{
    /// <summary>Records the video of an <see cref="FFmpegCameraCapturer"/> and the audio of an <see cref="IAudioRecorder"/> into one MP4 or Matroska file.</summary>
    /// <remarks>
    /// While recording, the capturer encodes video into a Matroska file in <see cref="MajEnv.TempDirectory"/> and a worker drains the
    /// audio recorder into a raw float PCM file beside it. Audio starts in the same frame as the capturer's clock, so both timelines
    /// share their origin. Stopping finalizes the capture and muxes both files on a worker thread, copying the video packets without
    /// re-encoding, then moves the result into <see cref="MajEnv.RecordOutputsDirectory"/>. If muxing fails, the video-only capture is
    /// kept in the output directory instead. Control the recorder on the Unity main thread. The recorder owns the audio recorder but
    /// not the capturer component.
    /// </remarks>
    public sealed class FFmpegRecorder : IRecorder
    {
        /// <summary>Sets how often the worker drains captured audio to the temporary PCM file.</summary>
        private const int AudioDrainIntervalMilliseconds = 20;
        /// <summary>Sets how many seconds of audio one drain pass can move.</summary>
        private const int AudioDrainBufferDivisor = 10;
        /// <summary>Supplies the camera video.</summary>
        private readonly FFmpegCameraCapturer _capturer;
        /// <summary>Supplies the final audio mix.</summary>
        private readonly IAudioRecorder _audioRecorder;
        /// <summary>Stores the output container.</summary>
        private readonly MediaContainerFormat _container;
        /// <summary>Stores the current lifecycle step.</summary>
        private RecorderState _state;
        /// <summary>Stores the output name prefix, usually the chart title.</summary>
        private string _name = string.Empty;
        /// <summary>Stores the files of the active recording, or null while idle.</summary>
        private RecordingFiles? _files;
        /// <summary>Completes when the current start attempt finishes.</summary>
        private Task _startTask = Task.CompletedTask;
        /// <summary>Completes when the current finalization finishes.</summary>
        private Task _finalizeTask = Task.CompletedTask;
        /// <summary>Completes when the audio worker has written and closed the PCM file.</summary>
        private Task _audioDrainTask = Task.CompletedTask;
        /// <summary>Asks the audio worker to perform its final drain and exit.</summary>
        private CancellationTokenSource? _audioDrainStop;
        /// <summary>Indicates that Dispose has been called.</summary>
        private bool _isDisposed;

        /// <summary>Creates a recorder over an existing capturer and audio recorder.</summary>
        /// <param name="capturer">The scene capturer supplying video; its encoder options select the video format.</param>
        /// <param name="audioRecorder">The audio source, such as one from <see cref="AudioManager.CreateAudioRecorder"/>; this recorder takes ownership.</param>
        /// <param name="container">The output container.</param>
        /// <exception cref="ArgumentNullException">The capturer or audio recorder is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The container is not defined.</exception>
        public FFmpegRecorder(FFmpegCameraCapturer capturer, IAudioRecorder audioRecorder, MediaContainerFormat container)
        {
            if (capturer == null)
            {
                throw new ArgumentNullException(nameof(capturer));
            }
            if (!Enum.IsDefined(typeof(MediaContainerFormat), container))
            {
                throw new ArgumentOutOfRangeException(nameof(container));
            }
            _capturer = capturer;
            _audioRecorder = audioRecorder ?? throw new ArgumentNullException(nameof(audioRecorder));
            _container = container;
        }

        /// <summary>Lists the recorder lifecycle steps.</summary>
        private enum RecorderState
        {
            /// <summary>No recording is active.</summary>
            Idle,
            /// <summary>The capturer is opening its encoder.</summary>
            Starting,
            /// <summary>Video and audio are being captured.</summary>
            Recording,
            /// <summary>Capture has stopped and the output is being written.</summary>
            Finalizing
        }

        /// <summary>Gets whether a recording is starting or capturing.</summary>
        public bool IsRecording => _state == RecorderState.Starting || _state == RecorderState.Recording;
        /// <summary>Gets whether the capturer component still exists and this recorder is not disposed.</summary>
        public bool IsConnected => _capturer != null && !_isDisposed;

        /// <summary>Starts recording without waiting; failures are logged.</summary>
        /// <exception cref="ObjectDisposedException">The recorder has been disposed.</exception>
        public void StartRecord()
        {
            EnsureIsOpen();
            _ = LogFailureAsync(StartRecordAsync(), "start");
        }

        /// <summary>Opens the capture files and starts video and audio capture together.</summary>
        /// <returns>A task completing when capture has begun; calls while not idle complete immediately.</returns>
        /// <exception cref="ObjectDisposedException">The recorder has been disposed.</exception>
        /// <exception cref="InvalidOperationException">The capturer cannot start or stopped before capture began.</exception>
        /// <exception cref="NotSupportedException">The capturer's encoder or GPU readback is unavailable.</exception>
        /// <exception cref="IOException">A temporary file cannot be created.</exception>
        public Task StartRecordAsync()
        {
            EnsureIsOpen();
            if (_state != RecorderState.Idle)
            {
                return Task.CompletedTask;
            }
            _state = RecorderState.Starting;
            _startTask = StartCoreAsync();
            return _startTask;
        }

        /// <summary>Stops recording without waiting for the output; failures are logged.</summary>
        /// <exception cref="ObjectDisposedException">The recorder has been disposed.</exception>
        public void StopRecord()
        {
            EnsureIsOpen();
            _ = LogFailureAsync(StopRecordAsync(), "stop");
        }

        /// <summary>Stops capture and waits until the output file has been written.</summary>
        /// <returns>A task completing after the output is written and temporary files are removed.</returns>
        /// <exception cref="ObjectDisposedException">The recorder has been disposed.</exception>
        /// <exception cref="InvalidOperationException">Capture or muxing failed.</exception>
        /// <exception cref="NotSupportedException">The output container cannot store the captured media.</exception>
        /// <exception cref="IOException">Reading the capture or writing the output failed.</exception>
        public Task StopRecordAsync()
        {
            EnsureIsOpen();
            return StopCoreAsync();
        }

        /// <summary>Sets the prefix of the next output file name.</summary>
        /// <param name="name">The prefix, usually the chart title; characters invalid in file names are replaced.</param>
        public void SetOutputName(string name)
        {
            _name = name ?? string.Empty;
        }

        /// <summary>Finalizes the recording when the capturer stops or fails on its own.</summary>
        public void OnLateUpdate()
        {
            if (_state != RecorderState.Recording || _isDisposed)
            {
                return;
            }
            if (_capturer == null || _capturer.State == CameraCaptureState.Error || _capturer.State == CameraCaptureState.Stopped
                || _audioDrainTask.IsFaulted)
            {
                MajDebug.LogWarning(nameof(FFmpegRecorder), "A recording source stopped unexpectedly; finalizing the recording");
                _ = LogFailureAsync(StopCoreAsync(), "finalize");
            }
        }

        /// <summary>Stops any active recording, lets its output finish, and then releases the audio recorder.</summary>
        /// <remarks>Repeated disposal has no effect.</remarks>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
            _ = LogFailureAsync(DisposeCoreAsync(), "dispose");
        }

        /// <summary>Starts the capturer and, from its Started event, the audio recorder and its drain worker.</summary>
        /// <returns>A task completing when capture has begun.</returns>
        private async Task StartCoreAsync()
        {
            RecordingFiles? files = null;
            Stream? audioOutput = null;
            try
            {
                files = RecordingFiles.Create(_name, _container);
                audioOutput = FileSystem.File.OpenWrite(files.AudioPath);
                // Started is raised in the same call that starts the capture clock, giving both streams one origin.
                _capturer.Started += OnCaptureStarted;
                try
                {
                    await _capturer.StartRecordingAsync(files.VideoPath);
                }
                finally
                {
                    _capturer.Started -= OnCaptureStarted;
                }
                if (!_capturer.IsRecording)
                {
                    throw new InvalidOperationException("The capturer stopped before recording began.");
                }

                var drainStop = new CancellationTokenSource();
                var drainOutput = audioOutput;
                audioOutput = null;
                _audioDrainStop = drainStop;
                _audioDrainTask = Task.Factory.StartNew(() => DrainAudio(drainOutput, drainStop.Token),
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                _files = files;
                _state = RecorderState.Recording;
                MajDebug.LogInfo(nameof(FFmpegRecorder), $"Recording {_capturer.Width}x{_capturer.Height}@{_capturer.FrameRate} "
                    + $"{_capturer.EncoderName} with {_audioRecorder.SampleRate} Hz x{_audioRecorder.Channels} audio");
            }
            catch
            {
                try
                {
                    try
                    {
                        _audioRecorder.Stop();
                    }
                    finally
                    {
                        audioOutput?.Dispose();
                        if (files != null)
                        {
                            DeleteTemporaryFile(files.AudioPath);
                            if (_capturer.CanStartRecording)
                            {
                                DeleteTemporaryFile(files.VideoPath);
                            }
                        }
                    }
                }
                finally
                {
                    _state = RecorderState.Idle;
                }
                throw;
            }
        }

        /// <summary>Starts audio capture at the capturer's clock origin.</summary>
        /// <param name="capturer">The capturer that began recording.</param>
        private void OnCaptureStarted(FFmpegCameraCapturer capturer)
        {
            _audioRecorder.Start();
        }

        /// <summary>Waits for a pending start, then finalizes an active recording or waits for one already finalizing.</summary>
        /// <returns>A task completing when no recording is active.</returns>
        private async Task StopCoreAsync()
        {
            if (_state == RecorderState.Starting)
            {
                try
                {
                    await _startTask;
                }
                catch
                {
                    // The start caller observes this failure; nothing was recorded.
                    return;
                }
            }
            if (_state == RecorderState.Recording)
            {
                _finalizeTask = FinalizeAsync();
            }
            await _finalizeTask;
        }

        /// <summary>Stops both sources in one frame, waits for their files, and writes the output.</summary>
        /// <returns>A task completing after the output is written and temporary files are removed.</returns>
        private async Task FinalizeAsync()
        {
            _state = RecorderState.Finalizing;
            var files = _files!;
            var drainStop = _audioDrainStop!;
            _files = null;
            _audioDrainStop = null;
            try
            {
                // Both sources stop in the same frame so their timelines also end together.
                Exception? stopError = null;
                try
                {
                    _audioRecorder.Stop();
                }
                catch (Exception error)
                {
                    stopError = error;
                }
                var videoCompletion = Task.CompletedTask;
                try
                {
                    videoCompletion = _capturer.StopRecordingAsync();
                }
                catch (Exception error)
                {
                    stopError ??= error;
                }
                drainStop.Cancel();
                try
                {
                    // Observe both tasks even if one source fails before the other has finished.
                    await Task.WhenAll(_audioDrainTask, videoCompletion);
                }
                catch (Exception error)
                {
                    stopError ??= error;
                }
                if (stopError != null)
                {
                    ExceptionDispatchInfo.Capture(stopError).Throw();
                }
                await WriteOutputAsync(files);
                DeleteTemporaryFile(files.AudioPath);
                DeleteTemporaryFile(files.VideoPath);
            }
            catch
            {
                MajDebug.LogError(nameof(FFmpegRecorder), $"Recording finalization failed; untransferred recovery sources are retained: video={files.VideoPath}; PCM={files.AudioPath}");
                throw;
            }
            finally
            {
                drainStop.Dispose();
                _state = RecorderState.Idle;
            }
        }

        /// <summary>Muxes the temporary files into a local file and moves it into the output directory.</summary>
        /// <param name="files">The files of the finished recording.</param>
        /// <returns>A task completing after the output has been moved.</returns>
        private async Task WriteOutputAsync(RecordingFiles files)
        {
            var sampleRate = _audioRecorder.SampleRate;
            var channels = _audioRecorder.Channels;
            var container = _container;
            try
            {
                var audioCodec = await Task.Run(() =>
                {
                    using (var pcm = FileSystem.File.OpenRead(files.AudioPath))
                    using (var output = FileSystem.File.OpenWrite(files.MuxedPath))
                    {
                        return FFmpegMediaMuxer.Mux(files.VideoPath, pcm, sampleRate, channels, output, container);
                    }
                });
                MoveToOutputDirectory(files.MuxedPath, files.OutputName);
                MajDebug.LogInfo(nameof(FFmpegRecorder), $"Saved {files.OutputName} with {audioCodec ?? "no"} audio");
            }
            catch (Exception error) when (!(error is OperationCanceledException))
            {
                DeleteTemporaryFile(files.MuxedPath);
                MajDebug.LogError(nameof(FFmpegRecorder), $"Muxing failed; keeping the video-only capture as {files.VideoOnlyOutputName}");
                try
                {
                    MoveToOutputDirectory(files.VideoPath, files.VideoOnlyOutputName);
                }
                catch (Exception moveError)
                {
                    // Keep the muxing failure as the reported error.
                    MajDebug.LogError(nameof(FFmpegRecorder), $"Cannot keep the video-only capture: {moveError.Message}");
                }
                throw;
            }
        }

        /// <summary>Copies captured audio to the PCM file until asked to stop, then drains the remainder and closes the file.</summary>
        /// <param name="output">The PCM file, owned and closed by this worker.</param>
        /// <param name="stopToken">Signals that the audio recorder has stopped.</param>
        private void DrainAudio(Stream output, CancellationToken stopToken)
        {
            var samples = new float[Math.Max(1, _audioRecorder.SampleRate / AudioDrainBufferDivisor) * _audioRecorder.Channels];
            using (output)
            {
                var waitHandle = stopToken.WaitHandle;
                while (!stopToken.IsCancellationRequested)
                {
                    WriteAvailableAudio(output, samples);
                    if (waitHandle.WaitOne(AudioDrainIntervalMilliseconds))
                    {
                        // Stopping is the normal exit; the final drain below collects the remaining samples.
                        break;
                    }
                }
                WriteAvailableAudio(output, samples);
                output.Flush();
            }
        }

        /// <summary>Writes every currently buffered audio sample as little-endian float bytes.</summary>
        /// <param name="output">The PCM file.</param>
        /// <param name="samples">The reusable transfer buffer.</param>
        private void WriteAvailableAudio(Stream output, float[] samples)
        {
            int count;
            while ((count = _audioRecorder.Read(samples)) > 0)
            {
                output.Write(MemoryMarshal.AsBytes(samples.AsSpan(0, count)));
            }
        }

        /// <summary>Finishes any recording and releases the audio recorder.</summary>
        /// <returns>A task completing after the audio recorder is disposed.</returns>
        private async Task DisposeCoreAsync()
        {
            try
            {
                await StopCoreAsync();
            }
            finally
            {
                _audioRecorder.Dispose();
            }
        }

        /// <summary>Moves a local file into the output directory, copying when the directory uses another storage backend.</summary>
        /// <param name="sourcePath">The local file to move.</param>
        /// <param name="outputName">The file name in the output directory.</param>
        private static void MoveToOutputDirectory(string sourcePath, string outputName)
        {
            var outputDirectory = MajEnv.RecordOutputsDirectory;
            var source = FileSystem.File.Open(sourcePath);
            if (ReferenceEquals(source.FileSystem, outputDirectory.FileSystem))
            {
                FileSystem.File.Move(source.Location, outputDirectory.Location, outputName);
            }
            else
            {
                source.CopyTo(outputDirectory, outputName);
                source.Delete();
            }
        }

        /// <summary>Deletes a temporary file if it exists, logging instead of throwing on failure.</summary>
        /// <param name="path">The temporary file path.</param>
        private static void DeleteTemporaryFile(string path)
        {
            try
            {
                var file = FileSystem.File.Open(path);
                if (file.Exists)
                {
                    file.Delete();
                }
            }
            catch (Exception error)
            {
                MajDebug.LogWarning(nameof(FFmpegRecorder), $"Cannot delete temporary recording file {path}: {error.Message}");
            }
        }

        /// <summary>Observes a fire-and-forget operation and logs its failure.</summary>
        /// <param name="operation">The operation to observe.</param>
        /// <param name="description">The operation name used in the log.</param>
        /// <returns>A task that never faults.</returns>
        private static async Task LogFailureAsync(Task operation, string description)
        {
            try
            {
                await operation;
            }
            catch (Exception error)
            {
                MajDebug.LogError(nameof(FFmpegRecorder), $"Recording {description} failed: {error}");
            }
        }

        /// <summary>Throws when the recorder has been disposed.</summary>
        /// <exception cref="ObjectDisposedException">The recorder has been disposed.</exception>
        private void EnsureIsOpen()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(FFmpegRecorder));
            }
        }

        /// <summary>Holds the temporary and output names of one recording.</summary>
        private sealed class RecordingFiles
        {
            /// <summary>Gets the local Matroska file written by the capturer.</summary>
            public string VideoPath { get; }
            /// <summary>Gets the local raw float PCM file written by the audio worker.</summary>
            public string AudioPath { get; }
            /// <summary>Gets the local file receiving the muxed output before it is moved.</summary>
            public string MuxedPath { get; }
            /// <summary>Gets the file name of the muxed output in the output directory.</summary>
            public string OutputName { get; }
            /// <summary>Gets the file name used for the video-only capture when muxing fails.</summary>
            public string VideoOnlyOutputName { get; }

            /// <summary>Stores the names of one recording.</summary>
            /// <param name="videoPath">The local capture file.</param>
            /// <param name="audioPath">The local PCM file.</param>
            /// <param name="muxedPath">The local muxed file.</param>
            /// <param name="outputName">The output file name.</param>
            /// <param name="videoOnlyOutputName">The fallback output file name.</param>
            private RecordingFiles(string videoPath, string audioPath, string muxedPath, string outputName, string videoOnlyOutputName)
            {
                VideoPath = videoPath;
                AudioPath = audioPath;
                MuxedPath = muxedPath;
                OutputName = outputName;
                VideoOnlyOutputName = videoOnlyOutputName;
            }

            /// <summary>Creates unique temporary paths and timestamped output names.</summary>
            /// <param name="name">The output name prefix, possibly empty.</param>
            /// <param name="container">The output container, which selects the extension.</param>
            /// <returns>The names of a new recording.</returns>
            public static RecordingFiles Create(string name, MediaContainerFormat container)
            {
                var timestamp = $"{DateTime.Now:yyyy-MM-dd_HH_mm_ss}";
                var baseName = string.IsNullOrWhiteSpace(name) ? timestamp : $"{SanitizeFileName(name)}_{timestamp}";
                var extension = container == MediaContainerFormat.MP4 ? ".mp4" : ".mkv";
                // The capturer requires a native path, which the cache-backed temporary directory always provides.
                var tempPrefix = Path.Combine(MajEnv.TempDirectory.Location, $"record_{Guid.NewGuid():N}");
                return new RecordingFiles(tempPrefix + ".video.mkv", tempPrefix + ".audio.pcm", tempPrefix + ".muxed" + extension,
                    baseName + extension, baseName + "_video-only.mkv");
            }

            /// <summary>Replaces characters that are invalid in file names on any target platform.</summary>
            /// <param name="name">The requested name.</param>
            /// <returns>The name with invalid characters replaced by underscores.</returns>
            private static string SanitizeFileName(string name)
            {
                var characters = name.Trim().ToCharArray();
                var invalid = Path.GetInvalidFileNameChars();
                for (var i = 0; i < characters.Length; i++)
                {
                    if (Array.IndexOf(invalid, characters[i]) >= 0 || characters[i] == ':' || characters[i] == '/' || characters[i] == '\\')
                    {
                        characters[i] = '_';
                    }
                }
                return new string(characters);
            }
        }
    }
}
