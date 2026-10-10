using Cysharp.Threading.Tasks;
using MajdataPlay.Collections;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;
using MajdataPlay.Scenes.Game.Notes.Skins;
using MajdataPlay.Utils;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;

#nullable enable

namespace MajdataPlay
{
    [DontDestroyOnLoad]
    internal sealed class SkinManager : MajComponent
    {
        public delegate void OnSkinChangedCallback(SkinManager sender, CustomSkin newSkin);
        public event OnSkinChangedCallback OnSkinChanged;

        public bool IsInited { get; private set; } = false;
        public CustomSkin SelectedSkin
        {
            get
            {
                return _selectedSkin;
            }
            set
            {
                if(value == _selectedSkin)
                {
                    return;
                }
                _selectedSkin.UnloadAsync()
                    .ContinueWith(async () =>
                    {
                        await value.LoadAsync();
                        _tapLines[0] = value.TapLine_Normal;
                        _tapLines[1] = value.TapLine_Each;
                        _tapLines[2] = value.TapLine_Break;

                        _starLines[0] = value.TapLine_Slide;
                        _starLines[1] = value.TapLine_Each;
                        _starLines[2] = value.TapLine_Break;

                        _holdEnds[0] = value.HoldEndPoint_Normal;
                        _holdEnds[1] = value.HoldEndPoint_Each;
                        _holdEnds[2] = value.HoldEndPoint_Break;

                        _touchHoldFans[0] = value.TouchHold[0];
                        _touchHoldFans[1] = value.TouchHold[1];
                        _touchHoldFans[2] = value.TouchHold[2];
                        _touchHoldFans[3] = value.TouchHold[3];

                        _touchHoldBreakFans[0] = value.TouchHold_Break[0];
                        _touchHoldBreakFans[1] = value.TouchHold_Break[1];
                        _touchHoldBreakFans[2] = value.TouchHold_Break[2];
                        _touchHoldBreakFans[3] = value.TouchHold_Break[3];

                        _touchHoldMineFans[0] = value.TouchHold_Mine[0];
                        _touchHoldMineFans[1] = value.TouchHold_Mine[1];
                        _touchHoldMineFans[2] = value.TouchHold_Mine[2];
                        _touchHoldMineFans[3] = value.TouchHold_Mine[3];

                        _touchHoldBreakMineFans[0] = value.TouchHold_Break_Mine[0];
                        _touchHoldBreakMineFans[1] = value.TouchHold_Break_Mine[1];
                        _touchHoldBreakMineFans[2] = value.TouchHold_Break_Mine[2];
                        _touchHoldBreakMineFans[3] = value.TouchHold_Break_Mine[3];

                        _selectedSkin = value;
                        if(OnSkinChanged is not null)
                        {
                            OnSkinChanged(this, value);
                        }
                    }).Forget();
            }
        }
        public CustomSkin[] LoadedSkins
        {
            get
            {
                return _loadedSkinArray;
            }
        }
        List<CustomSkin> _loadedSkins = new();
        CustomSkin[] _loadedSkinArray = Array.Empty<CustomSkin>();

        CustomSkin _selectedSkin;
        public Texture2D test;

        readonly static Sprite[] _tapLines = new Sprite[3];
        readonly static Sprite[] _starLines = new Sprite[3];
        readonly static Sprite[] _holdEnds = new Sprite[3];
        readonly static Sprite[] _touchHoldFans = new Sprite[4];
        readonly static Sprite[] _touchHoldBreakFans = new Sprite[4];
        readonly static Sprite[] _touchHoldMineFans = new Sprite[4];
        readonly static Sprite[] _touchHoldBreakMineFans = new Sprite[4];

        readonly static ReadOnlyMemory<Color> _tapAndHoldExEffects = new Color[3]
        {
            new Color(255 / 255f,172 / 255f,225 / 255f), // Pink
            new Color(255 / 255f,254 / 255f,119 / 255f), // Yellow
            new Color(255 / 255f,254 / 255f,119 / 255f), // Yellow
        };
        readonly static ReadOnlyMemory<Color> _starExEffects = new Color[3]
        {
            new Color(1f,1f,1f), // Pink
            new Color(255 / 255f,254 / 255f,119 / 255f), // Yellow
            new Color(255 / 255f,254 / 255f,119 / 255f), // Yellow
        };

        protected override void Awake()
        {
            base.Awake();
            Majdata<SkinManager>.SetAsSingleton(this);
        }
        internal async Task InitAsync()
        {
            var path = MajEnv.SkinPath;
            var selectedSkinName = MajEnv.Settings.Display.Skin;
            var directories = FileSystem.OpenDirectory(path).EnumerateDirectories();
            foreach (var directory in directories)
            {
                try
                {
                    _loadedSkins.Add(CustomSkin.Create(directory.Location));
                }
                catch(Exception e)
                {
                    MajDebug.LogError($"Failed to load skin from {directory.Location}");
                    MajDebug.LogException(e);
                }
            }
            if(_loadedSkins.Count == 0)
            {
                _loadedSkins.Add(CustomSkin.Empty);
            }
            var targetSkin = _loadedSkins.Find(x => x.Name == selectedSkinName);
            if (targetSkin is null)
            {
                targetSkin = _loadedSkins[0];
                if(targetSkin.Name != CustomSkin.Empty.Name)
                {
                    MajEnv.Settings.Display.Skin = targetSkin.Name;
                    await targetSkin.LoadAsync();
                }
            }
            if(!targetSkin.IsLoaded)
            {
                await targetSkin.LoadAsync();
            }
            _loadedSkinArray = _loadedSkins.ToArray();
            _selectedSkin = targetSkin;
            _tapLines[0] = targetSkin.TapLine_Normal;
            _tapLines[1] = targetSkin.TapLine_Each;
            _tapLines[2] = targetSkin.TapLine_Break;

            _starLines[0] = targetSkin.TapLine_Slide;
            _starLines[1] = targetSkin.TapLine_Each;
            _starLines[2] = targetSkin.TapLine_Break;

            _holdEnds[0] = targetSkin.HoldEndPoint_Normal;
            _holdEnds[1] = targetSkin.HoldEndPoint_Each;
            _holdEnds[2] = targetSkin.HoldEndPoint_Break;

            _touchHoldFans[0] = targetSkin.TouchHold[0];
            _touchHoldFans[1] = targetSkin.TouchHold[1];
            _touchHoldFans[2] = targetSkin.TouchHold[2];
            _touchHoldFans[3] = targetSkin.TouchHold[3];

            _touchHoldBreakFans[0] = targetSkin.TouchHold_Break[0];
            _touchHoldBreakFans[1] = targetSkin.TouchHold_Break[1];
            _touchHoldBreakFans[2] = targetSkin.TouchHold_Break[2];
            _touchHoldBreakFans[3] = targetSkin.TouchHold_Break[3];

            _touchHoldMineFans[0] = targetSkin.TouchHold_Mine[0];
            _touchHoldMineFans[1] = targetSkin.TouchHold_Mine[1];
            _touchHoldMineFans[2] = targetSkin.TouchHold_Mine[2];
            _touchHoldMineFans[3] = targetSkin.TouchHold_Mine[3];

            _touchHoldBreakMineFans[0] = targetSkin.TouchHold_Break_Mine[0];
            _touchHoldBreakMineFans[1] = targetSkin.TouchHold_Break_Mine[1];
            _touchHoldBreakMineFans[2] = targetSkin.TouchHold_Break_Mine[2];
            _touchHoldBreakMineFans[3] = targetSkin.TouchHold_Break_Mine[3];

            IsInited = true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JudgeTextSkin GetJudgeTextSkin()
        {
            return new()
            {
                CP_Shine = SelectedSkin.CriticalPerfect_Shine,
                P_Shine = SelectedSkin.Perfect_Shine,
                Break_2600_Shine = SelectedSkin.Break_2600_Shine,
                Break_2600 = new()
                {
                    Fast = SelectedSkin.Break_2600_Fast,
                    Normal = SelectedSkin.Break_2600,
                    Late = SelectedSkin.Break_2600_Late
                },
                Break_2550 = new()
                {
                    Fast = SelectedSkin.Break_2550_Fast,
                    Normal = SelectedSkin.Break_2550,
                    Late = SelectedSkin.Break_2550_Late
                },
                Break_2500 = new()
                {
                    Fast = SelectedSkin.Break_2500_Fast,
                    Normal = SelectedSkin.Break_2500,
                    Late = SelectedSkin.Break_2500_Late
                },
                Break_2000 = new()
                {
                    Fast = SelectedSkin.Break_2000_Fast,
                    Normal = SelectedSkin.Break_2000,
                    Late = SelectedSkin.Break_2000_Late
                },
                Break_1500 = new()
                {
                    Fast = SelectedSkin.Break_1500_Fast,
                    Normal = SelectedSkin.Break_1500,
                    Late = SelectedSkin.Break_1500_Late
                },
                Break_1250 = new()
                {
                    Fast = SelectedSkin.Break_1250_Fast,
                    Normal = SelectedSkin.Break_1250,
                    Late = SelectedSkin.Break_1250_Late
                },
                Break_1000 = new()
                {
                    Fast = SelectedSkin.Break_1000_Fast,
                    Normal = SelectedSkin.Break_1000,
                    Late = SelectedSkin.Break_1000_Late
                },
                Break_0 = SelectedSkin.Break_0,
                CriticalPerfect = new()
                {
                    Fast = SelectedSkin.CriticalPerfect_Fast,
                    Normal = SelectedSkin.CriticalPerfect,
                    Late = SelectedSkin.CriticalPerfect_Late
                },
                Perfect = new()
                {
                    Fast = SelectedSkin.Perfect_Fast,
                    Normal = SelectedSkin.Perfect,
                    Late = SelectedSkin.Perfect_Late
                },
                Great = new()
                {
                    Fast = SelectedSkin.Great_Fast,
                    Normal = SelectedSkin.Great,
                    Late = SelectedSkin.Great_Late
                },
                Good = new()
                {
                    Fast = SelectedSkin.Good_Fast,
                    Normal = SelectedSkin.Good,
                    Late = SelectedSkin.Good_Late
                },
                Miss = SelectedSkin.Miss,

                Fast = SelectedSkin.Fast,
                Late = SelectedSkin.Late
            };
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TapSkin GetTapSkin()
        {
            return new()
            {
                Normal = SelectedSkin.Tap,
                Each = SelectedSkin.Tap_Each,
                Break = SelectedSkin.Tap_Break,
                Ex = SelectedSkin.Tap_Ex,

                Mine = SelectedSkin.Tap_Mine,
                BreakMine = SelectedSkin.Tap_Break_Mine,

                GuideLines = _tapLines,
                ExEffects = _tapAndHoldExEffects.Span
            };
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public StarSkin GetStarSkin()
        {
            return new()
            {
                Normal = SelectedSkin.Star,
                Double = SelectedSkin.Star_Double,
                Each = SelectedSkin.Star_Each,
                EachDouble = SelectedSkin.Star_Each_Double,
                Break = SelectedSkin.Star_Break,
                BreakDouble = SelectedSkin.Star_Break_Double,
                Ex = SelectedSkin.Star_Ex,
                ExDouble = SelectedSkin.Star_Ex_Double,

                Mine = SelectedSkin.Star_Mine,
                DoubleMine = SelectedSkin.Star_Double_Mine,
                BreakMine = SelectedSkin.Star_Break_Mine,
                BreakDoubleMine = SelectedSkin.Star_Break_Double_Mine,

                GuideLines = _starLines,
                ExEffects = _starExEffects.Span
            };
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public HoldSkin GetHoldSkin()
        {
            return new()
            {
                Normal = SelectedSkin.Hold,
                Off = SelectedSkin.Hold_Off,
                Normal_On = SelectedSkin.Hold_On,
                Each = SelectedSkin.Hold_Each,
                Each_On = SelectedSkin.Hold_Each_On,
                Break = SelectedSkin.Hold_Break,
                Break_On = SelectedSkin.Hold_Break_On,
                Ex = SelectedSkin.Hold_Ex,

                Mine = SelectedSkin.Hold_Mine,
                Mine_On = SelectedSkin.Hold_Mine_On,
                BreakMine = SelectedSkin.Hold_Break_Mine,
                BreakMine_On = SelectedSkin.Hold_Break_Mine_On,

                GuideLines = _tapLines,
                Ends = _holdEnds,
                ExEffects = _tapAndHoldExEffects.Span
            };
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SlideSkin GetSlideSkin()
        {
            return new SlideSkin()
            {
                Star = GetStarSkin(),
                Normal = SelectedSkin.Slide,
                Each = SelectedSkin.Slide_Each,
                Break = SelectedSkin.Slide_Break,
                Mine = SelectedSkin.Slide_Mine,
                BreakMine = SelectedSkin.Slide_Break_Mine,
            };
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public WifiSkin GetWifiSkin()
        {
            return new WifiSkin()
            {
                Star = GetStarSkin(),
                Normal = SelectedSkin.Wifi,
                Each = SelectedSkin.Wifi_Each,
                Break = SelectedSkin.Wifi_Break,
                Mine = SelectedSkin.Wifi_Mine,
                BreakMine = SelectedSkin.Wifi_Break_Mine,
            };
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TouchHoldSkin GetTouchHoldSkin()
        {
            return new TouchHoldSkin()
            {
                Fans = _touchHoldFans,
                Fans_Break = _touchHoldBreakFans,
                Fans_Mine = _touchHoldMineFans,
                Fans_Break_Mine = _touchHoldBreakMineFans,
                Boader = SelectedSkin.TouchHold[4],
                Boader_Break = SelectedSkin.TouchHold_Break[4],
                Boader_Mine = SelectedSkin.TouchHold_Mine[4],
                Boader_Break_Mine = SelectedSkin.TouchHold_Break_Mine[4],
                Point = SelectedSkin.TouchPoint,
                Point_Each = SelectedSkin.TouchPoint_Each,
                Point_Break = SelectedSkin.TouchPoint_Break,
                Point_Mine = SelectedSkin.TouchPoint_Mine,
                Point_Break_Mine = SelectedSkin.TouchPoint_Break_Mine,
                Off = SelectedSkin.TouchHold_Off,
            };
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TouchSkin GetTouchSkin()
        {
            return new TouchSkin()
            {
                Normal = SelectedSkin.Touch,
                Each = SelectedSkin.Touch_Each,
                Break = SelectedSkin.Touch_Break,
                Mine = SelectedSkin.Touch_Mine,
                BreakMine = SelectedSkin.Touch_Break_Mine,
                Point_Normal = SelectedSkin.TouchPoint,
                Point_Each = SelectedSkin.TouchPoint_Each,
                Point_Break = SelectedSkin.TouchPoint_Break,
                Point_Mine = SelectedSkin.TouchPoint_Mine,
                Point_Break_Mine = SelectedSkin.TouchPoint_Break_Mine,
                Border_Each = SelectedSkin.TouchBorder_Each,
                Border_Normal = SelectedSkin.TouchBorder,
                Border_Break = SelectedSkin.TouchBorder_Break,
                Border_Mine = SelectedSkin.TouchBorder_Mine,
                Border_Break_Mine = SelectedSkin.TouchBorder_Break_Mine,
                JustBorder = SelectedSkin.TouchJust
            };
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EachLineSkin GetEachLineSkin()
        {
            return new EachLineSkin()
            {
                EachGuideLines = SelectedSkin.EachLines
            };
        }
    }
}
