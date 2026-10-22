using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.Game.Notes.Slide;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode.Doubles
{
    /// <summary>
    /// A minimal slide that can act as the parent of a connection slide part.
    /// </summary>
    internal sealed class ProbeConnectableSlide : SlideBase, IConnectableSlide
    {
        /// <summary>Gets the rotation of the star at the end of this slide.</summary>
        public Quaternion FinalStarAngle => Quaternion.identity;

        /// <summary>Marks the slide as initialized.</summary>
        public override void Init()
        {
            State = NoteStatus.Inited;
        }

        /// <summary>Ends the slide through the shared slide end logic.</summary>
        void IConnectableSlide.End()
        {
            End();
        }

        /// <summary>Does nothing; the probe has no skin.</summary>
        protected override void LoadSkin()
        {
        }
    }
}
