using System;

namespace MajdataPlay.IO
{
    internal interface IButtonRingDevice : IGameDevice
    {
        /// <summary>Copies the 12-button frame snapshot without consuming its latched edges.</summary>
        void ReadButtons(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff);
        /// <summary>Reads live state independently of the last frame snapshot.</summary>
        bool IsButtonCurrentlyOn(int index);
    }
}
