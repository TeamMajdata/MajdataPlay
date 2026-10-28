using MajdataPlay.Databases;
using MajdataPlay.Editor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace MajdataPlay
{
    public abstract class MajBehaviour : MonoBehaviour
    {
        protected GameRuntime RuntimeDatabase { get => GameRuntime.Instance; }

        protected virtual void Awake()
        {
            var type = GetType();
            if (type.GetCustomAttribute<DontDestroyOnLoadAttribute>() is not null)
            {
                DontDestroyOnLoad(this);
            }
        }
    }
}
