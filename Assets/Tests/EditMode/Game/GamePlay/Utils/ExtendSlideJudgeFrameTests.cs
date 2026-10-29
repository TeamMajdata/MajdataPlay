using System.Collections.Generic;
using MajdataPlay.Scenes.Game.Notes.Slide.Utils;
using MajdataPlay.Scenes.Game.Parsing;
using MajdataPlay.Scenes.Game.Parsing.Slide;
using MajdataPlay.Scenes.Game.Utils;
using MajdataPlay.Settings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Utils
{
    /// <summary>
    /// Checks where the guide star of an extended slide is at the judge frame: with a predefined table it must be where
    /// the star of the prefab slide of the same shape is, and with the generated constant it must be on the path where the
    /// last judge area begins.
    /// </summary>
    public class ExtendSlideJudgeFrameTests
    {
        /// <summary>The folder of the prefab slides.</summary>
        private const string PrefabFolder = "Assets/Prefabs/Game/Slides/";

        /// <summary>The distance from the centre at which a prefab slide starts and ends its star.</summary>
        private const float TapDistance = 4.8f;

        /// <summary>
        /// The largest distance, in world units, between the judge frame stars of an extended slide and a prefab slide.
        /// The arrows of the prefabs were placed by hand and differ from the generated arrows by a few pixels, and by up
        /// to about 13 px on the ppqq shapes.
        /// </summary>
        private const float PrefabTolerance = 0.15f;

        /// <summary>
        /// The largest distance, in world units, between the star at the generated judge frame and the path point where
        /// the last judge area begins. The star moves on straight lines between its poses, so on arcs it cuts the corner.
        /// </summary>
        private const float PathTolerance = 0.01f;

        /// <summary>
        /// Gets the prefab slide shapes as table name, prefab name and the slide code drawing the same path. v1, ppqq3 and
        /// s are left out: their prefabs have one arrow more than the slide code path, so the same constant puts the star
        /// of the extended slide 30 to 41 px earlier.
        /// </summary>
        private static object[] PrefabShapes => new object[]
        {
            new object[] { "circle1", "Star_Circle_1", "1Q9K1" },
            new object[] { "circle2", "Star_Circle_2", "1Q9K2" },
            new object[] { "circle3", "Star_Circle_3", "1Q9K3" },
            new object[] { "circle4", "Star_Circle_4", "1Q9K4" },
            new object[] { "circle5", "Star_Circle_5", "1Q9K5" },
            new object[] { "circle6", "Star_Circle_6", "1Q9K6" },
            new object[] { "circle7", "Star_Circle_7", "1Q9K7" },
            new object[] { "circle8", "Star_Circle_8", "1Q9K8" },
            new object[] { "line3", "Star_Line_3", "1K3" },
            new object[] { "line4", "Star_Line_4", "1K4" },
            new object[] { "line5", "Star_Line_5", "1K5" },
            new object[] { "line6", "Star_Line_6", "1K6" },
            new object[] { "line7", "Star_Line_7", "1K7" },
            new object[] { "v2", "Star_V_2", "1CK2" },
            new object[] { "v3", "Star_V_3", "1CK3" },
            new object[] { "v4", "Star_V_4", "1CK4" },
            new object[] { "v6", "Star_V_6", "1CK6" },
            new object[] { "v7", "Star_V_7", "1CK7" },
            new object[] { "v8", "Star_V_8", "1CK8" },
            new object[] { "ppqq1", "Star_ppqq_1", "1P3K1" },
            new object[] { "ppqq2", "Star_ppqq_2", "1P3K2" },
            new object[] { "ppqq4", "Star_ppqq_4", "1P3P3K4" },
            new object[] { "ppqq5", "Star_ppqq_5", "1P3P3K5" },
            new object[] { "ppqq6", "Star_ppqq_6", "1P3K6" },
            new object[] { "ppqq7", "Star_ppqq_7", "1P3K7" },
            new object[] { "ppqq8", "Star_ppqq_8", "1P3K8" },
            new object[] { "L2", "Star_L_2", "1A7K2" },
            new object[] { "L3", "Star_L_3", "1A7K3" },
            new object[] { "L4", "Star_L_4", "1A7K4" },
            new object[] { "L5", "Star_L_5", "1A7K5" },
            new object[] { "pq1", "Star_pq_1", "1P0K1" },
            new object[] { "pq2", "Star_pq_2", "1P0K2" },
            new object[] { "pq3", "Star_pq_3", "1P0K3" },
            new object[] { "pq4", "Star_pq_4", "1P0K4" },
            new object[] { "pq5", "Star_pq_5", "1P0K5" },
            new object[] { "pq6", "Star_pq_6", "1P0K6" },
            new object[] { "pq7", "Star_pq_7", "1P0K7" },
            new object[] { "pq8", "Star_pq_8", "1P0K8" }
        };

        /// <summary>Builds the judge area lookup table used to create the metadata.</summary>
        [SetUp]
        public void SetUp()
        {
            SlideDataBuilder.InitializeSlideAreaLookup();
        }

        /// <summary>
        /// Checks that, with the predefined table of a prefab shape, the star of the extended slide reaches the judge frame
        /// where the star of the prefab slide does, for both the modern and the classic constant.
        /// </summary>
        /// <param name="tableName">The predefined table name of the prefab shape.</param>
        /// <param name="prefabName">The prefab of the shape.</param>
        /// <param name="code">The slide code that draws the same path as the prefab, starting at key 1.</param>
        [TestCaseSource(nameof(PrefabShapes))]
        public void PredefinedConst_PlacesTheStarWhereThePrefabStarIs(string tableName, string prefabName, string code)
        {
            var metadata = ExtendSlideHelper.CreateSlideEntry(SlideCodeParser.Parse(code));
            using var table = SlideTables.FindTableByName(tableName);
            Assert.That(table, Is.Not.Null, tableName);
            var prefabStar = GetPrefabStarPositions(prefabName, (int)metadata.JudgeAreaQueue[^1].SensorA + 1);
            var extendStar = GetExtendStarPositions(metadata);

            foreach (var slideConst in new[] { table!.Const, table.ClassicConst })
            {
                var expected = GetStarPosition(prefabStar, 1f - slideConst);
                var actual = GetStarPosition(extendStar, 1f - slideConst);

                TestContext.WriteLine($"{tableName} const {slideConst}: prefab {expected} of {prefabStar.Count} points, extend {actual} of {extendStar.Count} points, distance {Vector3.Distance(expected, actual)}");
                Assert.That(Vector3.Distance(expected, actual), Is.LessThan(PrefabTolerance), $"{tableName} = {code}, const {slideConst}");
            }
        }

        /// <summary>
        /// Checks that the generated constant puts the star on the path where the last judge area begins, even though the
        /// star poses are not evenly spaced along the path.
        /// </summary>
        /// <param name="code">A valid slide code.</param>
        [TestCase("1K3")]
        [TestCase("1K4")]
        [TestCase("1CK5")]
        [TestCase("1Q9K2")]
        [TestCase("1Q9K4")]
        [TestCase("1P9K1")]
        [TestCase("1P0K5")]
        [TestCase("1P3K6")]
        [TestCase("1A7K2")]
        [TestCase("1B7B3K5")]
        [TestCase("1Q1Q9K3")]
        public void GeneratedConst_PlacesTheStarWhereTheLastAreaBegins(string code)
        {
            var path = SlideCodeParser.Parse(code);
            var areas = SlideDataBuilder.BuildSlideAreas(path);
            var metadata = ExtendSlideHelper.CreateSlideEntry(path);
            var judgePoint = path.GetPointAt(areas[^2].LengthAfterFinish / path.GetPathLength());
            var expected = new Vector3((float)judgePoint.Real, (float)judgePoint.Imaginary);

            var actual = GetStarPosition(GetExtendStarPositions(metadata), 1f - metadata.SlideConst);

            TestContext.WriteLine($"{code}: const {metadata.SlideConst}, path {expected}, star {actual}, distance {Vector3.Distance(expected, actual)}");
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(PathTolerance), code);
        }

        /// <summary>
        /// Checks that the generated constant of a connection slide puts the star on the path where the last judge area
        /// begins, although the star poses include segment joints without arrows.
        /// </summary>
        /// <param name="style">The style of the connection slide.</param>
        /// <param name="first">The first simai segment.</param>
        /// <param name="second">The second simai segment.</param>
        [TestCase(SlidePathStyle.Classic, "1^2", "2^3")]
        [TestCase(SlidePathStyle.Medium, "1^2", "2^3")]
        [TestCase(SlidePathStyle.Classic, "2^1", "1q8")]
        [TestCase(SlidePathStyle.Classic, "1-4", "4q7")]
        [TestCase(SlidePathStyle.Medium, "1q8", "8-4")]
        [TestCase(SlidePathStyle.Classic, "1>5", "5-2")]
        public void GeneratedConst_PlacesTheStarWhereTheLastAreaBeginsOnConnectionSlides(SlidePathStyle style, string first, string second)
        {
            var chainPath = SlideCodeParser.Parse(SimaiSlideTranslator.TranslateChain(new[] { first, second }));
            var segmentPaths = new[]
            {
                SlideCodeParser.Parse(SimaiSlideTranslator.Translate(first)),
                SlideCodeParser.Parse(SimaiSlideTranslator.Translate(second))
            };
            var areas = SlideDataBuilder.BuildSlideAreas(chainPath);
            var metadata = ExtendSlideHelper.CreateConnSlideEntry(chainPath, segmentPaths, style);
            var judgePoint = chainPath.GetPointAt(areas[^2].LengthAfterFinish / chainPath.GetPathLength());
            var expected = new Vector3((float)judgePoint.Real, (float)judgePoint.Imaginary);

            var actual = GetStarPosition(GetExtendStarPositions(metadata), 1f - metadata.SlideConst);

            TestContext.WriteLine($"{first}{second.Substring(1)} {style}: const {metadata.SlideConst}, path {expected}, star {actual}, distance {Vector3.Distance(expected, actual)}");
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(PathTolerance), $"{first}{second.Substring(1)} {style}");
        }

        /// <summary>Checks that the star skips only the conditional last arrow, and never the path start or end.</summary>
        [Test]
        public void IsStarPose_SkipsOnlyTheConditionalLastArrow()
        {
            Assert.That(ExtendSlideHelper.IsStarPose(3, 5, true), Is.False);
            Assert.That(ExtendSlideHelper.IsStarPose(3, 5, false), Is.True);
            Assert.That(ExtendSlideHelper.IsStarPose(0, 5, true), Is.True);
            Assert.That(ExtendSlideHelper.IsStarPose(2, 5, true), Is.True);
            Assert.That(ExtendSlideHelper.IsStarPose(4, 5, true), Is.True);
            Assert.That(ExtendSlideHelper.IsStarPose(0, 2, true), Is.True);
        }

        /// <summary>Gets the star positions of a prefab slide that starts at key 1, as <c>SlideDrop</c> builds them.</summary>
        /// <param name="prefabName">The prefab name.</param>
        /// <param name="endPos">The end key of the slide.</param>
        /// <returns>The start key position, the arrow positions and the end key position.</returns>
        private static List<Vector3> GetPrefabStarPositions(string prefabName, int endPos)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}{prefabName}.prefab");
            Assert.That(prefab, Is.Not.Null, prefabName);
            var root = prefab.transform;
            var positions = new List<Vector3>(root.childCount + 1)
            {
                NoteHelper.GetTapPosition(1, TapDistance)
            };
            // 最后一个子物体是 SlideOK
            for (var i = 0; i < root.childCount - 1; i++)
            {
                var position = root.GetChild(i).localPosition;
                position.z = 0f;
                positions.Add(position);
            }
            positions.Add(NoteHelper.GetTapPosition(endPos, TapDistance));
            return positions;
        }

        /// <summary>Gets the star positions of an extended slide, as <c>ExtendSlideDrop</c> builds them.</summary>
        /// <param name="metadata">The metadata of the slide.</param>
        /// <returns>The poses the star passes through.</returns>
        private static List<Vector3> GetExtendStarPositions(ExtendSlideMetadata metadata)
        {
            var poses = metadata.StarPoses;
            var positions = new List<Vector3>(poses.Length);
            for (var i = 0; i < poses.Length; i++)
            {
                positions.Add(new Vector3(poses[i].X, poses[i].Y));
            }
            return positions;
        }

        /// <summary>Gets where the star is once it has moved a share of its poses, as <c>SingleSlideBase</c> moves it.</summary>
        /// <param name="positions">The star positions.</param>
        /// <param name="progress">How far along the poses, from 0 to 1.</param>
        /// <returns>The star position.</returns>
        private static Vector3 GetStarPosition(List<Vector3> positions, float progress)
        {
            if (progress >= 1f)
            {
                return positions[^1];
            }
            var indexProgress = (positions.Count - 1) * progress;
            var index = (int)indexProgress;
            return Vector3.Lerp(positions[index], positions[index + 1], indexProgress - index);
        }
    }
}
