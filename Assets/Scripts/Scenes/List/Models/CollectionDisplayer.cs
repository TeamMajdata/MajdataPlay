using MajdataPlay.Collections;
using MajdataPlay.Scenes.Setting;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
#nullable enable
namespace MajdataPlay.Scenes.List.Models
{
    /// <summary>Binds a song collection to its authored category title, icon and selection presenter.</summary>
    public class CollectionDisplayer : MonoBehaviour
    {
        /// <summary>Gets the category's authored transform for carousel positioning.</summary>
        public RectTransform RectTransform => _rectTransform;

        /// <summary>Displays online collections and recommendations.</summary>
        [SerializeField]
        [FormerlySerializedAs("onlineCollectionIcon")]
        private Sprite _onlineCollectionIcon = null!;

        /// <summary>Defines the online collection and recommendation icon tint.</summary>
        [SerializeField]
        [FormerlySerializedAs("onlineCollectionIconColor")]
        private Color _onlineCollectionIconColor;

        /// <summary>Displays physical local folders and playlists.</summary>
        [SerializeField]
        [FormerlySerializedAs("folderCollectionIcon")]
        private Sprite _folderCollectionIcon = null!;

        /// <summary>Defines the local folder and playlist icon tint.</summary>
        [SerializeField]
        [FormerlySerializedAs("folderCollectionIconColor")]
        private Color _folderCollectionIconColor;

        /// <summary>Displays the favorite collection.</summary>
        [SerializeField]
        [FormerlySerializedAs("favoriteCollectionIcon")]
        private Sprite _favoriteCollectionIcon = null!;

        /// <summary>Defines the favorite collection icon tint.</summary>
        [SerializeField]
        [FormerlySerializedAs("favoriteCollectionIconColor")]
        private Color _favoriteCollectionIconColor;

        /// <summary>Displays course collections.</summary>
        [SerializeField]
        [FormerlySerializedAs("danCollectionIcon")]
        private Sprite _danCollectionIcon = null!;

        /// <summary>Defines the course collection icon tint.</summary>
        [SerializeField]
        [FormerlySerializedAs("danCollectionIconColor")]
        private Color _danCollectionIconColor;

        /// <summary>References the existing category icon without creating a separate display object.</summary>
        [SerializeField]
        [FormerlySerializedAs("iconDisplayer")]
        private Image _iconDisplayer = null!;

        /// <summary>References the existing category name label.</summary>
        [SerializeField]
        [FormerlySerializedAs("nameDisplayer")]
        private TextMeshProUGUI _nameDisplayer = null!;

        /// <summary>Reuses the settings carousel's moving background and selection presentation.</summary>
        [SerializeField]
        [FormerlySerializedAs("TabDisplayer")]
        private MenuTitleDisplayer _tabDisplayer = null!;

        /// <summary>Caches the authored category transform after initialization.</summary>
        private RectTransform _rectTransform = null!;

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
        }

        /// <summary>Updates the category name, icon and title layout for the bound collection.</summary>
        /// <param name="collection">The collection displayed by this reusable category.</param>
        /// <exception cref="ArgumentNullException"><paramref name="collection"/> is null.</exception>
        internal void SetCollection(SongCollection collection)
        {
            if (collection is null)
            {
                throw new ArgumentNullException(nameof(collection));
            }
            _nameDisplayer.text = collection.Name;
            SetCollectionIcon(collection);
            _tabDisplayer.SetHasIcon(_iconDisplayer.enabled);
            _tabDisplayer.RefreshTitle();
        }

        /// <summary>Selects collection artwork while keeping virtual local aggregates text-only.</summary>
        /// <param name="collection">The collection whose storage kind determines its icon.</param>
        private void SetCollectionIcon(SongCollection collection)
        {
            switch (collection.Type)
            {
                case ChartStorageType.List when collection.IsOnline:
                case ChartStorageType.Recommend:
                    _iconDisplayer.enabled = true;
                    _iconDisplayer.sprite = _onlineCollectionIcon;
                    _iconDisplayer.color = _onlineCollectionIconColor;
                    break;
                case ChartStorageType.List when !collection.IsVirtual:
                case ChartStorageType.PlayList:
                    _iconDisplayer.enabled = true;
                    _iconDisplayer.sprite = _folderCollectionIcon;
                    _iconDisplayer.color = _folderCollectionIconColor;
                    break;
                case ChartStorageType.List:
                    _iconDisplayer.enabled = false;
                    break;
                case ChartStorageType.Dan:
                    _iconDisplayer.enabled = true;
                    _iconDisplayer.sprite = _danCollectionIcon;
                    _iconDisplayer.color = _danCollectionIconColor;
                    break;
                case ChartStorageType.FavoriteList:
                    _iconDisplayer.enabled = true;
                    _iconDisplayer.sprite = _favoriteCollectionIcon;
                    _iconDisplayer.color = _favoriteCollectionIconColor;
                    break;
            }
        }

        /// <summary>Moves this category and blends its selected appearance using the settings carousel.</summary>
        /// <param name="distance">The category index relative to the animated selection cursor.</param>
        internal void SetDistance(float distance)
        {
            _tabDisplayer.SetDistance(distance);
        }

        /// <summary>Shows or hides this reusable category without changing its authored hierarchy.</summary>
        /// <param name="state">Whether the category object should be active.</param>
        public void SetActive(bool state)
        {
            gameObject.SetActive(state);
        }
    }
}
