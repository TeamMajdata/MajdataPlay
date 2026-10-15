using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;

namespace MajdataPlay.Behaviours.UI.Inicators
{
    public abstract class UIInicator : MajBehaviour
    {
        [SerializeField]
        [FormerlySerializedAs("actionTo")]
        protected GameObject ActionTo;
    }
}
