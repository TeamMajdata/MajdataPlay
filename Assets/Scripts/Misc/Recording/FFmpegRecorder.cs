/*using Cysharp.Threading.Tasks;
using MajdataPlay.IO;
using MajdataPlay.Types;
using MajdataPlay.Utils;
using ManagedBass.Asio;
using ManagedBass.Wasapi;
using NeoSmart.AsyncLock;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Profiling;
#nullable enable
namespace MajdataPlay.Recording
{
    public class FFmpegRecorder : IRecorder
    {
        public bool IsRecording { get; private set; } = false;
        public bool IsConnected { get; private set; } = true;


        bool _isDisposed = false;
        string _name = "";
        string _timestamp = string.Empty;
        string _wavPath = Path.Combine(MajEnv.RecordOutputsPath, $"{_defaultTimestamp}out.wav");
        string _mp4Path = Path.Combine(MajEnv.RecordOutputsPath, $"{_defaultTimestamp}out.mp4");
        string _outputPath = Path.Combine(MajEnv.RecordOutputsPath, $"{_defaultTimestamp}.mp4");

        readonly WavRecorder _wavRecorder = new(32);
        readonly ScreenRecorder _screenRecorder = new();
        readonly static string _defaultTimestamp = $"{DateTime.UnixEpoch:yyyy-MM-dd_HH_mm_ss}";
        readonly static string FFMPEG_PATH = Path.Combine(MajEnv.AssetsPath, "Libraries", "ffmpeg.exe");


        public void StartRecord()
        {
            EnsureIsOpen();
            if (IsRecording)
            {
                return;
            }
            IsRecording = true;
            _timestamp = $"{DateTime.Now:yyyy-MM-dd_HH_mm_ss}";
            _wavPath = Path.Combine(MajEnv.RecordOutputsPath, $"{_timestamp}out.wav");
            _mp4Path = Path.Combine(MajEnv.RecordOutputsPath, $"{_timestamp}out.mp4");
            _outputPath = Path.Combine(MajEnv.RecordOutputsPath, $"{_name}_{_timestamp}.mp4");

            _screenRecorder.StartRecord(_mp4Path);
            _wavRecorder.Start();
        }
        public async Task StartRecordAsync()
        {
            EnsureIsOpen();
            if (IsRecording)
            {
                return;
            }
            IsRecording = true;
            _timestamp = $"{DateTime.Now:yyyy-MM-dd_HH_mm_ss}";
            _wavPath = Path.Combine(MajEnv.RecordOutputsPath, $"{_timestamp}out.wav");
            _mp4Path = Path.Combine(MajEnv.RecordOutputsPath, $"{_timestamp}out.mp4");
            _outputPath = Path.Combine(MajEnv.RecordOutputsPath, $"{_name}_{_timestamp}.mp4");

            await _screenRecorder.StartRecordAsync(_mp4Path);
            _wavRecorder.Start();
        }
        public void StopRecord()
        {
            EnsureIsOpen();
            if (!IsRecording)
            {
                return;
            }
            IsRecording = false;
            _screenRecorder.StopRecord();
            _wavRecorder.Stop();
            using (var fileStream = File.Create(_wavPath))
            {
                _wavRecorder.Export(fileStream);
            }
            
            var args = $"-y -i {_mp4Path} -i {_wavPath} -c:v copy -c:a aac -strict experimental -shortest {_outputPath}";
            var startInfo = new ProcessStartInfo(FFMPEG_PATH, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var p = new Process { StartInfo = startInfo };
            p.Start();
            p.WaitForExit();
            File.Delete(_mp4Path);
            File.Delete(_wavPath);
            _timestamp = string.Empty;
        }
        public async Task StopRecordAsync()
        {
            EnsureIsOpen();
            if (!IsRecording)
            {
                return;
            }
            IsRecording = false;
            await _screenRecorder.StopRecordAsync();
            _wavRecorder.Stop();
            using (var fileStream = File.Create(_wavPath))
            {
                await _wavRecorder.ExportAsync(fileStream);
            }
            await Task.Run(() =>
            {
                var args = $"-y -i {_mp4Path} -i {_wavPath} -c:v copy -c:a aac -strict experimental -shortest {_outputPath}";
                var startInfo = new ProcessStartInfo(FFMPEG_PATH, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var p = new Process { StartInfo = startInfo };
                p.Start();
                p.WaitForExit();
                File.Delete(_mp4Path);
                File.Delete(_wavPath);
                _timestamp = string.Empty;
            });
        }

        public void SetOutputName(string name)
        {
            _name = name;
        }
        public void OnLateUpdate()
        {
            EnsureIsOpen();
            if(!IsRecording)
            {
                return;
            }
            _wavRecorder.OnLateUpdate();
        }
        public void Dispose()
        {
            if(_isDisposed)
            {
                return;
            }
            StopRecord();
            _wavRecorder.Dispose();
            _isDisposed = true;
        }
        void EnsureIsOpen()
        {
            if(_isDisposed)
            {
                throw new ObjectDisposedException(nameof(FFmpegRecorder));
            }
        }
        class WavRecorder : IDisposable
        {
            int _dataSize = 0;
            bool _isRecording = false;
            bool _isDisposed = false;
            
            readonly int _sampleRate;
            readonly int _channels;
            readonly int _bitsPerSample;
            readonly MemoryStream _stream = new(4096 * 10_0000);
            readonly ConcurrentQueue<Sample> _cachedSamples = new();

            public WavRecorder(int bitsPerSample)
            {
                var audioBackend = MajInstances.GameManager.Setting.Audio.Backend;
                if (audioBackend == SoundBackendType.Wasapi)
                {
                    BassWasapi.GetInfo(out var wasapiInfo);
                    _sampleRate = wasapiInfo.Frequency;
                    _channels = wasapiInfo.Channels;
                }
                else if (audioBackend == SoundBackendType.Asio)
                {
                    _channels = BassAsio.Info.Inputs;
                    _sampleRate = (int)BassAsio.Rate;
                }
                _bitsPerSample = bitsPerSample;
            }

            public void Start()
            {
                EnsureIsOpen();
                if (_isRecording)
                {
                    return;
                }
                try
                {
                    WriteHeader();
                    _isRecording = true;
                    _dataSize = 0;
                    AudioManager.OnBassProcessExtraLogic += HandleData;
                }
                catch (Exception e)
                {
                    MajDebug.LogError($"Failed to create WAV file: {e.Message}");
                    Stop();
                }
            }
            public void Stop()
            {
                AudioManager.OnBassProcessExtraLogic -= HandleData;
                EnsureIsOpen();
                WriteSampleIntoStream();
                Finish();
                _stream.Position = 0;
            }
            public void OnLateUpdate()
            {
                EnsureIsOpen();
                if (!_isRecording)
                    return;
                WriteSampleIntoStream();
            }
            public void Export(Stream stream)
            {
                EnsureIsOpen();
                if (_isRecording)
                {
                    Stop();
                }
                if(_dataSize == 0)
                {
                    return;
                }
                _stream.CopyTo(stream);
            }
            public async Task ExportAsync(Stream stream)
            {
                EnsureIsOpen();
                if (_isRecording)
                {
                    Stop();
                }
                if (_dataSize == 0)
                {
                    return;
                }
                await _stream.CopyToAsync(stream);
            }
            public void Dispose()
            {
                if (_isDisposed)
                {
                    return;
                }
                AudioManager.OnBassProcessExtraLogic -= HandleData;
                if (_isRecording)
                {
                    Stop();
                }
                _isDisposed = true;
                _stream?.Dispose();
            }



            void EnsureIsOpen()
            {
                if(_isDisposed)
                {
                    throw new ObjectDisposedException(nameof(WavRecorder));
                }
            }
            void WriteSampleIntoStream()
            {
                lock (_cachedSamples)
                {
                    while (_cachedSamples.TryDequeue(out var sample))
                    {
                        using (sample)
                        {
                            var buffer = sample.Buffer;
                            if (buffer.IsEmpty)
                            {
                                continue;
                            }
                            _stream.Write(buffer);
                            _dataSize += buffer.Length;
                        }
                    }
                }
            }
            private void WriteHeader()
            {
                // RIFF header
                WriteString("RIFF");
                WriteInt(0); // Placeholder for file size
                WriteString("WAVE");

                // fmt chunk
                WriteString("fmt ");
                WriteInt(16); // PCM chunk size
                WriteShort((ushort)(_bitsPerSample == 32 ? 3 : 1)); // Audio format (3 = IEEE float)
                WriteShort((ushort)_channels);
                WriteInt(_sampleRate);
                WriteInt(_sampleRate * _channels * (_bitsPerSample / 8)); // Byte rate
                WriteShort((ushort)(_channels * (_bitsPerSample / 8))); // Block align
                WriteShort((ushort)_bitsPerSample);

                // data chunk header
                WriteString("data");
                WriteInt(0); // Placeholder for data size
            }
            private void HandleData(IntPtr buffer, int length, IntPtr user)
            {
                if (!_isRecording)
                    return;

                try
                {
                    _cachedSamples.Enqueue(new Sample(buffer, length));
                }
                catch (Exception e)
                {
                    MajDebug.LogError($"Error writing audio data: {e.Message}");
                    Dispose();
                }
            }
            private void Finish()
            {
                if (!_isRecording) 
                    return;

                try
                {
                    _isRecording = false;

                    // Update RIFF size
                    _stream.Seek(4, SeekOrigin.Begin);
                    WriteInt(_dataSize + 36); // 36 = total header size

                    // Update data chunk size
                    _stream.Seek(40, SeekOrigin.Begin);
                    WriteInt(_dataSize);
                }
                catch (Exception e)
                {
                    MajDebug.LogError($"Error finalizing WAV file: {e.Message}");
                }
            }
            private unsafe void WriteShort(ushort value)
            {
                var ptr = stackalloc byte[2];
                *(short*)ptr = (short)value;

                _stream.Write(new ReadOnlySpan<byte>(ptr, 2));
            }
            private unsafe void WriteInt(int value)
            {
                var ptr = stackalloc byte[4];
                *(int*)ptr = (int)value;

                _stream.Write(BitConverter.GetBytes(value), 0, 4);
            }
            private void WriteString(string value)
            {
                _stream.Write(Encoding.ASCII.GetBytes(value), 0, value.Length);
            }
            struct Sample: IDisposable
            {
                public ReadOnlySpan<byte> Buffer
                {
                    get
                    {
                        if (_isDisposed)
                        {
                            throw new ObjectDisposedException(nameof(Sample));
                        }
                        var buffer = _buffer.AsSpan();
                        return buffer.Slice(0, _length);
                    }
                }
                bool _isDisposed;
                readonly byte[] _buffer;
                readonly int _length;
                public unsafe Sample(IntPtr buffer, int length)
                {
                    var data = new ReadOnlySpan<byte>((void*)buffer, length);
                    _length = length;
                    var pooledBuffer = ArrayPool<byte>.Shared.Rent(length);
                    data.CopyTo(pooledBuffer);
                    _buffer = pooledBuffer;
                    _isDisposed = false;
                }
                public void Dispose()
                {
                    if(_isDisposed)
                    {
                        return;
                    }
                    _isDisposed = true;
                    ArrayPool<byte>.Shared.Return(_buffer);
                }
            }
        }
        class ScreenRecorder
        {
            public bool IsRecording { get; private set; } = false;

            int _screenWidth = 1920;
            int _screenHeight = 1080;
            int _targetFrameRate = 60;

            int _originScreenWidth = 1920;
            int _originScreenHeight = 1080;
            int _originFrameRate = 60;

            string _exportPath = Path.Combine(MajEnv.RecordOutputsPath, $"{_defaultTimestamp}out.mp4");
            UniTask _captureScreenTask = UniTask.CompletedTask;
            Process? _ffmpegProcess = null;
            NamedPipeServerStream? _pipeStream = null;

            readonly AsyncLock _recodingSyncLock = new();
            readonly static string _defaultTimestamp = $"{DateTime.UnixEpoch:yyyy-MM-dd_HH_mm_ss}";

            public async UniTask StartRecordAsync(string exportPath)
            {
                if (IsRecording)
                    return;
                var task = _recodingSyncLock.LockAsync();
                while (!task.IsCompleted)
                {
                    await UniTask.Yield();
                }

                using (task.Result)
                {
                    StartRecordInternal(exportPath);
                }
            }
            public void StartRecord(string exportPath)
            {
                if (IsRecording)
                    return;
                using (_recodingSyncLock.Lock())
                {
                    StartRecordInternal(exportPath);
                }
            }
            public async UniTask StopRecordAsync()
            {
                if (!IsRecording)
                    return;
                var task = _recodingSyncLock.LockAsync();
                while (!task.IsCompleted)
                {
                    await UniTask.Yield();
                }
                using (task.Result)
                {
                    StopRecordInternal();
                    await WaitFFmpegExitAsync();
                }
            }
            public void StopRecord()
            {
                if (!IsRecording)
                    return;
                using (_recodingSyncLock.LockAsync())
                {
                    StopRecordInternal();
                    WaitFFmpegExit();
                }
            }
            void StartRecordInternal(string exportPath)
            {
                _originScreenWidth = Screen.width;
                _originScreenHeight = Screen.height;
                _screenWidth = _originScreenWidth;
                _screenHeight = _originScreenHeight;
                _originFrameRate = MajEnv.UserSettings.Rendering.FPSLimit;
                var isForceFullScreen = Screen.fullScreen;
                if (_screenWidth % 2 != 0)
                    _screenWidth++;
                if (_screenHeight % 2 != 0)
                    _screenHeight++;
                if (_screenWidth < 128)
                    _screenWidth = 128;
                if (_screenHeight < 128)
                    _screenHeight = 128;
                if (_originFrameRate <= 0)
                {
                    _targetFrameRate = 60;
                }
                else
                {
                    _targetFrameRate = _originFrameRate;
                }
                if(MajEnv.UserSettings.Rendering.VSync)
                {
                    _targetFrameRate = (int)Screen.currentResolution.refreshRateRatio.value;
                }
                Application.targetFrameRate = _targetFrameRate;
                Screen.SetResolution(_screenWidth, _screenHeight, isForceFullScreen);
                IsRecording = true;
                _exportPath = exportPath;
                _captureScreenTask = CaptureScreenAsync();
            }
            void StopRecordInternal()
            {
                IsRecording = false;
                _pipeStream = CreatePipeStream();
                Application.targetFrameRate = _originFrameRate;
                Screen.SetResolution(_originScreenWidth, _originScreenHeight, Screen.fullScreen);
            }
            async Task WaitFFmpegExitAsync()
            {
                if (_ffmpegProcess is null || _ffmpegProcess.HasExited)
                    return;
                var p = _ffmpegProcess;
                await Task.Run(() =>
                {
                    p.WaitForExit(2000);
                });
                if(!p.HasExited)
                {
                    p.Kill();
                }
            }
            void WaitFFmpegExit()
            {
                if (_ffmpegProcess is null || _ffmpegProcess.HasExited)
                    return;
                var p = _ffmpegProcess;
                p.WaitForExit(2000);
                if (!p.HasExited)
                {
                    p.Kill();
                }
            }
            async UniTask CaptureScreenAsync()
            {
                //var wait4EndOfFramePromise = new WaitForEndOfFrame();
                if(_pipeStream is null)
                {
                    _pipeStream = CreatePipeStream();
                }
                var pipeServer = _pipeStream;
                using (pipeServer)
                {
                    var encodeParams = GetEncodeParams();
                    var args =
                        $"-hide_banner -y -f rawvideo -pix_fmt rgba -s \"{_screenWidth}x{_screenHeight}\" -r {_targetFrameRate} -i \\\\.\\pipe\\MajdataPlayRec -vf \"vflip\" {encodeParams} -pix_fmt yuv420p -t \"{int.MaxValue:0.0000}\" \"{_exportPath}\"";
                    var startinfo = new ProcessStartInfo(FFMPEG_PATH, args)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    var behaviour = MajInstances.GameManager;
                    startinfo.EnvironmentVariables.Add("FFREPORT", "file=out.log:level=24");

                    _ffmpegProcess = Process.Start(startinfo);
                    var p = _ffmpegProcess;
                    var task = pipeServer.WaitForConnectionAsync();
                    while(!task.IsCompleted)
                    {
                        await UniTask.WaitForEndOfFrame(behaviour);
                    }                    
                    Texture2D lastPresentTexture;
                    var buffer = Array.Empty<byte>();
                    await UniTask.WaitForEndOfFrame(behaviour);
                    using (pipeServer)
                    {
                        while (pipeServer.IsConnected && IsRecording && !p.HasExited)
                        {
                            try
                            {
                                var read = 0;

                                Profiler.BeginSample("Capture Screenshot");
                                lastPresentTexture = ScreenCapture.CaptureScreenshotAsTexture();
                                Profiler.EndSample();
                                read = ReadDataIntoBuffer(lastPresentTexture.GetRawTextureData<byte>(), ref buffer);
                                pipeServer.Write(buffer, 0, read);
                                lastPresentTexture.Reinitialize(0, 0);
                            }
                            catch
                            {
                                // ignore single frame catching failed
                            }
                            finally
                            {
                                await UniTask.WaitForEndOfFrame(behaviour);
                            }
                        }
                    }
                    p.WaitForExit(2000);
                    if(!p.HasExited)
                    {
                        p.Kill();
                    }
                    MajDebug.Log("FFmpeg has exited");
                }
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            int ReadDataIntoBuffer(ReadOnlySpan<byte> data, ref byte[] buffer)
            {
                if (data.Length > buffer.Length)
                {
                    buffer = new byte[data.Length];
                }
                data.CopyTo(buffer);
                return data.Length;
            }
            NamedPipeServerStream CreatePipeStream()
            {
                if(_pipeStream is not null)
                {
                    _pipeStream.Dispose();
                }
                _pipeStream = new NamedPipeServerStream("MajdataPlayRec", PipeDirection.Out);
                return _pipeStream;
            }
            string GetEncodeParams()
            {
                var head = "-codec:v";
                var postfix = "";
                var encoder = "h264";
                var @params = "";
                var isHWEncoder = false;
                switch(MajEnv.UserSettings.Game.RecordEncoder)
                {
                    case RecordEncoder.H264:
                        encoder = "h264";
                        break;
                    case RecordEncoder.HEVC:
                        encoder = "hevc";
                        break;
                    case RecordEncoder.VP9:
                        encoder = "vp9";
                        break;
                    case RecordEncoder.AV1:
                        encoder = "av1";
                        break;
                }
                if(MajEnv.UserSettings.Debug.EnableHWEncoder)
                {
                    switch (MajEnv.HWEncoder)
                    {
                        case HardwareEncoder.None:
                            break;
                        case HardwareEncoder.NVENC:
                            if (MajEnv.UserSettings.Game.RecordEncoder == RecordEncoder.VP9)
                            {
                                break;
                            }
                            postfix = "_nvenc";
                            isHWEncoder = true;
                            break;
                        case HardwareEncoder.AMF:
                            if (MajEnv.UserSettings.Game.RecordEncoder == RecordEncoder.VP9)
                            {
                                break;
                            }
                            postfix = "_amf";
                            isHWEncoder = true;
                            break;
                        case HardwareEncoder.QSV:
                            postfix = "_qsv";
                            isHWEncoder = true;
                            break;
                    }
                }
                if(!isHWEncoder)
                {
                    @params = "-preset fast";
                }
                return $"{head} {encoder}{postfix} {@params}";
            }
        }
    }
}
*/