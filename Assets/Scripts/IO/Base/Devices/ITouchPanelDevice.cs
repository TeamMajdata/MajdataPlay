using System;

namespace MajdataPlay.IO
{
    internal interface ITouchPanelDevice : IGameDevice
    {
        /// <summary>Copies 35 physical sensor slots, including separate C1/C2, without consuming edges.</summary>
        void ReadTouchPanel(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff);
        /// <summary>Reads live state independently of the last frame snapshot.</summary>
        bool IsSensorCurrentlyOn(int index);
    }
}
