using System;
using System.Collections.Generic;
using System.Text;

#nullable enable
namespace MajdataPlay.Scenes.Game.Parsing
{
    /// <summary>
    /// Rotates, mirrors and formats slide codes. Rotating or mirroring a code moves its path the same way, so two codes
    /// draw the same shape when one can be turned into the other.
    /// </summary>
    public static class SlideCodeShape
    {
        /// <summary>The number of keys on the judgment ring.</summary>
        private const int KeyCount = 8;

        /// <summary>
        /// Formats parsed commands as slide code without shorthand, for example <c>1P3P3K4</c> for <c>1P33K4</c>.
        /// </summary>
        /// <param name="commands">The commands returned by <see cref="SlideCodeParser.ParseCommands"/>; the first one is the start key.</param>
        /// <returns>The slide code.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="commands"/> is empty or contains an unknown command type.</exception>
        public static string Format(IReadOnlyList<SlideCodeParser.Command> commands)
        {
            if (commands.Count == 0)
            {
                throw new ArgumentException("A slide code needs at least its start key", nameof(commands));
            }
            var builder = new StringBuilder(commands.Count * 2);
            builder.Append(commands[0].Value);
            for (var i = 1; i < commands.Count; i++)
            {
                var command = commands[i];
                builder.Append(GetCommandChar(command.Type));
                if (command.Type != SlideCodeParser.CommandType.NodeC)
                {
                    builder.Append(command.Value);
                }
            }
            return builder.ToString();
        }

        /// <summary>Rotates a command clockwise by whole keys.</summary>
        /// <param name="command">The command.</param>
        /// <param name="diff">The number of keys to rotate by; negative values rotate counter-clockwise.</param>
        /// <returns>The rotated command. The centre node, the centre circle and the judgment circle do not change.</returns>
        public static SlideCodeParser.Command Rotate(SlideCodeParser.Command command, int diff)
        {
            switch (command.Type)
            {
                case SlideCodeParser.CommandType.NodeA:
                case SlideCodeParser.CommandType.NodeB:
                case SlideCodeParser.CommandType.NodeEnd:
                    return new SlideCodeParser.Command(command.Type, RotateKey(command.Value, diff));
                case SlideCodeParser.CommandType.OrbitCcw:
                case SlideCodeParser.CommandType.OrbitCw:
                    if (IsSideCircle(command.Value))
                    {
                        // 侧边圆以它经过的 D 区命名，跟着键位一起旋转
                        return new SlideCodeParser.Command(command.Type, RotateKey(command.Value, diff));
                    }
                    return command;
                default:
                    return command;
            }
        }

        /// <summary>
        /// Mirrors a command about the line through key 1, the centre and key 5, the axis that
        /// <see cref="Slide.ParametricSlidePath.SetRotoreflection"/> and <c>SlideTable.Mirror</c> use.
        /// </summary>
        /// <param name="command">The command.</param>
        /// <returns>The mirrored command; orbits also swap their direction.</returns>
        public static SlideCodeParser.Command Mirror(SlideCodeParser.Command command)
        {
            switch (command.Type)
            {
                case SlideCodeParser.CommandType.NodeA:
                case SlideCodeParser.CommandType.NodeB:
                case SlideCodeParser.CommandType.NodeEnd:
                    return new SlideCodeParser.Command(command.Type, MirrorKey(command.Value));
                case SlideCodeParser.CommandType.OrbitCcw:
                    return new SlideCodeParser.Command(SlideCodeParser.CommandType.OrbitCw, MirrorCircle(command.Value));
                case SlideCodeParser.CommandType.OrbitCw:
                    return new SlideCodeParser.Command(SlideCodeParser.CommandType.OrbitCcw, MirrorCircle(command.Value));
                default:
                    return command;
            }
        }

        /// <summary>Rotates a slide code clockwise by whole keys.</summary>
        /// <param name="slideCode">The slide code, for example <c>1K4</c>.</param>
        /// <param name="diff">The number of keys to rotate by; negative values rotate counter-clockwise.</param>
        /// <returns>The rotated slide code without shorthand, for example <c>3K6</c> for a rotation by 2.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="slideCode"/> is not valid slide code.</exception>
        public static string Rotate(string slideCode, int diff)
        {
            var commands = SlideCodeParser.ParseCommands(slideCode);
            for (var i = 0; i < commands.Count; i++)
            {
                commands[i] = Rotate(commands[i], diff);
            }
            return Format(commands);
        }

        /// <summary>Mirrors a slide code about the line through key 1, the centre and key 5.</summary>
        /// <param name="slideCode">The slide code, for example <c>1P9K3</c>.</param>
        /// <returns>The mirrored slide code without shorthand, for example <c>1Q9K7</c>.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="slideCode"/> is not valid slide code.</exception>
        public static string Mirror(string slideCode)
        {
            var commands = SlideCodeParser.ParseCommands(slideCode);
            for (var i = 0; i < commands.Count; i++)
            {
                commands[i] = Mirror(commands[i]);
            }
            return Format(commands);
        }

        /// <summary>Rotates a slide code so that it starts at key 1.</summary>
        /// <param name="slideCode">The slide code, for example <c>3K6</c>.</param>
        /// <param name="rotation">
        /// Receives the clockwise rotation, in keys, that turns the returned code back into <paramref name="slideCode"/>.
        /// </param>
        /// <returns>The slide code starting at key 1 without shorthand, for example <c>1K4</c>.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="slideCode"/> is not valid slide code.</exception>
        public static string Normalize(string slideCode, out int rotation)
        {
            var commands = SlideCodeParser.ParseCommands(slideCode);
            rotation = commands[0].Value - 1;
            for (var i = 0; i < commands.Count; i++)
            {
                commands[i] = Rotate(commands[i], -rotation);
            }
            return Format(commands);
        }

        /// <summary>Rotates a 1-based key index clockwise.</summary>
        /// <param name="key">The key index.</param>
        /// <param name="diff">The number of keys to rotate by.</param>
        /// <returns>The rotated key index, between 1 and 8.</returns>
        private static int RotateKey(int key, int diff)
        {
            return ((key - 1 + diff) % KeyCount + KeyCount) % KeyCount + 1;
        }

        /// <summary>Mirrors a 1-based key index about the axis through key 1 and key 5.</summary>
        /// <param name="key">The key index.</param>
        /// <returns>The mirrored key index, between 1 and 8.</returns>
        private static int MirrorKey(int key)
        {
            return ((KeyCount - (key - 1)) % KeyCount + KeyCount) % KeyCount + 1;
        }

        /// <summary>Mirrors a circle index about the axis through key 1 and key 5.</summary>
        /// <param name="circle">The circle index, see <see cref="SlideGeo.GetCircle"/>.</param>
        /// <returns>The mirrored circle index; the centre and judgment circles map to themselves.</returns>
        private static int MirrorCircle(int circle)
        {
            if (!IsSideCircle(circle))
            {
                return circle;
            }
            // 侧边圆 k 的圆心辐角是 135° - 45°k，关于 67.5° 的轴反射后得到侧边圆 3 - k
            return ((2 - circle) % KeyCount + KeyCount) % KeyCount + 1;
        }

        /// <summary>Checks whether a circle index names one of the eight side circles used by ppqq slides.</summary>
        /// <param name="circle">The circle index.</param>
        /// <returns><see langword="true"/> for the side circles 1 to 8.</returns>
        private static bool IsSideCircle(int circle)
        {
            return circle >= 1 && circle <= KeyCount;
        }

        /// <summary>Gets the slide code letter of a command type.</summary>
        /// <param name="type">The command type.</param>
        /// <returns>The command letter.</returns>
        /// <exception cref="ArgumentException">Thrown for <see cref="SlideCodeParser.CommandType.Invalid"/>.</exception>
        private static char GetCommandChar(SlideCodeParser.CommandType type)
        {
            return type switch
            {
                SlideCodeParser.CommandType.NodeA => 'A',
                SlideCodeParser.CommandType.NodeB => 'B',
                SlideCodeParser.CommandType.NodeC => 'C',
                SlideCodeParser.CommandType.OrbitCcw => 'P',
                SlideCodeParser.CommandType.OrbitCw => 'Q',
                SlideCodeParser.CommandType.NodeEnd => 'K',
                _ => throw new ArgumentException($"\"{type}\" is not a slide code command", nameof(type))
            };
        }
    }
}
