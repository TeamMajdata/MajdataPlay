using Cysharp.Threading.Tasks;
using MajdataPlay.Buffers;

using MajdataPlay.Diagnostics;
using MajdataPlay.Drawing;
using MajdataPlay.Rendering;
using System;
using System.Collections.Generic;
using System.IO;

using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

using Unity.VectorGraphics;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Skins
{
    public class CustomSkin
    {
        public string Name { get; }
        public bool IsLoaded { get; private set; }
        public bool IsOutlineAvailable { get; private set; } = false;

        // --- 属性保持完全不变 ---
        public Sprite? SubDisplay { get; private set; }
        public Sprite? Tap { get; private set; }
        public Sprite? Tap_Each { get; private set; }
        public Sprite? Tap_Break { get; private set; }
        public Sprite? Tap_Ex { get; private set; }
        public Sprite? Tap_Mine { get; private set; }
        public Sprite? Tap_Break_Mine { get; private set; }

        public Sprite? Slide { get; private set; }
        public Sprite? Slide_Each { get; private set; }
        public Sprite? Slide_Break { get; private set; }
        public Sprite? Slide_Mine { get; private set; }
        public Sprite? Slide_Break_Mine { get; private set; }
        public Sprite?[] Wifi { get; private set; } = new Sprite[11];
        public Sprite?[] Wifi_Each { get; private set; } = new Sprite[11];
        public Sprite?[] Wifi_Break { get; private set; } = new Sprite[11];
        public Sprite?[] Wifi_Mine { get; private set; } = new Sprite[11];
        public Sprite?[] Wifi_Break_Mine { get; private set; } = new Sprite[11];

        public Sprite? Star { get; private set; }
        public Sprite? Star_Double { get; private set; }
        public Sprite? Star_Each { get; private set; }
        public Sprite? Star_Each_Double { get; private set; }
        public Sprite? Star_Break { get; private set; }
        public Sprite? Star_Break_Double { get; private set; }
        public Sprite? Star_Ex { get; private set; }
        public Sprite? Star_Ex_Double { get; private set; }
        public Sprite? Star_Mine { get; private set; }
        public Sprite? Star_Double_Mine { get; private set; }
        public Sprite? Star_Break_Mine { get; private set; }
        public Sprite? Star_Break_Double_Mine { get; private set; }

        public Sprite? Hold { get; private set; }
        public Sprite? Hold_On { get; private set; }
        public Sprite? Hold_Off { get; private set; }
        public Sprite? Hold_Each { get; private set; }
        public Sprite? Hold_Each_On { get; private set; }
        public Sprite? Hold_Ex { get; private set; }
        public Sprite? Hold_Mine { get; private set; }
        public Sprite? Hold_Mine_On { get; private set; }
        public Sprite? Hold_Break_Mine { get; private set; }
        public Sprite? Hold_Break_Mine_On { get; private set; }
        public Sprite? Hold_Break { get; private set; }
        public Sprite? Hold_Break_On { get; private set; }

        public Sprite?[] Just { get; private set; } = new Sprite[60];

        public Sprite? CriticalPerfect_Shine { get; private set; }
        public Sprite? Perfect_Shine { get; private set; }
        public Sprite? Break_2600_Shine { get; private set; }

        public Sprite? CriticalPerfect { get; private set; }
        public Sprite? Perfect { get; private set; }
        public Sprite? Great { get; private set; }
        public Sprite? Good { get; private set; }
        public Sprite? Miss { get; private set; }

        public Sprite? Break_2600 { get; private set; }
        public Sprite? Break_2550 { get; private set; }
        public Sprite? Break_2500 { get; private set; }
        public Sprite? Break_2000 { get; private set; }
        public Sprite? Break_1500 { get; private set; }
        public Sprite? Break_1250 { get; private set; }
        public Sprite? Break_1000 { get; private set; }
        public Sprite? Break_0 { get; private set; }
        public Sprite? Fast { get; private set; }
        public Sprite? Late { get; private set; }

        public Sprite? CriticalPerfect_Fast { get; private set; }
        public Sprite? Perfect_Fast { get; private set; }
        public Sprite? Great_Fast { get; private set; }
        public Sprite? Good_Fast { get; private set; }

        public Sprite? Break_2600_Fast { get; private set; }
        public Sprite? Break_2550_Fast { get; private set; }
        public Sprite? Break_2500_Fast { get; private set; }
        public Sprite? Break_2000_Fast { get; private set; }
        public Sprite? Break_1500_Fast { get; private set; }
        public Sprite? Break_1250_Fast { get; private set; }
        public Sprite? Break_1000_Fast { get; private set; }

        public Sprite? CriticalPerfect_Late { get; private set; }
        public Sprite? Perfect_Late { get; private set; }
        public Sprite? Great_Late { get; private set; }
        public Sprite? Good_Late { get; private set; }

        public Sprite? Break_2600_Late { get; private set; }
        public Sprite? Break_2550_Late { get; private set; }
        public Sprite? Break_2500_Late { get; private set; }
        public Sprite? Break_2000_Late { get; private set; }
        public Sprite? Break_1500_Late { get; private set; }
        public Sprite? Break_1250_Late { get; private set; }
        public Sprite? Break_1000_Late { get; private set; }

        public Sprite? Touch { get; private set; }
        public Sprite? Touch_Each { get; private set; }
        public Sprite? Touch_Break { get; private set; }
        public Sprite? Touch_Mine { get; private set; }
        public Sprite? Touch_Break_Mine { get; private set; }
        public Sprite? TouchPoint { get; private set; }
        public Sprite? TouchPoint_Each { get; private set; }
        public Sprite? TouchPoint_Break { get; private set; }
        public Sprite? TouchPoint_Mine { get; private set; }
        public Sprite? TouchPoint_Break_Mine { get; private set; }
        public Sprite? TouchJust { get; private set; }

        public Sprite?[] TouchBorder { get; private set; } = new Sprite[2];
        public Sprite?[] TouchBorder_Each { get; private set; } = new Sprite[2];
        public Sprite?[] TouchBorder_Break { get; private set; } = new Sprite[2];
        public Sprite?[] TouchBorder_Mine { get; private set; } = new Sprite[2];
        public Sprite?[] TouchBorder_Break_Mine { get; private set; } = new Sprite[2];

        public Sprite?[] TouchHold { get; private set; } = new Sprite[5];
        public Sprite?[] TouchHold_Break { get; private set; } = new Sprite[5];
        public Sprite?[] TouchHold_Mine { get; private set; } = new Sprite[5];
        public Sprite?[] TouchHold_Break_Mine { get; private set; } = new Sprite[5];
        public Sprite? TouchHold_Off { get; private set; }

        public Sprite? LoadingSplash { get; private set; }
        public Sprite? Outline { get; private set; }

        public Sprite? TapLine_Normal { get; private set; }
        public Sprite? TapLine_Each { get; private set; }
        public Sprite? TapLine_Slide { get; private set; }
        public Sprite? TapLine_Break { get; private set; }
        public Sprite? TapLine_Mine { get; private set; }

        public Sprite?[] EachLines { get; private set; } = new Sprite[4];
        public Sprite? HoldEndPoint_Normal { get; private set; }
        public Sprite? HoldEndPoint_Each { get; private set; }
        public Sprite? HoldEndPoint_Break { get; private set; }
        public Sprite? HoldEndPoint_Mine { get; private set; }
        // --- 属性定义结束 ---

        public static readonly CustomSkin Empty;
        static readonly Sprite _dummySprite = SpriteLoader.EmptySprite;

        bool _canUnload = true;
        readonly string _path = string.Empty;
        readonly SemaphoreSlim _loadSyncLock = new(1, 1);
        private Texture2D? _atlasTexture;

        // Source-pixel grid; values below 1 disable snapping.
        private const float SpriteOutlineSnap = 1f;
        // Normalized detail: 0 = simplified, 1 = pixel-exact boundary.
        private const float SpriteOutlineDetail = 0.5f;
        // Alpha <= this threshold is excluded from the generated mesh.
        private const byte SpriteAlphaTolerance = 30;

        static CustomSkin()
        {
            var skin = new CustomSkin("NotAvailable")
            {
                IsLoaded = true,
                IsOutlineAvailable = false,
                _canUnload = false,
            };

            Empty = skin;
        }

        CustomSkin(string name)
        {
            Name = name;
            SubDisplay = _dummySprite;
            Tap = _dummySprite;
            Tap_Each = _dummySprite;
            Tap_Break = _dummySprite;
            Tap_Ex = _dummySprite;

            Slide = _dummySprite;
            Slide_Each = _dummySprite;
            Slide_Break = _dummySprite;

            Star = _dummySprite;
            Star_Double = _dummySprite;
            Star_Each = _dummySprite;
            Star_Each_Double = _dummySprite;
            Star_Break = _dummySprite;
            Star_Break_Double = _dummySprite;
            Star_Ex = _dummySprite;
            Star_Ex_Double = _dummySprite;

            Hold = _dummySprite;
            Hold_On = _dummySprite;
            Hold_Off = _dummySprite;
            Hold_Each = _dummySprite;
            Hold_Each_On = _dummySprite;
            Hold_Ex = _dummySprite;
            Hold_Break = _dummySprite;
            Hold_Break_On = _dummySprite;

            CriticalPerfect_Shine = _dummySprite;
            Perfect_Shine = _dummySprite;
            Break_2600_Shine = _dummySprite;

            CriticalPerfect = _dummySprite;
            Perfect = _dummySprite;
            Great = _dummySprite;
            Good = _dummySprite;
            Miss = _dummySprite;

            Break_2600 = _dummySprite;
            Break_2550 = _dummySprite;
            Break_2500 = _dummySprite;
            Break_2000 = _dummySprite;
            Break_1500 = _dummySprite;
            Break_1250 = _dummySprite;
            Break_1000 = _dummySprite;
            Break_0 = _dummySprite;

            Fast = _dummySprite;
            Late = _dummySprite;

            CriticalPerfect_Fast = _dummySprite;
            Perfect_Fast = _dummySprite;
            Great_Fast = _dummySprite;
            Good_Fast = _dummySprite;

            Break_2600_Fast = _dummySprite;
            Break_2550_Fast = _dummySprite;
            Break_2500_Fast = _dummySprite;
            Break_2000_Fast = _dummySprite;
            Break_1500_Fast = _dummySprite;
            Break_1250_Fast = _dummySprite;
            Break_1000_Fast = _dummySprite;

            CriticalPerfect_Late = _dummySprite;
            Perfect_Late = _dummySprite;
            Great_Late = _dummySprite;
            Good_Late = _dummySprite;

            Break_2600_Late = _dummySprite;
            Break_2550_Late = _dummySprite;
            Break_2500_Late = _dummySprite;
            Break_2000_Late = _dummySprite;
            Break_1500_Late = _dummySprite;
            Break_1250_Late = _dummySprite;
            Break_1000_Late = _dummySprite;

            Touch = _dummySprite;
            Touch_Each = _dummySprite;
            Touch_Break = _dummySprite;
            TouchPoint = _dummySprite;
            TouchPoint_Each = _dummySprite;
            TouchPoint_Break = _dummySprite;
            TouchJust = _dummySprite;

            TouchHold_Off = _dummySprite;

            LoadingSplash = _dummySprite;

            Outline = _dummySprite;

            TapLine_Normal = _dummySprite;
            TapLine_Each = _dummySprite;
            TapLine_Slide = _dummySprite;
            TapLine_Break = _dummySprite;

            HoldEndPoint_Normal = _dummySprite;
            HoldEndPoint_Each = _dummySprite;
            HoldEndPoint_Break = _dummySprite;
            HoldEndPoint_Mine = _dummySprite;

            Array.Fill(Wifi, _dummySprite);
            Array.Fill(Wifi_Each, _dummySprite);
            Array.Fill(Wifi_Break, _dummySprite);
            Array.Fill(Wifi_Mine, _dummySprite);
            Array.Fill(Wifi_Break_Mine, _dummySprite);
            Array.Fill(Just, _dummySprite);
            Array.Fill(TouchBorder, _dummySprite);
            Array.Fill(TouchBorder_Each, _dummySprite);
            Array.Fill(TouchBorder_Break, _dummySprite);
            Array.Fill(TouchBorder_Mine, _dummySprite);
            Array.Fill(TouchBorder_Break_Mine, _dummySprite);
            Array.Fill(TouchHold, _dummySprite);
            Array.Fill(TouchHold_Break, _dummySprite);
            Array.Fill(TouchHold_Mine, _dummySprite);
            Array.Fill(TouchHold_Break_Mine, _dummySprite);
            Array.Fill(EachLines, _dummySprite);
        }

        public CustomSkin(string skinCollectionPath, bool loadIntoMemory) :
            this(new DirectoryInfo(skinCollectionPath).Name, skinCollectionPath, loadIntoMemory)
        { }
        public CustomSkin(string name, string skinCollectionPath, bool loadIntoMemory) : this(name)
        {
            _path = skinCollectionPath;

            if (loadIntoMemory)
            {
                PerformLoad();
            }
        }

        public static CustomSkin Create(string skinCollectionPath)
        {
            var dirInfo = new DirectoryInfo(skinCollectionPath);
            if (!dirInfo.Exists)
            {
                throw new DirectoryNotFoundException($"The skin collection path '{skinCollectionPath}' does not exist.");
            }

            return new CustomSkin(dirInfo.Name, skinCollectionPath, false);
        }

        public async UniTask LoadAsync(CancellationToken token = default)
        {
            if (IsLoaded)
            {
                return;
            }

            await _loadSyncLock.WaitAsync(token);
            try
            {
                if (IsLoaded)
                {
                    return;
                }
                await PerformLoadAsync();
            }
            finally
            {
                _loadSyncLock.Release();
            }
        }

        public async UniTask UnloadAsync(CancellationToken token = default)
        {
            if (!IsLoaded || !_canUnload)
            {
                return;
            }

            await _loadSyncLock.WaitAsync(token);
            try
            {
                await UniTask.SwitchToMainThread();
                if (!IsLoaded) return;
                NoteSpriteResources.Unload(this);

                SubDisplay = SafeDestroyIndependent(SubDisplay);
                LoadingSplash = SafeDestroyIndependent(LoadingSplash);

                Tap = SafeDestroyAtlasSprite(Tap);
                Tap_Each = SafeDestroyAtlasSprite(Tap_Each);
                Tap_Break = SafeDestroyAtlasSprite(Tap_Break);
                Tap_Ex = SafeDestroyAtlasSprite(Tap_Ex);
                Tap_Mine = SafeDestroyAtlasSprite(Tap_Mine);
                Tap_Break_Mine = SafeDestroyAtlasSprite(Tap_Break_Mine);

                Slide = SafeDestroyAtlasSprite(Slide);
                Slide_Each = SafeDestroyAtlasSprite(Slide_Each);
                Slide_Break = SafeDestroyAtlasSprite(Slide_Break);
                Slide_Mine = SafeDestroyAtlasSprite(Slide_Mine);
                Slide_Break_Mine = SafeDestroyAtlasSprite(Slide_Break_Mine);

                SafeDestroyAtlasSpriteArray(Wifi);
                SafeDestroyAtlasSpriteArray(Wifi_Each);
                SafeDestroyAtlasSpriteArray(Wifi_Break);
                SafeDestroyAtlasSpriteArray(Wifi_Mine);
                SafeDestroyAtlasSpriteArray(Wifi_Break_Mine);

                Star = SafeDestroyAtlasSprite(Star);
                Star_Double = SafeDestroyAtlasSprite(Star_Double);
                Star_Each = SafeDestroyAtlasSprite(Star_Each);
                Star_Each_Double = SafeDestroyAtlasSprite(Star_Each_Double);
                Star_Break = SafeDestroyAtlasSprite(Star_Break);
                Star_Break_Double = SafeDestroyAtlasSprite(Star_Break_Double);
                Star_Ex = SafeDestroyAtlasSprite(Star_Ex);
                Star_Ex_Double = SafeDestroyAtlasSprite(Star_Ex_Double);
                Star_Mine = SafeDestroyAtlasSprite(Star_Mine);
                Star_Double_Mine = SafeDestroyAtlasSprite(Star_Double_Mine);
                Star_Break_Mine = SafeDestroyAtlasSprite(Star_Break_Mine);
                Star_Break_Double_Mine = SafeDestroyAtlasSprite(Star_Break_Double_Mine);

                Hold = SafeDestroyAtlasSprite(Hold);
                Hold_On = SafeDestroyAtlasSprite(Hold_On);
                Hold_Off = SafeDestroyAtlasSprite(Hold_Off);
                Hold_Each = SafeDestroyAtlasSprite(Hold_Each);
                Hold_Each_On = SafeDestroyAtlasSprite(Hold_Each_On);
                Hold_Ex = SafeDestroyAtlasSprite(Hold_Ex);
                Hold_Break = SafeDestroyAtlasSprite(Hold_Break);
                Hold_Break_On = SafeDestroyAtlasSprite(Hold_Break_On);
                Hold_Mine = SafeDestroyAtlasSprite(Hold_Mine);
                Hold_Mine_On = SafeDestroyAtlasSprite(Hold_Mine_On);
                Hold_Break_Mine = SafeDestroyAtlasSprite(Hold_Break_Mine);
                Hold_Break_Mine_On = SafeDestroyAtlasSprite(Hold_Break_Mine_On);

                SafeDestroyAtlasSpriteArray(Just);

                CriticalPerfect_Shine = SafeDestroyAtlasSprite(CriticalPerfect_Shine);
                Perfect_Shine = SafeDestroyAtlasSprite(Perfect_Shine);
                Break_2600_Shine = SafeDestroyAtlasSprite(Break_2600_Shine);

                CriticalPerfect = SafeDestroyAtlasSprite(CriticalPerfect);
                Perfect = SafeDestroyAtlasSprite(Perfect);
                Great = SafeDestroyAtlasSprite(Great);
                Good = SafeDestroyAtlasSprite(Good);
                Miss = SafeDestroyAtlasSprite(Miss);

                Break_2600 = SafeDestroyAtlasSprite(Break_2600);
                Break_2550 = SafeDestroyAtlasSprite(Break_2550);
                Break_2500 = SafeDestroyAtlasSprite(Break_2500);
                Break_2000 = SafeDestroyAtlasSprite(Break_2000);
                Break_1500 = SafeDestroyAtlasSprite(Break_1500);
                Break_1250 = SafeDestroyAtlasSprite(Break_1250);
                Break_1000 = SafeDestroyAtlasSprite(Break_1000);
                Break_0 = SafeDestroyAtlasSprite(Break_0);
                Fast = SafeDestroyAtlasSprite(Fast);
                Late = SafeDestroyAtlasSprite(Late);

                CriticalPerfect_Fast = SafeDestroyAtlasSprite(CriticalPerfect_Fast);
                Perfect_Fast = SafeDestroyAtlasSprite(Perfect_Fast);
                Great_Fast = SafeDestroyAtlasSprite(Great_Fast);
                Good_Fast = SafeDestroyAtlasSprite(Good_Fast);

                Break_2600_Fast = SafeDestroyAtlasSprite(Break_2600_Fast);
                Break_2550_Fast = SafeDestroyAtlasSprite(Break_2550_Fast);
                Break_2500_Fast = SafeDestroyAtlasSprite(Break_2500_Fast);
                Break_2000_Fast = SafeDestroyAtlasSprite(Break_2000_Fast);
                Break_1500_Fast = SafeDestroyAtlasSprite(Break_1500_Fast);
                Break_1250_Fast = SafeDestroyAtlasSprite(Break_1250_Fast);
                Break_1000_Fast = SafeDestroyAtlasSprite(Break_1000_Fast);

                CriticalPerfect_Late = SafeDestroyAtlasSprite(CriticalPerfect_Late);
                Perfect_Late = SafeDestroyAtlasSprite(Perfect_Late);
                Great_Late = SafeDestroyAtlasSprite(Great_Late);
                Good_Late = SafeDestroyAtlasSprite(Good_Late);

                Break_2600_Late = SafeDestroyAtlasSprite(Break_2600_Late);
                Break_2550_Late = SafeDestroyAtlasSprite(Break_2550_Late);
                Break_2500_Late = SafeDestroyAtlasSprite(Break_2500_Late);
                Break_2000_Late = SafeDestroyAtlasSprite(Break_2000_Late);
                Break_1500_Late = SafeDestroyAtlasSprite(Break_1500_Late);
                Break_1250_Late = SafeDestroyAtlasSprite(Break_1250_Late);
                Break_1000_Late = SafeDestroyAtlasSprite(Break_1000_Late);

                Touch = SafeDestroyAtlasSprite(Touch);
                Touch_Each = SafeDestroyAtlasSprite(Touch_Each);
                Touch_Break = SafeDestroyAtlasSprite(Touch_Break);
                Touch_Mine = SafeDestroyAtlasSprite(Touch_Mine);
                Touch_Break_Mine = SafeDestroyAtlasSprite(Touch_Break_Mine);

                TouchPoint = SafeDestroyAtlasSprite(TouchPoint);
                TouchPoint_Each = SafeDestroyAtlasSprite(TouchPoint_Each);
                TouchPoint_Break = SafeDestroyAtlasSprite(TouchPoint_Break);
                TouchPoint_Mine = SafeDestroyAtlasSprite(TouchPoint_Mine);
                TouchPoint_Break_Mine = SafeDestroyAtlasSprite(TouchPoint_Break_Mine);
                TouchJust = SafeDestroyAtlasSprite(TouchJust);

                SafeDestroyAtlasSpriteArray(TouchBorder);
                SafeDestroyAtlasSpriteArray(TouchBorder_Each);
                SafeDestroyAtlasSpriteArray(TouchBorder_Break);
                SafeDestroyAtlasSpriteArray(TouchBorder_Mine);
                SafeDestroyAtlasSpriteArray(TouchBorder_Break_Mine);

                SafeDestroyAtlasSpriteArray(TouchHold);
                SafeDestroyAtlasSpriteArray(TouchHold_Break);
                SafeDestroyAtlasSpriteArray(TouchHold_Mine);
                SafeDestroyAtlasSpriteArray(TouchHold_Break_Mine);

                TouchHold_Off = SafeDestroyAtlasSprite(TouchHold_Off);
                Outline = SafeDestroyAtlasSprite(Outline);

                TapLine_Normal = SafeDestroyAtlasSprite(TapLine_Normal);
                TapLine_Each = SafeDestroyAtlasSprite(TapLine_Each);
                TapLine_Slide = SafeDestroyAtlasSprite(TapLine_Slide);
                TapLine_Break = SafeDestroyAtlasSprite(TapLine_Break);
                TapLine_Mine = SafeDestroyAtlasSprite(TapLine_Mine);

                SafeDestroyAtlasSpriteArray(EachLines);

                HoldEndPoint_Normal = SafeDestroyAtlasSprite(HoldEndPoint_Normal);
                HoldEndPoint_Each = SafeDestroyAtlasSprite(HoldEndPoint_Each);
                HoldEndPoint_Break = SafeDestroyAtlasSprite(HoldEndPoint_Break);
                HoldEndPoint_Mine = SafeDestroyAtlasSprite(HoldEndPoint_Mine);

                if (_atlasTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(_atlasTexture, true);
                    _atlasTexture = null;
                }

                IsLoaded = false;
            }
            finally
            {
                _loadSyncLock.Release();
            }
        }

        private async UniTask PerformLoadAsync()
        {
            IsOutlineAvailable = File.Exists($"{_path}/outline.png");

            SubDisplay = await LoadSpriteDirectAsync("SubBackgourd.png");
            LoadingSplash = await LoadSpriteDirectAsync("now_loading.png");

            var builder = new AtlasBuilder(_path, SpriteOutlineSnap, SpriteOutlineDetail, SpriteAlphaTolerance);
            QueueAtlasElements(builder);

            _atlasTexture = await builder.BuildAndAssignAsync();

            ProcessFallbacks();
            NoteSpriteResources.PreloadMeshes(this, builder.Sprites);
            IsLoaded = true;
        }

        private void PerformLoad()
        {
            IsOutlineAvailable = File.Exists($"{_path}/outline.png");

            SubDisplay = LoadSpriteDirectSync("SubBackgourd.png");
            LoadingSplash = LoadSpriteDirectSync("now_loading.png");

            var builder = new AtlasBuilder(_path, SpriteOutlineSnap, SpriteOutlineDetail, SpriteAlphaTolerance);
            QueueAtlasElements(builder);

            _atlasTexture = builder.BuildAndAssign();

            ProcessFallbacks();
            NoteSpriteResources.PreloadMeshes(this, builder.Sprites);
            IsLoaded = true;
        }

        private void QueueAtlasElements(AtlasBuilder builder)
        {
            var border = new Vector4(0, 58, 0, 58);

            builder.Add("outline.png", s => Outline = s);

            builder.Add("TapSkins/tap.png", s => Tap = s);
            builder.Add("TapSkins/tap_each.png", s => Tap_Each = s);
            builder.Add("TapSkins/tap_break.png", s => Tap_Break = s);
            builder.Add("TapSkins/tap_break_mine.png", s => Tap_Break_Mine = s);
            builder.Add("TapSkins/tap_mine.png", s => Tap_Mine = s);
            builder.Add("TapSkins/tap_ex.png", s => Tap_Ex = s);

            builder.Add("SlideSkins/slide.png", s => Slide = s);
            builder.Add("SlideSkins/slide_each.png", s => Slide_Each = s);
            builder.Add("SlideSkins/slide_break.png", s => Slide_Break = s);
            builder.Add("SlideSkins/slide_mine.png", s => Slide_Mine = s);
            builder.Add("SlideSkins/slide_break_mine.png", s => Slide_Break_Mine = s);

            for (var i = 0; i < 11; i++)
            {
                int index = i;
                builder.Add($"WifiSkins/wifi_{index}.png", s => Wifi[index] = s);
                builder.Add($"WifiSkins/wifi_each_{index}.png", s => Wifi_Each[index] = s);
                builder.Add($"WifiSkins/wifi_break_{index}.png", s => Wifi_Break[index] = s);
                builder.Add($"WifiSkins/wifi_mine_{index}.png", s => Wifi_Mine[index] = s);
                builder.Add($"WifiSkins/wifi_break_mine_{index}.png", s => Wifi_Break_Mine[index] = s);
            }

            builder.Add("StarSkins/star.png", s => Star = s);
            builder.Add("StarSkins/star_double.png", s => Star_Double = s);
            builder.Add("StarSkins/star_double_mine.png", s => Star_Double_Mine = s);
            builder.Add("StarSkins/star_each.png", s => Star_Each = s);
            builder.Add("StarSkins/star_mine.png", s => Star_Mine = s);
            builder.Add("StarSkins/star_each_double.png", s => Star_Each_Double = s);
            builder.Add("StarSkins/star_break.png", s => Star_Break = s);
            builder.Add("StarSkins/star_break_mine.png", s => Star_Break_Mine = s);
            builder.Add("StarSkins/star_break_double.png", s => Star_Break_Double = s);
            builder.Add("StarSkins/star_break_double_mine.png", s => Star_Break_Double_Mine = s);
            builder.Add("StarSkins/star_ex.png", s => Star_Ex = s);
            builder.Add("StarSkins/star_ex_double.png", s => Star_Ex_Double = s);

            builder.Add("HoldSkins/hold.png", s => Hold = s, border);
            builder.Add("HoldSkins/hold_mine.png", s => Hold_Mine = s, border);
            builder.Add("HoldSkins/hold_each.png", s => Hold_Each = s, border);
            builder.Add("HoldSkins/hold_ex.png", s => Hold_Ex = s, border);
            builder.Add("HoldSkins/hold_break.png", s => Hold_Break = s, border);
            builder.Add("HoldSkins/hold_break_mine.png", s => Hold_Break_Mine = s, border);
            builder.Add("HoldSkins/hold_off.png", s => Hold_Off = s, border);

            builder.Add("HoldSkins/hold_on.png", s => Hold_On = s, border);
            builder.Add("HoldSkins/hold_each_on.png", s => Hold_Each_On = s, border);
            builder.Add("HoldSkins/hold_break_on.png", s => Hold_Break_On = s, border);

            builder.Add("HoldSkins/hold_mine_on.png", s => Hold_Mine_On = s, border);
            builder.Add("HoldSkins/hold_break_mine_on.png", s => Hold_Break_Mine_On = s, border);

            string[] slideStates = { "curv_r", "str_r", "wifi_u", "curv_l", "str_l", "wifi_d" };
            for (int i = 0; i < 6; i++)
            {
                int index = i;
                string state = slideStates[index];
                builder.Add($"SlideOKSkins/just_{state}.png", s => Just[index] = s);
                builder.Add($"SlideOKSkins/just_{state}_p.png", s => Just[index + 6] = s);
                builder.Add($"SlideOKSkins/just_{state}_fast_p.png", s => Just[index + 12] = s);
                builder.Add($"SlideOKSkins/just_{state}_fast_gr.png", s => Just[index + 18] = s);
                builder.Add($"SlideOKSkins/just_{state}_fast_gd.png", s => Just[index + 24] = s);
                builder.Add($"SlideOKSkins/just_{state}_late_p.png", s => Just[index + 30] = s);
                builder.Add($"SlideOKSkins/just_{state}_late_gr.png", s => Just[index + 36] = s);
                builder.Add($"SlideOKSkins/just_{state}_late_gd.png", s => Just[index + 42] = s);
                builder.Add($"SlideOKSkins/miss_{state}.png", s => Just[index + 48] = s);
                builder.Add($"SlideOKSkins/toofast_{state}.png", s => Just[index + 54] = s);
            }

            builder.Add("JudgeTextSkins/judge_text_miss.png", s => Miss = s);
            builder.Add("JudgeTextSkins/judge_text_good.png", s => Good = s);
            builder.Add("JudgeTextSkins/judge_text_great.png", s => Great = s);
            builder.Add("JudgeTextSkins/judge_text_perfect.png", s => Perfect = s);
            builder.Add("JudgeTextSkins/judge_text_cPerfect.png", s => CriticalPerfect = s);

            builder.Add("JudgeTextSkins/judge_text_cPerfect_fast.png", s => CriticalPerfect_Fast = s);
            builder.Add("JudgeTextSkins/judge_text_cPerfect_late.png", s => CriticalPerfect_Late = s);
            builder.Add("JudgeTextSkins/judge_text_perfect_fast.png", s => Perfect_Fast = s);
            builder.Add("JudgeTextSkins/judge_text_perfect_late.png", s => Perfect_Late = s);
            builder.Add("JudgeTextSkins/judge_text_great_fast.png", s => Great_Fast = s);
            builder.Add("JudgeTextSkins/judge_text_great_late.png", s => Great_Late = s);
            builder.Add("JudgeTextSkins/judge_text_good_fast.png", s => Good_Fast = s);
            builder.Add("JudgeTextSkins/judge_text_good_late.png", s => Good_Late = s);

            builder.Add("JudgeTextSkins/judge_text_cPerfect_break.png", s => CriticalPerfect_Shine = s);
            builder.Add("JudgeTextSkins/judge_text_break_2600_shine.png", s => Break_2600_Shine = s);
            builder.Add("JudgeTextSkins/judge_text_perfect_break.png", s => Perfect_Shine = s);

            LoadBreakSetToBuilder(builder, 2600, s => Break_2600 = s, s => Break_2600_Fast = s, s => Break_2600_Late = s);
            LoadBreakSetToBuilder(builder, 2550, s => Break_2550 = s, s => Break_2550_Fast = s, s => Break_2550_Late = s);
            LoadBreakSetToBuilder(builder, 2500, s => Break_2500 = s, s => Break_2500_Fast = s, s => Break_2500_Late = s);
            LoadBreakSetToBuilder(builder, 2000, s => Break_2000 = s, s => Break_2000_Fast = s, s => Break_2000_Late = s);
            LoadBreakSetToBuilder(builder, 1500, s => Break_1500 = s, s => Break_1500_Fast = s, s => Break_1500_Late = s);
            LoadBreakSetToBuilder(builder, 1250, s => Break_1250 = s, s => Break_1250_Fast = s, s => Break_1250_Late = s);
            LoadBreakSetToBuilder(builder, 1000, s => Break_1000 = s, s => Break_1000_Fast = s, s => Break_1000_Late = s);
            LoadBreakSetToBuilder(builder, 0, s => Break_0 = s, null, null);

            builder.Add("JudgeTextSkins/fast.png", s => Fast = s);
            builder.Add("JudgeTextSkins/late.png", s => Late = s);

            builder.Add("TouchSkins/touch.png", s => Touch = s);
            builder.Add("TouchSkins/touch_mine.png", s => Touch_Mine = s);
            builder.Add("TouchSkins/touch_each.png", s => Touch_Each = s);
            builder.Add("TouchSkins/touch_break.png", s => Touch_Break = s);
            builder.Add("TouchSkins/touch_break_mine.png", s => Touch_Break_Mine = s);

            builder.Add("TouchSkins/touch_point.png", s => TouchPoint = s);
            builder.Add("TouchSkins/touch_point_each.png", s => TouchPoint_Each = s);
            builder.Add("TouchSkins/touch_break_point.png", s => TouchPoint_Break = s);
            builder.Add("TouchSkins/touch_point_mine.png", s => TouchPoint_Mine = s);
            builder.Add("TouchSkins/touch_break_point_mine.png", s => TouchPoint_Break_Mine = s);
            builder.Add("TouchSkins/touch_just.png", s => TouchJust = s);

            builder.Add("TouchSkins/touch_border_2.png", s => TouchBorder[0] = s);
            builder.Add("TouchSkins/touch_border_3.png", s => TouchBorder[1] = s);
            builder.Add("TouchSkins/touch_border_2_each.png", s => TouchBorder_Each[0] = s);
            builder.Add("TouchSkins/touch_border_3_each.png", s => TouchBorder_Each[1] = s);
            builder.Add("TouchSkins/touch_break_border_2.png", s => TouchBorder_Break[0] = s);
            builder.Add("TouchSkins/touch_break_border_3.png", s => TouchBorder_Break[1] = s);
            builder.Add("TouchSkins/touch_mine_border_2.png", s => TouchBorder_Mine[0] = s);
            builder.Add("TouchSkins/touch_mine_border_3_mine.png", s => TouchBorder_Mine[1] = s);
            builder.Add("TouchSkins/touch_break_mine_border_2.png", s => TouchBorder_Break_Mine[0] = s);
            builder.Add("TouchSkins/touch_break_mine_border_3.png", s => TouchBorder_Break_Mine[1] = s);

            for (var i = 0; i < 4; i++)
            {
                int index = i;
                builder.Add($"TouchHoldSkins/touchhold_{index}.png", s => TouchHold[index] = s);
                builder.Add($"TouchHoldSkins/touchhold_break_{index}.png", s => TouchHold_Break[index] = s);
                builder.Add($"TouchHoldSkins/touchhold_mine_{index}.png", s => TouchHold_Mine[index] = s);
                builder.Add($"TouchHoldSkins/touchhold_break_mine_{index}.png", s => TouchHold_Break_Mine[index] = s);
            }
            builder.Add("TouchHoldSkins/touchhold_border.png", s => TouchHold[4] = s);
            builder.Add("TouchHoldSkins/touchhold_break_border.png", s => TouchHold_Break[4] = s);
            builder.Add("TouchHoldSkins/touchhold_off.png", s => TouchHold_Off = s);

            builder.Add("NoteGuideSkins/Normal.png", s => TapLine_Normal = s);
            builder.Add("NoteGuideSkins/Each.png", s => TapLine_Each = s);
            builder.Add("NoteGuideSkins/Slide.png", s => TapLine_Slide = s);
            builder.Add("NoteGuideSkins/Break.png", s => TapLine_Break = s);
            builder.Add("NoteGuideSkins/Mine.png", s => TapLine_Mine = s);

            for (var i = 0; i < 4; i++)
            {
                int index = i;
                builder.Add($"NoteGuideSkins/EachLine{index + 1}.png", s => EachLines[index] = s);
            }

            builder.Add("NoteGuideSkins/Hold_End.png", s => HoldEndPoint_Normal = s);
            builder.Add("NoteGuideSkins/Hold_Each_End.png", s => HoldEndPoint_Each = s);
            builder.Add("NoteGuideSkins/Hold_Break_End.png", s => HoldEndPoint_Break = s);
            builder.Add("NoteGuideSkins/Hold_Mine_End.png", s => HoldEndPoint_Mine = s);
        }

        private void ProcessFallbacks()
        {
            Hold_On ??= Hold;
            Hold_Each_On ??= Hold_Each;
            Hold_Break_On ??= Hold_Break;
        }

        // --- 直接加载独立的图片 ---

        private async UniTask<Sprite?> LoadSpriteDirectAsync(string subPath)
        {
            var fullPath = Path.Combine(_path, subPath);
            if (!File.Exists(fullPath)) return null;

            byte[] data = await File.ReadAllBytesAsync(fullPath);

            // Keep pixels readable until the custom outline has been generated.
            var tex = await TextureLoader.LoadFromMemoryAsync(data, false);
            await UniTask.SwitchToMainThread();
            if (tex == null) return null;

            return CreateIndependentSprite(tex, subPath);
        }

        private Sprite? LoadSpriteDirectSync(string subPath)
        {
            var fullPath = Path.Combine(_path, subPath);
            if (!File.Exists(fullPath)) return null;

            byte[] data = File.ReadAllBytes(fullPath);
            var tex = TextureLoader.LoadFromMemory(data, false);
            if (tex == null) return null;

            return CreateIndependentSprite(tex, subPath);
        }

        private static Sprite CreateIndependentSprite(Texture2D texture, string name)
        {
            Sprite? sprite = null;
            try
            {
                sprite = AtlasBuilder.CreateOutlinedSprite(texture,
                    new Rect(0, 0, texture.width, texture.height), texture, name, default,
                    SpriteOutlineSnap, SpriteOutlineDetail, SpriteAlphaTolerance);
                texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                return sprite;
            }
            catch
            {
                if (sprite != null) UnityEngine.Object.DestroyImmediate(sprite, true);
                UnityEngine.Object.DestroyImmediate(texture, true);
                throw;
            }
        }

        private void LoadBreakSetToBuilder(AtlasBuilder builder, int value, Action<Sprite?> normal, Action<Sprite?>? fast, Action<Sprite?>? late)
        {
            builder.Add($"JudgeTextSkins/judge_text_break_{value}.png", normal);
            if (value != 0 && fast != null && late != null)
            {
                builder.Add($"JudgeTextSkins/judge_text_break_{value}_fast.png", fast);
                builder.Add($"JudgeTextSkins/judge_text_break_{value}_late.png", late);
            }
        }

        private Sprite? SafeDestroyIndependent(Sprite? sprite)
        {
            if (sprite != null && sprite != _dummySprite)
            {
                var tex = sprite.texture;
                UnityEngine.Object.DestroyImmediate(sprite, true);
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex, true);
            }
            return null;
        }

        private Sprite? SafeDestroyAtlasSprite(Sprite? sprite)
        {
            if (sprite != null && sprite != _dummySprite) UnityEngine.Object.DestroyImmediate(sprite, true);
            return null;
        }

        private void SafeDestroyAtlasSpriteArray(Sprite?[]? arr)
        {
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++) arr[i] = SafeDestroyAtlasSprite(arr[i]);
        }
    }

    /// <summary>
    /// 使用 TextureLoader 支持全异步与内存优化的图集构建器
    /// </summary>
    public class AtlasBuilder
    {
        private struct SpriteTask
        {
            public string FullPath;
            public Action<Sprite?> Assigner;
            public Vector4 Border;
        }

        private readonly List<SpriteTask> _tasks = new();
        private readonly List<Sprite> _sprites = new();
        internal IEnumerable<Sprite> Sprites => _sprites;
        private readonly string _basePath;
        private readonly float _outlineSnap;
        private readonly float _outlineDetail;
        private readonly byte _alphaTolerance;

        /// <param name="outlineSnap">Outline grid size in source pixels. Values below 1 disable snapping.</param>
        /// <param name="outlineDetail">Detail in [0, 1]; higher values retain more vertices.
        /// This is a runtime approximation, not Unity Editor's internal outline algorithm.</param>
        /// <param name="alphaTolerance">Pixels with alpha at or below this value are transparent.</param>
        public AtlasBuilder(string basePath, float outlineSnap = 1f, float outlineDetail = 1f, byte alphaTolerance = 0)
        {
            _basePath = basePath;
            _outlineSnap = Mathf.Max(0f, outlineSnap);
            _outlineDetail = Mathf.Clamp01(outlineDetail);
            _alphaTolerance = alphaTolerance;
        }


        public void Add(string subPath, Action<Sprite?> assigner, Vector4 border = default)
        {
            _tasks.Add(new SpriteTask
            {
                FullPath = Path.Combine(_basePath, subPath),
                Assigner = assigner,
                Border = border
            });
        }

        /// <summary>
        /// 全异步流程：异步读取IO -> 异步解析图像 -> 主线程打包图集
        /// </summary>
        public async UniTask<Texture2D?> BuildAndAssignAsync()
        {
            using var textures = new PooledList<Texture2D>(_tasks.Count);
            using var validTasks = new PooledList<SpriteTask>(_tasks.Count);

            foreach (var task in _tasks)
            {
                if (!File.Exists(task.FullPath))
                {
                    task.Assigner(null);
                    continue;
                }

                byte[] data = await File.ReadAllBytesAsync(task.FullPath);
                // 必须为 false：PackTextures 需要读取图片的 CPU 像素数据
                Texture2D? tex = await TextureLoader.LoadFromMemoryAsync(data, false);

                if (tex != null)
                {
                    textures.Add(tex);
                    validTasks.Add(task);
                }
                else
                {
                    task.Assigner(null);
                }
            }

            // PackTextures 只能在主线程执行
            await UniTask.SwitchToMainThread();
            return ExecutePack(textures, validTasks);
        }

        /// <summary>
        /// 兼容同步构造函数的同步流程
        /// </summary>
        public Texture2D? BuildAndAssign()
        {
            using var textures = new PooledList<Texture2D>(_tasks.Count);
            using var validTasks = new PooledList<SpriteTask>(_tasks.Count);

            foreach (var task in _tasks)
            {
                if (!File.Exists(task.FullPath))
                {
                    task.Assigner(null);
                    continue;
                }

                byte[] data = File.ReadAllBytes(task.FullPath);
                Texture2D? tex = TextureLoader.LoadFromMemory(data, false);

                if (tex != null)
                {
                    textures.Add(tex);
                    validTasks.Add(task);
                }
                else
                {
                    task.Assigner(null);
                }
            }

            return ExecutePack(textures, validTasks);
        }

        internal static Sprite CreateOutlinedSprite(Texture2D texture, Rect rect, Texture2D source,
    string name, Vector4 border, float snap, float detail, byte alphaTolerance)
        {
            // Polygon is an editor import mode, not a runtime SpriteMeshType.
            // Tight supplies the initial mesh; OverrideGeometry installs our polygon mesh.
            var sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, border, false);
            try
            {
                sprite.name = name;
                ApplyCustomOutline(sprite, source, snap, detail, alphaTolerance);
                return sprite;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(sprite, true);
                throw;
            }
        }

        private static void ApplyCustomOutline(Sprite sprite, Texture2D source, float snap, float detail, byte alphaTolerance)
        {
            using var points = new NativeList<float2>(128, Allocator.TempJob);
            using var outlines = new NativeList<int2>(16, Allocator.TempJob);
            var complete = BuildOutlines(source.GetPixelData<Color32>(0), source.width, source.height,
                snap, detail, alphaTolerance, points, outlines);
            if (complete && outlines.Length == 0)
            {
                // Nothing exceeds the alpha threshold: emit no visible triangles.
                sprite.OverrideGeometry(new Vector2[3], new ushort[] { 0, 1, 2 });
                return;
            }

            if (!complete || !TryTriangulate(points, outlines, out var vertices, out var triangles))
            {
                // Never keep a partial mesh (which would hide disconnected regions).
                MajDebug.LogWarning(nameof(AtlasBuilder), $"Custom outline too complex for sprite '{sprite.name}'; using its full rectangle.");
                var rect = sprite.rect;
                sprite.OverrideGeometry(new[] { Vector2.zero, new Vector2(rect.width, 0),
                    new Vector2(rect.width, rect.height), new Vector2(0, rect.height) },
                    new ushort[] { 0, 1, 2, 0, 2, 3 });
                return;
            }

            // OverrideGeometry uses rect-local pixels, NOT pivot-relative world units.
            // Scale for PackTextures resizing; never add the atlas rect's position.
            var size = sprite.rect.size;
            for (var i = 0; i < vertices.Length; i++)
            {
                vertices[i] = new Vector2(
                    Mathf.Clamp01(vertices[i].x / source.width) * size.x,
                    Mathf.Clamp01(vertices[i].y / source.height) * size.y);
            }
            sprite.OverrideGeometry(vertices, triangles);
        }

        private static unsafe bool BuildOutlines(NativeArray<Color32> pixels, int width, int height,
    float snap, float detail, byte alphaTolerance, NativeList<float2> points, NativeList<int2> outlines)
        {
            var pixelCount = checked(width * height);
            if (width <= 0 || height <= 0 || pixels.Length < pixelCount)
                throw new ArgumentException("Invalid sprite pixel dimensions.");

            // Four directed boundary bits per pixel, plus one completion byte. The
            // rental may be dirty; Execute overwrites EVERY used byte before reading.
            using var edges = Pool<byte>.Rent(checked(pixelCount + 1));
            using var loop = new NativeList<float2>(128, Allocator.TempJob);
            using var cleaned = new NativeList<float2>(128, Allocator.TempJob);
            using var snapped = new NativeList<float2>(128, Allocator.TempJob);
            using var simplified = new NativeList<float2>(128, Allocator.TempJob);
            using var keep = new NativeList<byte>(128, Allocator.TempJob);
            using var pending = new NativeList<int2>(32, Allocator.TempJob);

            // Run is deliberately synchronous: no job may outlive the fixed block,
            // the rented array, or the texture-owned pixel view. Do not Schedule this.
            fixed (byte* edgeBits = edges.AsArray())
            {
                new OutlineJob
                {
                    Pixels = pixels,
                    Width = width,
                    Height = height,
                    Snap = snap,
                    Tolerance = 4f * (1f - Mathf.Clamp01(detail)),
                    AlphaTolerance = alphaTolerance,
                    EdgeBits = edgeBits,
                    Points = points,
                    Outlines = outlines,
                    Loop = loop,
                    Cleaned = cleaned,
                    Snapped = snapped,
                    Simplified = simplified,
                    Keep = keep,
                    Pending = pending
                }.Run();
                return edgeBits[pixelCount] == 1;
            }
        }

        [BurstCompile(FloatMode = FloatMode.Strict)]
        private unsafe struct OutlineJob : IJob
        {
            [ReadOnly] public NativeArray<Color32> Pixels;
            [NativeDisableUnsafePtrRestriction] public byte* EdgeBits;
            public int Width, Height;
            public float Snap, Tolerance;
            public byte AlphaTolerance;
            public NativeList<float2> Points, Loop, Cleaned, Snapped, Simplified;
            public NativeList<int2> Outlines, Pending;
            public NativeList<byte> Keep;

            public void Execute()
            {
                Points.Clear();
                Outlines.Clear();
                var pixelCount = Width * Height;
                EdgeBits[pixelCount] = 0;
                for (var y = 0; y < Height; y++)
                {
                    for (var x = 0; x < Width; x++)
                    {
                        var mask = 0;
                        if (Solid(x, y))
                        {
                            // East, north, west, south: solid pixels stay on the left.
                            if (!Solid(x, y - 1)) mask |= 1;
                            if (!Solid(x + 1, y)) mask |= 2;
                            if (!Solid(x, y + 1)) mask |= 4;
                            if (!Solid(x - 1, y)) mask |= 8;
                        }
                        EdgeBits[y * Width + x] = (byte)mask;
                    }
                }

                for (var pixel = 0; pixel < pixelCount; pixel++)
                {
                    for (var direction = 0; direction < 4; direction++)
                    {
                        if ((EdgeBits[pixel] & (1 << direction)) == 0) continue;
                        if (!Trace(pixel, direction)) return;
                        ProcessLoop();
                        // Match the tessellator's input budget; never emit a partial mesh.
                        if (Points.Length > ushort.MaxValue / 2) return;
                    }
                }
                EdgeBits[pixelCount] = 1;
            }

            private bool Solid(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height
                && Pixels[y * Width + x].a > AlphaTolerance;

            private static int2 Step(int direction)
            {
                switch (direction)
                {
                    case 0: return new int2(1, 0);
                    case 1: return new int2(0, 1);
                    case 2: return new int2(-1, 0);
                    default: return new int2(0, -1);
                }
            }

            private bool TryEdge(int2 vertex, int direction, out int pixel)
            {
                // Recover the owning solid pixel from a directed lattice edge.
                var x = vertex.x - (direction == 1 || direction == 2 ? 1 : 0);
                var y = vertex.y - (direction >= 2 ? 1 : 0);
                pixel = -1;
                if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return false;
                pixel = y * Width + x;
                return (EdgeBits[pixel] & (1 << direction)) != 0;
            }

            private bool Trace(int pixel, int direction)
            {
                Loop.Clear();
                var first = new int2(pixel % Width, pixel / Width);
                if (direction == 1 || direction == 2) first.x++;
                if (direction >= 2) first.y++;
                var vertex = first;
                while (true)
                {
                    Loop.Add(new float2(vertex.x, vertex.y));
                    EdgeBits[pixel] = (byte)(EdgeBits[pixel] & ~(1 << direction));
                    vertex += Step(direction);
                    if (math.all(vertex == first)) return true;

                    // Prefer left, then straight, right, back. Diagonally touching
                    // islands must remain separate, just as in the original tracer.
                    var next = (direction + 1) & 3;
                    if (!TryEdge(vertex, next, out pixel))
                    {
                        next = direction;
                        if (!TryEdge(vertex, next, out pixel))
                        {
                            next = (direction + 3) & 3;
                            if (!TryEdge(vertex, next, out pixel))
                            {
                                next = (direction + 2) & 3;
                                if (!TryEdge(vertex, next, out pixel)) return false;
                            }
                        }
                    }
                    direction = next;
                }
            }

            private void ProcessLoop()
            {
                RemoveCollinear(Loop, Cleaned);
                Snapped.Clear();
                for (var i = 0; i < Cleaned.Length; i++)
                {
                    var point = Cleaned[i];
                    if (Snap >= 1f) point = math.round(point / Snap) * Snap;
                    point = math.clamp(point, float2.zero, new float2(Width, Height));
                    if (Snapped.Length == 0 || math.lengthsq(Snapped[Snapped.Length - 1] - point) > 0.0001f)
                        Snapped.Add(point);
                }
                if (Snapped.Length > 1 && math.lengthsq(Snapped[0] - Snapped[Snapped.Length - 1]) < 0.0001f)
                    Snapped.RemoveAt(Snapped.Length - 1);

                // Loop is no longer needed; reuse its allocation for cleaned snapping.
                RemoveCollinear(Snapped, Loop);
                var source = Loop;
                if (source.Length < 3 || SignedArea(source) * SignedArea(Cleaned) <= 0f) source = Cleaned;
                SimplifyClosed(source);
                var result = Simplified;
                if (result.Length < 3 || SignedArea(result) * SignedArea(source) <= 0f) result = source;
                var start = Points.Length;
                for (var i = 0; i < result.Length; i++) Points.Add(result[i]);
                Outlines.Add(new int2(start, result.Length));
            }

            private static void RemoveCollinear(NativeList<float2> source, NativeList<float2> destination)
            {
                destination.Clear();
                for (var i = 0; i < source.Length; i++)
                {
                    var a = source[(i + source.Length - 1) % source.Length];
                    var b = source[i];
                    var c = source[(i + 1) % source.Length];
                    var ab = b - a;
                    var bc = c - b;
                    var ac = c - a;
                    if (source.Length < 3 || math.abs(ab.x * ac.y - ab.y * ac.x) > 0.0001f || math.dot(ab, bc) < 0f)
                        destination.Add(b);
                }
            }

            private void SimplifyClosed(NativeList<float2> source)
            {
                Simplified.Clear();
                if (Tolerance <= 0f || source.Length < 4)
                {
                    for (var i = 0; i < source.Length; i++) Simplified.Add(source[i]);
                    return;
                }

                var split = 1;
                for (var i = 2; i < source.Length; i++)
                    if (math.lengthsq(source[i] - source[0]) > math.lengthsq(source[split] - source[0])) split = i;
                Keep.ResizeUninitialized(source.Length);
                for (var i = 0; i < Keep.Length; i++) Keep[i] = 0;
                Keep[0] = Keep[split] = 1;
                Pending.Clear();
                Pending.Add(new int2(0, split));
                Pending.Add(new int2(split, source.Length));
                while (Pending.Length > 0)
                {
                    var arc = Pending[Pending.Length - 1];
                    Pending.RemoveAt(Pending.Length - 1);
                    var a = source[arc.x];
                    var line = source[arc.y % source.Length] - a;
                    var lengthSquared = math.lengthsq(line);
                    var furthest = -1;
                    var maxDistance = Tolerance * Tolerance;
                    for (var i = arc.x + 1; i < arc.y; i++)
                    {
                        var t = lengthSquared > 0f ? math.saturate(math.dot(source[i] - a, line) / lengthSquared) : 0f;
                        var distance = math.lengthsq(source[i] - (a + t * line));
                        if (distance <= maxDistance) continue;
                        maxDistance = distance;
                        furthest = i;
                    }
                    if (furthest < 0) continue;
                    Keep[furthest] = 1;
                    Pending.Add(new int2(arc.x, furthest));
                    Pending.Add(new int2(furthest, arc.y));
                }
                for (var i = 0; i < source.Length; i++)
                    if (Keep[i] != 0) Simplified.Add(source[i]);
            }

            private static float SignedArea(NativeList<float2> polygon)
            {
                double area = 0;
                for (var i = 0; i < polygon.Length; i++)
                {
                    var a = polygon[i];
                    var b = polygon[(i + 1) % polygon.Length];
                    area += (double)a.x * b.y - (double)b.x * a.y;
                }
                return (float)(area * 0.5);
            }
        }


        private static bool TryTriangulate(NativeList<float2> points, NativeList<int2> outlines,
            out Vector2[] vertices, out ushort[] triangles)
        {
            vertices = Array.Empty<Vector2>();
            triangles = Array.Empty<ushort>();
            // Leave headroom for tessellator-generated intersection vertices.
            if (points.Length > ushort.MaxValue / 2) return false;
            // Vector Graphics consumes entire arrays, not count-limited spans. Pool
            // rentals can be oversized, so these API-boundary arrays must be exact.
            var contours = new BezierContour[outlines.Length];
            for (var i = 0; i < outlines.Length; i++)
            {
                var range = outlines[i];
                var segments = new BezierPathSegment[range.y];
                for (var j = 0; j < range.y; j++)
                {
                    var p0 = points[range.x + j];
                    var p1 = points[range.x + (j + 1) % range.y];
                    var a = new Vector2(p0.x, p0.y);
                    var b = new Vector2(p1.x, p1.y);
                    segments[j] = new BezierPathSegment
                    {
                        P0 = a,
                        P1 = Vector2.Lerp(a, b, 1f / 3f),
                        P2 = Vector2.Lerp(a, b, 2f / 3f)
                    };
                }
                contours[i] = new BezierContour { Segments = segments, Closed = true };
            }
            // The project already uses Unity Vector Graphics. Tessellate ALL contours
            // together with odd-even filling so holes and nested islands remain correct.
            var shape = new Shape
            {
                Contours = contours,
                Fill = new SolidFill { Color = Color.white, Opacity = 1f, Mode = FillMode.OddEven },
                IsConvex = false
            };
            var scene = new Scene
            {
                Root = new SceneNode { Transform = Matrix2D.identity, Shapes = new List<Shape> { shape } }
            };
            var options = new VectorUtils.TessellationOptions
            {
                StepDistance = float.MaxValue,
                MaxCordDeviation = float.MaxValue,
                MaxTanAngleDeviation = float.MaxValue,
                SamplingStepSize = 1f
            };
            var geometry = VectorUtils.TessellateScene(scene, options, null);
            long vertexCount = 0, indexCount = 0;
            foreach (var part in geometry)
            {
                if (part.Indices.Length % 3 != 0) return false;
                vertexCount += part.Vertices.Length;
                indexCount += part.Indices.Length;
                foreach (var index in part.Indices)
                    if (index >= part.Vertices.Length) return false;
            }
            if (vertexCount < 3 || vertexCount > ushort.MaxValue || indexCount == 0 || indexCount > int.MaxValue)
                return false;

            // Allocate the final exact-size arrays once, without intermediate Lists
            // or ToArray copies. OverrideGeometry also consumes the full array lengths.
            vertices = new Vector2[(int)vertexCount];
            triangles = new ushort[(int)indexCount];
            var vertexOffset = 0;
            var indexOffset = 0;
            foreach (var part in geometry)
            {
                Array.Copy(part.Vertices, 0, vertices, vertexOffset, part.Vertices.Length);
                foreach (var index in part.Indices)
                    triangles[indexOffset++] = checked((ushort)(vertexOffset + index));
                vertexOffset += part.Vertices.Length;
            }
            return true;
        }






        private Texture2D? ExecutePack(PooledList<Texture2D> textures, PooledList<SpriteTask> validTasks)
        {
            _sprites.Clear();
            if (textures.Count == 0) return null;

            var atlas = new Texture2D(8192, 8192);
            Rect[] rects = atlas.PackTextures(textures.ToArray(), 2, 8192);

            // Keep the atlas readable until all Tight sprites and custom meshes exist.



            for (int i = 0; i < rects.Length; i++)
            {
                Rect uv = rects[i];
                Rect pixelRect = new Rect(
                    uv.x * atlas.width,
                    uv.y * atlas.height,
                    uv.width * atlas.width,
                    uv.height * atlas.height
                );

                var scaleX = pixelRect.width / textures[i].width;
                var scaleY = pixelRect.height / textures[i].height;
                var sourceBorder = validTasks[i].Border;
                var border = new Vector4(sourceBorder.x * scaleX, sourceBorder.y * scaleY,
                    sourceBorder.z * scaleX, sourceBorder.w * scaleY);
                var sprite = CreateOutlinedSprite(atlas, pixelRect, textures[i], validTasks[i].FullPath,
                    border, _outlineSnap, _outlineDetail, _alphaTolerance);
                validTasks[i].Assigner(sprite);
                _sprites.Add(sprite);
            }

            atlas.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            foreach (var t in textures)
            {
                UnityEngine.Object.DestroyImmediate(t, true);
            }

            return atlas;
        }
    }
}
