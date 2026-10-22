using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using LitMotion;
using MajdataPlay;
using MajdataPlay.i18n;
using MajdataPlay.IO;
using MajdataPlay.Rendering;
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
        Run("rendering presets, manual customization and side-effect-free refresh", RenderingPresetInput);
        Run("rendering compatibility visibility, navigation and bounded pool", RenderingCompatibilityNavigation);
        Run("advanced rendering unlock, press deduplication and interrupted gestures", RenderingAdvancedUnlock);
        Run("fast physical A8 presses, delayed completion loss and cross-frame held deduplication", RenderingAdvancedPressCadence);
        Run("custom resolution step boundaries, forced output and exact apply counts", RenderingScaleInput);
        Run("current output path read-only status, dynamic labels and non-persisted metadata", RenderingOutputStatus);
        Run("four real rendering language tables and hidden-option font warmup", RenderingLocalizationWarmup);
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
        var display = new RenderingOptions();
        using var number = new EngineNumberSettingEnumerator();
        number.Init(typeof(RenderingOptions).GetProperty(nameof(RenderingOptions.FPSLimit)), display);
        display.FPSLimit = 82;
        number.Refresh();
        Check(Application.targetFrameRate == 82 && number.LocalizedValueText == "82", "FPS limit external change did not apply/synchronize");
        display.FPSLimit = -1;
        number.Refresh();
        Check(Application.targetFrameRate == -1 && number.LocalizedValueText == "-1", "unlimited FPS external change did not apply/synchronize");
        using var boolean = new EngineBooleanSettingEnumerator();
        boolean.Init(typeof(RenderingOptions).GetProperty(nameof(RenderingOptions.VSync)), display);
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

    static void RenderingPresetInput()
    {
        foreach (var (quality, expectedBias) in new[]
        {
            (RenderingTextureQualityOption.Low, 2),
            (RenderingTextureQualityOption.Medium, 1),
            (RenderingTextureQualityOption.High, 0)
        })
        {
            var precision = new RenderingOptions { TextureQuality = quality };
            Check(precision.GetEffectiveParameters().MipmapBias == expectedBias,
                "texture precision must map to an additive LOD bias: " + quality);
        }
        var settings = new RenderingOptions { RenderQuality = RenderingQualityOption.Refined };
        GameManager.RenderingApplies = 0;
        using var fixture = new Fixture(settings);
        var menu = fixture.Menus[0];
        var properties = SettingReflectionCache.GetVisibleProperties(typeof(RenderingOptions));
        Check(properties.Length == 13 && properties.Select(property => property.Name).ToHashSet().SetEquals(
            new[] { "RenderQuality", "AntiAliasing", "TextureQuality", "RenderScale", "OutputMode", "CompatibilityMode", "SystemResolutionScaling", "FPSLimit", "VSync", "SystemScalingStatus", "DisplayBufferResolution", "SceneRenderResolution", "CurrentOutputPath" }),
            "rendering metadata must retain all thirteen Windows Custom rows, including the read-only output status");
        Check(Get<int>(menu, "_displayedOptionCount") == 4 && Visible.Length == 2 &&
            Get<int[]>(menu, "_displayedPropertyIndices").Take(4).Select(i => properties[i].Name).ToHashSet()
                .SetEquals(new[] { "RenderQuality", "FPSLimit", "VSync", "CompatibilityMode" }),
            "named presets must expose only quality, FPS, VSync and compatibility");
        Check(GameManager.RenderingApplies == 0 && RenderingSignature(settings) == "Refined/150/4/High/Intermediate",
            "eager enumerator initialization must not apply or overwrite an existing preset");
        Check(Value(View("RenderQuality")) == Translations.LookupRendering("MAJSETTING_PROPERTY_RenderQuality_OPTION_Refined", Language.English),
            "quality selector must show only its localized preset name");

        foreach (var expected in new[] { "EnergySaving/100/1/Medium/Direct", "Performance/100/1/High/Direct", "Beautiful/100/4/High/Direct", "Refined/150/4/High/Intermediate" })
        {
            ClickNext(View("RenderQuality"));
            Frame();
            Check(RenderingSignature(settings) == expected, "quality input did not atomically choose preset parameters: " + expected);
        }
        Check(GameManager.RenderingApplies == 4, "each changed preset must apply once and skip Custom");
        Check(settings.SystemResolutionScaling == 100, "system scaling must default off");
        var systemSnapshot = RenderingSignature(settings);
        using (var systemScale = new RenderingNumberSettingEnumerator())
        {
            settings.SystemResolutionScaling = 75;
            systemScale.Init(typeof(RenderingOptions).GetProperty("SystemResolutionScaling"), settings);
            int before = GameManager.RenderingApplies;
            foreach (var language in Language.All)
            {
                Localization.Change(language);
                systemScale.RefreshLocalization();
                Check(systemScale.LocalizedValueText == Translations.LookupRendering(
                    "MAJSETTING_PROPERTY_SystemScalingStatus_OPTION_Unsupported", language),
                    "unsupported scale label must follow localization");
                Check(!systemScale.MovePrevious() && !systemScale.MoveNext() && settings.SystemResolutionScaling == 75 &&
                    GameManager.RenderingApplies == before && RenderingSignature(settings) == systemSnapshot,
                    "unsupported scale must reject input and retain its saved value");
                systemScale.Refresh();
                Check(!systemScale.LocalizedValueText.Contains("%"), "unsupported label must not get a percent suffix");
            }
            Localization.Change(Language.English);
            settings.SystemResolutionScaling = 100;
        }

        using (var quality = new RenderingEnumSettingEnumerator())
        {
            quality.Init(typeof(RenderingOptions).GetProperty("RenderQuality"), settings);
            foreach (var expected in new[] { RenderingQualityOption.Beautiful, RenderingQualityOption.Performance, RenderingQualityOption.EnergySaving, RenderingQualityOption.Refined })
            {
                Check(quality.MovePrevious() && settings.RenderQuality == expected, "reverse preset cycling reached Custom or skipped a normal preset");
            }
        }
        Check(GameManager.RenderingApplies == 8, "reverse preset changes must apply once each");
        Check(settings.EnterCustomMode() && !settings.EnterCustomMode(), "entering Custom must be an idempotent state transition");
        Frame();
        Check(Get<int>(menu, "_displayedOptionCount") == 13 && settings.RenderScale == 150,
            "Custom must immediately expose eight rows without resetting the prior preset");

        menu.ToOption("AntiAliasing");
        ClickNext(View("AntiAliasing"));
        Frame();
        Check(settings.AntiAliasing == RenderingAntialiasingOption.MSAA8x && settings.RenderQuality == RenderingQualityOption.Custom && settings.RenderScale == 150,
            "editing antialiasing must preserve Custom and the current scale");
        menu.ToHead();
        Check(Value(View("RenderQuality")) == Translations.LookupRendering("MAJSETTING_PROPERTY_RenderQuality_OPTION_Custom", Language.English),
            "the quality view did not synchronize its Custom state");
        menu.ToOption("TextureQuality");
        ClickNext(View("TextureQuality"));
        Check(settings.TextureQuality == RenderingTextureQualityOption.Low && settings.RenderQuality == RenderingQualityOption.Custom,
            "texture input did not preserve manual customization");
        settings.RenderScale = 100;
        Frame();
        menu.ToOption("OutputMode");
        ClickNext(View("OutputMode"));
        Check(settings.OutputMode == RenderingOutputMode.Direct && GameManager.RenderingApplies == 11,
            "output input must change the stored mode and apply exactly once");

        var snapshot = RenderingSignature(settings);
        menu.RefreshVisibleEnumerators();
        menu.ToHead();
        menu.ToTail();
        for (var i = 0; i < 120; i++) Frame();
        foreach (var language in Language.All) Localization.Change(language);
        Check(GameManager.RenderingApplies == 11 && RenderingSignature(settings) == snapshot,
            "refresh, pooled rebind, unchanged frames or language changes applied rendering settings");

        using var enumeration = new RenderingEnumSettingEnumerator();
        enumeration.Init(typeof(RenderingOptions).GetProperty("AntiAliasing"), settings);
        settings.AntiAliasing = RenderingAntialiasingOption.MSAA2x;
        enumeration.Refresh();
        Check(GameManager.RenderingApplies == 11 && enumeration.Current.Equals(RenderingAntialiasingOption.MSAA2x),
            "external-value refresh must synchronize without applying a stale preset");
        Check(enumeration.MovePrevious() && settings.AntiAliasing == RenderingAntialiasingOption.Disabled && GameManager.RenderingApplies == 12,
            "previous-value input must start at the refreshed antialiasing value");
    }

    static void RenderingCompatibilityNavigation()
    {
        var settings = new RenderingOptions { RenderQuality = RenderingQualityOption.Refined };
        settings.EnterCustomMode();
        GameManager.RenderingApplies = 0;
        using var fixture = new Fixture(new ManySettings(), settings, new ManySettings());
        fixture.Manager.NextMenu();
        var menu = fixture.Menus[1];
        var snapshot = RenderingSignature(settings);
        for (int iteration = 0; iteration < 4; iteration++)
        {
            menu.ToOption("CompatibilityMode");
            ClickMenuNext(menu);
            Check(settings.CompatibilityMode && Get<int>(menu, "_displayedOptionCount") == 3,
                "compatibility must retain only FPS, VSync and its switch");
            Check(Get<OptionData>(View("CompatibilityMode"), "_data").PropertyInfo.Name == "CompatibilityMode",
                "visibility change lost compatibility selection");
            foreach (string name in new[] { "FPSLimit", "VSync" })
            {
                menu.ToOption(name);
                Check(Get<OptionData>(View(name), "_data").PropertyInfo.Name == name,
                    "compatibility lost common row " + name);
            }
            var effective = settings.GetEffectiveParameters();
            Check(effective.RenderScale == 50 && effective.MSAASamples == 1 && effective.MipmapBias == 2 &&
                !effective.ResolveToScreen && RenderingSignature(settings) == snapshot,
                "compatibility changed saved preset or effective parameters");
            menu.ToOption("FPSLimit");
            ClickMenuNext(menu);
            Check(Application.targetFrameRate == settings.FPSLimit && settings.RenderQuality == RenderingQualityOption.Custom,
                "compatibility must allow independent FPS edits");
            menu.ToOption("CompatibilityMode");
            ClickMenuNext(menu);
            Check(!settings.CompatibilityMode && Get<int>(menu, "_displayedOptionCount") == 13 && RenderingSignature(settings) == snapshot,
                "compatibility restore lost custom values or controls");
            Check(UObject.InstantiateCount == 4 && Visible.Length <= 3 && Localization.Subscribers == Visible.Length,
                "common controls must retain bounded pool and subscriptions");
        }
        menu.ToHead();
        menu.SwitchOption(-1);
        Check(fixture.Manager.Index == 0, "previous category navigation failed");
        fixture.Manager.NextMenu();
        menu.ToTail();
        menu.SwitchOption(1);
        Check(fixture.Manager.Index == 2, "next category navigation failed");
    }

    static void RenderingAdvancedUnlock()
    {
        var settings = new RenderingOptions { RenderQuality = RenderingQualityOption.Beautiful };
        GameManager.RenderingApplies = 0;
        using (var fixture = new Fixture(new ManySettings(), settings, new ManySettings()))
        {
            fixture.Manager.NextMenu();
            var menu = fixture.Menus[1];
            for (var click = 0; click < 9; click++)
            {
                PressAdvanced(fixture.Manager, button: click % 2 == 0, sensor: click % 2 != 0);
                Check(settings.RenderQuality == RenderingQualityOption.Beautiful && GameManager.SaveRequests == 0 && GameManager.RenderingApplies == 0,
                    "fewer than ten independent A8 presses changed quality, applied or saved");
            }
            PressAdvanced(fixture.Manager, button: true, sensor: true);
            Check(settings.RenderQuality == RenderingQualityOption.Custom && Get<int>(menu, "_displayedOptionCount") == 13,
                "the tenth independent A8 press must immediately expose Custom controls");
            Check(GameManager.RenderingApplies == 1 && GameManager.SaveRequests == 1 && ReferenceEquals(GameManager.LastSaveRequester, menu),
                "unlock must immediately apply and request one save before leaving Setting");
            Check(MajInstances.SceneSwitcher.SwitchedScenes.Count == 0 && menu.SelectedIndex == 0,
                "rendering quality A8 must not jump to Calibrator or move the selection");
            Check(settings.RenderScale == 100 && settings.AntiAliasing == RenderingAntialiasingOption.MSAA4x && settings.OutputMode == RenderingOutputMode.Direct,
                "unlock must retain the active preset parameters");
            for (var click = 0; click < 20; click++) PressAdvanced(fixture.Manager, true, true);
            Check(GameManager.RenderingApplies == 1 && GameManager.SaveRequests == 1 && MajInstances.SceneSwitcher.SwitchedScenes.Count == 0,
                "A8 on an already Custom quality row must be consumed without repeat save/apply");
            ClickMenuNext(menu);
            Check(settings.RenderQuality == RenderingQualityOption.EnergySaving && Get<int>(menu, "_displayedOptionCount") == 4,
                "leaving Custom through quality selection must immediately restore the two-row page");
        }

        // Button and sensor press edges may represent one physical interaction. OR them once.
        settings = new RenderingOptions();
        GameManager.RenderingApplies = 0;
        using (var fixture = new Fixture(settings))
        {
            for (var click = 0; click < 5; click++) PressAdvanced(fixture.Manager, true, true);
            Check(settings.RenderQuality != RenderingQualityOption.Custom && GameManager.SaveRequests == 0,
                "five simultaneous button/sensor presses must not count as ten presses");
            for (var click = 0; click < 5; click++) PressAdvanced(fixture.Manager, true, true);
            Check(settings.RenderQuality == RenderingQualityOption.Custom && GameManager.SaveRequests == 1 && GameManager.RenderingApplies == 1,
                "ten de-duplicated presses must unlock exactly once");
        }

        // Use the production InputControlState: ClickCompleted is delayed from press and
        // may fire once while still held. It must never repeat into a ten-click unlock.
        settings = new RenderingOptions();
        GameManager.RenderingApplies = 0;
        using (var fixture = new Fixture(settings))
        {
            var state = new InputControlState();
            var completions = 0;
            for (var frame = 0; frame < 180; frame++)
            {
                state.Update(true, frame == 0, false, 1f / 60);
                InputManager.PressedButtons.Add(ButtonZone.A8);
                if (state.PressedThisFrame) InputManager.PressedThisFrameButtons.Add(ButtonZone.A8);
                if (state.ClickCompletedThisFrame) { InputManager.CompletedButtons.Add(ButtonZone.A8); completions++; }
                ManagerFrame(fixture.Manager);
            }
            Check(completions == 1 && settings.RenderQuality != RenderingQualityOption.Custom && GameManager.SaveRequests == 0,
                "one long held A8 must not be treated as repeated presses");
            state.Update(false, false, true, 1f / 60);
            for (var frame = 0; frame < 20; frame++)
            {
                state.Update(false, false, false, 1f / 60);
                ManagerFrame(fixture.Manager);
            }
            for (var click = 0; click < 9; click++) PressAdvanced(fixture.Manager);
            Check(settings.RenderQuality == RenderingQualityOption.Custom && GameManager.SaveRequests == 1,
                "the long press may contribute its initial press, but must not repeat");
        }

        AssertGestureReset("selected option changes", fixture => { fixture.Menus[0].SwitchOption(1); fixture.Menus[0].ToHead(); });
        AssertGestureReset("category leave and return", fixture => { fixture.Manager.NextMenu(); fixture.Manager.PreviousMenu(); });
        AssertGestureReset("menu disable and enable", fixture => { fixture.Menus[0].gameObject.SetActive(false); fixture.Menus[0].gameObject.SetActive(true); Frame(); });
        AssertGestureReset("quality preset changes", fixture => ClickMenuNext(fixture.Menus[0]));
        AssertGestureReset("same-frame option navigation", fixture =>
        {
            InputManager.PressedButtons.Add(ButtonZone.A3);
            QueueAdvancedPress();
            ManagerFrame(fixture.Manager);
            fixture.Menus[0].ToHead();
        });
        AssertGestureReset("same-frame category navigation", fixture =>
        {
            InputManager.PressedButtons.Add(ButtonZone.A2);
            QueueAdvancedPress();
            ManagerFrame(fixture.Manager);
            fixture.Manager.PreviousMenu();
        });
        AssertGestureReset("same-frame setting value input", fixture =>
        {
            InputManager.PositivePressed = true;
            QueueAdvancedPress();
            ManagerFrame(fixture.Manager);
        });

        foreach (var context in new[] { "quality row", "other category", "compatibility row", "custom detail", "compatibility enabled" })
        {
            settings = new RenderingOptions();
            if (context == "custom detail") settings.EnterCustomMode();
            if (context == "compatibility enabled") settings.CompatibilityMode = true;
            using var fixture = new Fixture(context == "other category" ? new ManySettings() : settings);
            if (context == "compatibility row") fixture.Menus[0].ToOption("CompatibilityMode");
            if (context == "custom detail") fixture.Menus[0].ToOption("AntiAliasing");
            InputManager.CompletedSensors.Add(SensorArea.A1);
            ManagerFrame(fixture.Manager);
            Check(MajInstances.SceneSwitcher.SwitchedScenes.SequenceEqual(new[] { "Calibrator" }),
                "A1 lost its existing calibration behavior: " + context);
        }

        foreach (var context in new[] { "other category", "compatibility row", "custom detail", "compatibility enabled" })
        {
            settings = new RenderingOptions();
            if (context == "custom detail") settings.EnterCustomMode();
            if (context == "compatibility enabled") settings.CompatibilityMode = true;
            using var fixture = new Fixture(context == "other category" ? new ManySettings() : settings);
            if (context == "compatibility row") fixture.Menus[0].ToOption("CompatibilityMode");
            if (context == "custom detail") fixture.Menus[0].ToOption("AntiAliasing");
            var snapshot = RenderingSignature(settings);
            for (var click = 0; click < 10; click++) PressAdvanced(fixture.Manager, true, true);
            Check(MajInstances.SceneSwitcher.SwitchedScenes.Count == 0 && RenderingSignature(settings) == snapshot && GameManager.SaveRequests == 0,
                "A8 outside the rendering quality row must have no unlock or navigation side effects: " + context);
        }

        settings = new RenderingOptions();
        using (var fixture = new Fixture(settings))
        {
            for (var click = 0; click < 9; click++) PressAdvanced(fixture.Manager);
            InputManager.CompletedButtons.Add(ButtonZone.A1);
            QueueAdvancedPress();
            ManagerFrame(fixture.Manager);
            Check(settings.RenderQuality != RenderingQualityOption.Custom && GameManager.SaveRequests == 0 &&
                MajInstances.SceneSwitcher.SwitchedScenes.SequenceEqual(new[] { "Calibrator" }),
                "same-frame A1 must retain calibration priority without completing the A8 unlock");
        }

        settings = new RenderingOptions();
        using (var fixture = new Fixture(settings))
        {
            for (var click = 0; click < 9; click++) PressAdvanced(fixture.Manager);
            QueueAdvancedPress();
            InputManager.CompletedButtons.Add(ButtonZone.A4);
            ManagerFrame(fixture.Manager);
            Check(settings.RenderQuality != RenderingQualityOption.Custom && GameManager.SaveRequests == 0 &&
                MajInstances.SceneSwitcher.SwitchedScenes.SequenceEqual(new[] { "List" }),
                "same-frame exit must interrupt unlock and retain the ordinary exit target");
        }
    }

    static void RenderingAdvancedPressCadence()
    {
        foreach (var sensor in new[] { false, true })
        {
            var settings = new RenderingOptions();
            GameManager.RenderingApplies = 0;
            using var fixture = new Fixture(settings);
            var state = new InputControlState();
            var previous = false;
            var presses = 0;
            var completions = 0;
            // Ten one-frame presses and releases at 60 Hz: faster than the 0.2s
            // animation-completion slot, but every physical edge is observable.
            for (var frame = 0; frame < 40; frame++)
            {
                var down = frame < 20 && (frame & 1) == 0;
                state.Update(down, down && !previous, !down && previous, 1f / 60);
                previous = down;
                if (state.IsPressed)
                {
                    if (sensor) InputManager.PressedSensors.Add(SensorArea.A8);
                    else InputManager.PressedButtons.Add(ButtonZone.A8);
                }
                if (state.PressedThisFrame)
                {
                    presses++;
                    if (sensor) InputManager.PressedThisFrameSensors.Add(SensorArea.A8);
                    else InputManager.PressedThisFrameButtons.Add(ButtonZone.A8);
                }
                if (state.ClickCompletedThisFrame)
                {
                    completions++;
                    if (sensor) InputManager.CompletedSensors.Add(SensorArea.A8);
                    else InputManager.CompletedButtons.Add(ButtonZone.A8);
                }
                ManagerFrame(fixture.Manager);
                if (presses < 10)
                    Check(settings.RenderQuality != RenderingQualityOption.Custom && GameManager.SaveRequests == 0,
                        "fast input unlocked before its tenth physical press");
                else
                    Check(settings.RenderQuality == RenderingQualityOption.Custom && GameManager.SaveRequests == 1 && GameManager.RenderingApplies == 1,
                        "the tenth physical press must unlock immediately without awaiting delayed completion");
            }
            Check(presses == 10 && completions > 0 && completions < presses,
                "production InputControlState did not reproduce completion-event coalescing for fast presses");
            Console.WriteLine($"EVIDENCE: {(sensor ? "sensor" : "button")} observed {presses} physical presses, {completions} delayed completions; unlock saved once.");
        }

        // One physical contact can change from outer button A8 to sensor A8 a
        // frame later. Both simultaneous overlap and direct channel handoff stay latched.
        foreach (var overlap in new[] { false, true })
        {
            var settings = new RenderingOptions();
            GameManager.RenderingApplies = 0;
            using var fixture = new Fixture(settings);
            for (var press = 0; press < 10; press++)
            {
                QueueAdvancedPress();
                ManagerFrame(fixture.Manager);
                if (overlap) InputManager.PressedButtons.Add(ButtonZone.A8);
                InputManager.PressedSensors.Add(SensorArea.A8);
                InputManager.PressedThisFrameSensors.Add(SensorArea.A8);
                ManagerFrame(fixture.Manager);
                InputManager.PressedSensors.Add(SensorArea.A8);
                ManagerFrame(fixture.Manager);
                ManagerFrame(fixture.Manager); // release the final A8 channel
                Check((settings.RenderQuality == RenderingQualityOption.Custom) == (press == 9),
                    "asynchronous A8 button/sensor reports counted one physical contact twice");
            }
            Check(GameManager.SaveRequests == 1 && GameManager.RenderingApplies == 1,
                "asynchronous A8 deduplication repeated an apply or save");
        }

        // Releasing other controls is not a new navigation/edit press and must
        // neither reset the consecutive count nor release the A8 channel latch.
        {
            var settings = new RenderingOptions();
            using var fixture = new Fixture(settings);
            for (var press = 0; press < 9; press++) PressAdvanced(fixture.Manager);
            InputManager.CompletedButtons.Add(ButtonZone.A2);
            InputManager.CompletedSensors.Add(SensorArea.B3);
            ManagerFrame(fixture.Manager);
            PressAdvanced(fixture.Manager);
            Check(settings.RenderQuality == RenderingQualityOption.Custom && GameManager.SaveRequests == 1,
                "an unrelated control completion/release discarded prior A8 presses");
        }

        // A8 pressed outside the quality row stays suppressed while either channel
        // remains held, even if the other channel reports its edge after entering.
        {
            var settings = new RenderingOptions();
            GameManager.RenderingApplies = 0;
            using var fixture = new Fixture(new ManySettings(), settings);
            QueueAdvancedPress();
            ManagerFrame(fixture.Manager);
            fixture.Manager.NextMenu();
            InputManager.PressedButtons.Add(ButtonZone.A8);
            InputManager.PressedSensors.Add(SensorArea.A8);
            InputManager.PressedThisFrameSensors.Add(SensorArea.A8);
            ManagerFrame(fixture.Manager);
            InputManager.PressedSensors.Add(SensorArea.A8);
            InputManager.CompletedButtons.Add(ButtonZone.A2); // unrelated release/completion
            ManagerFrame(fixture.Manager);
            InputManager.PressedSensors.Add(SensorArea.A8);
            ManagerFrame(fixture.Manager);
            ManagerFrame(fixture.Manager);
            for (var press = 0; press < 9; press++) PressAdvanced(fixture.Manager);
            Check(settings.RenderQuality != RenderingQualityOption.Custom && GameManager.SaveRequests == 0,
                "entering the quality row while A8 remained held incorrectly carried over its press");
            PressAdvanced(fixture.Manager);
            Check(settings.RenderQuality == RenderingQualityOption.Custom && GameManager.SaveRequests == 1 && GameManager.RenderingApplies == 1,
                "both-channel release did not rearm a fresh ten-press gesture");
        }

        // Calibration/exit still act on delayed Completed, but their initial press
        // must interrupt the hidden consecutive gesture immediately.
        foreach (var control in new[] { ButtonZone.A1, ButtonZone.A4, ButtonZone.A5 })
        {
            AssertGestureReset("pending calibration/exit button " + control, fixture =>
            {
                InputManager.PressedButtons.Add(control);
                ManagerFrame(fixture.Manager);
            });
        }
        AssertGestureReset("pending calibration sensor A1", fixture =>
        {
            InputManager.PressedSensors.Add(SensorArea.A1);
            ManagerFrame(fixture.Manager);
        });

        AssertGestureReset("navigation held on A8 press with delayed second channel", fixture =>
        {
            InputManager.PressedButtons.Add(ButtonZone.A3);
            QueueAdvancedPress();
            ManagerFrame(fixture.Manager);
            fixture.Menus[0].ToHead();
            InputManager.PressedButtons.Add(ButtonZone.A8);
            InputManager.PressedSensors.Add(SensorArea.A8);
            InputManager.PressedThisFrameSensors.Add(SensorArea.A8);
            ManagerFrame(fixture.Manager);
            ManagerFrame(fixture.Manager);
        });
    }

    static void AssertGestureReset(string label, Action<Fixture> interrupt)
    {
        var settings = new RenderingOptions();
        GameManager.RenderingApplies = 0;
        using var fixture = new Fixture(settings, new ManySettings());
        for (var click = 0; click < 9; click++) PressAdvanced(fixture.Manager);
        interrupt(fixture);
        fixture.Menus[0].ToHead();
        Frame();
        var applies = GameManager.RenderingApplies;
        PressAdvanced(fixture.Manager);
        Check(settings.RenderQuality != RenderingQualityOption.Custom && GameManager.SaveRequests == 0 &&
            MajInstances.SceneSwitcher.SwitchedScenes.Count == 0, label + " did not reset the consecutive-click gesture");
        for (var click = 0; click < 9; click++) PressAdvanced(fixture.Manager);
        Check(settings.RenderQuality == RenderingQualityOption.Custom && GameManager.SaveRequests == 1 &&
            GameManager.RenderingApplies == applies + 1, label + " prevented a fresh ten-click gesture");
    }

    static void RenderingScaleInput()
    {
        var settings = new RenderingOptions();
        settings.EnterCustomMode();
        GameManager.RenderingApplies = 0;
        using var fixture = new Fixture(settings);
        var menu = fixture.Menus[0];
        menu.ToOption("RenderScale");
        Check(Value(View("RenderScale")) == "100%", "custom RenderScale must display a percentage");
        ClickNext(View("RenderScale"));
        Check(settings.RenderScale == 105 && settings.OutputMode == RenderingOutputMode.Intermediate &&
            !settings.GetEffectiveParameters().ResolveToScreen && GameManager.RenderingApplies == 1,
            "a 5% scale step must force intermediate output and apply once");

        using var output = new RenderingEnumSettingEnumerator();
        output.Init(typeof(RenderingOptions).GetProperty("OutputMode"), settings);
        Check(!output.MoveNext() && !output.MovePrevious() && output.Current.Equals(RenderingOutputMode.Intermediate) &&
            settings.OutputMode == RenderingOutputMode.Intermediate && GameManager.RenderingApplies == 1,
            "rejected Direct output at non-100% must retain Current and not Apply");

        using var scale = new RenderingNumberSettingEnumerator();
        scale.Init(typeof(RenderingOptions).GetProperty("RenderScale"), settings);
        Check(scale.LocalizedValueText == "105%" && scale.MovePrevious() && settings.RenderScale == 100 &&
            settings.OutputMode == RenderingOutputMode.Intermediate && !settings.GetEffectiveParameters().ResolveToScreen &&
            GameManager.RenderingApplies == 2, "returning to 100% must preserve intermediate output until explicitly changed");
        output.Refresh();
        Check(output.MoveNext() && settings.OutputMode == RenderingOutputMode.Direct && settings.GetEffectiveParameters().ResolveToScreen &&
            GameManager.RenderingApplies == 3, "Direct output must become selectable at 100%");
        Check(scale.MovePrevious() && settings.RenderScale == 95 && settings.OutputMode == RenderingOutputMode.Intermediate &&
            GameManager.RenderingApplies == 4, "downscaling must force intermediate output too");

        settings.RenderScale = 10;
        scale.Refresh();
        var applies = GameManager.RenderingApplies;
        Check(!scale.MovePrevious() && settings.RenderScale == 10 && scale.LocalizedValueText == "10%" && GameManager.RenderingApplies == applies,
            "10% lower boundary must neither wrap nor Apply");
        for (var expected = 15; expected <= 200; expected += 5)
        {
            Check(scale.MoveNext() && settings.RenderScale == expected && scale.LocalizedValueText == expected + "%",
                "custom scale increment did not follow the 5% step: " + expected);
        }
        Check(GameManager.RenderingApplies == applies + 38 && !scale.MoveNext() && settings.RenderScale == 200 &&
            GameManager.RenderingApplies == applies + 38 && scale.LocalizedValueText == "200%", "200% upper boundary wrapped or applied twice");
        for (var expected = 195; expected >= 10; expected -= 5)
            Check(scale.MovePrevious() && settings.RenderScale == expected, "custom scale decrement did not follow the 5% step: " + expected);
        Check(GameManager.RenderingApplies == applies + 76 && !scale.MovePrevious(), "scale decrement boundary changed the exact Apply count");
        for (var refresh = 0; refresh < 3; refresh++) { scale.Refresh(); scale.RefreshLocalization(); }
        Check(scale.LocalizedValueText == "10%" && GameManager.RenderingApplies == applies + 76,
            "scale refresh duplicated percent suffix or triggered rendering side effects");
        Frame();
        var snapshot = RenderingSignature(settings);
        menu.ToOption("CompatibilityMode");
        ClickMenuNext(menu);
        ClickMenuNext(menu);
        Check(Get<int>(menu, "_displayedOptionCount") == 13 && RenderingSignature(settings) == snapshot,
            "disabling compatibility must restore all thirteen Windows Custom rows and the saved scale");
    }

    static void PressAdvanced(SettingManager manager, bool button = true, bool sensor = false)
    {
        // Model an independent press with both channels released on either side.
        ManagerFrame(manager);
        QueueAdvancedPress(button, sensor);
        ManagerFrame(manager);
        ManagerFrame(manager);
    }

    static void QueueAdvancedPress(bool button = true, bool sensor = false)
    {
        if (button)
        {
            InputManager.PressedButtons.Add(ButtonZone.A8);
            InputManager.PressedThisFrameButtons.Add(ButtonZone.A8);
        }
        if (sensor)
        {
            InputManager.PressedSensors.Add(SensorArea.A8);
            InputManager.PressedThisFrameSensors.Add(SensorArea.A8);
        }
    }

    static void RenderingOutputStatus()
    {
        var settings = new RenderingOptions();
        settings.EnterCustomMode();
        MajRenderDiagnostics.Invalidate();
        GameManager.RenderingApplies = 0;
        using var fixture = new Fixture(settings);
        var menu = fixture.Menus[0];
        var property = typeof(RenderingOptions).GetProperty(nameof(RenderingOptions.CurrentOutputPath));
        Check(!property.CanWrite && property.IsDefined(typeof(ReadOnlyOptionAttribute), true),
            "current output status must have neither a setter nor editable setting semantics");
        Check(property.IsDefined(typeof(Newtonsoft.Json.JsonIgnoreAttribute), true),
            "runtime output status must be excluded from settings JSON");
        using (var enumerator = SettingReflectionCache.GetOption(property).CreateEnumerator())
        {
            enumerator.Init(property, settings);
            Check(enumerator is DefaultEnumEnumerator && !enumerator.MoveNext() && !enumerator.MovePrevious(),
                "the read-only status must use the enum label/warmup path without accepting either input direction");
        }
        menu.ToOption(nameof(RenderingOptions.CurrentOutputPath));
        Check(Get<int>(menu, "_displayedOptionCount") == 13 && menu.SelectedIndex == 12,
            "Custom must expose current output status as its seventh row");
        Check(Value(View("CurrentOutputPath")) == Translations.LookupRendering(
            "MAJSETTING_PROPERTY_CurrentOutputPath_OPTION_Unavailable", Language.English),
            "status did not initialize from the current diagnostic state");
        MajRenderDiagnostics.BeginFrame(100);
        Frame();
        Check(settings.CurrentOutputPath == MajRenderOutputStatus.Unavailable,
            "a render frame with no main camera must not retain an earlier output result");
        MajRenderDiagnostics.BeginCamera();
        Frame();
        Check(settings.CurrentOutputPath == MajRenderOutputStatus.Pending && Value(View("CurrentOutputPath")) ==
            Translations.LookupRendering("MAJSETTING_PROPERTY_CurrentOutputPath_OPTION_Pending", Language.English),
            "beginning the main camera must refresh the visible Pending label");
        MajRenderDiagnostics.Publish(MajRenderOutputStatus.Direct);
        MajRenderDiagnostics.BeginFrame(100);
        Frame();
        Check(settings.CurrentOutputPath == MajRenderOutputStatus.Direct && Value(View("CurrentOutputPath")) ==
            Translations.LookupRendering("MAJSETTING_PROPERTY_CurrentOutputPath_OPTION_Direct", Language.English),
            "another context in the same frame must retain the submitted main-screen status");
        MajRenderDiagnostics.BeginFrame(101);
        Frame();
        Check(settings.CurrentOutputPath == MajRenderOutputStatus.Unavailable && Value(View("CurrentOutputPath")) ==
            Translations.LookupRendering("MAJSETTING_PROPERTY_CurrentOutputPath_OPTION_Unavailable", Language.English),
            "a new frame without main-screen submission must invalidate the prior frame result");

        MajRenderDiagnostics.PublishDimensions(1080, 2400, 540, 1200);
        MajRenderDiagnostics.Publish(MajRenderOutputStatus.IntermediateScaled);
        menu.ToOption("DisplayBufferResolution");
        Frame();
        Check(Value(View("DisplayBufferResolution")) == "1080 x 2400", "display buffer readout must reflect submitted dimensions");
        menu.ToOption("SceneRenderResolution");
        Frame();
        Check(Value(View("SceneRenderResolution")) == "540 x 1200", "scene resolution readout must differ from display during scaling");
        var cached = MajRenderDiagnostics.SceneResolution;
        MajRenderDiagnostics.PublishDimensions(1080, 2400, 540, 1200);
        Check(ReferenceEquals(cached, MajRenderDiagnostics.SceneResolution), "stable dimensions must reuse cached text");
        ClickNext(View("SceneRenderResolution"));
        Check(MajRenderDiagnostics.SceneResolution == cached && GameManager.RenderingApplies == 0,
            "read-only dimension rows must reject input without applying settings");
        MajRenderDiagnostics.Invalidate();
        Check(MajRenderDiagnostics.DisplayResolution == "--" && MajRenderDiagnostics.SceneResolution == "--",
            "invalid output must not advertise old dimensions");
        menu.ToOption("CurrentOutputPath");

        var snapshot = RenderingSignature(settings);
        foreach (var status in Enum.GetValues<MajRenderOutputStatus>())
        {
            MajRenderDiagnostics.Publish(status);
            Frame();
            var view = View("CurrentOutputPath");
            var expected = Translations.LookupRendering("MAJSETTING_PROPERTY_CurrentOutputPath_OPTION_" + status, Language.English);
            Check(!string.IsNullOrWhiteSpace(expected) && settings.CurrentOutputPath == status && Value(view) == expected,
                "external diagnostic changes must refresh the current output enum label: " + status);
            ClickNext(view);
            InputManager.NegativePressed = true;
            view.HandleInput();
            InputManager.NegativePressed = false;
            view.HandleInput();
            Check(settings.CurrentOutputPath == status && RenderingSignature(settings) == snapshot &&
                GameManager.RenderingApplies == 0 && GameManager.SaveRequests == 0,
                "editing the status row changed a setting, requested Apply or saved: " + status);
        }
        var writes = TextMeshProUGUI.TotalWrites;
        for (var frame = 0; frame < 120; frame++) Frame();
        Check(TextMeshProUGUI.TotalWrites == writes, "unchanged diagnostic frames rewrote status text");
        MajRenderDiagnostics.Publish(MajRenderOutputStatus.DirectResolve);
        Frame();
        foreach (var language in Language.All)
        {
            Localization.Change(language);
            Frame();
            Check(Value(View("CurrentOutputPath")) == Translations.LookupRendering(
                "MAJSETTING_PROPERTY_CurrentOutputPath_OPTION_DirectResolve", language),
                language.Code + " did not update the visible status when the language changed");
        }
        Check(GameManager.RenderingApplies == 0 && GameManager.SaveRequests == 0,
            "diagnostic updates or localization triggered a render Apply/save");

        menu.ToHead();
        MajRenderDiagnostics.Publish(MajRenderOutputStatus.IntermediateScaled);
        Frame();
        menu.ToTail();
        Check(Value(View("CurrentOutputPath")) == Translations.LookupRendering(
            "MAJSETTING_PROPERTY_CurrentOutputPath_OPTION_IntermediateScaled", Localization.Current),
            "rebinding the pooled status view missed an offscreen diagnostic change");
        menu.ToOption("CompatibilityMode");
        ClickMenuNext(menu);
        MajRenderDiagnostics.Publish(MajRenderOutputStatus.IntermediateRequested);
        Frame();
        Check(Get<int>(menu, "_displayedOptionCount") == 3 && GameManager.RenderingApplies == 1,
            "diagnostic refresh expanded compatibility mode or repeated its Apply");
        ClickMenuNext(menu);
        menu.ToTail();
        Check(Get<int>(menu, "_displayedOptionCount") == 13 && settings.RenderQuality == RenderingQualityOption.Custom &&
            Value(View("CurrentOutputPath")) == Translations.LookupRendering(
                "MAJSETTING_PROPERTY_CurrentOutputPath_OPTION_IntermediateRequested", Localization.Current),
            "restoring Custom must restore the status row with its latest accepted result");
        menu.ToHead();
        ClickMenuNext(menu);
        MajRenderDiagnostics.Invalidate();
        Frame();
        Check(Get<int>(menu, "_displayedOptionCount") == 4,
            "named presets must hide advanced diagnostics");
        Check(UObject.InstantiateCount == 4 && Localization.Subscribers == Visible.Length && GameManager.SaveRequests == 0,
            "status refresh or visibility changes leaked pooled views/subscriptions or caused a save");
    }

    static void ManagerFrame(SettingManager manager)
    {
        Call(manager, "Update");
        Frame();
        InputManager.ClearFrame();
    }

    static void RenderingLocalizationWarmup()
    {
        var getMenus = typeof(SettingManager).GetMethod("GetMenuMetadata", BindingFlags.Static | BindingFlags.NonPublic);
        var categories = ((Array)getMenus.Invoke(null, new object[] { typeof(GameSetting) })).Cast<object>().Select(value =>
            (string)value.GetType().GetProperty("Name", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value)).ToArray();
        Check(Array.IndexOf(categories, "Rendering") == Array.IndexOf(categories, "Display") + 1 &&
            Array.IndexOf(categories, "Volume") == Array.IndexOf(categories, "Rendering") + 1,
            "the rendering category must be between Display and the nested audio Volume page");

        var settings = new RenderingOptions { CompatibilityMode = true };
        GameManager.RenderingApplies = 0;
        using var fixture = new Fixture(settings);
        var menu = fixture.Menus[0];
        var properties = SettingReflectionCache.GetVisibleProperties(typeof(RenderingOptions));
        foreach (var language in Language.All)
        {
            Localization.Change(language);
            var optionFont = new TMP_FontAsset();
            var descriptionFont = new TMP_FontAsset();
            var warmup = new SettingFontWarmup();
            menu.CollectWarmupText(warmup, new[] { optionFont }, descriptionFont);
            warmup.Warmup();
            Check(!string.IsNullOrWhiteSpace(Translations.LookupRendering("MAJSETTING_CATEGORY_Rendering", language)),
                language.Code + " is missing its rendering category translation");
            foreach (var property in properties)
            {
                var nameKey = "MAJSETTING_PROPERTY_" + property.Name;
                var name = Translations.LookupRendering(nameKey, language);
                var description = Translations.LookupRendering(nameKey + "_DESC", language);
                Check(!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(description), language.Code + " is missing " + property.Name + " text");
                Check(name.All(character => optionFont.characterLookupTable.ContainsKey(character)), language.Code + " did not warm hidden option name " + property.Name);
                Check(description.Where(character => !char.IsControl(character)).All(character => descriptionFont.characterLookupTable.ContainsKey(character)),
                    language.Code + " did not warm hidden option description " + property.Name);
                if (!property.PropertyType.IsEnum) continue;
                foreach (var value in Enum.GetNames(property.PropertyType))
                {
                    var key = nameKey + "_OPTION_" + value;
                    var label = Translations.LookupRendering(key, language);
                    Check(!string.IsNullOrWhiteSpace(label), language.Code + " is missing " + key);
                    Check(label.Where(character => !char.IsControl(character)).All(character => optionFont.characterLookupTable.ContainsKey(character)),
                        language.Code + " did not warm unselected label " + key);
                }
            }
            Check(Get<int>(menu, "_displayedOptionCount") == 3 && GameManager.RenderingApplies == 0, "localization warmup changed compatibility visibility or applied settings");
        }
    }

    static string RenderingSignature(RenderingOptions settings) => $"{settings.RenderQuality}/{settings.RenderScale}/{(int)settings.AntiAliasing}/{settings.TextureQuality}/{settings.OutputMode}";

    static void ClickMenuNext(Menu menu)
    {
        InputManager.PositivePressed = false;
        menu.HandleInput();
        InputManager.PositivePressed = true;
        menu.HandleInput();
        InputManager.PositivePressed = false;
        menu.HandleInput();
        Frame();
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
            InputManager.ClearFrame();
            MajEnv.Settings = new GameSetting();
            MajEnv.Mode = RunningMode.Normal;
            MajInstances.SceneSwitcher.SwitchedScenes.Clear();
            GameManager.SaveRequests = 0;
            GameManager.LastSaveRequester = null;
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
            Set(Manager, "_isInited", true);
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
