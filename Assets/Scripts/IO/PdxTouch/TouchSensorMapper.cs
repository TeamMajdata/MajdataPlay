#if UNITY_STANDALONE_WIN
using System;
using System.Linq;
using MajdataPlay.Numerics;
using UnityEngine;

namespace MajdataPlay.IO;

public class TouchSensorMapper
{
    private readonly float _minX;
    private readonly float _minY;
    private readonly float _maxX;
    private readonly float _maxY;
    private readonly float _radius;
    private readonly bool _flip;
    private readonly float _aExtraRadius;
    private readonly float _bExtraRadius;
    private readonly float _cExtraRadius;
    private readonly float _dExtraRadius;
    private readonly float _eExtraRadius;

    public TouchSensorMapper(float minX, float minY, float maxX, float maxY, float radius, bool flip,
        float aExtraRadius = 0, float bExtraRadius = 0, float cExtraRadius = 0, float dExtraRadius = 0,
        float eExtraRadius = 0)
    {
        _minX = minX;
        _minY = minY;
        _maxX = maxX;
        _maxY = maxY;
        _radius = radius;
        _flip = flip;
        _aExtraRadius = aExtraRadius;
        _bExtraRadius = bExtraRadius;
        _cExtraRadius = cExtraRadius;
        _dExtraRadius = dExtraRadius;
        _eExtraRadius = eExtraRadius;
    }
    private static readonly Vector2[][] _sensors = new Vector2[][] {
        // A1 (0)
        MakePolygon(786, 11, new Vector2[] {
                new Vector2(150, 28), new Vector2(245, 65), new Vector2(360, 133), new Vector2(208, 338),
                new Vector2(145, 338), new Vector2(49, 297), new Vector2(0, 249), new Vector2(35, 0)
            }),

        // A2 (1)
        MakePolygon(1091, 292, new Vector2[] {
                new Vector2(261, 101), new Vector2(303, 195), new Vector2(339, 327), new Vector2(91, 362),
                new Vector2(42, 314), new Vector2(0, 219), new Vector2(0, 150), new Vector2(202, 0)
            }),

        // A3 (2)
        MakePolygon(1092, 786, new Vector2[] {
                new Vector2(305, 150), new Vector2(269, 246), new Vector2(201, 364), new Vector2(0, 213),
                new Vector2(0, 144), new Vector2(41, 48), new Vector2(89, 0), new Vector2(337, 34)
            }),

        // A4 (3)
        MakePolygon(786, 1092, new Vector2[] {
                new Vector2(260, 259), new Vector2(167, 301), new Vector2(37, 335), new Vector2(0, 83),
                new Vector2(48, 35), new Vector2(144, 0), new Vector2(212, 0), new Vector2(364, 200)
            }),

        // A5 (4)
        MakePolygon(291, 1092, new Vector2[] {
                new Vector2(104, 259), new Vector2(197, 301), new Vector2(327, 335), new Vector2(363, 83),
                new Vector2(316, 35), new Vector2(220, 0), new Vector2(152, 0), new Vector2(0, 201)
            }),

        // A6 (5)
        MakePolygon(16, 785, new Vector2[] {
                new Vector2(32, 150), new Vector2(68, 246), new Vector2(133, 365), new Vector2(333, 214),
                new Vector2(333, 144), new Vector2(296, 48), new Vector2(248, 0), new Vector2(0, 35)
            }),

        // A7 (6)
        MakePolygon(16, 291, new Vector2[] {
                new Vector2(78, 101), new Vector2(36, 195), new Vector2(0, 327), new Vector2(248, 362),
                new Vector2(297, 314), new Vector2(333, 219), new Vector2(333, 151), new Vector2(132, 0)
            }),

        // A8 (7)
        MakePolygon(295, 11, new Vector2[] {
                new Vector2(210, 28), new Vector2(115, 65), new Vector2(0, 138), new Vector2(153, 338),
                new Vector2(215, 338), new Vector2(311, 297), new Vector2(359, 249), new Vector2(324, 0)
                }),

        // B1 (8)
        MakePolygon(720, 346, new Vector2[] {
                new Vector2(0, 78), new Vector2(78, 0), new Vector2(209, 55),
                new Vector2(209, 165), new Vector2(180, 195), new Vector2(70, 195), new Vector2(0, 130)
            }),

        // B2 (9)
        MakePolygon(900, 511, new Vector2[] {
                new Vector2(117, 209), new Vector2(195, 132), new Vector2(140, 0),
                new Vector2(30, 0), new Vector2(0, 30), new Vector2(0, 139), new Vector2(65, 209)
            }),

        // B3 (10)
        MakePolygon(900, 721, new Vector2[] {
                new Vector2(120, 0), new Vector2(198, 78), new Vector2(140, 208),
                new Vector2(30, 208), new Vector2(0, 180), new Vector2(0, 71), new Vector2(65, 0)
            }),

        // B4 (11)
        MakePolygon(721, 901, new Vector2[] {
                new Vector2(0, 112), new Vector2(87, 198), new Vector2(208, 140),
                new Vector2(208, 29), new Vector2(177, 0), new Vector2(71, 0), new Vector2(0, 65)
            }),

        // B5 (12)
        MakePolygon(512, 901, new Vector2[] {
                new Vector2(208, 112), new Vector2(121, 198), new Vector2(0, 140),
                new Vector2(0, 29), new Vector2(31, 0), new Vector2(137, 0), new Vector2(208, 65)
            }),

        // B6 (13)
        MakePolygon(349, 721, new Vector2[] {
                new Vector2(78, 0), new Vector2(0, 78), new Vector2(58, 208),
                new Vector2(163, 208), new Vector2(193, 180), new Vector2(193, 71), new Vector2(133, 0)
                }),

        // B7 (14)
        MakePolygon(345, 511, new Vector2[] {
                new Vector2(82, 209), new Vector2(0, 127), new Vector2(55, 0),
                new Vector2(165, 0), new Vector2(195, 30), new Vector2(195, 139), new Vector2(137, 209)
            }),

        // B8 (15)
        MakePolygon(511, 346, new Vector2[] {
                new Vector2(209, 78), new Vector2(131, 0), new Vector2(0, 55),
                new Vector2(0, 165), new Vector2(29, 195), new Vector2(139, 195), new Vector2(209, 130)
            }),

        // C1 (16)
        MakePolygon(720, 583, new Vector2[] {
                new Vector2(0, 0), new Vector2(60, 0), new Vector2(140, 80),
                new Vector2(140, 200), new Vector2(60, 280), new Vector2(0, 280), new Vector2(0, 0)
            }),

        // C2 (17)
        MakePolygon(579, 583, new Vector2[] {
                new Vector2(141, 280), new Vector2(81, 280), new Vector2(0, 199),
                new Vector2(1, 81), new Vector2(81, 0), new Vector2(141, 0), new Vector2(141, 280)
            }),

        // D1 (18)
        MakePolygon(620, 6, new Vector2[] {
                new Vector2(0, 5), new Vector2(50, 2), new Vector2(100, 0), new Vector2(150, 2),
                new Vector2(200, 5), new Vector2(165, 253), new Vector2(100, 188), new Vector2(35, 253)
            }),

        // D2 (19)
        MakePolygon(995, 144, new Vector2[] {
                new Vector2(153, 0), new Vector2(187, 32), new Vector2(225, 67), new Vector2(259, 104),
                new Vector2(295, 147), new Vector2(96, 297), new Vector2(96, 205), new Vector2(0, 205)
                }),

        // D3 (20)
        MakePolygon(1182, 620, new Vector2[] {
                new Vector2(248, 0), new Vector2(251, 48), new Vector2(253, 100), new Vector2(251, 150),
                new Vector2(247, 199), new Vector2(0, 165), new Vector2(65, 100), new Vector2(0, 35)
            }),

        // D4 (21)
        MakePolygon(1000, 1000, new Vector2[] {
                new Vector2(292, 151), new Vector2(260, 187), new Vector2(225, 225), new Vector2(188, 259),
                new Vector2(151, 291), new Vector2(0, 92), new Vector2(92, 92), new Vector2(92, 0)
            }),

        // D5 (22)
        MakePolygon(621, 1175, new Vector2[] {
                new Vector2(199, 252), new Vector2(151, 255), new Vector2(99, 257), new Vector2(49, 255),
                new Vector2(0, 252), new Vector2(34, 0), new Vector2(99, 65), new Vector2(164, 0)
            }),

        // D6 (23)
        MakePolygon(150, 1000, new Vector2[] {
                new Vector2(140, 292), new Vector2(104, 260), new Vector2(66, 225), new Vector2(32, 188),
                new Vector2(0, 151), new Vector2(199, 0), new Vector2(199, 92), new Vector2(291, 92)
            }),

        // D7 (24)
        MakePolygon(10, 620, new Vector2[] {
                new Vector2(5, 199), new Vector2(2, 151), new Vector2(0, 99), new Vector2(2, 49),
                new Vector2(6, 0), new Vector2(253, 34), new Vector2(188, 99), new Vector2(253, 164)
            }),

        // D8 (25)
        MakePolygon(149, 150, new Vector2[] {
                new Vector2(0, 140), new Vector2(32, 104), new Vector2(67, 66), new Vector2(104, 32),
                new Vector2(145, 0), new Vector2(298, 199), new Vector2(200, 199), new Vector2(200, 291)
                }),

        // E1 (26)
        MakePolygon(607, 195, new Vector2[] {
                new Vector2(0, 113), new Vector2(113, 0), new Vector2(226, 113), new Vector2(113, 226)
            }),

        // E2 (27)
        MakePolygon(930, 350, new Vector2[] {
                new Vector2(0, 0), new Vector2(0, 160), new Vector2(160, 160),
                new Vector2(160, 0), new Vector2(0, 0)
            }),

        // E3 (28)
        MakePolygon(1020, 607, new Vector2[] {
                new Vector2(0, 113), new Vector2(113, 0), new Vector2(226, 113), new Vector2(113, 226)
            }),

        // E4 (29)
        MakePolygon(930, 930, new Vector2[] {
                new Vector2(0, 0), new Vector2(0, 160), new Vector2(160, 160),
                new Vector2(160, 0), new Vector2(0, 0)
            }),

        // E5 (30)
        MakePolygon(607, 1013, new Vector2[] {
                new Vector2(0, 113), new Vector2(113, 0), new Vector2(226, 113), new Vector2(113, 226)
            }),

        // E6 (31)
        MakePolygon(350, 930, new Vector2[] {
                new Vector2(0, 0), new Vector2(0, 160), new Vector2(160, 160),
                new Vector2(160, 0), new Vector2(0, 0)
                }),

        // E7 (32)
        MakePolygon(200, 607, new Vector2[] {
                new Vector2(0, 113), new Vector2(113, 0), new Vector2(226, 113), new Vector2(113, 226)
            }),

        // E8 (33)
        MakePolygon(350, 350, new Vector2[] {
                new Vector2(0, 0), new Vector2(0, 160), new Vector2(160, 160),
                new Vector2(160, 0), new Vector2(0, 0)
            }),
    };

    private static Vector2[] MakePolygon(int offsetX, int offsetY, Vector2[] points)
    {
        return points.Select(p => p + new Vector2(offsetX, offsetY)).ToArray();
    }

    public ulong ParseTouchPoint(float x, float y)
    {
        var canvasPoint = new Vector2(MapCoordinate(x, _minX, _maxX, 0, 1440), MapCoordinate(y, _minY, _maxY, 0, 1440));
        if (canvasPoint.x < 0 || canvasPoint.x > 1440 || canvasPoint.y < 0 || canvasPoint.y > 1440)
        {
            return 0;
        }

        if (_flip)
        {
            canvasPoint = new Vector2(canvasPoint.y, canvasPoint.x);
        }

        ulong res = 0;

        // 检查所有传感器
        for (int i = 0; i < 34; i++)
        {
            bool isInsidePolygon;
            float effectiveRadius = GetEffectiveRadius(i);

            if (effectiveRadius > 0)
            {
                // 当有半径时，需要检查圆与多边形的关系
                isInsidePolygon = PolygonRaycasting.IsVertDistance(_sensors[i], canvasPoint, effectiveRadius);
                if (!isInsidePolygon)
                {
                    isInsidePolygon = PolygonRaycasting.IsCircleIntersectingPolygonEdges(_sensors[i], canvasPoint, effectiveRadius);
                }
                if (!isInsidePolygon)
                {
                    isInsidePolygon = PolygonRaycasting.InPointInInternal(_sensors[i], canvasPoint);
                }
            }
            else
            {
                // 当半径为0时，只需要检查点是否在多边形内部
                isInsidePolygon = PolygonRaycasting.InPointInInternal(_sensors[i], canvasPoint);
            }
            
            if (isInsidePolygon)
            {
                res |= 1ul << i;
            }
        }

        return res;
    }

    /// <summary>
    /// 线性映射坐标
    /// </summary>
    private float MapCoordinate(float value, float fromMin, float fromMax, float toMin, float toMax)
    {
        return toMin + (value - fromMin) * (toMax - toMin) / (fromMax - fromMin);
    }

    /// <summary>
    /// 根据传感器索引获取有效半径（基础半径 + 区域额外半径）
    /// A: 0-7, B: 8-15, C: 16-17, D: 18-25, E: 26-33
    /// </summary>
    private float GetEffectiveRadius(int sensorIndex)
    {
        float extra;
        if (sensorIndex <= 7) extra = _aExtraRadius;
        else if (sensorIndex <= 15) extra = _bExtraRadius;
        else if (sensorIndex <= 17) extra = _cExtraRadius;
        else if (sensorIndex <= 25) extra = _dExtraRadius;
        else extra = _eExtraRadius;

        return Math.Max(0, _radius + extra);
    }
}
#endif
