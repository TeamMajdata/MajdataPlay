using System;
using MajdataPlay.Databases;
using MajdataPlay.Game.Notes;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.Game.Notes.Controllers;
using MajdataPlay.Scenes.Game.Notes.Skins;
using MajdataPlay.Scenes.Game.Notes.Touch;
using MajdataPlay.Settings;
using MajdataPlay.Tests;
using UnityEngine;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode.Support
{
    /// <summary>
    /// Builds the part of the gameplay scene that note behaviours depend on: the note manager with its layers and
    /// updaters, the pool, effect, audio and multi-touch managers, the object counter, a fake note controller and a skin
    /// with distinct sprites. One frame must pass after construction so that Unity runs the Start messages.
    /// </summary>
    internal sealed class GameplayRig : IDisposable
    {
        /// <summary>The index of the tap perfect sound request.</summary>
        public const int SfxTapPerfect = 0;

        /// <summary>The index of the tap great sound request.</summary>
        public const int SfxTapGreat = 1;

        /// <summary>The index of the tap good sound request.</summary>
        public const int SfxTapGood = 2;

        /// <summary>The index of the EX tap sound request.</summary>
        public const int SfxTapEx = 3;

        /// <summary>The index of the break judgment sound request.</summary>
        public const int SfxBreakJudge = 4;

        /// <summary>The index of the break perfect sound request.</summary>
        public const int SfxBreak = 5;

        /// <summary>The index of the slide sound request.</summary>
        public const int SfxSlide = 6;

        /// <summary>The index of the break slide start sound request.</summary>
        public const int SfxBreakSlide = 7;

        /// <summary>The index of the break slide judgment sound request.</summary>
        public const int SfxBreakSlideJudge = 8;

        /// <summary>The index of the touch sound request.</summary>
        public const int SfxTouch = 9;

        /// <summary>The index of the touch hold riser sound request.</summary>
        public const int SfxTouchHold = 10;

        /// <summary>The index of the firework sound request.</summary>
        public const int SfxFirework = 11;

        /// <summary>The index of the answer sound request.</summary>
        public const int SfxAnswer = 12;

        /// <summary>The index of the answer clock sound request.</summary>
        public const int SfxAnswerClock = 13;

        /// <summary>The index of the mine answer sound request.</summary>
        public const int SfxMine = 14;

        /// <summary>The index of the miss sound request.</summary>
        public const int SfxMiss = 15;

        /// <summary>Creates the gameplay objects.</summary>
        /// <param name="initializePools">Whether to initialize the note pools with zero capacity right away.</param>
        /// <param name="initializeEffects">Whether to initialize the effect pool for every key and sensor right away.</param>
        public GameplayRig(bool initializePools = true, bool initializeEffects = true)
        {
            Environment = new NoteTestEnvironment();
            try
            {
                Templates = Objects.Create("Templates").transform;
                Templates.gameObject.SetActive(false);

                Skin = TestSkinFactory.Create(Objects);
                SkinManager = CreateInactive<SkinManager>("SkinManager");
                PrivateMember.SetField(SkinManager, "_selectedSkin", Skin);
                FillSkinCaches();
                Majdata<SkinManager>.Instance = SkinManager;

                AudioOutput = CreateInactive<AudioManager>("AudioManager");
                Majdata<AudioManager>.Instance = AudioOutput;

                Runtime = Resources.Load<GameRuntime>("Databases/RuntimeDatabase");
                if (Runtime != null)
                {
                    Majdata<GameRuntime>.Instance = Runtime;
                }
                DefaultMaterial = Objects.Track(new Material(Shader.Find("Sprites/Default")));
                BreakMaterial = Objects.Track(new Material(DefaultMaterial) { name = "Break" });
                HoldShineMaterial = Objects.Track(new Material(DefaultMaterial) { name = "HoldShine" });
                Controller = new FakeNoteController()
                {
                    DefaultMaterial = DefaultMaterial,
                    BreakMaterial = BreakMaterial,
                    HoldShineMaterial = HoldShineMaterial
                };
                Majdata<INoteController>.Instance = Controller;

                Counter = CreateInactive<ObjectCounter>("ObjectCounter");
                Majdata<ObjectCounter>.Instance = Counter;
                Loader = CreateInactive<NoteLoader>("NoteLoader");
                Majdata<NoteLoader>.Instance = Loader;
                SetLoaderFlags(true);

                var fireworkHolder = Objects.Create("FireworkHolder");
                fireworkHolder.SetActive(false);
                Firework = NoteTemplates.Firework(fireworkHolder.transform);
                fireworkHolder.SetActive(true);

                TapLineTemplate = NoteTemplates.TapLine(Templates);
                StarTemplate = NoteTemplates.Star(Templates);
                TapTemplate = NoteTemplates.Tap(Templates, TapLineTemplate);
                HoldTemplate = NoteTemplates.Hold(Templates, TapLineTemplate);
                TouchTemplate = NoteTemplates.Touch(Templates);
                TouchHoldTemplate = NoteTemplates.TouchHold(Templates);
                EachLineTemplate = NoteTemplates.EachLine(Templates);
                TouchBorderTemplate = NoteTemplates.TouchBorder(Templates);
                TapEffectTemplate = NoteTemplates.TapEffect(Templates, false);
                TouchHoldEffectTemplate = NoteTemplates.TapEffect(Templates, true);
                TouchEffectTemplate = NoteTemplates.TouchEffect(Templates);
                HoldEffectTemplate = NoteTemplates.HoldEffect(Templates, DefaultMaterial);
                FeedbackTemplate = NoteTemplates.TouchFeedback(Templates);

                var notes = Objects.Create("Notes");
                for (var i = 0; i < 8; i++)
                {
                    NoteTemplates.Child($"Layer{i}", notes.transform);
                }
                NoteManager = notes.AddComponent<NoteManager>();
                SlideParent = notes.transform.GetChild(3);

                var pools = Objects.Create("Pools");
                for (var i = 0; i < 7; i++)
                {
                    NoteTemplates.Child($"Pool{i}", pools.transform);
                }
                PoolManager = pools.AddComponent<NotePoolManager>();
                PrivateMember.SetField(PoolManager, "tapPrefab", TapTemplate);
                PrivateMember.SetField(PoolManager, "holdPrefab", HoldTemplate);
                PrivateMember.SetField(PoolManager, "touchPrefab", TouchTemplate);
                PrivateMember.SetField(PoolManager, "touchHoldPrefab", TouchHoldTemplate);
                PrivateMember.SetField(PoolManager, "eachLinePrefab", EachLineTemplate);

                TapUpdater = AddUpdater<TapUpdater>(TapParent);
                HoldUpdater = AddUpdater<HoldUpdater>(HoldParent);
                TouchUpdater = AddUpdater<TouchUpdater>(TouchParent);
                TouchHoldUpdater = AddUpdater<TouchHoldUpdater>(TouchHoldParent);
                EachLineUpdater = AddUpdater<EachLineUpdater>(EachLineParent);
                SlideUpdater = AddUpdater<SlideUpdater>(SlideParent);
                SlideOKUpdater = AddUpdater<SlideOKUpdater>(SlideParent);

                TouchHandler = Objects.Add<MultTouchHandler>("MultTouchHandler");
                TouchHandler.BorderPrefab = TouchBorderTemplate;

                NoteAudio = Objects.Add<NoteAudioManager>("NoteAudioManager");

                var effects = Objects.Create("Effects");
                for (var i = 0; i < 4; i++)
                {
                    NoteTemplates.Child($"EffectLayer{i}", effects.transform);
                }
                EffectPool = effects.AddComponent<NoteEffectPool>();
                PrivateMember.SetField(EffectPool, "_tapEffectPrefab", TapEffectTemplate);
                PrivateMember.SetField(EffectPool, "_touchHoldEffectPrefab", TouchHoldEffectTemplate);
                PrivateMember.SetField(EffectPool, "_touchEffectPrefab", TouchEffectTemplate);
                PrivateMember.SetField(EffectPool, "_holdEffectPrefab", HoldEffectTemplate);
                PrivateMember.SetField(EffectPool, "_touchFeedbackEffectPrefab", FeedbackTemplate);
                EffectManager = Objects.Add<NoteEffectManager>("NoteEffectManager");

                if (initializePools)
                {
                    PoolManager.Init();
                }
                if (initializeEffects)
                {
                    EffectPool.Init();
                }
            }
            catch
            {
                Environment.Dispose();
                throw;
            }
        }

        /// <summary>Gets the isolation scope that owns all objects and restores global state.</summary>
        public NoteTestEnvironment Environment { get; }

        /// <summary>Gets the object tracker of the test.</summary>
        public TestObjects Objects => Environment.Objects;

        /// <summary>Gets the settings installed for the test.</summary>
        public GameSetting Settings => Environment.Settings;

        /// <summary>Gets the inactive root that holds every template.</summary>
        public Transform Templates { get; }

        /// <summary>Gets the generated skin.</summary>
        public CustomSkin Skin { get; }

        /// <summary>Gets the skin manager that serves the generated skin.</summary>
        public SkinManager SkinManager { get; }

        /// <summary>Gets the audio manager that serves empty sound effects.</summary>
        public AudioManager AudioOutput { get; }

        /// <summary>Gets the runtime database loaded from resources, if available.</summary>
        public GameRuntime? Runtime { get; }

        /// <summary>Gets the material used for ordinary notes.</summary>
        public Material DefaultMaterial { get; }

        /// <summary>Gets the material used for break notes.</summary>
        public Material BreakMaterial { get; }

        /// <summary>Gets the material used for pressed holds.</summary>
        public Material HoldShineMaterial { get; }

        /// <summary>Gets the fake note controller that provides the chart clock.</summary>
        public FakeNoteController Controller { get; }

        /// <summary>Gets the object counter that receives judgment results.</summary>
        public ObjectCounter Counter { get; }

        /// <summary>Gets the note loader whose key flags drive pool and effect creation.</summary>
        public NoteLoader Loader { get; }

        /// <summary>Gets the firework effect.</summary>
        public FireworkDisplayer Firework { get; }

        /// <summary>Gets the note manager.</summary>
        public NoteManager NoteManager { get; }

        /// <summary>Gets the parent of slides, slide stars and slide results.</summary>
        public Transform SlideParent { get; }

        /// <summary>Gets the note pool manager.</summary>
        public NotePoolManager PoolManager { get; }

        /// <summary>Gets the parent of pooled taps.</summary>
        public Transform TapParent => PoolManager.transform.GetChild(0);

        /// <summary>Gets the parent of pooled holds.</summary>
        public Transform HoldParent => PoolManager.transform.GetChild(1);

        /// <summary>Gets the parent of pooled touches.</summary>
        public Transform TouchParent => PoolManager.transform.GetChild(4);

        /// <summary>Gets the parent of pooled touch holds.</summary>
        public Transform TouchHoldParent => PoolManager.transform.GetChild(5);

        /// <summary>Gets the parent of pooled each lines.</summary>
        public Transform EachLineParent => PoolManager.transform.GetChild(6);

        /// <summary>Gets the tap updater.</summary>
        public TapUpdater TapUpdater { get; }

        /// <summary>Gets the hold updater.</summary>
        public HoldUpdater HoldUpdater { get; }

        /// <summary>Gets the touch updater.</summary>
        public TouchUpdater TouchUpdater { get; }

        /// <summary>Gets the touch hold updater.</summary>
        public TouchHoldUpdater TouchHoldUpdater { get; }

        /// <summary>Gets the each line updater.</summary>
        public EachLineUpdater EachLineUpdater { get; }

        /// <summary>Gets the slide updater.</summary>
        public SlideUpdater SlideUpdater { get; }

        /// <summary>Gets the slide result updater.</summary>
        public SlideOKUpdater SlideOKUpdater { get; }

        /// <summary>Gets the multiple-touch border handler.</summary>
        public MultTouchHandler TouchHandler { get; }

        /// <summary>Gets the note audio manager.</summary>
        public NoteAudioManager NoteAudio { get; }

        /// <summary>Gets the effect pool.</summary>
        public NoteEffectPool EffectPool { get; }

        /// <summary>Gets the effect manager.</summary>
        public NoteEffectManager EffectManager { get; }

        /// <summary>Gets the tap guide line template.</summary>
        public GameObject TapLineTemplate { get; }

        /// <summary>Gets the slide star template.</summary>
        public GameObject StarTemplate { get; }

        /// <summary>Gets the tap template.</summary>
        public GameObject TapTemplate { get; }

        /// <summary>Gets the hold template.</summary>
        public GameObject HoldTemplate { get; }

        /// <summary>Gets the touch template.</summary>
        public GameObject TouchTemplate { get; }

        /// <summary>Gets the touch hold template.</summary>
        public GameObject TouchHoldTemplate { get; }

        /// <summary>Gets the each line template.</summary>
        public GameObject EachLineTemplate { get; }

        /// <summary>Gets the multiple-touch border template.</summary>
        public GameObject TouchBorderTemplate { get; }

        /// <summary>Gets the tap effect template.</summary>
        public GameObject TapEffectTemplate { get; }

        /// <summary>Gets the touch hold effect template.</summary>
        public GameObject TouchHoldEffectTemplate { get; }

        /// <summary>Gets the touch effect template.</summary>
        public GameObject TouchEffectTemplate { get; }

        /// <summary>Gets the hold particle effect template.</summary>
        public GameObject HoldEffectTemplate { get; }

        /// <summary>Gets the touch feedback template.</summary>
        public GameObject FeedbackTemplate { get; }

        /// <summary>Gets the sound requests of the current frame.</summary>
        public bool[] SoundRequests => PrivateMember.GetStaticField<bool[]>(typeof(NoteAudioManager), "_noteSFXPlaybackRequests");

        /// <summary>Runs one gameplay frame in the order used by the gameplay manager.</summary>
        /// <param name="sec">The chart time of the frame in seconds.</param>
        public void Step(float sec)
        {
            Controller.ThisFrameSec = sec;
            NoteAudio.OnPreUpdate();
            NoteManager.OnPreUpdate();
            PoolManager.OnPreUpdate();
            NoteManager.OnUpdate();
            NoteAudio.OnLateUpdate();
            NoteManager.OnLateUpdate();
            EffectPool.OnLateUpdate();
        }

        /// <summary>Writes the raw button state that the next frame reads from the input manager.</summary>
        /// <param name="zone">The button.</param>
        /// <param name="previous">The state in the previous frame.</param>
        /// <param name="current">The state in the current frame.</param>
        public void SetButton(ButtonZone zone, SwitchStatus previous, SwitchStatus current)
        {
            PrivateMember.GetStaticField<SwitchStatus[]>(typeof(InputManager), "_btnStatusInPreviousFrame")[(int)zone] = previous;
            PrivateMember.GetStaticField<SwitchStatus[]>(typeof(InputManager), "_btnStatusInThisFrame")[(int)zone] = current;
        }

        /// <summary>Writes the raw sensor state that the next frame reads from the input manager.</summary>
        /// <param name="area">The sensor.</param>
        /// <param name="previous">The state in the previous frame.</param>
        /// <param name="current">The state in the current frame.</param>
        public void SetSensor(SensorArea area, SwitchStatus previous, SwitchStatus current)
        {
            PrivateMember.GetStaticField<SwitchStatus[]>(typeof(InputManager), "_sensorStatusInPreviousFrame")[(int)area] = previous;
            PrivateMember.GetStaticField<SwitchStatus[]>(typeof(InputManager), "_sensorStatusInThisFrame")[(int)area] = current;
        }

        /// <summary>Releases every button and sensor in the raw input state.</summary>
        public void ClearInput()
        {
            Array.Clear(PrivateMember.GetStaticField<SwitchStatus[]>(typeof(InputManager), "_btnStatusInPreviousFrame"), 0, 12);
            Array.Clear(PrivateMember.GetStaticField<SwitchStatus[]>(typeof(InputManager), "_btnStatusInThisFrame"), 0, 12);
            Array.Clear(PrivateMember.GetStaticField<SwitchStatus[]>(typeof(InputManager), "_sensorStatusInPreviousFrame"), 0, 33);
            Array.Clear(PrivateMember.GetStaticField<SwitchStatus[]>(typeof(InputManager), "_sensorStatusInThisFrame"), 0, 33);
        }

        /// <summary>Sets every key and sensor flag of the note loader.</summary>
        /// <param name="value">The flag value.</param>
        public void SetLoaderFlags(bool value)
        {
            foreach (var name in new[] { "_isHasTap", "_isHasHold", "_isHasTouch", "_isHasTouchHold" })
            {
                var flags = PrivateMember.GetStaticField<bool[]>(typeof(NoteLoader), name);
                for (var i = 0; i < flags.Length; i++)
                {
                    flags[i] = value;
                }
            }
        }

        /// <summary>Instantiates a template into an active parent, which runs its Awake.</summary>
        /// <typeparam name="T">The component to return.</typeparam>
        /// <param name="template">The template.</param>
        /// <param name="parent">The active parent.</param>
        /// <returns>The component on the clone.</returns>
        public T Spawn<T>(GameObject template, Transform parent) where T : Component
        {
            var clone = Object.Instantiate(template, parent);
            return clone.GetComponent<T>();
        }

        /// <summary>Spawns and initializes a tap and registers it with the tap updater.</summary>
        /// <param name="info">The pooling information.</param>
        /// <returns>The tap.</returns>
        public TapDrop SpawnTap(TapPoolingInfo info)
        {
            var tap = Spawn<TapDrop>(TapTemplate, TapParent);
            tap.Init(info);
            TapUpdater.Init();
            return tap;
        }

        /// <summary>Spawns and initializes a hold and registers it with the hold updater.</summary>
        /// <param name="info">The pooling information.</param>
        /// <returns>The hold.</returns>
        public HoldDrop SpawnHold(HoldPoolingInfo info)
        {
            var hold = Spawn<HoldDrop>(HoldTemplate, HoldParent);
            hold.Init(info);
            HoldUpdater.Init();
            return hold;
        }

        /// <summary>Spawns and initializes a touch and registers it with the touch updater.</summary>
        /// <param name="info">The pooling information.</param>
        /// <returns>The touch.</returns>
        public TouchDrop SpawnTouch(TouchPoolingInfo info)
        {
            var touch = Spawn<TouchDrop>(TouchTemplate, TouchParent);
            touch.Init(info);
            TouchUpdater.Init();
            return touch;
        }

        /// <summary>Spawns and initializes a touch hold and registers it with the touch hold updater.</summary>
        /// <param name="info">The pooling information.</param>
        /// <returns>The touch hold.</returns>
        public TouchHoldDrop SpawnTouchHold(TouchHoldPoolingInfo info)
        {
            var touchHold = Spawn<TouchHoldDrop>(TouchHoldTemplate, TouchHoldParent);
            touchHold.Init(info);
            TouchHoldUpdater.Init();
            return touchHold;
        }

        /// <summary>Spawns and initializes an each line and registers it with the each line updater.</summary>
        /// <param name="info">The pooling information.</param>
        /// <returns>The each line.</returns>
        public EachLineDrop SpawnEachLine(EachLinePoolingInfo info)
        {
            var eachLine = Spawn<EachLineDrop>(EachLineTemplate, EachLineParent);
            eachLine.Init(info);
            EachLineUpdater.Init();
            return eachLine;
        }

        /// <summary>Creates a predefined slide template.</summary>
        /// <param name="slideType">The slide table name.</param>
        /// <param name="barCount">The number of arrow bars.</param>
        /// <param name="endPos">The key the bars run towards.</param>
        /// <returns>The template.</returns>
        public GameObject CreateSlideTemplate(string slideType, int barCount = 12, int endPos = 3)
        {
            return NoteTemplates.Slide(Templates, slideType, barCount, endPos, StarTemplate);
        }

        /// <summary>Spawns a slide from a template under the slide layer.</summary>
        /// <typeparam name="T">The slide component type.</typeparam>
        /// <param name="template">The slide template.</param>
        /// <returns>The slide.</returns>
        public T SpawnSlide<T>(GameObject template) where T : Component
        {
            return Spawn<T>(template, SlideParent);
        }

        /// <summary>Gets the number of slide stars created under the slide layer.</summary>
        public int SlideStarCount
        {
            get
            {
                var count = 0;
                foreach (Transform child in SlideParent)
                {
                    if (child.name.StartsWith(StarTemplate.name))
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        /// <summary>Gets a slide star created under the slide layer, in creation order.</summary>
        /// <param name="index">The zero-based star index.</param>
        /// <returns>The star transform.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Fewer stars exist.</exception>
        public Transform SlideStar(int index)
        {
            foreach (Transform child in SlideParent)
            {
                if (child.name.StartsWith(StarTemplate.name) && index-- == 0)
                {
                    return child;
                }
            }
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        /// <summary>Registers slides with the slide updater and the slide results with the slide result updater.</summary>
        /// <param name="slides">The slides with the time they become active.</param>
        public void RegisterSlides(params (SlideBase Slide, float AppearTiming)[] slides)
        {
            var infos = new SlideQueueInfo[slides.Length];
            for (var i = 0; i < slides.Length; i++)
            {
                infos[i] = new SlideQueueInfo()
                {
                    Index = i,
                    AppearTiming = slides[i].AppearTiming,
                    SlideObject = slides[i].Slide
                };
            }
            SlideUpdater.AddSlideQueueInfos(infos);
            SlideUpdater.Init();
            SlideOKUpdater.Init();
        }

        /// <summary>Creates tap pooling information.</summary>
        /// <param name="startPos">The key from 1 to 8.</param>
        /// <param name="timing">The judge timing in seconds.</param>
        /// <param name="isBreak">Whether the note is a break.</param>
        /// <param name="isEach">Whether the note is an each note.</param>
        /// <param name="isEx">Whether the note is an EX note.</param>
        /// <param name="isMine">Whether the note is a mine.</param>
        /// <param name="isStar">Whether the note is a slide star head.</param>
        /// <param name="isDouble">Whether the star head is a double star.</param>
        /// <param name="index">The queue index on its key.</param>
        /// <param name="binding">The optional each line binding.</param>
        /// <returns>The information.</returns>
        public static TapPoolingInfo TapInfo(int startPos = 1,
                                             float timing = 1f,
                                             bool isBreak = false,
                                             bool isEach = false,
                                             bool isEx = false,
                                             bool isMine = false,
                                             bool isStar = false,
                                             bool isDouble = false,
                                             long index = 0,
                                             EachLineBinding? binding = null)
        {
            return new TapPoolingInfo()
            {
                StartPos = startPos,
                Timing = timing,
                AppearTiming = timing - 1f,
                Speed = 7.5f,
                NoteSortOrder = 100,
                IsBreak = isBreak,
                IsEach = isEach,
                IsEX = isEx,
                IsMine = isMine,
                IsStar = isStar,
                IsDouble = isDouble,
                RotateSpeed = 90f,
                QueueInfo = new TapQueueInfo()
                {
                    Index = index,
                    KeyIndex = startPos
                },
                EachLineBinding = binding
            };
        }

        /// <summary>Creates hold pooling information.</summary>
        /// <param name="startPos">The key from 1 to 8.</param>
        /// <param name="timing">The judge timing in seconds.</param>
        /// <param name="length">The hold length in seconds.</param>
        /// <param name="isBreak">Whether the note is a break.</param>
        /// <param name="isEach">Whether the note is an each note.</param>
        /// <param name="isEx">Whether the note is an EX note.</param>
        /// <param name="isMine">Whether the note is a mine.</param>
        /// <param name="index">The queue index on its key.</param>
        /// <returns>The information.</returns>
        public static HoldPoolingInfo HoldInfo(int startPos = 1,
                                               float timing = 1f,
                                               float length = 1f,
                                               bool isBreak = false,
                                               bool isEach = false,
                                               bool isEx = false,
                                               bool isMine = false,
                                               long index = 0)
        {
            return new HoldPoolingInfo()
            {
                StartPos = startPos,
                Timing = timing,
                AppearTiming = timing - 1f,
                Speed = 7.5f,
                NoteSortOrder = 100,
                LastFor = length,
                IsBreak = isBreak,
                IsEach = isEach,
                IsEX = isEx,
                IsMine = isMine,
                QueueInfo = new TapQueueInfo()
                {
                    Index = index,
                    KeyIndex = startPos
                }
            };
        }

        /// <summary>Creates touch pooling information.</summary>
        /// <param name="area">The sensor.</param>
        /// <param name="timing">The judge timing in seconds.</param>
        /// <param name="isBreak">Whether the note is a break.</param>
        /// <param name="isEach">Whether the note is an each note.</param>
        /// <param name="isMine">Whether the note is a mine.</param>
        /// <param name="isFirework">Whether the note triggers a firework.</param>
        /// <param name="group">The optional touch group.</param>
        /// <param name="index">The queue index on its sensor.</param>
        /// <returns>The information.</returns>
        public static TouchPoolingInfo TouchInfo(SensorArea area = SensorArea.C,
                                                 float timing = 1f,
                                                 bool isBreak = false,
                                                 bool isEach = false,
                                                 bool isMine = false,
                                                 bool isFirework = false,
                                                 TouchGroup? group = null,
                                                 long index = 0)
        {
            return new TouchPoolingInfo()
            {
                StartPos = 1,
                Timing = timing,
                AppearTiming = timing - 1f,
                Speed = 7.5f,
                NoteSortOrder = 100,
                SensorPos = area,
                AreaPos = area.ToString()[0],
                IsBreak = isBreak,
                IsEach = isEach,
                IsMine = isMine,
                IsFirework = isFirework,
                GroupInfo = group,
                QueueInfo = new TouchQueueInfo()
                {
                    Index = index,
                    SensorPos = area
                }
            };
        }

        /// <summary>Creates touch hold pooling information.</summary>
        /// <param name="area">The sensor.</param>
        /// <param name="timing">The judge timing in seconds.</param>
        /// <param name="length">The hold length in seconds.</param>
        /// <param name="isBreak">Whether the note is a break.</param>
        /// <param name="isEach">Whether the note is an each note.</param>
        /// <param name="isMine">Whether the note is a mine.</param>
        /// <param name="isFirework">Whether the note triggers a firework.</param>
        /// <param name="group">The optional head touch group.</param>
        /// <param name="bodyGroup">The optional body touch hold group.</param>
        /// <param name="index">The queue index on its sensor.</param>
        /// <returns>The information.</returns>
        public static TouchHoldPoolingInfo TouchHoldInfo(SensorArea area = SensorArea.C,
                                                         float timing = 1f,
                                                         float length = 1f,
                                                         bool isBreak = false,
                                                         bool isEach = false,
                                                         bool isMine = false,
                                                         bool isFirework = false,
                                                         TouchGroup? group = null,
                                                         TouchHoldGroup? bodyGroup = null,
                                                         long index = 0)
        {
            return new TouchHoldPoolingInfo()
            {
                StartPos = 1,
                Timing = timing,
                AppearTiming = timing - 1f,
                Speed = 7.5f,
                NoteSortOrder = 100,
                LastFor = length,
                SensorPos = area,
                AreaPos = area.ToString()[0],
                IsBreak = isBreak,
                IsEach = isEach,
                IsMine = isMine,
                IsFirework = isFirework,
                GroupInfo = group,
                TouchHoldGroupInfo = bodyGroup,
                QueueInfo = new TouchQueueInfo()
                {
                    Index = index,
                    SensorPos = area
                }
            };
        }

        /// <summary>Destroys the gameplay objects and restores the global state.</summary>
        public void Dispose()
        {
            Environment.Dispose();
        }

        /// <summary>Creates a component on an inactive object so that none of its messages run.</summary>
        /// <typeparam name="T">The component type.</typeparam>
        /// <param name="name">The object name.</param>
        /// <returns>The component.</returns>
        private T CreateInactive<T>(string name) where T : Component
        {
            var gameObject = Objects.Create(name);
            gameObject.SetActive(false);
            return gameObject.AddComponent<T>();
        }

        /// <summary>Creates an updater whose note list root is the given transform.</summary>
        /// <typeparam name="T">The updater type.</typeparam>
        /// <param name="root">The parent of the notes it updates.</param>
        /// <returns>The updater.</returns>
        private T AddUpdater<T>(Transform root) where T : Component
        {
            var updater = Objects.Add<T>(typeof(T).Name);
            PrivateMember.SetField(updater, "_noteListRoot", root);
            return updater;
        }

        /// <summary>Fills the static sprite caches the skin manager normally fills when a skin loads.</summary>
        private void FillSkinCaches()
        {
            Fill("_tapLines", Skin.TapLine_Normal, Skin.TapLine_Each, Skin.TapLine_Break);
            Fill("_starLines", Skin.TapLine_Slide, Skin.TapLine_Each, Skin.TapLine_Break);
            Fill("_holdEnds", Skin.HoldEndPoint_Normal, Skin.HoldEndPoint_Each, Skin.HoldEndPoint_Break);
            Fill("_touchHoldFans", Skin.TouchHold[0], Skin.TouchHold[1], Skin.TouchHold[2], Skin.TouchHold[3]);
            Fill("_touchHoldBreakFans", Skin.TouchHold_Break[0], Skin.TouchHold_Break[1], Skin.TouchHold_Break[2], Skin.TouchHold_Break[3]);
            Fill("_touchHoldMineFans", Skin.TouchHold_Mine[0], Skin.TouchHold_Mine[1], Skin.TouchHold_Mine[2], Skin.TouchHold_Mine[3]);
            Fill("_touchHoldBreakMineFans", Skin.TouchHold_Break_Mine[0], Skin.TouchHold_Break_Mine[1], Skin.TouchHold_Break_Mine[2], Skin.TouchHold_Break_Mine[3]);
        }

        /// <summary>Writes sprites into a static sprite cache of the skin manager.</summary>
        /// <param name="field">The cache field name.</param>
        /// <param name="sprites">The sprites in cache order.</param>
        private static void Fill(string field, params Sprite?[] sprites)
        {
            var cache = PrivateMember.GetStaticField<Sprite?[]>(typeof(SkinManager), field);
            for (var i = 0; i < sprites.Length; i++)
            {
                cache[i] = sprites[i];
            }
        }
    }
}
