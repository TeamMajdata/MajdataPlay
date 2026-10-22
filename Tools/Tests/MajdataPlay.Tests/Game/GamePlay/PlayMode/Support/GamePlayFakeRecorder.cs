using System.Threading.Tasks;
using MajdataPlay.Recording;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.PlayMode.Support
{
    /// <summary>
    /// An <see cref="IRecorder"/> whose connection and recording state are set by the test, so recorder status
    /// displays can be checked without OBS or FFmpeg.
    /// </summary>
    internal sealed class GamePlayFakeRecorder : IRecorder
    {
        /// <summary>Gets or sets a value indicating whether the recorder reports an active recording.</summary>
        public bool IsRecording { get; set; }

        /// <summary>Gets or sets a value indicating whether the recorder reports a connection.</summary>
        public bool IsConnected { get; set; }

        /// <summary>Marks the recorder as recording.</summary>
        public void StartRecord()
        {
            IsRecording = true;
        }

        /// <summary>Marks the recorder as recording.</summary>
        /// <returns>A completed task.</returns>
        public Task StartRecordAsync()
        {
            StartRecord();
            return Task.CompletedTask;
        }

        /// <summary>Marks the recorder as stopped.</summary>
        public void StopRecord()
        {
            IsRecording = false;
        }

        /// <summary>Marks the recorder as stopped.</summary>
        /// <returns>A completed task.</returns>
        public Task StopRecordAsync()
        {
            StopRecord();
            return Task.CompletedTask;
        }

        /// <summary>Does nothing.</summary>
        public void OnLateUpdate()
        {
        }

        /// <summary>Ignores the output name.</summary>
        /// <param name="name">The output name.</param>
        public void SetOutputName(string name)
        {
        }

        /// <summary>Does nothing.</summary>
        public void Dispose()
        {
        }
    }
}
