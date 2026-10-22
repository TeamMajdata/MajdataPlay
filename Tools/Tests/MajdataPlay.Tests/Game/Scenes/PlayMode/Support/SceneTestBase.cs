using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MajdataPlay.Collections;
using MajdataPlay.Databases;
using MajdataPlay.Diagnostics;
using MajdataPlay.i18n;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes.Skins;
using MajdataPlay.Scenes.List;
using MajdataPlay.Settings;
using MajdataPlay.Settings.Runtime;
using MajdataPlay.Timer;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.Support
{
    /// <summary>
    /// Isolates scene tests from the game's global state: settings, runtime configuration, timeline, input, logging,
    /// singletons and static caches are replaced or snapshotted before each test and restored afterwards.
    /// </summary>
    public abstract class SceneTestBase
    {
        /// <summary>The restore actions run in reverse order after each test.</summary>
        private readonly List<Action> _restoreActions = new();

        /// <summary>The fake samples registered on the stub audio manager, by SFX name.</summary>
        private readonly Dictionary<string, FakeAudioSample> _sfx = new(StringComparer.Ordinal);

        /// <summary>Gets the tracker of every Unity object created by the current test.</summary>
        protected TestObjects Objects { get; private set; } = null!;

        /// <summary>Gets the deterministic time source installed on <see cref="MajTimeline"/>.</summary>
        protected FakeTimeProvider Clock { get; private set; } = null!;

        /// <summary>Gets the stub audio manager, or null when the test did not create one.</summary>
        protected AudioManager? Audio { get; private set; }

        /// <summary>Gets the number of save requests raised through <see cref="GameManager.RequestSave"/> during the test.</summary>
        protected int SaveRequestCount { get; private set; }

        /// <summary>Replaces global state with fresh, test-owned instances.</summary>
        [SetUp]
        public void SetUpSceneEnvironment()
        {
            Objects = new TestObjects();
            _sfx.Clear();
            Audio = null;

            var settings = MajEnv.Settings;
            var runtimeConfig = MajEnv.RuntimeConfig;
            var assetsDirectory = MajEnv.AssetsDirectory;
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.Settings), new GameSetting());
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.RuntimeConfig), new RuntimeConfig());
            _restoreActions.Add(() =>
            {
                PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.Settings), settings);
                PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.RuntimeConfig), runtimeConfig);
                PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.AssetsDirectory), assetsDirectory);
            });

            var timeProvider = MajTimeline.TimeProvider;
            var deltaTime = MajTimeline.UnscaledDeltaTime;
            var timeScale = MajTimeline.TimeScale;
            Clock = new FakeTimeProvider { Ticks = TimeSpan.FromSeconds(100).Ticks };
            MajTimeline.TimeProvider = Clock;
            MajTimeline.TimeScale = 1f;
            SetDeltaTime(0f);
            _restoreActions.Add(() =>
            {
                MajTimeline.TimeProvider = timeProvider;
                MajTimeline.TimeScale = timeScale;
                SetDeltaTime(deltaTime);
            });

            // Production code logs through MajDebug asynchronously; those logs cannot be attributed to a test.
            var minLogLevel = MajDebug.MinLogLevel;
            MajDebug.MinLogLevel = LogLevel.Fatal;
            _restoreActions.Add(() =>
            {
                MajDebug.FlushLog();
                MajDebug.MinLogLevel = minLogLevel;
            });

            var ringEdge = InputManager.TouchButtonRingEdge;
            InputSimulator.Reset();
            _restoreActions.Add(() =>
            {
                InputSimulator.Reset();
                InputManager.TouchButtonRingEdge = ringEdge;
            });

            var language = Localization.Current;
            Localization.Current = Language.Default;
            _restoreActions.Add(() => Localization.Current = language);

            // Saving would serialize the test-owned settings into the staged project's runtime files.
            var onSave = PrivateMember.GetStaticField<EventHandler<EventArgs?>?>(typeof(GameManager), "OnSave");
            SaveRequestCount = 0;
            PrivateMember.SetStaticField(typeof(GameManager), "OnSave", new EventHandler<EventArgs?>((_, _) => SaveRequestCount++));
            _restoreActions.Add(() => PrivateMember.SetStaticField(typeof(GameManager), "OnSave", onSave));

            var backgroundTasks = new List<Task>(ListManager.AllBackgroundTasks);
            _restoreActions.Add(() =>
            {
                // Observe faults so finalizers do not report them as unobserved exceptions during later tests.
                foreach (var task in ListManager.AllBackgroundTasks)
                {
                    if (task is not null && task.IsFaulted)
                    {
                        _ = task.Exception;
                    }
                }
                ListManager.AllBackgroundTasks.Clear();
                ListManager.AllBackgroundTasks.AddRange(backgroundTasks);
            });

            var xxlb = XxlbAnimation.instance;
            _restoreActions.Add(() => XxlbAnimation.instance = xxlb);

            var collections = SongStorage.Collections;
            _restoreActions.Add(() => PrivateMember.SetStaticProperty(typeof(SongStorage), nameof(SongStorage.Collections), collections));

            SnapshotDictionaryKeys(typeof(ScoreManager), "_buckets");
            SnapshotDictionaryKeys(typeof(ChartSettingStorage), "_storage");
            var onlineBuckets = PrivateMember.GetStaticField<object?>(typeof(ScoreManager), "_onlineBuckets");
            _restoreActions.Add(() => PrivateMember.SetStaticField(typeof(ScoreManager), "_onlineBuckets", onlineBuckets));

            SnapshotSingleton<GameManager>();
            SnapshotSingleton<SkinManager>();
            SnapshotSingleton<AudioManager>();
            SnapshotSingleton<SceneSwitcher>();
            SnapshotSingleton<BackgroundVideoController>();
            SnapshotSingleton<ListManager>();
            SnapshotSingleton<CoverListManager>();
            SnapshotSingleton<CollectionListManager>();
            SnapshotSingleton<GameInfo>();
            SnapshotSingleton<GameRuntime>();
            SnapshotSingleton<ChartVisualDisplayer>();
        }

        /// <summary>Destroys the test's objects and restores every snapshotted global.</summary>
        [TearDown]
        public void TearDownSceneEnvironment()
        {
            // Several production motions are not bound to their component's lifetime; stop them before destroying their targets.
            LitMotion.MotionDispatcher.Clear();
            Objects.Dispose();
            for (var i = _restoreActions.Count - 1; i >= 0; i--)
            {
                _restoreActions[i]();
            }
            _restoreActions.Clear();
            _sfx.Clear();
            Audio = null;
        }

        /// <summary>Registers a cleanup action that runs after the test, before the base state is restored.</summary>
        /// <param name="action">The cleanup action.</param>
        protected void AddCleanup(Action action)
        {
            _restoreActions.Add(action);
        }

        /// <summary>Sets the frame duration that <see cref="MajTimeline.DeltaTime"/> reports.</summary>
        /// <param name="seconds">The unscaled frame duration.</param>
        protected static void SetDeltaTime(float seconds)
        {
            PrivateMember.SetStaticProperty(typeof(MajTimeline), nameof(MajTimeline.UnscaledDeltaTime), seconds);
        }

        /// <summary>Creates a component on a new inactive game object so serialized fields can be set before Awake.</summary>
        /// <typeparam name="T">The component type.</typeparam>
        /// <param name="name">The game object name.</param>
        /// <param name="parent">An optional parent.</param>
        /// <returns>The component; activate it with <see cref="Activate"/>.</returns>
        protected T AddInactive<T>(string? name = null, Transform? parent = null) where T : Component
        {
            var gameObject = Objects.Create(name ?? typeof(T).Name, parent);
            gameObject.SetActive(false);
            var component = gameObject.AddComponent<T>();
            Assert.That(component, Is.Not.Null, $"{typeof(T).Name} could not be attached.");
            return component;
        }

        /// <summary>Creates a component on a new inactive UI game object, which has a rect transform.</summary>
        /// <typeparam name="T">The component type.</typeparam>
        /// <param name="name">The game object name.</param>
        /// <param name="parent">An optional parent.</param>
        /// <returns>The component; activate it with <see cref="Activate"/>.</returns>
        protected T AddInactiveUi<T>(string? name = null, Transform? parent = null) where T : Component
        {
            var gameObject = Objects.Create(name ?? typeof(T).Name, parent);
            gameObject.AddComponent<RectTransform>();
            gameObject.SetActive(false);
            var component = gameObject.AddComponent<T>();
            Assert.That(component, Is.Not.Null, $"{typeof(T).Name} could not be attached.");
            return component;
        }

        /// <summary>Activates a component's game object, which runs Awake and OnEnable.</summary>
        /// <param name="component">The component.</param>
        protected static void Activate(Component component)
        {
            component.gameObject.SetActive(true);
        }

        /// <summary>Creates an active canvas so TextMeshPro can generate meshes for children.</summary>
        /// <param name="name">The game object name.</param>
        /// <returns>The canvas transform.</returns>
        protected Transform CreateCanvas(string name = "Canvas")
        {
            var canvas = Objects.Add<Canvas>(name);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            return canvas.transform;
        }

        /// <summary>Creates an active text component.</summary>
        /// <param name="name">The game object name.</param>
        /// <param name="parent">An optional parent.</param>
        /// <returns>The text component.</returns>
        protected TextMeshProUGUI CreateText(string name = "Text", Transform? parent = null)
        {
            return Objects.Add<TextMeshProUGUI>(name, parent);
        }

        /// <summary>Creates an active TextMeshPro input field with a viewport and a text component.</summary>
        /// <param name="name">The game object name.</param>
        /// <param name="parent">An optional parent.</param>
        /// <returns>The input field.</returns>
        protected TMP_InputField CreateInputField(string name = "Input", Transform? parent = null)
        {
            var root = CreateRect(name, parent);
            root.SetActive(false);
            var viewport = CreateRect("Text Area", root.transform);
            viewport.AddComponent<RectMask2D>();
            var text = CreateText("Text", viewport.transform);
            var field = root.AddComponent<TMP_InputField>();
            field.textViewport = (RectTransform)viewport.transform;
            field.textComponent = text;
            root.SetActive(true);
            return field;
        }

        /// <summary>Creates an active image component.</summary>
        /// <param name="name">The game object name.</param>
        /// <param name="parent">An optional parent.</param>
        /// <returns>The image component.</returns>
        protected Image CreateImage(string name = "Image", Transform? parent = null)
        {
            return Objects.Add<Image>(name, parent);
        }

        /// <summary>Creates an active game object with a rect transform.</summary>
        /// <param name="name">The game object name.</param>
        /// <param name="parent">An optional parent.</param>
        /// <returns>The game object.</returns>
        protected GameObject CreateRect(string name = "Rect", Transform? parent = null)
        {
            var gameObject = Objects.Create(name, parent);
            gameObject.AddComponent<RectTransform>();
            return gameObject;
        }

        /// <summary>Creates a small readable sprite tracked for destruction.</summary>
        /// <param name="name">The sprite name.</param>
        /// <param name="size">The texture edge length; zero creates an empty sprite.</param>
        /// <returns>The sprite.</returns>
        protected Sprite CreateSprite(string name = "Sprite", int size = 4)
        {
            var texture = Objects.Track(new Texture2D(Math.Max(1, size), Math.Max(1, size)));
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            return Objects.Track(sprite);
        }

        /// <summary>Creates a runtime database with seven distinct difficulty colors and installs it as the singleton.</summary>
        /// <returns>The runtime database.</returns>
        protected GameRuntime CreateRuntimeDatabase()
        {
            var database = Objects.Track(ScriptableObject.CreateInstance<GameRuntime>());
            PrivateMember.SetField(database, "_difficultyColors", DifficultyColors());
            Majdata<GameRuntime>.Free();
            Majdata<GameRuntime>.Instance = database;
            return database;
        }

        /// <summary>Gets seven distinct difficulty colors matching <see cref="CreateRuntimeDatabase"/>.</summary>
        /// <returns>The colors indexed by chart level.</returns>
        protected static Color[] DifficultyColors()
        {
            return new[]
            {
                new Color(0.1f, 0.9f, 0.1f, 1f),
                new Color(0.9f, 0.9f, 0.1f, 1f),
                new Color(0.9f, 0.1f, 0.1f, 1f),
                new Color(0.6f, 0.1f, 0.9f, 1f),
                new Color(0.8f, 0.6f, 1f, 1f),
                new Color(1f, 1f, 1f, 1f),
                new Color(1f, 0.4f, 0.8f, 1f),
            };
        }

        /// <summary>Installs an inactive game manager as the singleton so its settings accessor is reachable without Awake.</summary>
        /// <returns>The game manager stub.</returns>
        protected GameManager CreateGameManagerStub()
        {
            var manager = AddInactive<GameManager>("GameManagerStub");
            Majdata<GameManager>.Free();
            Majdata<GameManager>.Instance = manager;
            return manager;
        }

        /// <summary>Installs an inactive scene switcher whose transition references are missing, so fades are no-ops.</summary>
        /// <returns>The scene switcher stub.</returns>
        protected SceneSwitcher CreateSceneSwitcherStub()
        {
            var switcher = AddInactive<SceneSwitcher>("SceneSwitcherStub");
            Majdata<SceneSwitcher>.Free();
            Majdata<SceneSwitcher>.Instance = switcher;
            return switcher;
        }

        /// <summary>
        /// Expects the exception a scene switch raises with the stub scene switcher, whose missing overlay images stop the
        /// switch before any scene is loaded.
        /// </summary>
        protected static void ExpectSceneSwitch()
        {
            LogAssert.Expect(LogType.Exception, new Regex("NullReferenceException"));
        }

        /// <summary>Installs an inactive skin manager that exposes one unloaded skin.</summary>
        /// <param name="skinName">The skin name.</param>
        /// <returns>The skin manager stub.</returns>
        private protected SkinManager CreateSkinManagerStub(string skinName = "Default")
        {
            var manager = AddInactive<SkinManager>("SkinManagerStub");
            var skin = (CustomSkin)FormatterServices.GetUninitializedObject(typeof(CustomSkin));
            typeof(CustomSkin).GetField("<Name>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(skin, skinName);
            PrivateMember.SetField(manager, "_selectedSkin", skin);
            PrivateMember.SetField(manager, "_loadedSkinArray", new[] { skin });
            Majdata<SkinManager>.Free();
            Majdata<SkinManager>.Instance = manager;
            return manager;
        }

        /// <summary>Creates a dynamic font asset from an installed system font, tracked for destruction.</summary>
        /// <returns>The font asset.</returns>
        protected TMP_FontAsset CreateDynamicFont()
        {
            var asset = TMP_FontAsset.CreateFontAsset("Arial", "Regular")
                ?? TMP_FontAsset.CreateFontAsset("Segoe UI", "Regular");
            Assert.That(asset, Is.Not.Null, "No system font is available to create a dynamic TMP font asset.");
            Objects.Track(asset!);
            return asset!;
        }

        /// <summary>Installs an inactive audio manager whose SFX list holds fake samples.</summary>
        /// <param name="sfxNames">The SFX names to register.</param>
        /// <returns>The audio manager stub.</returns>
        protected AudioManager CreateAudioManager(params string[] sfxNames)
        {
            var manager = AddInactive<AudioManager>("AudioManagerStub");
            Majdata<AudioManager>.Free();
            Majdata<AudioManager>.Instance = manager;
            Audio = manager;
            foreach (var name in sfxNames)
            {
                AddSfx(name);
            }
            return manager;
        }

        /// <summary>Adds a fake SFX sample to the stub audio manager.</summary>
        /// <param name="name">The SFX name.</param>
        /// <returns>The fake sample.</returns>
        /// <exception cref="InvalidOperationException">No audio manager was created.</exception>
        protected FakeAudioSample AddSfx(string name)
        {
            if (Audio is null)
            {
                throw new InvalidOperationException("Create the audio manager first.");
            }
            var sample = new FakeAudioSample(name);
            PrivateMember.GetField<List<AudioSampleWrap>>(Audio, "SFXSamples").Add(sample);
            _sfx[name] = sample;
            return sample;
        }

        /// <summary>Gets a fake SFX sample registered on the stub audio manager.</summary>
        /// <param name="name">The SFX name.</param>
        /// <returns>The fake sample.</returns>
        protected FakeAudioSample Sfx(string name)
        {
            return _sfx[name];
        }

        /// <summary>Creates a song collection from fake songs.</summary>
        /// <param name="name">The collection name.</param>
        /// <param name="songs">The songs.</param>
        /// <returns>The collection.</returns>
        protected static SongCollection CreateCollection(string name, params ISongDetail[] songs)
        {
            return new SongCollection(name, songs)
            {
                Id = Guid.NewGuid()
            };
        }

        /// <summary>Creates fake songs with unique hashes.</summary>
        /// <param name="prefix">The hash and title prefix.</param>
        /// <param name="count">The number of songs.</param>
        /// <returns>The songs.</returns>
        protected static FakeSongDetail[] CreateSongs(string prefix, int count)
        {
            var songs = new FakeSongDetail[count];
            for (var i = 0; i < count; i++)
            {
                songs[i] = new FakeSongDetail($"majtest-{prefix}-{i}", $"{prefix} {i}", "1", "3", "7", "10", "13", "14+", string.Empty);
            }
            return songs;
        }

        /// <summary>Waits frame by frame until a condition holds or a real-time limit is reached.</summary>
        /// <param name="condition">The condition.</param>
        /// <param name="timeoutSeconds">The maximum real time to wait.</param>
        /// <returns>The coroutine.</returns>
        protected static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds = 5f)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        /// <summary>Removes, after the test, every dictionary key a test added to a static cache.</summary>
        /// <param name="owner">The type declaring the static dictionary.</param>
        /// <param name="fieldName">The dictionary field name.</param>
        private void SnapshotDictionaryKeys(Type owner, string fieldName)
        {
            var dictionary = PrivateMember.GetStaticField<IDictionary>(owner, fieldName);
            var keys = new HashSet<object>();
            foreach (var key in dictionary.Keys)
            {
                keys.Add(key);
            }
            _restoreActions.Add(() =>
            {
                var added = new List<object>();
                foreach (var key in dictionary.Keys)
                {
                    if (!keys.Contains(key))
                    {
                        added.Add(key);
                    }
                }
                foreach (var key in added)
                {
                    dictionary.Remove(key);
                }
            });
        }

        /// <summary>Restores a <see cref="Majdata{T}"/> singleton slot after the test.</summary>
        /// <typeparam name="T">The singleton type.</typeparam>
        private void SnapshotSingleton<T>() where T : class
        {
            var instance = Majdata<T>.Instance;
            var isSingleton = Majdata<T>.IsSingleton;
            _restoreActions.Add(() =>
            {
                Majdata<T>.Free();
                if (instance is null)
                {
                    return;
                }
                if (isSingleton)
                {
                    Majdata<T>.SetAsSingleton(instance);
                }
                else
                {
                    Majdata<T>.Instance = instance;
                }
            });
        }
    }
}
