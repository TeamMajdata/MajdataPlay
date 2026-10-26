using MajdataPlay.Utils;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MajdataPlay.Behaviours.UI.Inicators
{
    public class ModEnabledInicator : UIInicator
    {
        
        protected override void Awake()
        {
            base.Awake();
            var actionTo = ActionTo;
            if(actionTo == null)
            {
                actionTo = gameObject;
            }
            actionTo.SetActive(MajInstances.GameManager.Settings.Mod.IsAnyModActive());
        }
    }
}