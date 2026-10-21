using System.Collections.Generic;
using ManagedBass;
using ManagedBass.Mix;
#if UNITY_STANDALONE_WIN
using ManagedBass.Wasapi;
using ManagedBass.Asio;
#endif
using UnityEngine;
using System;
using System.Linq;
using System.Threading;
using MajdataPlay.Extensions;
using MajdataPlay.Settings;
using MajdataPlay.Numerics;
using Cysharp.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;


#nullable enable
namespace MajdataPlay.IO
{
    public class AudioManager : MonoBehaviour
    {
        public static float[,] MixingMatrix { get; private set; } = new float[0, 0];

        private List<AudioSampleWrap> SFXSamples = new();

        private static int BassGlobalMixer = -114514;
        /// <summary>Holds the seconds of output audio an audio recorder buffers for a slow consumer.</summary>
        private const int AudioRecorderBufferSeconds = 4;
        /// <summary>Holds the BassSimple device output stream that exposes the final device mix, or zero before a recorder needs it.</summary>
        private static int s_bassDeviceOutputStream;

        public bool PlayDebug;

        private static bool _isInited = false;

        private void Awake()
        {
            Majdata<AudioManager>.SetAsSingleton(this);
        }
        internal void Init()
        {
            if (_isInited)
            {
                return;
            }
            _isInited = true;
            try
            {
                var sfxDirectory = MajEnv.AssetsDirectory.FindDirectory("SFX");
                var voiceDirectory = MajEnv.AssetsDirectory.FindDirectory("Voice");

                DontDestroyOnLoad(this);

                var backend = MajEnv.Settings.Audio.Backend;
                var isBassBackend = backend is (SoundBackendOption.BassSimple or SoundBackendOption.Asio or SoundBackendOption.Wasapi);
#if UNITY_STANDALONE_WIN
                var wasapiOptions = MajEnv.Settings.Audio.Wasapi;
                var asioOptions = MajEnv.Settings.Audio.Asio;

                var deviceIndex = asioOptions.DeviceIndex;

#endif
#if UNITY_ANDROID || UNITY_IOS || !UNITY_STANDALONE_WIN
                switch (backend)
                {
                    case SoundBackendOption.BassSimple:
                    case SoundBackendOption.Unity:
                        break;
                    default:

#if UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID
                        backend = SoundBackendOption.BassSimple;
#else
                        backend = SoundBackendOption.Unity;
#endif
                        MajEnv.Settings.Audio.Backend = backend;
                        MajDebug.LogDebug($"[Audio]Fallback to {backend}");
                        break;
                }
#endif
                if (isBassBackend)
                {
                    var bassAudioOptions = MajEnv.Settings.Audio.Bass;
                    bassAudioOptions.UpdatePeriodMs = bassAudioOptions.UpdatePeriodMs.Clamp(5, 100);
                    bassAudioOptions.BufferLengthMs = bassAudioOptions.BufferLengthMs.Clamp(bassAudioOptions.UpdatePeriodMs + 1, 5000);
                    bassAudioOptions.DeviceUpdatePeriodMs = bassAudioOptions.DeviceUpdatePeriodMs.Clamp(1, int.MaxValue);
                    bassAudioOptions.DeviceBufferLengthMs = bassAudioOptions.DeviceBufferLengthMs.Clamp(bassAudioOptions.DeviceUpdatePeriodMs * 2, int.MaxValue);
                    var @return = default(bool);
#if UNITY_ANDROID // Android Only (AAudio Config)
                    @return = Bass.Configure(Configuration.AndroidAAudio, bassAudioOptions.EnableAAudio);
                    MajDebug.LogInfo("Audio", $"[Bass]Set AndroidAAudio: {@return}");
                    Bass.LastError.LogInfo();
#endif
                    @return = Bass.Configure(Configuration.UpdatePeriod, bassAudioOptions.UpdatePeriodMs);
                    MajDebug.LogInfo("Audio", $"[Bass]Set UpdatePeriod: {@return}");
                    Bass.LastError.LogInfo();
                    @return = Bass.Configure(Configuration.PlaybackBufferLength, bassAudioOptions.BufferLengthMs);
                    MajDebug.LogInfo("Audio", $"[Bass]Set PlaybackBufferLength: {@return}");
                    Bass.LastError.LogInfo();
                    @return = Bass.Configure(Configuration.DevicePeriod, bassAudioOptions.DeviceUpdatePeriodMs);
                    MajDebug.LogInfo("Audio", $"[Bass]Set DevicePeriod: {@return}");
                    Bass.LastError.LogInfo();
                    @return = Bass.Configure(Configuration.DeviceBufferLength, bassAudioOptions.DeviceBufferLengthMs);
                    MajDebug.LogInfo("Audio", $"[Bass]Set DeviceBufferLength: {@return}");
                    Bass.LastError.LogInfo();

                    Bass.Configure(Configuration.DevNonStop, true);
                }
                switch (backend)
                {
#if UNITY_STANDALONE_WIN
                    case SoundBackendOption.Asio:
                        {
                            MajDebug.LogInfo("Audio", "[Bass]Init: " + Bass.Init(Bass.NoSoundDevice));
                            var asioCount = BassAsio.DeviceCount;
                            for (int i = 0; i < asioCount; i++)
                            {
                                BassAsio.GetDeviceInfo(i, out var info);
                                var text = "[BassAsio]ASIO Device " + i + ": " + info.Name;
                                if (i == deviceIndex)
                                {
                                    text += " (Selected)";
                                }
                                MajDebug.LogInfo("Audio", text);
                            }

                            MajDebug.LogInfo("Audio", "[BassAsio]Init: " + BassAsio.Init(deviceIndex, AsioInitFlags.Thread));
                            Bass.LastError.LogInfo();
                            var asioInfo = BassAsio.Info;
                            var deviceInfo = BassAsio.GetDeviceInfo(BassAsio.CurrentDevice);
                            BassAsio.Rate = asioOptions.SampleRate;
                            BassGlobalMixer = BassMix.CreateMixerStream((int)BassAsio.Rate, asioInfo.Outputs, BassFlags.MixerNonStop | BassFlags.Decode | BassFlags.Float);
                            Bass.ChannelSetAttribute(BassGlobalMixer, ChannelAttribute.Buffer, 0);
                            Bass.ChannelSetAttribute(BassGlobalMixer, (ChannelAttribute)86017, 8);
                            //BassAsio.ChannelEnable(false, 0, asioProcedure, IntPtr.Zero);
                            BassAsio.ChannelEnableBass(false, 0, BassGlobalMixer, true);
                            BassAsio.ChannelSetFormat(false, 0, AsioSampleFormat.Float);
                            //we dont use Asio.Inputs because we only use stero channels
                            for (int i = 1; i < 2; i++)
                            {
                                if (!BassAsio.ChannelJoin(false, i, 0)) // let channel i follow channel 0
                                {
                                    MajDebug.LogError("Audio", $"[BassAsio]ASIO Channel {i} Join to 0 Failed: " + BassAsio.LastError);
                                }
                                else
                                {
                                    BassAsio.ChannelSetFormat(false, i, AsioSampleFormat.Float);
                                }
                            }
                            MajDebug.LogInfo("Audio", $"[BassAsio]Channel count: {asioInfo.Outputs}");
                            GenerateMixingMatrix(asioInfo.Outputs);

                            BassAsio.Start();
                        }
                        break;
                    case SoundBackendOption.Wasapi:
                        {
                            MajDebug.LogInfo("Audio", "[Bass]Init: " + Bass.Init(Bass.NoSoundDevice));
                            var isExclusiveRequest = wasapiOptions.Exclusive;
                            var isRawMode = wasapiOptions.RawMode;
                            var isAsyncMode = wasapiOptions.AsyncMode;
                            bool wasapiOk = false;
                            // Priority order: exclusive+raw > exclusive > shared+raw > shared
                            // Start from user config, fall through on failure.
                            var combos = new (bool exclusive, bool raw)[]
                            {
                                (true,  true),
                                (true,  false),
                                (false, true),
                                (false, false),
                            };
                            int startIdx = 0;
                            for (int i = 0; i < combos.Length; i++)
                            {
                                if (combos[i].exclusive == isExclusiveRequest && combos[i].raw == isRawMode)
                                {
                                    startIdx = i;
                                    break;
                                }
                            }

                            int successIdx = -1;
                            for (int i = startIdx; i < combos.Length; i++)
                            {
                                var (exclusive, raw) = combos[i];
                                var flags = WasapiInitFlags.EventDriven;
                                if (exclusive)
                                {
                                    flags |= WasapiInitFlags.Exclusive;
                                    if (isAsyncMode)
                                    {
                                        flags |= WasapiInitFlags.Async;
                                    }
                                }
                                else
                                {
                                    flags |= WasapiInitFlags.Shared;
                                }
                                if (raw)
                                {
                                    flags |= WasapiInitFlags.Raw;
                                }
                                float buffer = exclusive ? wasapiOptions.BufferSize : 0f;
                                float period = exclusive ? wasapiOptions.Period : 0f;
                                wasapiOk = TryInitWasapiOutput(flags, buffer, period);
                                MajDebug.LogInfo("Audio", $"[BassWasapi]Init (exclusive={exclusive}, raw={raw}): {wasapiOk}");
                                Bass.LastError.LogInfo();
                                if (wasapiOk)
                                {
                                    successIdx = i;
                                    break;
                                }
                            }

                            if (!wasapiOk)
                            {
                                MajDebug.LogError("Audio", "[BassWasapi]All WASAPI init combinations failed");
                            }
                            else
                            {
                                if (successIdx != startIdx)
                                {
                                    var (exclusive, raw) = combos[successIdx];
                                    MajDebug.LogWarning("Audio", $"[BassWasapi]WASAPI fallback: config requested (exclusive={isExclusiveRequest}, raw={isRawMode}) but succeeded with (exclusive={exclusive}, raw={raw})");
                                }
                                BassWasapi.GetInfo(out var wasapiInfo);
                                MajDebug.LogInfo("Audio", $"[BassWasapi]Channel count: {wasapiInfo.Channels}");
                                GenerateMixingMatrix(wasapiInfo.Channels);
                                BassWasapi.Start();
                            }
                        }
                        break;
#endif
                    case SoundBackendOption.BassSimple:
                        {
                            MajDebug.LogInfo("Audio", "[Bass]Init: " + Bass.Init());
                            Bass.LastError.LogInfo();
                            GenerateMixingMatrix(Bass.Info.SpeakerCount);
                        }
                        break;
                }
                if (isBassBackend)
                {
                    var info = Bass.Info;
                    MajDebug.LogInfo("Audio", $"[Bass]Min playback buffer length: {info.MinBufferLength}");
                    MajDebug.LogInfo("Audio", $"[Bass]Current device buffer length: {Bass.GetConfig(Configuration.DeviceBufferLength)}");
                    MajDebug.LogInfo("Audio", $"[Bass]Current device period: {Bass.GetConfig(Configuration.DevicePeriod)}");
                    MajDebug.LogInfo("Audio", $"[Bass]Channel count: {Bass.Info.SpeakerCount}");
                }
                InitSFXSample(sfxDirectory, isVoice: false);
                InitSFXSample(voiceDirectory, isVoice: true);

                if (isBassBackend)
                {
                    Bass.LastError.LogInfo();
                }

                if (PlayDebug)
                {
                    InputManager.BindAnyArea(OnAnyAreaDown);
                }
                ReadVolumeFromSettings();
                GameManager.OnAppPause += OnAppPause;
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
            }
        }
#if UNITY_STANDALONE_WIN
        /// <summary>Initializes the default WASAPI output in one mode and lets BASSWASAPI pull the global mixer natively.</summary>
        /// <remarks>
        /// A managed output callback would register the WASAPI output thread with the managed runtime, whose garbage collections
        /// suspend every registered thread, so a blocking collection such as the one during a scene load would starve the device.
        /// Native feeding needs the mixer before initialization and plays it in the mixer's own format, so the format that this mode
        /// selects is read first with a push-mode initialization that is freed again. Push mode rejects event-driven buffering, which
        /// does not affect the format, so that flag is left out of the probe.
        /// </remarks>
        /// <param name="flags">The WASAPI mode flags.</param>
        /// <param name="buffer">The device buffer length in seconds, or zero for the default.</param>
        /// <param name="period">The device update period in seconds, or zero for the default.</param>
        /// <returns>True when the output is initialized and <see cref="BassGlobalMixer"/> feeds it; false when this mode is unavailable.</returns>
        private static bool TryInitWasapiOutput(WasapiInitFlags flags, float buffer, float period)
        {
            var probeFlags = flags & ~WasapiInitFlags.EventDriven;
            if (!BassWasapi.InitEx(BassWasapi.DefaultDevice, 0, 0, probeFlags, buffer, period, BassWasapi.WasapiProc_Push))
            {
                return false;
            }
            var hasInfo = BassWasapi.GetInfo(out var info);
            BassWasapi.Free();
            if (!hasInfo)
            {
                return false;
            }

            var mixer = BassMix.CreateMixerStream(info.Frequency, info.Channels, BassFlags.MixerNonStop | BassFlags.Decode | BassFlags.Float);
            if (mixer == 0)
            {
                return false;
            }
            Bass.ChannelSetAttribute(mixer, ChannelAttribute.Buffer, 0);
            Bass.ChannelSetAttribute(mixer, (ChannelAttribute)86017, 8);
            if (!BassWasapi.InitEx(BassWasapi.DefaultDevice, info.Frequency, info.Channels, flags, buffer, period, BassWasapi.WasapiProc_Bass, (IntPtr)mixer))
            {
                Bass.StreamFree(mixer);
                return false;
            }
            BassGlobalMixer = mixer;
            return true;
        }
#endif
        /// <summary>Loads every file of a streaming assets sound directory through the entries its backend enumerates.</summary>
        /// <param name="directory">The sound directory, or null when the streaming assets do not provide it.</param>
        /// <param name="isVoice">Whether the directory holds voice samples instead of sound effects.</param>
        private void InitSFXSample(StorageDirectory? directory, bool isVoice)
        {
            if (directory is null)
            {
                MajDebug.LogWarning($"[Audio]{(isVoice ? "Voice" : "SFX")} directory does not exist");
                return;
            }
            foreach (var entry in directory.EnumerateEntries())
            {
                if (entry.IsDirectory || entry.Name.EndsWith(".meta"))
                {
                    continue;
                }
                var fileName = entry.Name;
                var sample = LoadMusic(entry.Location, false, false);
                if (sample.IsEmpty)
                {
                    // The shared empty sample must keep its own name and type.
                    SFXSamples.Add(sample);
                    continue;
                }
                sample.Name = fileName;

                //group the samples
                sample.SampleType = fileName switch
                {
                    var _ when isVoice => SFXSampleType.Voice,
                    "tap_ex.wav" => SFXSampleType.Ex,
                    "touch_hanabi.wav" => SFXSampleType.Hanabi,
                    var p when p.StartsWith("bgm") => SFXSampleType.BGM,
                    var p when p.StartsWith("answer") => SFXSampleType.Answer,
                    var p when p.StartsWith("break") => SFXSampleType.Break,
                    var p when p.StartsWith("slide") => SFXSampleType.Slide,
                    var p when p.StartsWith("tap") => SFXSampleType.Tap,
                    var p when p.StartsWith("touch") => SFXSampleType.Touch,
                    _ => sample.SampleType
                };
                SFXSamples.Add((sample));
            }
        }
        private void OnAnyAreaDown(object sender, InputEventArgs e)
        {
            if (e.Status != SwitchStatus.On)
            {
                return;
            }
            if (e.IsButton)
            {
                PlaySFX("answer.wav");
            }
            else
            {
                PlaySFX("touch.wav");
            }
        }

        private void OnDestroy()
        {
            GameManager.OnAppPause -= OnAppPause;
            if (MajEnv.Settings.Audio.Backend == SoundBackendOption.Wasapi
                || MajEnv.Settings.Audio.Backend == SoundBackendOption.Asio ||
                MajEnv.Settings.Audio.Backend == SoundBackendOption.BassSimple)
            {
                foreach (var sample in SFXSamples)
                {
                    if (sample is not null)
                    {
                        sample.Dispose();
                    }
                }

                Bass.StreamFree(BassGlobalMixer);
#if UNITY_STANDALONE_WIN
                BassAsio.Stop();
                BassAsio.Free();
                BassWasapi.Stop();
                BassWasapi.Free();
#endif
                Bass.Stop();
                Bass.Free();
                // Bass.Free released the device output stream with the device.
                s_bassDeviceOutputStream = 0;
            }
        }

        private void OnAppPause(object? sender, bool isPaused)
        {
#if UNITY_ANDROID || UNITY_IOS
            if (isPaused)
            {
                MajDebug.LogDebug("Application paused, attempting to mute Bass output");
                Bass.GlobalMusicVolume = 0;
                Bass.GlobalSampleVolume = 0;
                Bass.GlobalStreamVolume = 0;
                MajDebug.LogDebug($"[Bass] {Bass.LastError}");
            }
            else
            {
                MajDebug.LogDebug("Application resumed, attempting to restore Bass output volume");
                Bass.GlobalMusicVolume = 10000;
                Bass.GlobalSampleVolume = 10000;
                Bass.GlobalStreamVolume = 10000;
                MajDebug.LogDebug($"[Bass] {Bass.LastError}");
            }
#endif
        }
        public void ReadVolumeFromSettings()
        {
            var volume = MajEnv.Settings.Audio.Volume;
            foreach (var sample in SFXSamples)
            {
                if (sample is null || sample.IsEmpty)
                {
                    continue;
                }
                var vol = sample.SampleType switch
                {
                    SFXSampleType.Answer => volume.Answer,
                    SFXSampleType.Tap => volume.Tap,
                    SFXSampleType.Ex => volume.Ex,
                    SFXSampleType.Break => volume.Break,
                    SFXSampleType.Touch => volume.Touch,
                    SFXSampleType.Hanabi => volume.Hanabi,
                    SFXSampleType.BGM => volume.BGM,
                    SFXSampleType.Slide => volume.Slide,
                    SFXSampleType.Voice => volume.Voice,
                    _ => 1f
                };
                sample.SetVolume(vol);
            }
        }

        public AudioSampleWrap LoadMusic(string path, bool normalize = true, bool speedChange = false)
        {
            MajDebug.LogInfo($"Try creating channel from file: {path}");
            var backend = MajEnv.Settings.Audio.Backend;
            if (FileSystem.File.Open(path).Exists)
            {
                AudioSampleWrap? sample;
                switch (backend)
                {
                    case SoundBackendOption.Unity:
                        sample = UnityAudioSample.Create($"file://{path}", gameObject);
                        break;
                    case SoundBackendOption.Asio:
                    case SoundBackendOption.Wasapi:
                        sample = BassAudioSample.Create(path, BassGlobalMixer, normalize, speedChange);
                        break;
                    case SoundBackendOption.BassSimple:
                        sample = BassSimpleAudioSample.Create(path, normalize, speedChange);
                        break;
                    default:
                        MajDebug.LogError("Backend not supported");
                        return AudioSampleWrap.Empty;
                }
                MajDebug.LogInfo("Channel created");
                return sample;
            }
            else
            {
                MajDebug.LogWarning(path + " dos not exists");
                return AudioSampleWrap.Empty;
            }
        }
        public AudioSampleWrap LoadMusicFromUri(Uri uri)
        {
            MajDebug.LogInfo($"Try creating channel from uri: {uri}");
            var backend = MajEnv.Settings.Audio.Backend;
            AudioSampleWrap? sample;
            switch (backend)
            {
                case SoundBackendOption.Unity:
                    sample = UnityAudioSample.Create(uri.OriginalString, gameObject);
                    break;
                case SoundBackendOption.Asio:
                case SoundBackendOption.Wasapi:
                    sample = BassAudioSample.CreateFromUri(uri, BassGlobalMixer);
                    break;
                case SoundBackendOption.BassSimple:
                    sample = BassSimpleAudioSample.CreateFromUri(uri);
                    break;
                default:
                    MajDebug.LogError("Backend not supported");
                    return AudioSampleWrap.Empty;
            }
            MajDebug.LogInfo("Channel created");
            return sample;
        }
        public async UniTask<AudioSampleWrap> LoadMusicAsync(string path, bool normalize = true, bool speedChange = false)
        {
            MajDebug.LogInfo($"Try creating channel from file: {path}");
            await UniTask.SwitchToThreadPool();
            var backend = MajEnv.Settings.Audio.Backend;
            if (FileSystem.File.Open(path).Exists)
            {
                AudioSampleWrap? sample;
                switch (backend)
                {
                    case SoundBackendOption.Unity:
                        await UniTask.SwitchToMainThread();
                        sample = await UnityAudioSample.CreateAsync($"file://{path}", gameObject);
                        break;
                    case SoundBackendOption.Asio:
                    case SoundBackendOption.Wasapi:
                        sample = await BassAudioSample.CreateAsync(path, BassGlobalMixer, normalize, speedChange);
                        break;
                    case SoundBackendOption.BassSimple:
                        sample = await BassSimpleAudioSample.CreateAsync(path, normalize, speedChange);
                        break;
                    default:
                        MajDebug.LogError("Backend not supported");
                        return AudioSampleWrap.Empty;
                }
                MajDebug.LogInfo("Channel created");
                return sample;
            }
            else
            {
                MajDebug.LogWarning(path + " dos not exists");
                return AudioSampleWrap.Empty;
            }
        }
        /// <summary>Creates a music sample that streams an online audio file with the configured audio backend.</summary>
        /// <param name="uri">The absolute HTTP or HTTPS address of the audio file.</param>
        /// <param name="token">Cancels the download and the channel creation; it does not affect the returned sample.</param>
        /// <returns>The created sample, or <see cref="AudioSampleWrap.Empty"/> when the backend is not supported.</returns>
        /// <exception cref="OperationCanceledException"><paramref name="token"/> was cancelled.</exception>
        /// <exception cref="System.Net.Http.HttpRequestException">A BASS backend's request failed or returned an error status code.</exception>
        /// <exception cref="System.IO.IOException">A BASS backend's download failed before the format could be identified.</exception>
        /// <exception cref="BassException">BASS could not create the stream.</exception>
        public async UniTask<AudioSampleWrap> LoadMusicFromUriAsync(Uri uri, CancellationToken token = default)
        {
            MajDebug.LogInfo($"Try creating channel from uri: {uri}");
            await UniTask.SwitchToThreadPool();
            var backend = MajEnv.Settings.Audio.Backend;
            AudioSampleWrap? sample;
            switch (backend)
            {
                case SoundBackendOption.Unity:
                    await UniTask.SwitchToMainThread();
                    sample = await UnityAudioSample.CreateAsync(uri.OriginalString, gameObject);
                    break;
                case SoundBackendOption.Asio:
                case SoundBackendOption.Wasapi:
                    sample = await BassAudioSample.CreateFromUriAsync(uri, BassGlobalMixer, token);
                    break;
                case SoundBackendOption.BassSimple:
                    sample = await BassSimpleAudioSample.CreateFromUriAsync(uri, token);
                    break;
                default:
                    MajDebug.LogError("Backend not supported");
                    return AudioSampleWrap.Empty;
            }
            MajDebug.LogInfo("Channel created");
            return sample;
        }
        public AudioSampleWrap PlaySFX(string name, bool isLoop = false)
        {
            var psp = SFXSamples.FirstOrDefault(o => o.Name == name);
            if (psp is not null)
            {
                if (psp.SampleType == SFXSampleType.Voice)
                {
                    foreach (var voice in SFXSamples.FindAll(o => o.SampleType == SFXSampleType.Voice))
                    {
                        if (voice is not null)
                        {
                            voice.Stop();
                        }
                    }
                }
                psp.PlayOneShot();
                psp.IsLoop = isLoop;
                return psp;
            }
            else
            {
                MajDebug.LogError($"No such SFX\nName: {name}");
                return AudioSampleWrap.Empty;
            }
        }

        public AudioSampleWrap GetSFX(string name)
        {
            var psp = SFXSamples.FirstOrDefault(o => o.Name == name);
            if (psp is not null)
            {
                return psp;
            }
            else
            {
                return AudioSampleWrap.Empty;
            }
        }

        public void StopSFX(string name)
        {
            var psp = SFXSamples.FirstOrDefault(o => o.Name == name);
            if (psp is not null)
            {
                psp.Stop();
            }
            else
            {
                MajDebug.LogError($"No such SFX\nName: {name}");
            }
        }
        public void OpenAsioPannel()
        {
#if UNITY_STANDALONE_WIN
            if (MajEnv.Settings.Audio.Backend == SoundBackendOption.Asio)
            {
                BassAsio.ControlPanel();
            }
#endif
        }
        /// <summary>Creates a recorder that captures the final audio output of the Bass backend.</summary>
        /// <remarks>
        /// WASAPI and ASIO capture the global mixer that feeds the device; BassSimple captures the device's final output mix.
        /// Every call returns an independent recorder with its own capture DSP, buffer, start/stop state, and output latency
        /// probe; recorders only share the observed output channel, which disposing a recorder never frees. The caller owns the
        /// returned recorder and must dispose it before this manager is destroyed.
        /// </remarks>
        /// <returns>A stopped recorder producing interleaved float samples at the output sample rate.</returns>
        /// <exception cref="NotSupportedException">The active backend is not a Bass backend, or the output uses 8-bit samples.</exception>
        /// <exception cref="InvalidOperationException">The Bass output has not been initialized or cannot accept a capture DSP or latency probe.</exception>
        public IAudioRecorder CreateAudioRecorder()
        {
            var backend = MajEnv.Settings.Audio.Backend;
            int outputChannel;
            BassOutputLatencyProbe latencyProbe;
            switch (backend)
            {
#if UNITY_STANDALONE_WIN
                case SoundBackendOption.Asio:
                    outputChannel = GetBassGlobalMixer();
                    latencyProbe = BassOutputLatencyProbe.CreateForAsio();
                    break;
                case SoundBackendOption.Wasapi:
                    outputChannel = GetBassGlobalMixer();
                    latencyProbe = BassOutputLatencyProbe.CreateForWasapi();
                    break;
#endif
                case SoundBackendOption.BassSimple:
                    outputChannel = GetBassDeviceOutputStream();
                    latencyProbe = BassOutputLatencyProbe.CreateForDeviceStream(outputChannel);
                    break;
                default:
                    throw new NotSupportedException($"Audio recording requires a Bass backend, but the active backend is {backend}.");
            }
            try
            {
                return new BassAudioRecorder(outputChannel, AudioRecorderBufferSeconds, latencyProbe);
            }
            catch
            {
                latencyProbe.Dispose();
                throw;
            }
        }
        /// <summary>Gets the global mixer feeding the WASAPI or ASIO device.</summary>
        /// <returns>The decoding mixer handle.</returns>
        /// <exception cref="InvalidOperationException">The mixer has not been created.</exception>
        private static int GetBassGlobalMixer()
        {
            if (BassGlobalMixer == -114514 || BassGlobalMixer == 0)
            {
                throw new InvalidOperationException("The Bass global mixer has not been initialized.");
            }
            return BassGlobalMixer;
        }
        /// <summary>Gets the stream representing the BassSimple device's final output mix, creating it on first use.</summary>
        /// <returns>The device output stream handle.</returns>
        /// <exception cref="InvalidOperationException">Bass cannot create or start the device output stream.</exception>
        private static int GetBassDeviceOutputStream()
        {
            if (s_bassDeviceOutputStream != 0)
            {
                return s_bassDeviceOutputStream;
            }
            // Bass creates the stream in the device's own output format and frees it in Bass.Free.
            var stream = Bass.CreateStream(0, 0, BassFlags.Default, StreamProcedureType.Device);
            if (stream == 0)
            {
                throw new InvalidOperationException($"Cannot create the Bass device output stream: {Bass.LastError}");
            }
            if (Bass.ChannelIsActive(stream) != PlaybackState.Playing && !Bass.ChannelPlay(stream))
            {
                throw new InvalidOperationException($"Cannot start the Bass device output stream: {Bass.LastError}");
            }
            MajDebug.LogInfo("[Audio][Bass]Created device output stream for recording");
            s_bassDeviceOutputStream = stream;
            return stream;
        }
        private static void GenerateMixingMatrix(int chCount)
        {
            //        var matrix = new float[8, 2]
            //        {
            //// Input      L   R
            //            { 1f, 0f }, // LF
            //            { 0f, 1f }, // RF
            //            { 0f, 0f }, // Center
            //            { 0f, 0f }, // LFE
            //            { 0f, 0f }, // LR
            //            { 0f, 0f }, // RR
            //            { 0f, 0f }, // LR Center
            //            { 0f, 0f }, // RR Center
            //        };
            // 3 channels      left - front, right - front, center.
            // 4 channels      left - front, right - front, left - rear / side, right - rear / side.
            // 6 channels(5.1) left - front, right - front, center, LFE, left - rear / side, right - rear / side.
            // 8 channels(7.1) left - front, right - front, center, LFE, left - rear / side, right - rear / side, left - rear center, right - rear center.

            // LFE = left
            // Center = right

            float[,] matrix;

            var isForceMono = MajEnv.Settings.Audio.ForceMono;
#if UNITY_ANDROID || UNITY_IOS
            var volumeSettings = new
            {
                FrontVolume = 1f,
                CenterAndLFEVolume = 1f,
                SideVolume = 1f,
                RearVolume = 1f
            };
#else
            var volumeSettings = MajEnv.Settings.Audio.Channel;
#endif
            if (isForceMono)
            {
                switch (chCount)
                {
                    case 1:// Mono
                        matrix = new float[1, 2]
                        {
                            { 0.5f, 0.5f }
                        };
                        break;
                    case 2: // 2.0
                        matrix = new float[2, 2]
                        {
                            { 0.5f, 0.5f },
                            { 0.5f, 0.5f }
                        };
                        break;
                    case 3: // 3.0
                        matrix = new float[3, 2]
                        {
                            { 0.5f, 0.5f },
                            { 0.5f, 0.5f },
                            { 0.5f, 0.5f },
                        };
                        break;
                    case 4: // 4.0
                        matrix = new float[4, 2]
                        {
                            { 0.5f, 0.5f },
                            { 0.5f, 0.5f },
                            { 0.5f, 0.5f },
                            { 0.5f, 0.5f },
                        };
                        break;
                    case 6: // 5.1
                        matrix = new float[6, 2]
                        {
                            { 0.5f, 0.5f },
                            { 0.5f, 0.5f },
                            { 0.5f, 0.5f },
                            { 0.5f, 0.5f },
                            { 0.5f, 0.5f },
                            { 0.5f, 0.5f },
                        };
                        break;
                    case 8: // 7.1
                        matrix = new float[8, 2]
                        {
                            { 0.5f, 0.5f },
                            { 0.5f, 0.5f },
                            { 0.5f, 0.5f },
                            { 0.5f, 0.5f },
                            { 0.5f, 0.5f },
                            { 0.5f, 0.5f },
                            { 0.5f, 0.5f },
                            { 0.5f, 0.5f },
                        };
                        break;
                    default:
                        matrix = new float[1, 2]
                        {
                            { 0.5f, 0.5f }
                        };
                        break;
                }
            }
            else
            {
                switch (chCount)
                {
                    case 1:// Mono
                        matrix = new float[1, 2]
                        {
                            { 0.5f, 0.5f }
                        };
                        break;
                    case 2: // 2.0
                        matrix = new float[2, 2]
                        {
                            { volumeSettings.FrontVolume, 0f },
                            { 0f, volumeSettings.FrontVolume }
                        };
                        break;
                    case 3: // 3.0
                        matrix = new float[3, 2]
                        {
                            { volumeSettings.FrontVolume, 0f },
                            { volumeSettings.CenterAndLFEVolume/2f, volumeSettings.CenterAndLFEVolume/2f },
                            { 0f, volumeSettings.FrontVolume },
                        };
                        break;
                    case 4: // 4.0
                        matrix = new float[4, 2]
                        {
                            { volumeSettings.FrontVolume, 0f },
                            { 0f, volumeSettings.FrontVolume },
                            { volumeSettings.RearVolume, 0f },
                            { 0f, volumeSettings.RearVolume },
                        };
                        break;
                    case 6: // 5.1
                        matrix = new float[6, 2]
                        {
                            { volumeSettings.FrontVolume, 0f },
                            { 0f, volumeSettings.FrontVolume },
                            { volumeSettings.CenterAndLFEVolume, 0f },
                            { 0f, volumeSettings.CenterAndLFEVolume },
                            { volumeSettings.SideVolume, 0f },
                            { 0f, volumeSettings.SideVolume }
                        };
                        break;
                    case 8: // 7.1
                        matrix = new float[8, 2]
                        {
                            { volumeSettings.FrontVolume, 0f },
                            { 0f, volumeSettings.FrontVolume },
                            { volumeSettings.CenterAndLFEVolume, 0f },
                            { 0f, volumeSettings.CenterAndLFEVolume },
                            { volumeSettings.SideVolume, 0f },
                            { 0f, volumeSettings.SideVolume },
                            { volumeSettings.RearVolume, 0f },
                            { 0f, volumeSettings.RearVolume },
                        };
                        break;
                    default:
                        matrix = new float[1, 2]
                        {
                            { 0.5f, 0.5f }
                        };
                        break;
                }
            }


            MixingMatrix = matrix;
        }
    }
}
