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
    [RequireComponent(typeof(Image))]
    [ExecuteAlways]
    public class ImageCircleMaskUpdater : MonoBehaviour
    {
        [SerializeField]
        [FormerlySerializedAs("radius")]
        float _radius;

        [SerializeField]
        [FormerlySerializedAs("feather")]
        float _feather;

        Image _imageDisplayer;
        Material _material;

        static readonly int RadiusPropId = Shader.PropertyToID("_Radius");
        static readonly int FeatherPropId = Shader.PropertyToID("_Feather");

        void Awake()
        {
            _imageDisplayer = GetComponent<Image>();
            _material = _imageDisplayer.material;

            if (_material == null)
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

