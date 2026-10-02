using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using LitMotion;
using MajdataPlay;
using MajdataPlay.i18n;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Setting;
using MajdataPlay.Settings;
using MajdataPlay.Settings.OptionEnumerators;
using TMPro;
using UnityEngine;
using UObject = UnityEngine.Object;

internal static class Program
{
    static int _assertions;
    static void Main()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Run("bounded shared pool, category traversal and motion cancellation", PoolNavigation);
        Run("cross-option values, immediate input and idle TMP writes", ValueSynchronization);
        Run("localization, rebind ownership and subscription lifecycle", LocalizationAndOwnership);
        Run("zero offset unit change and engine/audio side effects", SpecializedEnumerators);
        Run("shared reflection metadata and eager preparation without retained settings", CachedMetadataAndWarmup);
        Run("eager initialization preserves deserialized note-mask values", PreserveNoteMask);
        Console.WriteLine($"SETTING_VALIDATION_PASSED ({_assertions} assertions)");
    }
    static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS: {name}");
    }
    static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    static void Call(object target, string method) => UObject.Invoke(target, method);
    static Option[] Visible => UObject.Objects.Where(x => x.activeInHierarchy).SelectMany(x => x.Components).OfType<Option>().ToArray();
    static Option View(string property) => Visible.Single(x => Get<OptionData>(x, "_data").PropertyInfo.Name == property);
    static string Value(Option option) => Get<TextMeshProUGUI>(option, "_valueTextDisplayer").text;
    static void Frame()
    {
        foreach (var menu in UObject.Objects.Where(x => x.activeInHierarchy).SelectMany(x => x.Components).OfType<Menu>().ToArray()) Call(menu, "LateUpdate");
        foreach (var option in Visible) Call(option, "LateUpdate");
    }
    static void ClickNext(Option option)
    {
        InputManager.PositivePressed = false;
        option.HandleInput();
        InputManager.PositivePressed = true;
        option.HandleInput();
        InputManager.PositivePressed = false;
        option.HandleInput();
    }

    static void PoolNavigation()
    {
        using var fixture = new Fixture(new ManySettings(), new ManySettings());
        var first = fixture.Menus[0];
        first.ToOption(nameof(ManySettings.P4));
        Check(Visible.Length == 3, "middle option should have exactly three idle views");
        var identities = UObject.Objects.SelectMany(x => x.Components).OfType<Option>().Where(x => x.gameObject != fixture.Prefab).ToArray();
        for (var pass = 0; pass < 5; pass++)
        {
            foreach (var menu in fixture.Menus)
            {
                for (var i = 0; i < 9; i++)
                {
                    fixture.Menus[fixture.Manager.Index].SwitchOption(1);
                    LMotion.Step(.4f);
                    Check(Visible.Length <= 4, "slide exceeded four active views");
                    LMotion.Step(1f);
                    Check(Visible.Length <= 3, "completed slide exceeded three active views");
                    Check(UObject.InstantiateCount == 4, "navigation instantiated an option beyond the shared pool");
                }
                fixture.Manager.NextMenu();
                Check(Visible.All(x => x.transform.parent == fixture.Menus[fixture.Manager.Index].transform), "old category retained a visible option");
                Check(Visible.Length <= 3, "category switch exceeded three idle views");
            }
        }
        var current = fixture.Menus[fixture.Manager.Index];
        current.ToOption(nameof(ManySettings.P4));
        current.SwitchOption(1);
        LMotion.Step(.35f);
        Check(Visible.Length == 4, "fractional middle slide should keep one transition view");
        current.SwitchOption(1);
        current.SwitchOption(1);
        current.SwitchOption(-1);
        current.SwitchOption(-1);
        LMotion.Step(.7f);
        Check(Visible.Length <= 4, "rapid reversal exceeded the pool");
        var selected = current.SelectedIndex;
        current.gameObject.SetActive(false);
        Check(Visible.Length == 0 && Localization.Subscribers == 0, "disabled menu retained views or language listeners");
        LMotion.Step(1f);
        Check(Visible.Length == 0, "canceled motion repopulated a disabled menu");
        current.gameObject.SetActive(true);
        Frame();
        Check(current.SelectedIndex == selected && Visible.Length == 3, "re-enabled menu must snap to selected option with three views");
        Check(Visible.All(identities.Contains), "menu re-enable replaced pooled identities");
        Check(Localization.Subscribers == Visible.Length, "active listener count differs from visible views");
        current.ToOption("not-an-option");
        Check(current.SelectedIndex == 0 && Visible.Length <= 3, "unknown restored option should fall back to head");
    }

    static void ValueSynchronization()
    {
        var settings = new Values();
        using var fixture = new Fixture(settings);
        var menu = fixture.Menus[0];
        menu.ToOption(nameof(Values.Flag));
        var numeric = View(nameof(Values.Number));
        var boolean = View(nameof(Values.Flag));
        var enumeration = View(nameof(Values.Mode));
        settings.Number = 25;
        settings.Flag = true;
        settings.Mode = SampleMode.Last;
        Frame();
        Check(Value(numeric) == "25", "externally changed number stayed stale");
        Check(Value(boolean).EndsWith("_True"), "externally changed bool stayed stale");
        Check(Value(enumeration) == "Last", "externally changed enum stayed stale");
        var writes = TextMeshProUGUI.TotalWrites;
        for (var i = 0; i < 120; i++) Frame();
        Check(TextMeshProUGUI.TotalWrites == writes, "idle frames assigned unchanged TMP text");
        menu.ToOption(nameof(Values.Number));
        numeric = View(nameof(Values.Number));
        InputManager.PositivePressed = false;
        numeric.HandleInput();
        settings.Number = 80;
        InputManager.PositivePressed = true;
        numeric.HandleInput();
        InputManager.PositivePressed = false;
        Check(settings.Number == 81, "input before LateUpdate incremented stale numeric state");
        menu.ToOption(nameof(Values.Flag));
        boolean = View(nameof(Values.Flag));
        ClickNext(boolean);
        Check(!settings.Flag, "bool cycle did not start from external value");
        menu.ToOption(nameof(Values.Mode));
        enumeration = View(nameof(Values.Mode));
        ClickNext(enumeration);
        Check(settings.Mode == SampleMode.First, "enum cycle did not start from externally set Last");
        menu.ToOption(nameof(Values.Text));
        var readOnly = View(nameof(Values.Text));
        Check(Value(readOnly) == "initial", "read-only enumerator did not initialize value text");
        settings.Text = "modified by another option";
        Frame();
        Check(Value(readOnly) == settings.Text, "external read-only value stayed stale");
        ClickNext(readOnly);
        Check(settings.Text == "modified by another option", "read-only option was edited");
        menu.ToOption(nameof(Values.OptionalFlag));
        var optional = View(nameof(Values.OptionalFlag));
        settings.OptionalFlag = null;
        Frame();
        Check(Value(optional) == "UNSET", "nullable bool did not track null");
        ClickNext(optional);
        Check(settings.OptionalFlag == false, "nullable bool did not cycle from null");
        settings.Number = 120;
        menu.ToOption(nameof(Values.Number));
        Check(Value(View(nameof(Values.Number))) == "120", "offscreen external value was not refreshed when rebound");
    }

    static void LocalizationAndOwnership()
    {
        CountingEnumerator.Created = CountingEnumerator.Disposed = 0;
        var settings = new OwnershipSettings();
        using (var fixture = new Fixture(settings))
        {
            var menu = fixture.Menus[0];
            menu.ToOption(nameof(OwnershipSettings.Counted));
            var view = View(nameof(OwnershipSettings.Counted));
            var name = Get<TextMeshProUGUI>(view, "_nameTextDisplayer");
            var initial = name.text;
            Localization.Change(Language.Chinese);
            Check(name.text != initial && name.text.StartsWith("Chinese:"), "language change did not update visible name");
            var writes = TextMeshProUGUI.TotalWrites;
            Localization.Change(Language.Chinese);
            Frame();
            Check(TextMeshProUGUI.TotalWrites == writes, "same language notification dirtied unchanged text");
            menu.ToTail();
            Check(CountingEnumerator.Disposed == 0, "returning a view disposed persistent option data");
            menu.ToHead();
            Check(CountingEnumerator.Created == 1, "rebinding recreated option data/enumerator");
            Check(Value(View(nameof(OwnershipSettings.Counted))) == "3", "rebound view displayed another option's value");
            Check(Localization.Subscribers == Visible.Length, "rebinding accumulated language subscriptions");
            menu.gameObject.SetActive(false);
            Localization.Change(Language.English);
            menu.gameObject.SetActive(true);
            Frame();
            Check(Get<TextMeshProUGUI>(View(nameof(OwnershipSettings.Counted)), "_nameTextDisplayer").text.StartsWith("English:"), "inactive language change was missed on rebind");
        }
        Check(CountingEnumerator.Disposed == 1, "menu destruction must dispose its enumerator once");
        Check(Localization.Subscribers == 0, "scene destruction leaked localization handlers");
    }

    static void SpecializedEnumerators()
    {
        var settings = new OffsetSettings();
        using (var fixture = new Fixture(settings))
        {
            var menu = fixture.Menus[0];
            menu.ToHead();
            var offset = View(nameof(OffsetSettings.DisplayOffset));
            MajEnv.Settings.Debug.OffsetUnit = OffsetUnitOption.Second;
            menu.RefreshVisibleEnumerators();
            Check(Value(offset) == "0", "zero offset value unexpectedly changed");
            Check(fixture.Description.text.Contains("Second"), "zero offset unit change missed selected description");
            ClickNext(offset);
            Check(Math.Abs(settings.DisplayOffset - .001f) < .0000001f, "zero offset unit change missed numeric step");
        }
        var display = new DisplayOptions();
        using var number = new EngineNumberSettingEnumerator();
        number.Init(typeof(DisplayOptions).GetProperty(nameof(DisplayOptions.RenderScale)), display);
        display.RenderScale = 82;
        number.Refresh();
        Check(GameManager.RenderScale == 82 && number.LocalizedValueText == "82", "engine number external change did not apply/synchronize");
        using var boolean = new EngineBooleanSettingEnumerator();
        boolean.Init(typeof(DisplayOptions).GetProperty(nameof(DisplayOptions.VSync)), display);
        display.VSync = false;
        boolean.Refresh();
        Check(QualitySettings.vSyncCount == 0 && boolean.LocalizedValueText.EndsWith("_False"), "engine bool external change did not apply/synchronize");
        var volume = new AudioSettings();
        using var audio = new AudioVolumeEnumerator();
        audio.Init(typeof(AudioSettings).GetProperty(nameof(AudioSettings.Tap)), volume);
        var reads = MajInstances.AudioManager.VolumeReads;
        var previews = MajInstances.AudioManager.Previews;
        volume.Tap = .5f;
        audio.Refresh();
        Check(MajInstances.AudioManager.VolumeReads == reads + 1 && MajInstances.AudioManager.Previews == previews + 1, "changed audio volume failed to apply and preview once");
        audio.Refresh();
        Check(MajInstances.AudioManager.VolumeReads == reads + 1 && MajInstances.AudioManager.Previews == previews + 1, "unchanged audio volume replayed side effects");
    }

    static void CachedMetadataAndWarmup()
    {
        var getMenus = typeof(SettingManager).GetMethod("GetMenuMetadata", BindingFlags.Static | BindingFlags.NonPublic);
        ChartDiscoveryProbeAttribute.Created = 0;
        var categories = (Array)getMenus.Invoke(null, new object[] { typeof(GameSetting) });
        Check(ChartDiscoveryProbeAttribute.Created == 1, "first metadata preparation must include chart settings even without a selected chart");
        Check(ReferenceEquals(categories, getMenus.Invoke(null, new object[] { typeof(GameSetting) })), "manager rebuilt category metadata");
        var volumeCategory = categories.Cast<object>().Single(value =>
            (string)value.GetType().GetProperty("Name", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value) == "Volume");
        var getInstance = volumeCategory.GetType().GetMethod("GetInstance", BindingFlags.Instance | BindingFlags.NonPublic);
        var originalSettings = new GameSetting();
        var freshSettings = new GameSetting();
        Check(ReferenceEquals(getInstance.Invoke(volumeCategory, new object[] { originalSettings }), originalSettings.Audio.Volume), "audio metadata must target nested Volume settings");
        Check(ReferenceEquals(getInstance.Invoke(volumeCategory, new object[] { freshSettings }), freshSettings.Audio.Volume), "cached category metadata retained an earlier settings instance");

        DiscoveryProbeAttribute.Created = 0;
        WarmupEnumerator.Created = WarmupEnumerator.Initialized = WarmupEnumerator.Disposed = 0;
        var type = typeof(CacheSettings);
        var properties = SettingReflectionCache.GetVisibleProperties(type);
        var firstProperty = SettingReflectionCache.GetProperty(type, nameof(CacheSettings.First));
        var descriptor = SettingReflectionCache.GetOption(firstProperty);
        var attributes = SettingReflectionCache.GetAttributes(firstProperty);
        var enumValues = SettingReflectionCache.GetEnumValues(typeof(SampleMode));
        Check(properties.Length == 6, "cached visible-property discovery did not exclude hidden setting");
        Check(DiscoveryProbeAttribute.Created == 6, "metadata discovery should construct each counted attribute exactly once");
        Check(ReferenceEquals(firstProperty, properties[0]), "property lookup did not reuse discovered reflection object");

        for (var scene = 0; scene < 3; scene++)
        {
            var first = new CacheSettings { First = 100 + scene, Last = 200 + scene };
            var second = new CacheSettings { First = 300 + scene, Last = 400 + scene };
            using (var fixture = new Fixture(first, second))
            {
                var expectedConstructors = (scene + 1) * properties.Length * fixture.Menus.Length;
                Check(WarmupEnumerator.Created == expectedConstructors && WarmupEnumerator.Initialized == expectedConstructors,
                    "all categories' enumerators must be constructed and initialized before first input");
                Check(Value(View(nameof(CacheSettings.First))) == first.First.ToString(), "scene recreation retained prior setting instance value");
                Check(ReferenceEquals(properties, SettingReflectionCache.GetVisibleProperties(type)), "scene recreation rebuilt visible-property metadata");
                Check(ReferenceEquals(descriptor, SettingReflectionCache.GetOption(firstProperty)), "scene recreation rebuilt option descriptor");
                Check(ReferenceEquals(attributes, SettingReflectionCache.GetAttributes(firstProperty)), "scene recreation rebuilt attribute array");
                Check(ReferenceEquals(enumValues, SettingReflectionCache.GetEnumValues(typeof(SampleMode))), "scene recreation rebuilt enum values");

                for (var category = 0; category < fixture.Menus.Length; category++)
                {
                    for (var index = 1; index < properties.Length; index++)
                    {
                        fixture.Menus[fixture.Manager.Index].SwitchOption(1);
                        LMotion.Step(.4f);
                        LMotion.Step(1);
                        Check(WarmupEnumerator.Created == expectedConstructors && WarmupEnumerator.Initialized == expectedConstructors,
                            "first traversal constructed or initialized an unseen option enumerator");
                        Check(DiscoveryProbeAttribute.Created == 6, "first traversal repeated reflection attribute discovery");
                    }
                    var instance = category == 0 ? first : second;
                    Check(Value(View(nameof(CacheSettings.Last))) == instance.Last.ToString(), "shared metadata retained another category instance value");
                    Check(UObject.InstantiateCount == 4, "eager model preparation changed the bounded view pool");
                    fixture.Manager.NextMenu();
                }
            }
            Check(WarmupEnumerator.Disposed == (scene + 1) * 12, "scene exit failed to release all eagerly prepared enumerators");
            Check(DiscoveryProbeAttribute.Created == 6, "scene recreation repeated cached attribute discovery");
            Check(ReferenceEquals(categories, getMenus.Invoke(null, new object[] { typeof(GameSetting) })) && ChartDiscoveryProbeAttribute.Created == 1,
                "scene recreation rediscovered category or chart metadata");
        }
    }

    static void PreserveNoteMask()
    {
        var settings = new MaskSettings { NoteMask = new string("Inner".ToCharArray()) };
        Check(!ReferenceEquals(settings.NoteMask, "Inner"), "test requires a deserialized, non-interned string");
        using var fixture = new Fixture(new ManySettings(), settings);
        Check(settings.NoteMask == "Inner", "eager initialization reset an unseen category's valid note mask");
        fixture.Manager.NextMenu();
        fixture.Menus[1].ToOption(nameof(MaskSettings.NoteMask));
        var view = View(nameof(MaskSettings.NoteMask));
        Check(Value(view) == "Inner", "note-mask enumerator did not retain deserialized selection");
        ClickNext(view);
        Check(settings.NoteMask == "Outer", "note-mask input did not start from deserialized Inner selection");
    }

    sealed class Fixture : IDisposable
    {
        public readonly SettingManager Manager;
        public readonly Menu[] Menus;
        public readonly GameObject Prefab;
        public readonly TextMeshProUGUI Description;
        public Fixture(params object[] settings)
        {
            UObject.Objects.Clear();
            UObject.InstantiateCount = 0;
            LMotion.Motions.Clear();
            InputManager.PositivePressed = InputManager.NegativePressed = false;
            MajEnv.Settings = new GameSetting();
            Localization.Change(Language.English);
            Manager = new GameObject("SettingManager").AddComponent<SettingManager>();
            Description = new GameObject("Description").AddComponent<TextMeshProUGUI>();
            Set(Manager, "_menuListRoot", new GameObject("Menus"));
            Set(Manager, "_descriptionTextDisplayer", Description);
            Prefab = CreateOption();
            Prefab.SetActive(false);
            Prefab.Clone = CreateOption;
            Menus = settings.Select((value, index) =>
            {
                var menu = new GameObject($"Menu {index}").AddComponent<Menu>();
                menu.Instance = value;
                menu.Name = $"Category {index}";
                Set(menu, "_optionPrefab", Prefab);
                Call(menu, "Awake");
                menu.gameObject.SetActive(false);
                return menu;
            }).ToArray();
            Set(Manager, "menus", Menus);
            // Match SettingManager.Start: prepare all models before input/fade.
            foreach (var menu in Menus) menu.Init();
            Menus[0].ToHead();
            Menus[0].gameObject.SetActive(true);
        }
        static GameObject CreateOption()
        {
            var go = new GameObject("Option");
            var option = go.AddComponent<Option>();
            var name = new GameObject("Name").AddComponent<TextMeshProUGUI>();
            var value = new GameObject("Value").AddComponent<TextMeshProUGUI>();
            name.transform.SetParent(go.transform, false);
            value.transform.SetParent(go.transform, false);
            Set(option, "_nameTextDisplayer", name);
            Set(option, "_valueTextDisplayer", value);
            return go;
        }
        public void Dispose()
        {
            foreach (var menu in Menus) UObject.Destroy(menu.gameObject);
            foreach (var root in UObject.Objects.Where(x => x.transform.parent == null).ToArray()) UObject.Destroy(root);
            UObject.Objects.Clear();
            LMotion.Motions.Clear();
        }
    }
    sealed class ManySettings
    {
        public int P0 { get; set; } public int P1 { get; set; } public int P2 { get; set; }
        public int P3 { get; set; } public int P4 { get; set; } public int P5 { get; set; }
        public int P6 { get; set; } public int P7 { get; set; } public int P8 { get; set; }
        public int P9 { get; set; }
    }
    enum SampleMode { First, Middle, Last }
    sealed class Values
    {
        public int Number { get; set; } = 4;
        public bool Flag { get; set; }
        public SampleMode Mode { get; set; }
        public string Text { get; set; } = "initial";
        [Optional] public bool? OptionalFlag { get; set; } = true;
    }
    sealed class OwnershipSettings
    {
        [OptionEnumerator(typeof(CountingEnumerator))] public int Counted { get; set; } = 3;
        public int P1 { get; set; } public int P2 { get; set; } public int P3 { get; set; } public int P4 { get; set; }
    }
    sealed class OffsetSettings { [OptionEnumerator(typeof(GameOffsetEnumerator))] public float DisplayOffset { get; set; } }
    sealed class AudioSettings { public float Tap { get; set; } = .3f; }
    sealed class MaskSettings { [OptionEnumerator(typeof(NoteMaskEnumerator))] public string NoteMask { get; set; } }
    sealed class CacheSettings
    {
        [DiscoveryProbe, OptionEnumerator(typeof(WarmupEnumerator))] public int First { get; set; }
        [DiscoveryProbe, OptionEnumerator(typeof(WarmupEnumerator))] public int P1 { get; set; }
        [DiscoveryProbe, OptionEnumerator(typeof(WarmupEnumerator))] public int P2 { get; set; }
        [DiscoveryProbe, OptionEnumerator(typeof(WarmupEnumerator))] public int P3 { get; set; }
        [DiscoveryProbe, OptionEnumerator(typeof(WarmupEnumerator))] public int P4 { get; set; }
        [DiscoveryProbe, OptionEnumerator(typeof(WarmupEnumerator))] public int Last { get; set; }
        [HideInSettingUI] public int Hidden { get; set; }
    }
}

public sealed class CountingEnumerator : DefaultNumberEnumerator
{
    public static int Created, Disposed;
    public CountingEnumerator() => Created++;
    public override void Dispose() => Disposed++;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class DiscoveryProbeAttribute : Attribute
{
    public static int Created;
    public DiscoveryProbeAttribute() => Created++;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class ChartDiscoveryProbeAttribute : Attribute
{
    public static int Created;
    public ChartDiscoveryProbeAttribute() => Created++;
}

public sealed class WarmupEnumerator : DefaultNumberEnumerator
{
    public static int Created, Initialized, Disposed;
    public WarmupEnumerator() => Created++;
    protected override void InitInternal()
    {
        Initialized++;
        if (GetCustomAttribute<DiscoveryProbeAttribute>() == null)
            throw new InvalidOperationException("Enumerator did not receive its cached option attributes.");
        base.InitInternal();
    }
    public override void Dispose() => Disposed++;
}
