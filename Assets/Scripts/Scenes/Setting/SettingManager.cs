using Cysharp.Threading.Tasks;
using LitMotion;
using MajdataPlay.Collections;
using MajdataPlay.i18n;
using MajdataPlay.IO;
using MajdataPlay.Settings;
using MajdataPlay.Settings.Runtime;
using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

namespace MajdataPlay.Scenes.Setting
{
    public class SettingManager : MonoBehaviour
    {
        private const float MenuTitleVisibleDistance = 3.5f;

        // Metadata survives scene changes, but never retains a GameSetting/ChartSetting instance.
        private static readonly Dictionary<Type, MenuMetadata[]> s_menuMetadataCache = new();

        public int Index { get; private set; } = 0;
        public GameSetting Setting => MajEnv.Settings;

        [SerializeField]
        [FormerlySerializedAs("menuPrefab")]
        private GameObject _menuPrefab;

        [SerializeField]
        [FormerlySerializedAs("menuTitleDisplayerPrefab")]
        private MenuTitleDisplayer _menuTitleDisplayerPrefab;

        [SerializeField]
        [FormerlySerializedAs("menuListRoot")]
        private GameObject _menuListRoot;

        [SerializeField]
        [FormerlySerializedAs("menuTitleDisplayerListRoot")]
        private GameObject _menuTitleDisplayerListRoot;

        [SerializeField]
        [FormerlySerializedAs("descriptionText")]
        private TextMeshProUGUI _descriptionTextDisplayer;

        private Menu[] _menus = Array.Empty<Menu>();
        // Whether each menu is listed; its title cursor counts listed menus only.
        private bool[] _isMenuDisplayed = Array.Empty<bool>();
        private MenuTitleDisplayer[] _menuTitleDisplayers = Array.Empty<MenuTitleDisplayer>();

        private float _listCursorPos = 0;
        private int _listCursorTarget = 0;
        private MotionHandle _menuTitleDisplayerAnim;
        private readonly Stack<Option> _optionPool = new(Menu.OPTION_POOL_CAPACITY);
        private readonly HashSet<TMP_FontAsset> _optionFonts = new();
        private Transform _optionPoolRoot = null!;
        private string _descriptionText = null!;

        private bool _isExited = false;
        private bool _isInited = false;
        private OffsetUnitOption _lastOffsetUnit;
        private InputRepeatState _categoryInput;
        private InputRepeatState _optionInput;
        private bool _advancedInputHeld;

        private readonly SettingConfig _settingConfig = MajEnv.RuntimeConfig?.Setting ?? new();
        private void Awake()
        {
            InputManager.TouchButtonRingEdge = 4.8f;
            _lastOffsetUnit = Setting.Debug.OffsetUnit;
            EnsureNestedCanvas(_menuTitleDisplayerListRoot);
            EnsureNestedCanvas(_descriptionTextDisplayer.gameObject);
        }
        private void Start()
        {
            var categories = GetMenuMetadata(Setting.GetType());
            var offset = 0;
            var listRoot = _menuListRoot.transform;
            if (!_settingConfig.IgnoreChartSettingPage && !string.IsNullOrEmpty(MajEnv.RuntimeConfig.List.SelectedSongHash))
            {
                _menus = new Menu[categories.Length + 1];
                offset = 1;
                var chartSetting = ChartSettingStorage.GetSetting(MajEnv.RuntimeConfig.List.SelectedSongHash);
                var chartSettingType = chartSetting.GetType();
                var menuObj = Instantiate(_menuPrefab, listRoot);
                menuObj.SetActive(false);
                menuObj.name = chartSettingType.Name;
                var menu = menuObj.GetComponent<Menu>();
                _menus[0] = menu;
                menu.Instance = chartSetting;
                menu.Name = chartSettingType.Name;
                menu.TitleTranslation = SettingReflectionCache.GetMenuTranslation(null, chartSettingType.Name);
            }
            else
            {
                _menus = new Menu[categories.Length];
            }
            foreach (var (i, category) in categories.WithIndex())
            {
                var menuObj = Instantiate(_menuPrefab, listRoot);
                menuObj.SetActive(false);
                menuObj.name = category.Name;
                var menu = menuObj.GetComponent<Menu>();
                _menus[i + offset] = menu;
                menu.Instance = category.GetInstance(Setting);
                menu.Name = category.Name;
                menu.TitleTranslation = category.TitleTranslation;
                menu.Visibility = category.Visibility;
            }
            _isMenuDisplayed = new bool[_menus.Length];
            // Build scene-local enumerators before the fade opens and before any navigation.
            // Only immutable reflection metadata is shared with subsequent Setting visits.
            foreach (var menu in _menus)
            {
                menu.Init();
            }
            RestoreSelectedMenu();
            _menus[Index].ToOption(_settingConfig.SelectedOption);
            _menus[Index].gameObject.SetActive(true);

            MajInstances.AudioManager.PlaySFX("settings.wav");

            _menuTitleDisplayers = new MenuTitleDisplayer[_menus.Length];
            var menuTitleDisplayerListRoot = _menuTitleDisplayerListRoot.transform;
            for (var i = 0; i < _menus.Length; i++)
            {
                var menu = _menus[i];
                var displayer = Instantiate(_menuTitleDisplayerPrefab, menuTitleDisplayerListRoot);
                displayer.Initialize(menu.GetLocalizedTitle);
                _menuTitleDisplayers[i] = displayer;
            }
            WarmupTextFonts();
            Localization.OnLanguageChanged += OnLanguageChanged;
            UpdateMenuTitleDisplayerPosition();
            InitializeCurrentMenu().Forget();
        }
        private void OnLanguageChanged(object sender, Language language)
        {
            // Complete glyph/feature discovery before TMP reaches Canvas PreRender.
            WarmupTextFonts();
        }
        private void WarmupTextFonts()
        {
            var warmup = new SettingFontWarmup();
            var descriptionFont = _descriptionTextDisplayer.font;
            foreach (var font in _optionFonts)
            {
                // Numeric values may change without visiting a new enum label.
                warmup.AddText(font, "0123456789.,+-:%/()eE NaNInfinity∞");
            }
            foreach (var menu in _menus)
            {
                menu.CollectWarmupText(warmup, _optionFonts, descriptionFont);
            }
            for (var i = 0; i < _menuTitleDisplayers.Length; i++)
            {
                warmup.AddText(_menuTitleDisplayers[i].Font, _menus[i].GetLocalizedTitle());
            }
            warmup.AddText(descriptionFont, $"MAJTEXT_SETTING_OFFSETUNIT_{OffsetUnitOption.Second}".i18n());
            warmup.AddText(descriptionFont, $"MAJTEXT_SETTING_OFFSETUNIT_{OffsetUnitOption.Frame}".i18n());
            warmup.Warmup();
        }
        private static MenuMetadata[] GetMenuMetadata(Type settingType)
        {
            if (s_menuMetadataCache.TryGetValue(settingType, out var cached))
            {
                return cached;
            }

            var properties = SettingReflectionCache.GetVisibleProperties(settingType);
            var categories = new MenuMetadata[properties.Length];
            for (var i = 0; i < properties.Length; i++)
            {
                var property = properties[i];
                var valueProperty = property.Name == nameof(GameSetting.Audio)
                    ? SettingReflectionCache.GetProperty(property.PropertyType, "Volume")
                    : property;
                categories[i] = new MenuMetadata(property, valueProperty);
                SettingReflectionCache.GetVisibleProperties(valueProperty.PropertyType);
            }
            // Include chart options even if the first visit has no selected chart.
            SettingReflectionCache.GetVisibleProperties(typeof(ChartSetting));
            s_menuMetadataCache.Add(settingType, categories);
            return categories;
        }

        private sealed class MenuMetadata
        {
            private readonly PropertyInfo _categoryProperty;
            private readonly PropertyInfo _valueProperty;
            internal string Name => _valueProperty.Name;
            // The translation attributes live on the root category property; {0} is the displayed menu name.
            internal SettingTranslation TitleTranslation { get; }
            // Like the translation attributes, the visibility conditions live on the root category property.
            internal SettingVisibility Visibility { get; }

            internal MenuMetadata(PropertyInfo categoryProperty, PropertyInfo valueProperty)
            {
                _categoryProperty = categoryProperty;
                _valueProperty = valueProperty;
                TitleTranslation = SettingReflectionCache.GetMenuTranslation(categoryProperty, valueProperty.Name);
                Visibility = SettingReflectionCache.GetVisibility(categoryProperty);
            }

            internal object GetInstance(GameSetting setting)
            {
                var root = _categoryProperty == _valueProperty
                    ? (object)setting
                    : _categoryProperty.GetValue(setting);
                return _valueProperty.GetValue(root);
            }
        }
        private void Update()
        {
            if (_isExited || !_isInited)
            {
                return;
            }
            var currentMenu = _menus[Index];
            var isCalibratorRequested = InputManager.IsButtonClickCompletedInThisFrame(ButtonZone.A1) ||
                                        InputManager.IsSensorClickCompletedInThisFrame(SensorArea.A1);
            if (isCalibratorRequested)
            {
                currentMenu.ResetRenderingAdvancedInput();
                MajInstances.AudioManager.ReadVolumeFromSettings();
                _isExited = true;
                MajInstances.SceneSwitcher.SwitchScene("Calibrator");
                return;
            }
            var advancedPressedThisFrame = InputManager.IsButtonPressedInThisFrame(ButtonZone.A8) ||
                                           InputManager.IsSensorPressedInThisFrame(SensorArea.A8);
            var advancedHeld = InputManager.CheckButtonStatusInThisFrame(ButtonZone.A8, SwitchStatus.On) ||
                               InputManager.CheckSensorStatusInThisFrame(SensorArea.A8, SwitchStatus.On);
            // ClickCompleted is a delayed animation notification with one pending slot; rapid
            // presses can be merged there. The hidden gesture counts immediate press edges.
            // Keep one latch across contexts until both A8 channels release, so a delayed
            // second channel or a held key while changing rows cannot count the same press twice.
            var isAdvancedClicked = advancedPressedThisFrame && !_advancedInputHeld;
            _advancedInputHeld = advancedHeld;
            var isExitRequested = InputManager.IsButtonClickCompletedInThisFrame(ButtonZone.A4) ||
                                  InputManager.IsButtonClickCompletedInThisFrame(ButtonZone.A5);
            // Calibration / exit retain their delayed completed-click actions, but a held
            // navigation control must interrupt the consecutive A8 gesture immediately.
            var sceneNavigationHeld =
                InputManager.CheckButtonStatusInThisFrame(ButtonZone.A1, SwitchStatus.On) ||
                InputManager.CheckSensorStatusInThisFrame(SensorArea.A1, SwitchStatus.On) ||
                InputManager.CheckButtonStatusInThisFrame(ButtonZone.A4, SwitchStatus.On) ||
                InputManager.CheckButtonStatusInThisFrame(ButtonZone.A5, SwitchStatus.On);
            var nextCategoryPressed = InputManager.CheckButtonStatusInThisFrame(ButtonZone.A2, SwitchStatus.On);
            var previousCategoryPressed = InputManager.CheckButtonStatusInThisFrame(ButtonZone.A7, SwitchStatus.On);
            var nextOptionPressed = InputManager.CheckButtonStatusInThisFrame(ButtonZone.A3, SwitchStatus.On);
            var previousOptionPressed = InputManager.CheckButtonStatusInThisFrame(ButtonZone.A6, SwitchStatus.On);
            var optionValuePressed =
                InputManager.CheckSensorStatusInThisFrame(SensorArea.E4, SwitchStatus.On) ||
                InputManager.CheckSensorStatusInThisFrame(SensorArea.B4, SwitchStatus.On) ||
                InputManager.CheckSensorStatusInThisFrame(SensorArea.B3, SwitchStatus.On) ||
                InputManager.CheckSensorStatusInThisFrame(SensorArea.E6, SwitchStatus.On) ||
                InputManager.CheckSensorStatusInThisFrame(SensorArea.B5, SwitchStatus.On) ||
                InputManager.CheckSensorStatusInThisFrame(SensorArea.B6, SwitchStatus.On);
            var interruptsAdvancedInput = isExitRequested || sceneNavigationHeld || nextCategoryPressed || previousCategoryPressed ||
                nextOptionPressed || previousOptionPressed || optionValuePressed;
            if (interruptsAdvancedInput)
            {
                currentMenu.ResetRenderingAdvancedInput();
            }
            // Navigation / edits discard a simultaneous press rather than postponing it.
            // Unrelated release events do not reset the counter or unlock the A8 latch.
            // A1 keeps its original completed-click calibrator behavior in every context.
            currentMenu.TryHandleRenderingAdvancedInput(isAdvancedClicked && !interruptsAdvancedInput);
            if (isExitRequested)
            {
                MajInstances.AudioManager.ReadVolumeFromSettings();
                _isExited = true;
                if (MajEnv.Mode == RunningMode.View)
                {
                    MajInstances.SceneSwitcher.SwitchScene("View");
                }
                else
                {
                    MajInstances.SceneSwitcher.SwitchScene("List", false);
                }
                return;
            }
            currentMenu.HandleInput();
            if (UpdateOffsetUnitIfNeeded())
            {
                currentMenu.RefreshVisibleEnumerators();
            }
            // An edited option can list or unlist other menus; update the list before navigating it.
            if (UpdateMenuVisibility())
            {
                _menuTitleDisplayerAnim.TryCancel();
                NormalizeMenuTitleCursor();
            }
            if (nextCategoryPressed || previousCategoryPressed)
            {
                if (_categoryInput.Update(
                    nextCategoryPressed,
                    previousCategoryPressed,
                    MajTimeline.DeltaTime,
                    0.7f,
                    0.2f,
                    out var direction))
                {
                    SwitchCategory(direction);
                }
                _optionInput.SuppressUntilRelease();
                return;
            }
            _categoryInput.Reset();

            if (nextOptionPressed || previousOptionPressed)
            {
                if (_optionInput.Update(
                    nextOptionPressed,
                    previousOptionPressed,
                    MajTimeline.DeltaTime,
                    0.7f,
                    0.2f,
                    out var direction))
                {
                    SwitchOption(direction);
                }
                return;
            }
            _optionInput.Reset();
        }

        private void SwitchOption(int direction)
        {
            _menus[Index].SwitchOption(direction);
        }

        private void SwitchCategory(int direction)
        {
            if (direction > 0)
            {
                NextMenu();
            }
            else
            {
                PreviousMenu();
            }
        }

        private async UniTaskVoid InitializeCurrentMenu()
        {
            await UniTask.DelayFrame(3);
            MajInstances.SceneSwitcher.FadeOut();
            SetSettingLights();
            _categoryInput.SuppressUntilRelease();
            _optionInput.SuppressUntilRelease();
            _isInited = true;
        }

        private void SetSettingLights()
        {
            CabinetLed.SetAllLight(Color.white);
            CabinetLed.SetButtonLight(Color.green, 3);
            CabinetLed.SetButtonLight(Color.red, 4);
            CabinetLed.SetButtonLight(Color.blue, 2);
            CabinetLed.SetButtonLight(Color.blue, 5);
            CabinetLed.SetButtonLight(Color.blue, 0);
            CabinetLed.SetButtonLight(Color.blue, 7);
        }

        private void RestoreSelectedMenu()
        {
            var index = Array.FindIndex(_menus, x => x.Name == _settingConfig.SelectedMenu);
            if (index == -1 || !_menus[index].Visibility.IsVisible(Setting))
            {
                index = Array.FindIndex(_menus, x => x.Name == nameof(GameSetting.Game));
                _settingConfig.SelectedOption = string.Empty;
            }

            Index = index;
            UpdateMenuVisibility();
            var position = GetDisplayedMenuPosition(index);
            _listCursorPos = position;
            _listCursorTarget = position;
        }

        internal void PreviousMenu()
        {
            MoveMenu(-1);
        }
        internal void NextMenu()
        {
            MoveMenu(1);
        }
        private void MoveMenu(int direction)
        {
            if (_menus.Length == 0)
            {
                return;
            }

            // Skip unlisted menus; a full cycle ends back on the current menu, which is always listed.
            var targetIndex = Index;
            var crossesBoundary = false;
            for (var step = 0; step < _menus.Length; step++)
            {
                targetIndex += direction;
                if (targetIndex < 0)
                {
                    targetIndex = _menus.Length - 1;
                    crossesBoundary = true;
                }
                else if (targetIndex >= _menus.Length)
                {
                    targetIndex = 0;
                    crossesBoundary = true;
                }
                if (_isMenuDisplayed[targetIndex])
                {
                    break;
                }
            }

            Index = targetIndex;
            _settingConfig.SelectedMenu = _menus[Index].Name;
            // The previous menu leaves the list here when only being selected kept it listed;
            // the title positions then shift, so they snap instead of animating.
            var isListChanged = UpdateMenuVisibility();
            _listCursorTarget = GetDisplayedMenuPosition(Index);
            UpdateMenu(direction, !crossesBoundary && !isListChanged);
        }
        public void SetDescriptionText(string text)
        {
            if (_descriptionText == text)
            {
                return;
            }
            _descriptionText = text;
            _descriptionTextDisplayer.text = text.Replace('\n', ',');
        }
        internal void InitializeOptionPool(GameObject prefab)
        {
            if (_optionPoolRoot != null)
            {
                return;
            }

            var poolRoot = new GameObject("Option Pool", typeof(RectTransform));
            _optionPoolRoot = poolRoot.transform;
            _optionPoolRoot.SetParent(_menuListRoot.transform, false);
            poolRoot.SetActive(false);
            for (var i = 0; i < Menu.OPTION_POOL_CAPACITY; i++)
            {
                var optionObject = Instantiate(prefab, _optionPoolRoot);
                optionObject.SetActive(false);
                var option = optionObject.GetComponent<Option>();
                option.CollectFontAssets(_optionFonts);
                _optionPool.Push(option);
            }
        }
        internal Option RentOption(Transform parent)
        {
            var option = _optionPool.Pop();
            option.transform.SetParent(parent, false);
            return option;
        }
        internal void ReturnOption(Option option)
        {
            option.Unbind();
            option.gameObject.SetActive(false);
            // OnDisable can run while Unity is deactivating the parent hierarchy.
            // Reparent only on rent, after that transition has finished.
            _optionPool.Push(option);
        }
        private void UpdateMenu(int direction, bool animateTitle = true)
        {
            // Return the old category's views before the new category rents them.
            foreach (var (i, menu) in _menus.WithIndex())
            {
                if (i != Index)
                {
                    menu.gameObject.SetActive(false);
                }
            }
            var currentMenu = _menus[Index];
            currentMenu.Init();
            if (direction > 0)
            {
                currentMenu.ToHead();
            }
            else
            {
                currentMenu.ToTail();
            }
            currentMenu.gameObject.SetActive(true);
            _menuTitleDisplayerAnim.TryCancel();
            if (!animateTitle)
            {
                NormalizeMenuTitleCursor();
                return;
            }
            _menuTitleDisplayerAnim = LMotion.Create(_listCursorPos, _listCursorTarget, 0.25f)
                                             .WithEase(Ease.OutQuad)
                                             .WithOnComplete(NormalizeMenuTitleCursor)
                                             .Bind(x =>
                                             {
                                                 _listCursorPos = x;
                                                 UpdateMenuTitleDisplayerPosition();
                                             });
        }
        private void UpdateMenuTitleDisplayerPosition()
        {
            var position = 0;
            for (var i = 0; i < _menuTitleDisplayers.Length; i++)
            {
                var displayer = _menuTitleDisplayers[i];
                if (!_isMenuDisplayed[i])
                {
                    displayer.SetVisible(false);
                    continue;
                }
                var distance = position - _listCursorPos;
                position++;
                var isVisible = Mathf.Abs(distance) <= MenuTitleVisibleDistance;
                displayer.SetVisible(isVisible);
                if (isVisible)
                {
                    displayer.SetDistance(distance);
                }
            }
        }
        private void NormalizeMenuTitleCursor()
        {
            var position = GetDisplayedMenuPosition(Index);
            _listCursorPos = position;
            _listCursorTarget = position;
            UpdateMenuTitleDisplayerPosition();
        }
        /// <summary>
        /// Re-evaluates which menus the category list shows from their <see cref="ShowInSettingUIWhenAttribute"/> conditions.
        /// </summary>
        /// <remarks>
        /// The selected menu is always listed, so a menu never vanishes while it is being edited and the list is never empty.
        /// Menus without conditions are not read, so the check is free until a menu declares one.
        /// </remarks>
        /// <returns>True when a menu was listed or unlisted since the last evaluation.</returns>
        private bool UpdateMenuVisibility()
        {
            var hasChanged = false;
            for (var i = 0; i < _menus.Length; i++)
            {
                var isDisplayed = i == Index || _menus[i].Visibility.IsVisible(Setting);
                if (_isMenuDisplayed[i] != isDisplayed)
                {
                    _isMenuDisplayed[i] = isDisplayed;
                    hasChanged = true;
                }
            }
            return hasChanged;
        }
        /// <summary>
        /// Gets the position of a menu in the category list, which counts listed menus only.
        /// </summary>
        /// <param name="menuIndex">The index of the menu in the full menu array.</param>
        /// <returns>The number of listed menus before the menu.</returns>
        private int GetDisplayedMenuPosition(int menuIndex)
        {
            var position = 0;
            for (var i = 0; i < menuIndex; i++)
            {
                if (_isMenuDisplayed[i])
                {
                    position++;
                }
            }
            return position;
        }
        private bool UpdateOffsetUnitIfNeeded()
        {
            var currentOffsetUnit = Setting.Debug.OffsetUnit;
            if (currentOffsetUnit == _lastOffsetUnit)
            {
                return false;
            }

            var convertToSecond = currentOffsetUnit == OffsetUnitOption.Second;
            Setting.Game.SlideFadeInOffset = ConvertOffsetUnit(Setting.Game.SlideFadeInOffset, convertToSecond);
            Setting.Judge.AudioOffset = ConvertOffsetUnit(Setting.Judge.AudioOffset, convertToSecond);
            Setting.Judge.JudgeOffset = ConvertOffsetUnit(Setting.Judge.JudgeOffset, convertToSecond);
            Setting.Judge.AnswerOffset = ConvertOffsetUnit(Setting.Judge.AnswerOffset, convertToSecond);
            Setting.Judge.TouchPanelOffset = ConvertOffsetUnit(Setting.Judge.TouchPanelOffset, convertToSecond);
            Setting.Debug.DisplayOffset = ConvertOffsetUnit(Setting.Debug.DisplayOffset, convertToSecond);

            if (convertToSecond)
            {
                ChartSettingStorage.ConvertUnitToSecond();
            }
            else
            {
                ChartSettingStorage.ConvertUnitToFrame();
            }
            _lastOffsetUnit = currentOffsetUnit;
            return true;
        }
        private static float ConvertOffsetUnit(float value, bool convertToSecond)
        {
            var decimalValue = Convert.ToDecimal(value);
            var convertedValue = convertToSecond
                ? Math.Round((decimal)MajEnv.FRAME_LENGTH_SEC * decimalValue, 3)
                : Math.Round(decimalValue / (decimal)MajEnv.FRAME_LENGTH_SEC, 1);
            return Convert.ToSingle(convertedValue);
        }
        private static void EnsureNestedCanvas(GameObject target)
        {
            if (!target.TryGetComponent<Canvas>(out _))
            {
                target.AddComponent<Canvas>();
            }
        }
        private void OnDestroy()
        {
            _isExited = true;
            Localization.OnLanguageChanged -= OnLanguageChanged;
            _menuTitleDisplayerAnim.TryCancel();
            InputManager.TouchButtonRingEdge = 5.4f;
            GameManager.RequestSave(this);
        }
    }
}
