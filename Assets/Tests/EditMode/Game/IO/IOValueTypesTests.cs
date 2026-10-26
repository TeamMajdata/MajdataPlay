using System;
using MajdataPlay.IO;
using ManagedBass;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using KeyCode = MajdataPlay.IO.KeyCode;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>Verifies the small IO value types, exceptions and the keyboard binding tables.</summary>
    public class IOValueTypesTests
    {
        /// <summary>Checks the press and release classification of input events.</summary>
        [Test]
        public void InputEventArgs_ClassifiesTransitions()
        {
            var down = new InputEventArgs { OldStatus = SwitchStatus.Off, Status = SwitchStatus.On, IsButton = true, BZone = ButtonZone.A3 };
            var up = new InputEventArgs { OldStatus = SwitchStatus.On, Status = SwitchStatus.Off, SArea = SensorArea.C };
            var held = new InputEventArgs { OldStatus = SwitchStatus.On, Status = SwitchStatus.On };

            Assert.That(down.IsDown && !down.IsUp, Is.True);
            Assert.That(up.IsUp && !up.IsDown, Is.True);
            Assert.That(held.IsDown || held.IsUp, Is.False);
            Assert.That(down.BZone, Is.EqualTo(ButtonZone.A3));
            Assert.That(up.SArea, Is.EqualTo(SensorArea.C));
        }

        /// <summary>Checks that the audio track exceptions keep their path and message.</summary>
        [Test]
        public void AudioTrackExceptions_KeepTheirPath()
        {
            var invalid = new InvalidAudioTrackException("bad track", "a.mp3");
            var missing = new AudioTrackNotFoundException("b.mp3");

            Assert.That(invalid.Message, Is.EqualTo("bad track"));
            Assert.That(invalid.TrackPath, Is.EqualTo("a.mp3"));
            Assert.That(missing.TrackPath, Is.EqualTo("b.mp3"));
            Assert.That(missing.Message, Is.EqualTo("Audio track not found"));
            Assert.That(missing, Is.InstanceOf<InvalidAudioTrackException>());
        }

        /// <summary>Checks that only the OK BASS status is treated as success.</summary>
        [Test]
        public void EnsureSuccessStatusCode_ThrowsForErrors()
        {
            Assert.DoesNotThrow(() => Errors.OK.EnsureSuccessStatusCode());
            var exception = Assert.Throws<BassException>(() => Errors.FileFormat.EnsureSuccessStatusCode());
            Assert.That(exception.ErrorCode, Is.EqualTo(Errors.FileFormat));
        }

        /// <summary>Checks that the binding table follows the button zone order.</summary>
        [Test]
        public void KeyboardHelper_BindingsFollowButtonZones()
        {
            var bindings = KeyboardHelper.ButtonBindings;

            Assert.That(bindings.Length, Is.EqualTo(12));
            for (var i = 0; i < bindings.Length; i++)
            {
                Assert.That(KeyboardHelper.GetBindingKey((ButtonZone)i), Is.EqualTo(bindings[i]));
            }
            Assert.That(KeyboardHelper.GetBindingKey(ButtonZone.Service), Is.EqualTo(KeyCode.Service));
            Assert.Throws<ArgumentOutOfRangeException>(() => KeyboardHelper.GetBindingKey((ButtonZone)12));
            Assert.Throws<ArgumentOutOfRangeException>(() => KeyboardHelper.GetBindingKey((ButtonZone)(-1)));
        }

        /// <summary>Checks the Unity Input System key mapping of every logical key.</summary>
        /// <param name="key">The logical key as its integer value.</param>
        /// <param name="expected">The expected keyboard key.</param>
        [TestCase((int)KeyCode.B1, Key.W)]
        [TestCase((int)KeyCode.B2, Key.E)]
        [TestCase((int)KeyCode.B3, Key.D)]
        [TestCase((int)KeyCode.B4, Key.C)]
        [TestCase((int)KeyCode.B5, Key.X)]
        [TestCase((int)KeyCode.B6, Key.Z)]
        [TestCase((int)KeyCode.B7, Key.A)]
        [TestCase((int)KeyCode.B8, Key.Q)]
        [TestCase((int)KeyCode.Test, Key.Numpad9)]
        [TestCase((int)KeyCode.SelectP1, Key.NumpadMultiply)]
        [TestCase((int)KeyCode.Service, Key.Numpad7)]
        [TestCase((int)KeyCode.SelectP2, Key.Numpad3)]
        public void KeyboardHelper_ToUnityKeyCode(int key, Key expected)
        {
            Assert.That(KeyboardHelper.ToUnityKeyCode((KeyCode)key), Is.EqualTo(expected));
        }

        /// <summary>Checks the gamepad mapping of every logical key.</summary>
        /// <param name="key">The logical key as its integer value.</param>
        /// <param name="expected">The expected gamepad button.</param>
        [TestCase((int)KeyCode.B1, GamepadButton.North)]
        [TestCase((int)KeyCode.B2, GamepadButton.East)]
        [TestCase((int)KeyCode.B3, GamepadButton.South)]
        [TestCase((int)KeyCode.B4, GamepadButton.West)]
        [TestCase((int)KeyCode.B5, GamepadButton.DpadUp)]
        [TestCase((int)KeyCode.B6, GamepadButton.DpadRight)]
        [TestCase((int)KeyCode.B7, GamepadButton.DpadDown)]
        [TestCase((int)KeyCode.B8, GamepadButton.DpadLeft)]
        [TestCase((int)KeyCode.Test, GamepadButton.LeftShoulder)]
        [TestCase((int)KeyCode.SelectP1, GamepadButton.RightShoulder)]
        [TestCase((int)KeyCode.Service, GamepadButton.LeftStick)]
        [TestCase((int)KeyCode.SelectP2, GamepadButton.RightStick)]
        public void KeyboardHelper_ToGamepadButton(int key, GamepadButton expected)
        {
            Assert.That(KeyboardHelper.ToGamepadButton((KeyCode)key), Is.EqualTo(expected));
        }

#if UNITY_STANDALONE_WIN
        /// <summary>Checks that the Windows virtual key mapping covers every logical key and rejects unknown keys.</summary>
        [Test]
        public void KeyboardHelper_ToWinKeyCode_CoversEveryKey()
        {
            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
            {
                Assert.That(seen.Add((int)KeyboardHelper.ToWinKeyCode(key)), Is.True, key.ToString());
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => KeyboardHelper.ToWinKeyCode((KeyCode)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => KeyboardHelper.ToUnityKeyCode((KeyCode)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => KeyboardHelper.ToGamepadButton((KeyCode)99));
        }
#endif

        /// <summary>Checks the Linux serial device name derived from a Windows COM port number.</summary>
        [Test]
        public void IODetector_WinSerialPortToLinuxPortName_UsesZeroBasedTtyUsb()
        {
            Assert.That(PrivateMember.InvokeStatic<string>(typeof(IODetector), "WinSerialPortToLinuxPortName", 3), Is.EqualTo("/dev/ttyUSB2"));
            Assert.That(PrivateMember.InvokeStatic<string>(typeof(IODetector), "WinSerialPortToLinuxPortName", 21), Is.EqualTo("/dev/ttyUSB20"));
        }

        /// <summary>Checks that the serial span helpers skip empty buffers and require an open port otherwise.</summary>
        [Test]
        public void SerialPortExtensions_HandleEmptyBuffersAndClosedPorts()
        {
            using var port = new System.IO.Ports.SerialPort("COM250");

            Assert.That(port.Read(Span<byte>.Empty), Is.EqualTo(0));
            Assert.DoesNotThrow(() => port.Write(ReadOnlySpan<byte>.Empty));
            Assert.Throws<InvalidOperationException>(() => port.Read(new byte[4].AsSpan()));
            Assert.Throws<InvalidOperationException>(() => port.Write(new byte[] { 1, 2 }.AsSpan()));
        }

        /// <summary>Checks that reading function buttons only touches the function slots and never clears a set flag.</summary>
        [Test]
        public void KeyboardHelper_ReadFunctionButtons_OnlyAddsFunctionStates()
        {
            var states = new bool[12];
            states[0] = true;
            states[8] = true;

            KeyboardHelper.ReadFunctionButtons(states);

            Assert.That(states[0], Is.True);
            Assert.That(states[8], Is.True);
            for (var i = 1; i < KeyboardHelper.GameButtonCount; i++)
            {
                Assert.That(states[i], Is.False);
            }
        }
    }
}
