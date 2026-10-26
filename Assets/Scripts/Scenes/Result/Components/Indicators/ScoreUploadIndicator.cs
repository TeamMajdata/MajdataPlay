using LitMotion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using Unity.VectorGraphics;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace MajdataPlay.Scenes.Result.Components.Indicators
{
    public class ScoreUploadIndicator : MonoBehaviour
    {
        [SerializeField]
        [FormerlySerializedAs("uploadingIconDisplayer")]
        SVGImage _uploadingIconDisplayer;

        [SerializeField]
        [FormerlySerializedAs("successIconDisplayer")]
        SVGImage _successIconDisplayer;

        [SerializeField]
        [FormerlySerializedAs("errorIconDisplayer")]
        SVGImage _errorIconDisplayer;

        [SerializeField]
        [FormerlySerializedAs("textDisplayer")]
        TextMeshProUGUI _textDisplayer;

        MotionHandle _iconAnimHandle;


        public void SetUploading()
        {
            _iconAnimHandle.TryCancel();
            _uploadingIconDisplayer.gameObject.SetActive(true);
            _successIconDisplayer.gameObject.SetActive(false);
            _errorIconDisplayer.gameObject.SetActive(false);
            _iconAnimHandle = LMotion.Create(0f, 1f, 0.8f)
                                     .WithEase(Ease.InOutSine)
                                     .WithLoops(-1, LoopType.Yoyo)
                                     .Bind(x =>
                                     {
                                         _uploadingIconDisplayer.color = new Color(0.3098039f, 0.2470588f, 0.2156863f, x);
                                     });
        }

        void OnDestroy()
        {
            // The looping motion binds to the uploading icon, which is destroyed with this indicator.
            _iconAnimHandle.TryCancel();
        }

        public void SetText(string text)
        {
            _textDisplayer.text = text;
        }
        public void SetSuccess()
        {
            _iconAnimHandle.TryCancel();
            _uploadingIconDisplayer.gameObject.SetActive(false);
            _successIconDisplayer.gameObject.SetActive(true);
            _errorIconDisplayer.gameObject.SetActive(false);
        }
        public void SetError()
        {
            _iconAnimHandle.TryCancel();
            _uploadingIconDisplayer.gameObject.SetActive(false);
            _successIconDisplayer.gameObject.SetActive(false);
            _errorIconDisplayer.gameObject.SetActive(true);
        }
    }
}
