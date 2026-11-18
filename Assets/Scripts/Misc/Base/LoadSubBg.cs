using Cysharp.Threading.Tasks;
using MajdataPlay.Scenes.Game.Notes.Skins;
using MajdataPlay.Utils;
using UnityEngine;
using UnityEngine.UI;

#nullable enable

namespace MajdataPlay
{
    /// <summary>Applies the selected skin's sub-display artwork and tint without changing the tint alpha.</summary>
    /// <remarks>
    /// Both tint writes preserve alpha. A scene transition overlay can share this image and own that alpha,
    /// so writing it here would reopen a cover that the transition has already faded out.
    /// </remarks>
    public class LoadSubBg : MajBehaviour
    {
        /// <summary>The sub-display image whose sprite and tint this behaviour applies.</summary>
        private Image _img = null!;

        protected override void Awake()
        {
            base.Awake();
            _img = GetComponent<Image>();
            _img.sprite = null!;
            // 只压黑 RGB：alpha 属于转场遮盖的所有者，在这里写死会重新打开已经淡出的遮盖。
            var tint = _img.color;
            _img.color = new Color(0f, 0f, 0f, tint.a);
            _img.useSpriteMesh = true;
            WaitSkinLoadedAsync().Forget();
        }

        private void OnDestroy()
        {
            var skinManager = MajInstances.SkinManager;
            if (skinManager is not null)
            {
                skinManager.OnSkinChanged -= OnSkinChanged;
            }
        }

        /// <summary>Waits for skin initialization, then applies the artwork and its tint.</summary>
        /// <returns>A task that completes after the artwork has been applied.</returns>
        private async UniTask WaitSkinLoadedAsync()
        {
            var cancellationToken = this.GetCancellationTokenOnDestroy();
            while (MajInstances.SkinManager?.IsInited != true)
            {
                await UniTask.Yield(cancellationToken);
            }
            ApplySkin(MajInstances.SkinManager.SelectedSkin.SubDisplay);
            MajInstances.SkinManager.OnSkinChanged += OnSkinChanged;
        }

        /// <summary>Applies a sub-display sprite and immediately tints it white while preserving its alpha.</summary>
        /// <param name="sprite">The selected skin's sub-display artwork, or null when the skin provides none.</param>
        private void ApplySkin(Sprite? sprite)
        {
            _img.sprite = sprite!;
            var tint = _img.color;
            _img.color = new Color(1f, 1f, 1f, tint.a);
        }

        /// <summary>Re-applies the artwork when the player selects a different skin.</summary>
        /// <param name="sender">The skin manager that raised the change.</param>
        /// <param name="newSkin">The skin that is now selected.</param>
        private void OnSkinChanged(SkinManager sender, CustomSkin newSkin)
        {
            ApplySkin(newSkin.SubDisplay);
        }
    }
}
