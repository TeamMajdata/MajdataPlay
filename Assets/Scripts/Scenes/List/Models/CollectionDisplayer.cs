using MajdataPlay.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
#nullable enable
namespace MajdataPlay.Scenes.List.Models
{
    public class CollectionDisplayer : MonoBehaviour
    {
        public RectTransform RectTransform
        {
            get => _rectTransform;
        }

        [SerializeField]
        [FormerlySerializedAs("onlineCollectionIcon")]
        Sprite _onlineCollectionIcon;

        [SerializeField]
        [FormerlySerializedAs("onlineCollectionIconColor")]
        Color _onlineCollectionIconColor;

        [SerializeField]
        [FormerlySerializedAs("folderCollectionIcon")]
        Sprite _folderCollectionIcon;

        [SerializeField]
        [FormerlySerializedAs("folderCollectionIconColor")]
        Color _folderCollectionIconColor;

        [SerializeField]
        [FormerlySerializedAs("favoriteCollectionIcon")]
        Sprite _favoriteCollectionIcon;

        [SerializeField]
        [FormerlySerializedAs("favoriteCollectionIconColor")]
        Color _favoriteCollectionIconColor;

        [SerializeField]
        [FormerlySerializedAs("onlineFavoriteCollectionIcon")]
        Sprite _onlineFavoriteCollectionIcon;

        [SerializeField]
        [FormerlySerializedAs("onlineFavoriteCollectionIconColor")]
        Color _onlineFavoriteCollectionIconColor;

        [SerializeField]
        [FormerlySerializedAs("danCollectionIcon")]
        Sprite _danCollectionIcon;

        [SerializeField]
        [FormerlySerializedAs("danCollectionIconColor")]
        Color _danCollectionIconColor;

        [SerializeField]
        [FormerlySerializedAs("iconDisplayer")]
        Image _iconDisplayer;

        [SerializeField]
        [FormerlySerializedAs("nameDisplayer")]
        TextMeshProUGUI _nameDisplayer;

        RectTransform _rectTransform;
        RectTransform _nameRectTransform;
        SongCollection? _bindingCollection;

        void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            _nameRectTransform = _nameDisplayer.GetComponent<RectTransform>();
        }

        internal void SetCollection(SongCollection collection)
        {
            if(collection is null)
            {
                throw new ArgumentNullException(nameof(collection));
            }
            _bindingCollection = collection;

            _nameDisplayer.text = collection.Name;
            _nameRectTransform.anchoredPosition = new Vector2(0, -25);
            SetCollectionIcon(collection);
        }

        void SetCollectionIcon(SongCollection collection)
        {
            switch (collection.Type)
            {
                case ChartStorageType.List:
                    if (collection.IsOnline)
                    {
                        _iconDisplayer.enabled = true;
                        _iconDisplayer.sprite = _onlineCollectionIcon;
                        _iconDisplayer.color = _onlineCollectionIconColor;
                    }
                    else
                    {
                        _iconDisplayer.enabled = false;
                        _nameRectTransform.anchoredPosition = Vector2.zero;
                    }
                    break;
                case ChartStorageType.Recommend:
                    _iconDisplayer.enabled = true;
                    _iconDisplayer.sprite = _onlineCollectionIcon;
                    _iconDisplayer.color = _onlineCollectionIconColor;
                    break;
                case ChartStorageType.Dan:
                    _iconDisplayer.enabled = true;
                    _iconDisplayer.sprite = _danCollectionIcon;
                    _iconDisplayer.color = _danCollectionIconColor;
                    break;
                case ChartStorageType.PlayList:
                    _iconDisplayer.enabled = true;
                    _iconDisplayer.sprite = _folderCollectionIcon;
                    _iconDisplayer.color = _folderCollectionIconColor;
                    break;
                case ChartStorageType.FavoriteList:
                    _iconDisplayer.enabled = true;
                    _iconDisplayer.sprite = _favoriteCollectionIcon;
                    _iconDisplayer.color = _favoriteCollectionIconColor;
                    break;
            }
        }

        public void SetActive(bool state)
        {
            gameObject.SetActive(state);
        }
    }
}
