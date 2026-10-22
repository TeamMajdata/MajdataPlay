using System;
using MajdataPlay.IO;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>
    /// A scripted button ring, touch panel and LED device: tests set the reported states directly and inspect the LED
    /// frames written to it.
    /// </summary>
    internal sealed class FakeInputDevice : IButtonRingDevice, ITouchPanelDevice, ILedDevice
    {
        /// <summary>Gets the button states copied into each snapshot.</summary>
        public bool[] Buttons { get; } = new bool[12];

        /// <summary>Gets the button press edges copied into each snapshot.</summary>
        public bool[] ButtonHadOn { get; } = new bool[12];

        /// <summary>Gets the 35 sensor slots copied into each snapshot.</summary>
        public bool[] Sensors { get; } = new bool[35];

        /// <summary>Gets the sensor press edges copied into each snapshot.</summary>
        public bool[] SensorHadOn { get; } = new bool[35];

        /// <summary>Gets the most recent LED colours written to the device.</summary>
        public Color[] LastColors { get; } = new Color[8];

        /// <summary>Gets the most recent cabinet brightness written to the device.</summary>
        public byte LastCabinetBrightness { get; private set; }

        /// <summary>Gets the number of LED writes.</summary>
        public int LedWrites { get; private set; }

        /// <summary>Gets the number of frame updates received.</summary>
        public int PreUpdates { get; private set; }

        /// <summary>Gets or sets the reported connection state.</summary>
        public bool IsConnected { get; set; }

        /// <inheritdoc/>
        public void OnPreUpdate()
        {
            PreUpdates++;
        }

        /// <inheritdoc/>
        public void ReadButtons(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff)
        {
            Buttons.AsSpan().CopyTo(states);
            ButtonHadOn.AsSpan().CopyTo(hadOn);
            hadOff.Clear();
        }

        /// <inheritdoc/>
        public bool IsButtonCurrentlyOn(int index)
        {
            return Buttons[index];
        }

        /// <inheritdoc/>
        public void ReadTouchPanel(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff)
        {
            Sensors.AsSpan().CopyTo(states);
            SensorHadOn.AsSpan().CopyTo(hadOn);
            hadOff.Clear();
        }

        /// <inheritdoc/>
        public bool IsSensorCurrentlyOn(int index)
        {
            return Sensors[index];
        }

        /// <inheritdoc/>
        public void WriteLeds(ReadOnlySpan<Color> colors, byte cabinetBrightness)
        {
            colors.CopyTo(LastColors);
            LastCabinetBrightness = cabinetBrightness;
            LedWrites++;
        }
    }
}
