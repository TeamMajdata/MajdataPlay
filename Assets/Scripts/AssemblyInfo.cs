#nullable enable
using System.Runtime.CompilerServices;

// Unity Test Framework assemblies under Assets/Tests that test the internal members of this assembly.
[assembly: InternalsVisibleTo("MajdataPlay.Tests.Game")]
[assembly: InternalsVisibleTo("MajdataPlay.Tests.Game.PlayMode")]
// The editor assembly under Assets/Scripts/Editor, whose inspectors show internal behaviours such as slides.
[assembly: InternalsVisibleTo("MajdataPlay.Core.Editor")]
