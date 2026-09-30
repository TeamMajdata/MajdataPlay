using MajdataPlay.Collections;
using MajdataPlay.Scenes.Game.Notes.Slide;
using MajdataPlay.IO;
using MajdataPlay.Utils;
using System;
using System.Collections.Generic;
using MajdataPlay.Settings;
using MajdataPlay.Buffers;

#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Slide.Utils
{
    public static class SlideTables
    {
        class StaticSlideTable
        {
            public string Name { get; init; } = string.Empty;
            public PredefinedSlideArea[] JudgeQueue { get; init; } = Array.Empty<PredefinedSlideArea>();
            public float Const { get; init; } = 0f;
            public float ClassicConst { get; init; } = 0f;
            public SlideTable Build()
            {
                var rentedArray = Pool<SlideArea>.Rent(JudgeQueue.Length, true);
                for (var i = 0; i < JudgeQueue.Length; i++)
                {
                    rentedArray[i] = JudgeQueue[i].Build();
                }
                return new SlideTable(rentedArray, JudgeQueue.Length)
                {
                    Name = Name,
                    Const = Const,
                    ClassicConst = ClassicConst
                };
            }
        }
        class StaticWifiTable
        {
            public string Name { get; init; } = string.Empty;
            public PredefinedSlideArea[] Left { get; init; } = Array.Empty<PredefinedSlideArea>();
            public PredefinedSlideArea[] Center { get; init; } = Array.Empty<PredefinedSlideArea>();
            public PredefinedSlideArea[] Right { get; init; } = Array.Empty<PredefinedSlideArea>();
            public float Const { get; init; } = 0f;
            public WifiTable Build()
            {
                var rentedArrayForLeft = Pool<SlideArea>.Rent(Left.Length, true);
                var rentedArrayForCenter = Pool<SlideArea>.Rent(Center.Length, true);
                var rentedArrayForRight = Pool<SlideArea>.Rent(Right.Length, true);
                for (var i = 0; i < Left.Length; i++)
                {
                    rentedArrayForLeft[i] = Left[i].Build();
                }
                for (var i = 0; i < Center.Length; i++)
                {
                    rentedArrayForCenter[i] = Center[i].Build();
                }
                for (var i = 0; i < Right.Length; i++)
                {
                    rentedArrayForRight[i] = Right[i].Build();
                }
                return new WifiTable(rentedArrayForLeft,
                                     rentedArrayForCenter,
                                     rentedArrayForRight,
                                     Left.Length,
                                     Center.Length,
                                     Right.Length)
                {
                    Name = Name,
                    Const = Const
                };
            }
        }
        readonly struct PredefinedSlideArea
        {
            public ReadOnlySpan<SensorArea> Areas
            {
                get => _areas;
            }
            public int ArrowProgressWhenOn { get; init; }
            public int ArrowProgressWhenFinished { get; init; }
            public bool IsSkippable { get; init; }
            public bool IsLast { get; init; }

            readonly SensorArea[] _areas;
            public PredefinedSlideArea(ReadOnlySpan<SensorArea> areas)
            {
                _areas = areas.ToArray();
            }
            public SlideArea Build()
            {
                Span<(SensorArea, bool)> areaInfos = stackalloc (SensorArea, bool)[_areas.Length];
                for (var i = 0; i < _areas.Length; i++)
                {
                    areaInfos[i] = (_areas[i], IsLast);
                }
                return new SlideArea(areaInfos, ArrowProgressWhenOn, ArrowProgressWhenFinished)
                {
                    IsSkippable = IsSkippable
                };
            }
        }
        readonly static StaticSlideTable[] SLIDE_TABLES = new StaticSlideTable[]
        {
            new StaticSlideTable()
            {
                Name = "circle2",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3,false),
                    BuildSlideArea(SensorArea.A2,5,7,true,true)
                },
                Const = 0.465f,
                ClassicConst = 0.505f

            },
            new StaticSlideTable()
            {
                Name = "circle3",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(SensorArea.A2,7,11,false),
                    BuildSlideArea(SensorArea.A3,13,15,true,true)
                },
                Const = 0.233f,
                ClassicConst = 0.263f
            },
            new StaticSlideTable()
            {
                Name = "circle4",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(SensorArea.A2,7,11),
                    BuildSlideArea(SensorArea.A3,14,19),
                    BuildSlideArea(SensorArea.A4,21,23,true,true)
                },
                Const = 0.155f,
                ClassicConst = 0.175f
            },
            new StaticSlideTable()
            {
                Name = "circle5",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(SensorArea.A2,7,11),
                    BuildSlideArea(SensorArea.A3,14,19),
                    BuildSlideArea(SensorArea.A4,23,27),
                    BuildSlideArea(SensorArea.A5,29,31,true,true)
                },
                Const = 0.116f,
                ClassicConst = 0.131f
            },
            new StaticSlideTable()
            {
                Name = "circle6",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(SensorArea.A2,7,11),
                    BuildSlideArea(SensorArea.A3,14,19),
                    BuildSlideArea(SensorArea.A4,23,27),
                    BuildSlideArea(SensorArea.A5,31,35),
                    BuildSlideArea(SensorArea.A6,37,39,true,true)
                },
                Const = 0.093f,
                ClassicConst = 0.108f
            },
            new StaticSlideTable()
            {
                Name = "circle7",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(SensorArea.A2,7,11),
                    BuildSlideArea(SensorArea.A3,14,19),
                    BuildSlideArea(SensorArea.A4,23,27),
                    BuildSlideArea(SensorArea.A5,31,35),
                    BuildSlideArea(SensorArea.A6,39,43),
                    BuildSlideArea(SensorArea.A7,45,47,true,true)
                },
                Const = 0.078f,
                ClassicConst = 0.0855f
            },
            new StaticSlideTable()
            {
                Name = "circle8",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(SensorArea.A2,7,11),
                    BuildSlideArea(SensorArea.A3,14,19),
                    BuildSlideArea(SensorArea.A4,23,27),
                    BuildSlideArea(SensorArea.A5,31,35),
                    BuildSlideArea(SensorArea.A6,39,43),
                    BuildSlideArea(SensorArea.A7,46,51),
                    BuildSlideArea(SensorArea.A8,53,55,true,true)
                },
                Const = 0.066f,
                ClassicConst = 0.076f
            },
            new StaticSlideTable()
            {
                Name = "circle1",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(SensorArea.A2,7,11),
                    BuildSlideArea(SensorArea.A3,14,19),
                    BuildSlideArea(SensorArea.A4,23,27),
                    BuildSlideArea(SensorArea.A5,31,35),
                    BuildSlideArea(SensorArea.A6,39,43),
                    BuildSlideArea(SensorArea.A7,46,51),
                    BuildSlideArea(SensorArea.A8,54,59),
                    BuildSlideArea(SensorArea.A1,61,63,true,true)
                },
                Const = 0.058f,
                ClassicConst = 0.0655f
            },
            new StaticSlideTable()
            {
                Name = "line3",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(new SensorArea[]{SensorArea.A2,SensorArea.B2 },6,9,false),
                    BuildSlideArea(SensorArea.A3,10,13,true,true)
                },
                Const = 0.182f,
                ClassicConst = 0.277f
            },
            new StaticSlideTable()
            {
                Name = "line4",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,4),
                    BuildSlideArea(SensorArea.B2,6,9),
                    BuildSlideArea(SensorArea.B3,11,14),
                    BuildSlideArea(SensorArea.A4,15,18,true,true)
                },
                Const = 0.19f,
                ClassicConst = 0.23f
            },
            new StaticSlideTable()
            {
                Name = "line5",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,4),
                    BuildSlideArea(SensorArea.B1,5,7),
                    BuildSlideArea(SensorArea.C,10,12),
                    BuildSlideArea(SensorArea.B5,13,16),
                    BuildSlideArea(SensorArea.A5,17,19,true,true)
                },
                Const = 0.152f,
                ClassicConst = 0.167f
            },
            new StaticSlideTable()
            {
                Name = "line6",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,4),
                    BuildSlideArea(SensorArea.B8,6, 9),
                    BuildSlideArea(SensorArea.B7,11,14),
                    BuildSlideArea(SensorArea.A6,15,18,true,true)
                },
                Const = 0.19f,
                ClassicConst = 0.23f
            },
            new StaticSlideTable()
            {
                Name = "line7",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(new SensorArea[]{SensorArea.A8,SensorArea.B8 },6,9,false),
                    BuildSlideArea(SensorArea.A7,10,13,true,true)
                },
                Const = 0.182f,
                ClassicConst = 0.277f
            },
            new StaticSlideTable()
            {
                Name = "v1",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(SensorArea.B1,4,7),
                    BuildSlideArea(SensorArea.C,8,13),
                    BuildSlideArea(SensorArea.B1,14,16),
                    BuildSlideArea(SensorArea.A1,17,19,true,true)
                },
                Const = 0.185f,
                ClassicConst = 0.205f
            },
            new StaticSlideTable()
            {
                Name = "v2",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(SensorArea.B1,4,7),
                    BuildSlideArea(SensorArea.C,8,13),
                    BuildSlideArea(SensorArea.B2,14,16),
                    BuildSlideArea(SensorArea.A2,17,19,true,true)
                },
                Const = 0.15f,
                ClassicConst = 0.17f
            },
            new StaticSlideTable()
            {
                Name = "v3",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(SensorArea.B1,4,7),
                    BuildSlideArea(SensorArea.C,8,13),
                    BuildSlideArea(SensorArea.B3,14,16),
                    BuildSlideArea(SensorArea.A3,17,19,true,true)
                },
                Const = 0.158f,
                ClassicConst = 0.178f
            },
            new StaticSlideTable()
            {
                Name = "v4",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(SensorArea.B1,4,7),
                    BuildSlideArea(SensorArea.C,8,13),
                    BuildSlideArea(SensorArea.B4, 14, 16),
                    BuildSlideArea(SensorArea.A4,17,19,true,true)
                },
                Const = 0.158f,
                ClassicConst = 0.178f
            },
            new StaticSlideTable()
            {
                Name = "v6",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(SensorArea.B1,4,7),
                    BuildSlideArea(SensorArea.C,8,13),
                    BuildSlideArea(SensorArea.B6,14,16),
                    BuildSlideArea(SensorArea.A6,17,19,true,true)
                },
                Const = 0.158f,
                ClassicConst = 0.178f
            },
            new StaticSlideTable()
            {
                Name = "v7",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(SensorArea.B1,4,7),
                    BuildSlideArea(SensorArea.C,8,13),
                    BuildSlideArea(SensorArea.B7,14,16),
                    BuildSlideArea(SensorArea.A7,17,19,true,true)
                },
                Const = 0.158f,
                ClassicConst = 0.178f
            },
            new StaticSlideTable()
            {
                Name = "v8",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(SensorArea.B1,4,7),
                    BuildSlideArea(SensorArea.C,8,13),
                    BuildSlideArea(SensorArea.B8,14,16),
                    BuildSlideArea(SensorArea.A8,17,19,true,true)
                },
                Const = 0.154f,
                ClassicConst = 0.174f
            },
            new StaticSlideTable()
            {
                Name = "ppqq1",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(SensorArea.B1,5,7),
                    BuildSlideArea(SensorArea.C,10,13),
                    BuildSlideArea(SensorArea.B4,15,17),
                    BuildSlideArea(SensorArea.A3,21,26),
                    BuildSlideArea(SensorArea.A2,29,32),
                    BuildSlideArea(SensorArea.A1,33,35,true,true)
                },
                Const = 0.065f,
                ClassicConst = 0.095f

            },
            new StaticSlideTable()
            {
                Name = "ppqq2",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(SensorArea.B1,5,7),
                    BuildSlideArea(SensorArea.C,9,13),
                    BuildSlideArea(SensorArea.B4,14,17),
                    BuildSlideArea(SensorArea.A3,20,25),
                    BuildSlideArea(SensorArea.A2,26,28,true,true),
                },
                Const = 0.086f,
                ClassicConst = 0.131f
            },
            new StaticSlideTable()
            {
                Name = "ppqq3",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(SensorArea.B1,4,7),
                    BuildSlideArea(SensorArea.C,9,13),
                    BuildSlideArea(SensorArea.B4,14,17),
                    BuildSlideArea(SensorArea.A3,19,22,true,true),
                },
                Const = 0.157f,
                ClassicConst = 0.197f
            },
            new StaticSlideTable()
            {
                Name = "ppqq4",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(SensorArea.B1,5,7),
                    BuildSlideArea(SensorArea.C,9,13),
                    BuildSlideArea(SensorArea.B4,14,17),
                    BuildSlideArea(SensorArea.A3,20,25),
                    BuildSlideArea(SensorArea.A2,28,33),
                    BuildSlideArea(SensorArea.B1,34,37),
                    BuildSlideArea(SensorArea.C,39,43),
                    BuildSlideArea(SensorArea.B4,44,46),
                    BuildSlideArea(SensorArea.A4,47,49,true,true),
                },
                Const = 0.065f,
                ClassicConst = 0.0725f
            },
            new StaticSlideTable()
            {
                Name = "ppqq5",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(SensorArea.B1,5,7),
                    BuildSlideArea(SensorArea.C,9,13),
                    BuildSlideArea(SensorArea.B4,14,17),
                    BuildSlideArea(SensorArea.A3,20,25),
                    BuildSlideArea(SensorArea.A2,28,33),
                    BuildSlideArea(SensorArea.B1,34,37),
                    BuildSlideArea(SensorArea.C,39,43),
                    BuildSlideArea(SensorArea.B5,44,46),
                    BuildSlideArea(SensorArea.A5,47,49,true,true),
                },
                Const = 0.065f,
                ClassicConst = 0.075f
            },
            new StaticSlideTable()
            {
                Name = "ppqq6",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(SensorArea.B1,5,7),
                    BuildSlideArea(SensorArea.C,9,13),
                    BuildSlideArea(SensorArea.B4,14,17),
                    BuildSlideArea(SensorArea.A3,20,25),
                    BuildSlideArea(SensorArea.A2,28,33),
                    BuildSlideArea(SensorArea.B1,34,37),
                    BuildSlideArea(new SensorArea[] { SensorArea.C,SensorArea.B8 },38,40),
                    BuildSlideArea(new SensorArea[] { SensorArea.B7,SensorArea.B6 },42,44),
                    BuildSlideArea(SensorArea.A6,46,48,true,true),
                },
                Const = 0.067f,
                ClassicConst = 0.077f
            },
            new StaticSlideTable()
            {
                Name = "ppqq7",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(SensorArea.B1,5,7),
                    BuildSlideArea(SensorArea.C,9,13),
                    BuildSlideArea(SensorArea.B4,14,17),
                    BuildSlideArea(SensorArea.A3,20,25),
                    BuildSlideArea(SensorArea.A2,28,33),
                    BuildSlideArea(SensorArea.B1,34,37),
                    BuildSlideArea(SensorArea.B8,38,42),
                    BuildSlideArea(SensorArea.A7,43,46,true,true),
                },
                Const = 0.079f,
                ClassicConst = 0.094f
            },
            new StaticSlideTable()
            {
                Name = "ppqq8",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(SensorArea.B1,5,7),
                    BuildSlideArea(SensorArea.C,9,13),
                    BuildSlideArea(SensorArea.B4,14,17),
                    BuildSlideArea(SensorArea.A3,20,25),
                    BuildSlideArea(SensorArea.A2,28,33),
                    BuildSlideArea(new SensorArea[] { SensorArea.B1,SensorArea.A1 },35,37),
                    BuildSlideArea(SensorArea.A8,38,41,true,true),
                },
                Const = 0.0626f,
                ClassicConst = 0.0801f
            },
            new StaticSlideTable()
            {
                Name = "L2",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(new SensorArea[] { SensorArea.B8,SensorArea.A8 },6,10,false),
                    BuildSlideArea(SensorArea.A7,12,19),
                    BuildSlideArea(SensorArea.B8,21,24),
                    BuildSlideArea(SensorArea.B1,25,28),
                    BuildSlideArea(SensorArea.A2,29,32,true,true),
                },
                Const = 0.1f,
                ClassicConst = 0.12f
            },
            new StaticSlideTable()
            {
                Name = "L3",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(new SensorArea[] { SensorArea.B8,SensorArea.A8 },6,10,false),
                    BuildSlideArea(SensorArea.A7,12,18),
                    BuildSlideArea(SensorArea.B7,20,22),
                    BuildSlideArea(SensorArea.C,25,27),
                    BuildSlideArea(SensorArea.B3,28,31),
                    BuildSlideArea(SensorArea.A3,32,34,true,true),
                },
                Const = 0.104f,
                ClassicConst = 0.114f
            },
            new StaticSlideTable()
            {
                Name = "L4",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(new SensorArea[] { SensorArea.B8,SensorArea.A8 },6,10,false),
                    BuildSlideArea(SensorArea.A7,12,19),
                    BuildSlideArea(SensorArea.B6,21,24),
                    BuildSlideArea(SensorArea.B5,25,28),
                    BuildSlideArea(SensorArea.A4,29,32,true,true),
                },
                Const = 0.098f,
                ClassicConst = 0.123f
            },
            new StaticSlideTable()
            {
                Name = "L5",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,3),
                    BuildSlideArea(new SensorArea[] { SensorArea.B8,SensorArea.A8 },6,10,false),
                    BuildSlideArea(SensorArea.A7,12,18),
                    BuildSlideArea(new SensorArea[] { SensorArea.B6,SensorArea.A6 },21,24,false),
                    BuildSlideArea(SensorArea.A5,27,28,true,true),
                },
                Const = 0.105f,
                ClassicConst = 0.15f
            },
            new StaticSlideTable()
            {
                Name = "s",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,4),
                    BuildSlideArea(SensorArea.B8,7,9),
                    BuildSlideArea(SensorArea.B7,10,12),
                    BuildSlideArea(SensorArea.C,14,17),
                    BuildSlideArea(SensorArea.B3,19,21),
                    BuildSlideArea(SensorArea.B4,22,25),
                    BuildSlideArea(SensorArea.A5,27,30,true,true),
                },
                Const = 0.13f,
                ClassicConst = 0.155f
            },
            new StaticSlideTable()
            {
                Name = "pq1",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,4),
                    BuildSlideArea(SensorArea.B8,5, 8),
                    BuildSlideArea(SensorArea.B7,9,11),
                    BuildSlideArea(SensorArea.B6,12,14),
                    BuildSlideArea(SensorArea.B5,15,17),
                    BuildSlideArea(SensorArea.B4,19,21),
                    BuildSlideArea(SensorArea.B3,22,24),
                    BuildSlideArea(SensorArea.B2,25,29),
                    BuildSlideArea(SensorArea.A1,30,33,true,true),
                },
                Const = 0.095f,
                ClassicConst = 0.115f
            },
            new StaticSlideTable()
            {
                Name = "pq2",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,4),
                    BuildSlideArea(SensorArea.B8,5,8),
                    BuildSlideArea(SensorArea.B7,9,11),
                    BuildSlideArea(SensorArea.B6,12,14),
                    BuildSlideArea(SensorArea.B5,16,18),
                    BuildSlideArea(SensorArea.B4,19,21),
                    BuildSlideArea(SensorArea.B3,22,26),
                    BuildSlideArea(SensorArea.A2,27,30,true,true),
                },
                Const = 0.112f,
                ClassicConst = 0.137f
            },
            new StaticSlideTable()
            {
                Name = "pq3",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,4),
                    BuildSlideArea(SensorArea.B8,5,8),
                    BuildSlideArea(SensorArea.B7,9,11),
                    BuildSlideArea(SensorArea.B6,12,14),
                    BuildSlideArea(SensorArea.B5,16,18),
                    BuildSlideArea(SensorArea.B4,20,23),
                    BuildSlideArea(SensorArea.A3,25,27,true,true),
                },
                Const = 0.125f,
                ClassicConst = 0.150f
            },
            new StaticSlideTable()
            {
                Name = "pq4",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,4),
                    BuildSlideArea(SensorArea.B8,5,8),
                    BuildSlideArea(SensorArea.B7,9,11),
                    BuildSlideArea(SensorArea.B6,12,14),
                    BuildSlideArea(SensorArea.B5,16,20),
                    BuildSlideArea(SensorArea.A4,22,24,true,true),
                },
                Const = 0.139f,
                ClassicConst = 0.169f
            },
            new StaticSlideTable()
            {
                Name = "pq5",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,4),
                    BuildSlideArea(SensorArea.B8,5,8),
                    BuildSlideArea(SensorArea.B7,9,12),
                    BuildSlideArea(SensorArea.B6,14,17),
                    BuildSlideArea(SensorArea.A5,19,21,true,true),
                },
                Const = 0.160f,
                ClassicConst = 0.1925f
            },
            new StaticSlideTable()
            {
                Name = "pq6",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,4),
                    BuildSlideArea(SensorArea.B8,5,8),
                    BuildSlideArea(SensorArea.B7,9,11),
                    BuildSlideArea(SensorArea.B6,13,15),
                    BuildSlideArea(SensorArea.B5,16,18),
                    BuildSlideArea(SensorArea.B4,19,21),
                    BuildSlideArea(SensorArea.B3,22,24),
                    BuildSlideArea(SensorArea.B2,25,27),
                    BuildSlideArea(SensorArea.B1,28,30),
                    BuildSlideArea(SensorArea.B8,31,33),
                    BuildSlideArea(SensorArea.B7,35,38),
                    BuildSlideArea(SensorArea.A6,40,42,true,true),
                },
                Const = 0.080f,
                ClassicConst = 0.0975f
            },
            new StaticSlideTable()
            {
                Name = "pq7",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,4),
                    BuildSlideArea(SensorArea.B8,7,9),
                    BuildSlideArea(SensorArea.B7,10,12),
                    BuildSlideArea(SensorArea.B6,13,15),
                    BuildSlideArea(SensorArea.B5,16,18),
                    BuildSlideArea(SensorArea.B4,20,22),
                    BuildSlideArea(SensorArea.B3,23,25),
                    BuildSlideArea(SensorArea.B2,26,28),
                    BuildSlideArea(SensorArea.B1,30,32),
                    BuildSlideArea(SensorArea.B8,33,36),
                    BuildSlideArea(SensorArea.A7,37,40,true,true),
                },
                Const = 0.084f,
                ClassicConst = 0.104f
            },
            new StaticSlideTable()
            {
                Name = "pq8",
                JudgeQueue = new PredefinedSlideArea[]
                {
                    BuildSlideArea(SensorArea.A1,0,4),
                    BuildSlideArea(SensorArea.B8,5,8),
                    BuildSlideArea(SensorArea.B7,9,11),
                    BuildSlideArea(SensorArea.B6,12,14),
                    BuildSlideArea(SensorArea.B5,15,17),
                    BuildSlideArea(SensorArea.B4,19,21),
                    BuildSlideArea(SensorArea.B3,22,24),
                    BuildSlideArea(SensorArea.B2,25,27),
                    BuildSlideArea(SensorArea.B1,28,32),
                    BuildSlideArea(SensorArea.A8,33,36,true,true),
                },
                Const = 0.0895f,
                ClassicConst = 0.1095f
            },
        };
        readonly static StaticWifiTable WIFISLIDE_JUDGE_QUEUE = new StaticWifiTable
        {
            Name = "wifi",
            Left = new PredefinedSlideArea[] // L
            {
                BuildSlideArea(SensorArea.A1,0),
                BuildSlideArea(SensorArea.B8,2),
                BuildSlideArea(SensorArea.B7,4),
                BuildSlideArea(stackalloc SensorArea[]{ SensorArea.A6 , SensorArea.D6 },7,true,true)
            },
            Center = new PredefinedSlideArea[] // Center
            {
                BuildSlideArea(SensorArea.A1,0),
                BuildSlideArea(SensorArea.B1,2),
                BuildSlideArea(SensorArea.C,4),
                BuildSlideArea(stackalloc SensorArea[]{ SensorArea.A5 , SensorArea.B5 },7,true,true)
            },
            Right = new PredefinedSlideArea[] // R
            {
                BuildSlideArea(SensorArea.A1,0),
                BuildSlideArea(SensorArea.B2,2),
                BuildSlideArea(SensorArea.B3,4),
                BuildSlideArea(stackalloc SensorArea[]{ SensorArea.A4 , SensorArea.D5 },7,true,true)
            },
            Const = 0.162870f
        };
        readonly static StaticWifiTable WIFISLIDE_JUDGE_QUEUE_CLASSIC = new StaticWifiTable
        {
            Name = "wifi",
            Left = new PredefinedSlideArea[] // L
            {
                BuildSlideArea(SensorArea.A1,0),
                BuildSlideArea(SensorArea.B8,2),
                BuildSlideArea(SensorArea.B7,4),
                BuildSlideArea(stackalloc SensorArea[]{ SensorArea.A6 , SensorArea.D6 },7,true,true)
            },
            Center = new PredefinedSlideArea[] // Center
            {
                BuildSlideArea(SensorArea.A1,0),
                BuildSlideArea(SensorArea.B1,2),
                BuildSlideArea(SensorArea.C,7,true,false),
            },
            Right = new PredefinedSlideArea[] // R
            {
                BuildSlideArea(SensorArea.A1,0),
                BuildSlideArea(SensorArea.B2,2),
                BuildSlideArea(SensorArea.B3,4),
                BuildSlideArea(stackalloc SensorArea[]{ SensorArea.A4 , SensorArea.D5 },7,true,true)
            },
            Const = 0.162870f
        };
        public static SlideTable? FindTableByName(string prefabName)
        {
            var predefinedTable = SLIDE_TABLES.Find(x => x.Name == prefabName);
            
            return predefinedTable?.Build();
        }
        public static WifiTable GetWifiTable(int startPos)
        {
            var predefinedTable = (MajEnv.Settings?.Judge.Mode ?? JudgeModeOption.Modern) == JudgeModeOption.Modern ? WIFISLIDE_JUDGE_QUEUE : WIFISLIDE_JUDGE_QUEUE_CLASSIC;
            var table = predefinedTable.Build();
            var diff = Math.Abs(1 - startPos);

            if (diff != 0)
            {
                table.Diff(diff);
            }

            return table;
        }
        static PredefinedSlideArea BuildSlideArea(SensorArea type, int arrowProgress, bool isSkippable = true, bool isLast = false)
        {
            ReadOnlySpan<SensorArea> areaInfos = stackalloc SensorArea[]
            {
                type
            };
            var a = new PredefinedSlideArea(areaInfos)
            {
                ArrowProgressWhenOn = arrowProgress,
                ArrowProgressWhenFinished = arrowProgress,
                IsSkippable = isSkippable,
                IsLast = isLast
            };

            return a;
        }
        static PredefinedSlideArea BuildSlideArea(SensorArea type, int progressWhenOn, int progressWhenFinished, bool isSkippable = true, bool isLast = false)
        {
            ReadOnlySpan<SensorArea> areaInfos = stackalloc SensorArea[]
            {
                type
            };
            var a = new PredefinedSlideArea(areaInfos)
            {
                ArrowProgressWhenOn = progressWhenOn,
                ArrowProgressWhenFinished = progressWhenFinished,
                IsSkippable = isSkippable,
                IsLast = isLast
            };

            return a;
        }
        static PredefinedSlideArea BuildSlideArea(ReadOnlySpan<SensorArea> type, int arrowProgress, bool isSkippable = true, bool isLast = false)
        {
            var a = new PredefinedSlideArea(type)
            {
                ArrowProgressWhenOn = arrowProgress,
                ArrowProgressWhenFinished = arrowProgress,
                IsSkippable = isSkippable,
                IsLast = isLast
            };

            return a;
        }
        static PredefinedSlideArea BuildSlideArea(ReadOnlySpan<SensorArea> type, int progressWhenOn, int progressWhenFinished, bool isSkippable = true, bool isLast = false)
        {
            var a = new PredefinedSlideArea(type)
            {
                ArrowProgressWhenOn = progressWhenOn,
                ArrowProgressWhenFinished = progressWhenFinished,
                IsSkippable = isSkippable,
                IsLast = isLast
            };

            return a;
        }
    }
}
