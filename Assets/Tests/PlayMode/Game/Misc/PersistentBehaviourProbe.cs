#nullable enable
namespace MajdataPlay.Tests.Game.Misc.PlayMode
{
    /// <summary>A <see cref="MajBehaviour"/> marked to survive scene loads, used to check the attribute handling in Awake.</summary>
    [global::MajdataPlay.DontDestroyOnLoad]
    public class PersistentBehaviourProbe : MajBehaviour
    {
    }
}
