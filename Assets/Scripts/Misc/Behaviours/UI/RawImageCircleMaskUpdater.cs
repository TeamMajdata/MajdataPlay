using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace MajdataPlay.Behaviours.UI
{
    [RequireComponent(typeof(RawImage))]
    [ExecuteAlways]
    public class RawImageCircleMaskUpdater : MonoBehaviour
    {
        [SerializeField]
        [FormerlySerializedAs("radius")]
        float _radius;

        [SerializeField]
        [FormerlySerializedAs("feather")]
        float _feather;

        RawImage _imageDisplayer;
        Material _material;

        static readonly int RadiusPropId = Shader.PropertyToID("_Radius");
        static readonly int FeatherPropId = Shader.PropertyToID("_Feather");

        void Awake()
        {
            _imageDisplayer = GetComponent<RawImage>();
            _material = _imageDisplayer.material;

            if(_material == null)
            {
                enabled = false;
            }
        }

        void LateUpdate()
        {
            _material.SetFloat(RadiusPropId, _radius);
            _material.SetFloat(FeatherPropId, _feather);
        }
    }
}
