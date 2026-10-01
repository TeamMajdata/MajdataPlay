using System.Collections;
using Cysharp.Threading.Tasks;
using MajdataPlay.Scenes.Game.Notes.Skins;
using MajdataPlay.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace MajdataPlay
{
    public class LoadSubBg : MajBehaviour
    {
        Image _img;
        protected override void Awake()
        {
            base.Awake();
            _img = GetComponent<Image>();
            _img.sprite = null;
            _img.color = Color.black;
            _img.useSpriteMesh = true;
            WaitSkinLoadedAsync().Forget();
        }
        void OnDestroy()
        {
            var skinManager = MajInstances.SkinManager;
            if (skinManager is not null)
            {
                skinManager.OnSkinChanged -= OnSkinChanged;
            }
        }
        async UniTask WaitSkinLoadedAsync()
        {
            while (MajInstances.SkinManager?.IsInited != true)
            {
                await UniTask.Yield();
            }
            _img.sprite = MajInstances.SkinManager.SelectedSkin.SubDisplay;
            _img.color = Color.white;
            MajInstances.SkinManager.OnSkinChanged += OnSkinChanged;
        }
        void OnSkinChanged(SkinManager sender, CustomSkin newSkin)
        {
            _img.sprite = MajInstances.SkinManager.SelectedSkin.SubDisplay;
            _img.color = Color.white;
        }
    }
}
