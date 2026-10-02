namespace MajdataPlay.IO
{
    internal interface IGameDevice
    {
        bool IsConnected { get; }

        /// <summary>Publishes state once per frame, even when several capabilities share this device.</summary>
        void OnPreUpdate();
    }
}
