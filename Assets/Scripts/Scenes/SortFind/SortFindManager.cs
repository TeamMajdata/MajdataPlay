using MajdataPlay.Editor;
using MajdataPlay.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
#nullable enable

namespace MajdataPlay.Scenes.SortFind
{
    /// <summary>Edits a pending search and sort choice, applying it only when explicitly confirmed.</summary>
    public class SortFindManager : MonoBehaviour
    {
        /// <summary>Displays the pending sort order without changing stored selection.</summary>
        [SerializeField]
        [FormerlySerializedAs("SlotText")]
        private TextMeshProUGUI _slotText = null!;

        /// <summary>Edits the pending keyword and provides the native desktop submit action.</summary>
        [SerializeField]
        [FormerlySerializedAs("SearchBar")]
        private TMP_InputField _searchBar = null!;

        /// <summary>Shows the existing clear affordance only while the pending keyword is nonempty.</summary>
        [SerializeField]
        [FormerlySerializedAs("ClearButton")]
        private GameObject _clearButton = null!;

        /// <summary>Retains the pending sort choice until confirmation.</summary>
        [SerializeField, ReadOnlyField]
        [FormerlySerializedAs("SortType")]
        private SortType _sortType;

        /// <summary>Prevents duplicate navigation or submission after either exit action.</summary>
        private bool _isExited;

        /// <summary>Tracks the pending sort choice in the existing six-item order.</summary>
        private int _selectIndex = (int)SortType.Default;

        /// <summary>Retains the existing sort-option count and enum mapping.</summary>
        private readonly SortType[] _slots = new SortType[6];

        /// <summary>Maps sort choices to their existing localized labels.</summary>
        private readonly string[] _slotTexts = new string[6]
        {
            "MAJTEXT_SORTFIND_SortbyDefault",
            "MAJTEXT_SORTFIND_SortbyTime",
            "MAJTEXT_SORTFIND_SortbyDiff",
            "MAJTEXT_SORTFIND_SortbyDes",
            "MAJTEXT_SORTFIND_SortbyTitle",
            "MAJTEXT_SORTFIND_SortbyRank"
        };

        private void Awake()
        {
            InputManager.TouchButtonRingEdge = 4.8f;
        }

        private void Start()
        {
            for (var i = 0; i < _slots.Length; i++)
            {
                _slots[i] = (SortType)i;
            }
            CabinetLed.SetAllLight(Color.black);
            CabinetLed.SetButtonLight(Color.green, 3);
            CabinetLed.SetButtonLight(Color.red, 4);
            _searchBar.text = SongStorage.OrderBy.Keyword;
            _searchBar.onValueChanged.AddListener(OnKeywordChanged);
            _clearButton.SetActive(!string.IsNullOrEmpty(_searchBar.text));
            _searchBar.onSubmit.AddListener(OnSearchSubmitted);
            _selectIndex = (int)SongStorage.OrderBy.SortBy;
            SetActiveSort((SortType)_selectIndex);
            Focus(_searchBar);
        }

        private void Update()
        {
            if (_isExited)
            {
                return;
            }
            if (InputManager.IsButtonClickCompletedInThisFrame(ButtonZone.A5) ||
                InputManager.IsSensorClickCompletedInThisFrame(SensorArea.A5))
            {
                Exit(false);
                return;
            }
            if (InputManager.IsButtonClickCompletedInThisFrame(ButtonZone.A4) ||
                InputManager.IsSensorClickCompletedInThisFrame(SensorArea.A4))
            {
                SortAndExit();
                return;
            }
            if (InputManager.IsSensorPressedInThisFrame(SensorArea.E6) ||
                InputManager.IsSensorPressedInThisFrame(SensorArea.B5))
            {
                _selectIndex--;
                if (_selectIndex < 0)
                {
                    _selectIndex = _slots.Length - 1;
                }
                SetActiveSort((SortType)_selectIndex);
            }
            else if (InputManager.IsSensorPressedInThisFrame(SensorArea.B4) ||
                     InputManager.IsSensorPressedInThisFrame(SensorArea.E4))
            {
                _selectIndex++;
                if (_selectIndex > _slots.Length - 1)
                {
                    _selectIndex = 0;
                }
                SetActiveSort((SortType)_selectIndex);
            }

            if (InputManager.IsSensorPressedInThisFrame(SensorArea.E3))
            {
                _searchBar.text = string.Empty;
            }
            else if (InputManager.IsSensorPressedInThisFrame(SensorArea.E7) ||
                     InputManager.IsSensorPressedInThisFrame(SensorArea.B7) ||
                     InputManager.IsSensorPressedInThisFrame(SensorArea.C) ||
                     InputManager.IsSensorPressedInThisFrame(SensorArea.B2))
            {
                Focus(_searchBar);
            }
        }

        private void OnDestroy()
        {
            InputManager.TouchButtonRingEdge = 5.4f;
            if (_searchBar != null)
            {
                _searchBar.onValueChanged.RemoveListener(OnKeywordChanged);
                _searchBar.onSubmit.RemoveListener(OnSearchSubmitted);
            }
        }

        /// <summary>Updates the authored clear affordance when the pending keyword changes.</summary>
        /// <param name="keyword">The text currently shown by the input field.</param>
        private void OnKeywordChanged(string keyword)
        {
            _clearButton.SetActive(!string.IsNullOrEmpty(keyword));
        }

        /// <summary>Preserves native input-field submission, including desktop Enter, as an explicit confirmation.</summary>
        /// <param name="keyword">The text submitted by the input field; the live field remains the authoritative edit.</param>
        private void OnSearchSubmitted(string keyword)
        {
            SortAndExit();
        }

        /// <summary>Focuses an existing input field only at initialization or after an explicit focus gesture.</summary>
        /// <param name="inputField">The authored search field whose caret should move to the end.</param>
        private static void Focus(TMP_InputField inputField)
        {
            if (!inputField.interactable || inputField.readOnly || inputField.isFocused)
            {
                return;
            }
            inputField.Select();
            inputField.ActivateInputField();
            inputField.MoveTextEnd(false);
        }

        /// <summary>Displays a pending sort choice without writing the shared collection order.</summary>
        /// <param name="sortType">The pending sort choice selected by the arrow controls.</param>
        private void SetActiveSort(SortType sortType)
        {
            _slotText.text = _slotTexts[(int)sortType].i18n();
            _sortType = sortType;
        }

        /// <summary>Confirms the pending keyword and sort choice once before returning to the collection list.</summary>
        private void SortAndExit()
        {
            Exit(true);
        }

        /// <summary>Returns to the list, committing edits only for the confirm action.</summary>
        /// <param name="applyOrder">Whether the pending keyword and sort choice should replace the stored values.</param>
        private void Exit(bool applyOrder)
        {
            if (_isExited)
            {
                return;
            }
            _isExited = true;
            if (applyOrder)
            {
                var orderBy = SongStorage.OrderBy;
                orderBy.Keyword = _searchBar.text;
                orderBy.SortBy = _sortType;
            }
            MajInstances.SceneSwitcher.SwitchScene("List", false);
        }
    }
}
