#nullable enable
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using Sorting = MajdataPlay.Scenes.Game.Utils.NoteHelper.Sorting;
using Object = UnityEngine.Object;

namespace NoteSortingValidation
{
    /// <summary>
    /// Checks production note depth helpers with Unity APIs and serialized gameplay assets.
    /// </summary>
    public static class Validation
    {
        /// <summary>
        /// The expected distance between adjacent gameplay groups.
        /// </summary>
        private const float LayerSpacing = 0.5f;

        /// <summary>
        /// The expected depth between adjacent sort orders (2^-17).
        /// </summary>
        private const float OrderStep = 1f / 131072f;

        /// <summary>
        /// The serialized ID of the stripped gameplay Camera in Init.unity, also referenced by GlobalCanvas.
        /// </summary>
        private const long GameplayCameraId = 550395852;

        /// <summary>
        /// Gets the repository root that contains the isolated validation project.
        /// </summary>
        private static string RepositoryRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));

        /// <summary>
        /// Runs helper, asset and desktop rendering regressions.
        /// </summary>
        /// <exception cref="InvalidOperationException">A regression check fails.</exception>
        public static void Run()
        {
            ValidateDepthOrder();
            ValidateWorldPrecision();
            ValidateTransforms();
            ValidateNestedRenderers();
            ValidateRendering();
            Debug.Log("NOTE_SORTING_RENDERING_PASSED");
            ValidateAssets();
            Debug.Log("NOTE_SORTING_VALIDATION_PASSED");
        }

        /// <summary>
        /// Runs helper and asset regressions without a graphics device.
        /// </summary>
        /// <exception cref="InvalidOperationException">A regression check fails.</exception>
        public static void RunWithoutRendering()
        {
            ValidateDepthOrder();
            ValidateWorldPrecision();
            ValidateTransforms();
            ValidateNestedRenderers();
            ValidateAssets();
            Debug.Log("NOTE_SORTING_VALIDATION_PASSED");
        }

        /// <summary>
        /// Checks category bands, the exact order grid, out-of-range clamping and component priorities.
        /// </summary>
        /// <exception cref="InvalidOperationException">A depth check fails.</exception>
        private static void ValidateDepthOrder()
        {
            var layers = (Sorting.NoteLayer[])Enum.GetValues(typeof(Sorting.NoteLayer));
            for (var i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                var back = -i * LayerSpacing;
                var front = back - LayerSpacing;
                Check(Sorting.GetZIndex(layer, short.MinValue) == back, $"Category {layer} back boundary");
                Check(Sorting.GetZIndex(layer) == back - (LayerSpacing / 2), $"Category {layer} center");
                var previous = float.PositiveInfinity;
                for (var order = (int)short.MinValue; order <= short.MaxValue; order++)
                {
                    var z = Sorting.GetZIndex(layer, order);
                    Check(z <= back && z > front, $"Bounded depth {layer}/{order}");
                    Check(z == back - ((order - short.MinValue) * OrderStep), $"Exact grid depth {layer}/{order}");
                    Check(z < previous, $"Adjacent order priority {layer}/{order}");
                    previous = z;
                }
                foreach (var order in new[] { int.MinValue, -1000000, -65536, -32769 })
                {
                    Check(Sorting.GetZIndex(layer, order) == back, $"Clamped low order {layer}/{order}");
                }
                foreach (var order in new[] { 32768, 65536, 1000000, int.MaxValue })
                {
                    Check(Sorting.GetZIndex(layer, order) == Sorting.GetZIndex(layer, short.MaxValue), $"Clamped high order {layer}/{order}");
                }
                if (i > 0)
                {
                    Check(Sorting.GetZIndex(layer, int.MinValue) < Sorting.GetZIndex(layers[i - 1], int.MaxValue), $"Disjoint categories {layer}");
                }
            }

            foreach (var order in new[] { -32762, -3, 0, 32767 })
            {
                var body = Sorting.GetZIndex(Sorting.NoteLayer.Tap, order - 1);
                var end = Sorting.GetZIndex(Sorting.NoteLayer.Tap, order - 2);
                var ex = Sorting.GetZIndex(Sorting.NoteLayer.Tap, order);
                Check(ex < body && body < end, $"Hold EX/body/end priority {order}");
                var justBorder = Sorting.GetZIndex(Sorting.NoteLayer.Touch, order - 1);
                var point = Sorting.GetZIndex(Sorting.NoteLayer.Touch, order - 2);
                Check(justBorder < point, $"Touch just-border/point priority {order}");
                var previous = point;
                for (var fan = 0; fan < 4; fan++)
                {
                    var z = Sorting.GetZIndex(Sorting.NoteLayer.Touch, order - (3 + fan));
                    Check(previous < z, $"Touch fan {fan} priority {order}");
                    previous = z;
                }
            }
            Debug.Log("NOTE_SORTING_DEPTH_PASSED: all categories, every signed 16-bit order, clamped int32 extremes and component priorities");
        }

        /// <summary>
        /// Checks that every adjacent order keeps a distinct, exact world depth under the serialized gameplay pools,
        /// stays behind the note effects and keeps a distinct camera distance.
        /// </summary>
        /// <exception cref="InvalidOperationException">A world depth check fails.</exception>
        private static void ValidateWorldPrecision()
        {
            var depths = ReadGameplayDepths();
            var layers = (Sorting.NoteLayer[])Enum.GetValues(typeof(Sorting.NoteLayer));
            var parent = new GameObject("World precision pool");
            var child = new GameObject("World precision sprite");
            var checkedOrders = 0;
            try
            {
                child.transform.SetParent(parent.transform, false);
                foreach (var poolZ in depths.PoolZ)
                {
                    parent.transform.position = new Vector3(0, 0, poolZ);
                    var previousWorld = float.PositiveInfinity;
                    var previousDistance = float.PositiveInfinity;
                    for (var i = 0; i < layers.Length; i++)
                    {
                        for (var order = (int)short.MinValue; order <= short.MaxValue; order++)
                        {
                            var local = Sorting.GetZIndex(layers[i], order);
                            Sorting.SetLocalZPosition(child.transform, local);
                            var world = child.transform.position.z;
                            var distance = world - depths.CameraZ;
                            Check(world == poolZ + local, $"Exact world depth {poolZ}/{layers[i]}/{order}: {world}");
                            Check(world < previousWorld, $"Distinct world depth {poolZ}/{layers[i]}/{order}: {world}");
                            Check(distance < previousDistance, $"Distinct camera distance {poolZ}/{layers[i]}/{order}: {distance}");
                            Check(world > depths.EffectsZ, $"Behind note effects {poolZ}/{layers[i]}/{order}: {world}");
                            Check(Math.Abs(world) < 128 && Math.Abs(distance) < 128, $"Exact grid range {poolZ}/{layers[i]}/{order}");
                            previousWorld = world;
                            previousDistance = distance;
                            checkedOrders++;
                        }
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(child);
                Object.DestroyImmediate(parent);
            }
            ValidateWorldNotes(depths.PoolZ[0]);
            Debug.Log($"NOTE_SORTING_WORLD_PASSED: {checkedOrders} orders at pool depths [{string.Join(", ", depths.PoolZ)}], " +
                $"note effects {depths.EffectsZ}, camera {depths.CameraZ}");
        }

        /// <summary>
        /// Checks real Tap, Hold, Touch and TouchHold hierarchies for consecutive notes at the gameplay pool depth.
        /// Earlier notes must keep every component in front of every component of the next note.
        /// </summary>
        /// <param name="poolZ">The serialized world depth of the gameplay note pools.</param>
        /// <exception cref="InvalidOperationException">A component or note priority check fails.</exception>
        private static void ValidateWorldNotes(float poolZ)
        {
            // Offsets mirror TapDrop, HoldDrop, TouchDrop and TouchHoldDrop; strides mirror NoteLoader.NOTE_LAYER_COUNT.
            var notes = new[]
            {
                (Name: "Tap", Layer: Sorting.NoteLayer.Tap, Stride: 2, Root: 1, Children: new[] { 0 }, Nested: true),
                (Name: "Hold", Layer: Sorting.NoteLayer.Tap, Stride: 3, Root: 1, Children: new[] { 0, 2 }, Nested: true),
                (Name: "Touch", Layer: Sorting.NoteLayer.Touch, Stride: 6, Root: 1, Children: new[] { 2, 3, 4, 5, 6 }, Nested: false),
                (Name: "TouchHold", Layer: Sorting.NoteLayer.Touch, Stride: 6, Root: 1, Children: new[] { 2, 3, 4, 5 }, Nested: false),
            };
            var pool = new GameObject("World notes pool");
            try
            {
                pool.transform.position = new Vector3(0, 0, poolZ);
                foreach (var note in notes)
                {
                    var noteObject = new GameObject(note.Name);
                    noteObject.transform.SetParent(pool.transform, false);
                    var rootRenderer = note.Nested ? noteObject.AddComponent<SpriteRenderer>() : CreateChildRenderer(noteObject.transform);
                    var childRenderers = new SpriteRenderer[note.Children.Length];
                    for (var i = 0; i < childRenderers.Length; i++)
                    {
                        childRenderers[i] = CreateChildRenderer(note.Nested ? rootRenderer.transform : noteObject.transform);
                    }
                    var deepestOffset = note.Root;
                    foreach (var offset in note.Children)
                    {
                        deepestOffset = Math.Max(deepestOffset, offset);
                    }
                    var previousBack = float.NegativeInfinity;
                    var count = 0;
                    for (var sortOrder = (int)short.MaxValue; sortOrder - deepestOffset >= short.MinValue; sortOrder -= note.Stride)
                    {
                        Sorting.Apply(rootRenderer, note.Layer, sortOrder - note.Root);
                        var parentZIndex = note.Nested ? rootRenderer.transform.localPosition.z : 0f;
                        var front = float.PositiveInfinity;
                        var back = float.NegativeInfinity;
                        var depths = new List<(int Offset, float World)> { (note.Root, rootRenderer.transform.position.z) };
                        for (var i = 0; i < childRenderers.Length; i++)
                        {
                            Sorting.Apply(childRenderers[i], note.Layer, sortOrder - note.Children[i], parentZIndex);
                            depths.Add((note.Children[i], childRenderers[i].transform.position.z));
                        }
                        foreach (var (offset, world) in depths)
                        {
                            Check(world == poolZ + Sorting.GetZIndex(note.Layer, sortOrder - offset), $"{note.Name} exact component depth {sortOrder}/{offset}");
                            front = Math.Min(front, world);
                            back = Math.Max(back, world);
                            foreach (var (otherOffset, otherWorld) in depths)
                            {
                                Check(offset >= otherOffset || world < otherWorld, $"{note.Name} component priority {sortOrder}/{offset}/{otherOffset}");
                            }
                        }
                        Check(previousBack < front, $"{note.Name} earlier note stays in front {sortOrder}");
                        previousBack = back;
                        count++;
                    }
                    Check(count == (short.MaxValue - short.MinValue + 1) / note.Stride, $"{note.Name} capacity {count}");
                    Debug.Log($"NOTE_SORTING_WORLD_NOTES_PASSED: {note.Name}, {count} consecutive notes");
                }
            }
            finally
            {
                Object.DestroyImmediate(pool);
            }

            /// <summary>Creates a sprite renderer under a note transform.</summary>
            /// <param name="parent">The note transform.</param>
            /// <returns>The new child renderer.</returns>
            SpriteRenderer CreateChildRenderer(Transform parent)
            {
                var child = new GameObject("Component");
                child.transform.SetParent(parent, false);
                return child.AddComponent<SpriteRenderer>();
            }
        }

        /// <summary>
        /// Reads the serialized world depths of the gameplay note pools, the note effects and the gameplay camera.
        /// </summary>
        /// <returns>The distinct pool depths, the note effect depth and the camera depth.</returns>
        /// <exception cref="InvalidOperationException">A required object or transform is missing or tilted.</exception>
        private static (float[] PoolZ, float EffectsZ, float CameraZ) ReadGameplayDepths()
        {
            var game = File.ReadAllText(Path.Combine(RepositoryRoot, "Assets/Scenes/Game.unity"));
            var init = File.ReadAllText(Path.Combine(RepositoryRoot, "Assets/Scenes/Init.unity"));
            var poolDepths = new List<float>();
            foreach (var name in new[] { "TapPool", "HoldPool", "TouchPool", "TouchHoldPool", "SlidePool", "SlideArrowPool", "EachLines", "TapLines" })
            {
                var z = ReadWorldZ(game, name);
                if (!poolDepths.Contains(z))
                {
                    poolDepths.Add(z);
                }
            }
            var effectsZ = ReadWorldZ(game, "NoteEffects");

            var camera = Regex.Match(init, @"(?ms)^--- !u!20 &" + GameplayCameraId + @" stripped\r?\nCamera:\r?\n(?<body>.*?)(?=^--- !u!|\z)");
            Check(camera.Success, "Gameplay camera exists");
            var instanceId = Regex.Match(camera.Groups["body"].Value, @"m_PrefabInstance: \{fileID: (\d+)\}");
            Check(instanceId.Success, "Gameplay camera prefab instance");
            var instance = Regex.Match(init, @"(?ms)^--- !u!1001 &" + instanceId.Groups[1].Value + @"\r?\nPrefabInstance:\r?\n(?<body>.*?)(?=^--- !u!|\z)");
            Check(instance.Success, "Gameplay camera prefab instance exists");
            var cameraZ = ReadOverride(instance.Groups["body"].Value, "m_LocalPosition.z");
            Check(ReadOverride(instance.Groups["body"].Value, "m_LocalRotation.x") == 0 &&
                ReadOverride(instance.Groups["body"].Value, "m_LocalRotation.y") == 0, "Gameplay camera faces forward");
            return (poolDepths.ToArray(), effectsZ, cameraZ);

            /// <summary>Reads one numeric prefab override.</summary>
            /// <param name="body">The prefab instance text.</param>
            /// <param name="property">The overridden property path.</param>
            /// <returns>The override value.</returns>
            /// <exception cref="InvalidOperationException">The override is missing.</exception>
            float ReadOverride(string body, string property)
            {
                var match = Regex.Match(body, @"propertyPath: " + Regex.Escape(property) + @"\r?\n\s+value: (?<value>[^\r\n]+)");
                Check(match.Success, $"Gameplay camera override {property}");
                return float.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Reads the world Z of a uniquely named scene object whose ancestors only translate, rotate around Z and keep Z scale one.
        /// </summary>
        /// <param name="scene">The scene text.</param>
        /// <param name="name">The GameObject name.</param>
        /// <returns>The world Z coordinate.</returns>
        /// <exception cref="InvalidOperationException">The object is missing, ambiguous, tilted or Z-scaled.</exception>
        private static float ReadWorldZ(string scene, string name)
        {
            var objects = Regex.Matches(scene, @"(?ms)^--- !u!1 &(?<id>\d+)\r?\nGameObject:\r?\n(?<body>(?:(?!^--- !u!).)*?^  m_Name: " + Regex.Escape(name) + @"\r?$)");
            Check(objects.Count == 1, $"Unique scene object {name}");
            var gameObjectId = objects[0].Groups["id"].Value;
            var transform = FindTransform(@"m_GameObject: \{fileID: " + gameObjectId + @"\}");
            var z = 0f;
            while (true)
            {
                var body = transform.Groups["body"].Value;
                var rotation = Regex.Match(body, @"m_LocalRotation: \{x: (?<x>[^,]+), y: (?<y>[^,]+),");
                var position = Regex.Match(body, @"m_LocalPosition: \{x: [^,]+, y: [^,]+, z: (?<z>[^}]+)\}");
                var scale = Regex.Match(body, @"m_LocalScale: \{x: [^,]+, y: [^,]+, z: (?<z>[^}]+)\}");
                Check(rotation.Success && position.Success && scale.Success, $"Transform of {name}");
                Check(float.Parse(rotation.Groups["x"].Value, CultureInfo.InvariantCulture) == 0 &&
                    float.Parse(rotation.Groups["y"].Value, CultureInfo.InvariantCulture) == 0, $"{name} ancestors only rotate around Z");
                Check(float.Parse(scale.Groups["z"].Value, CultureInfo.InvariantCulture) == 1, $"{name} ancestors keep Z scale one");
                z += float.Parse(position.Groups["z"].Value, CultureInfo.InvariantCulture);
                var father = Regex.Match(body, @"m_Father: \{fileID: (?<id>-?\d+)\}");
                Check(father.Success, $"Father of {name}");
                if (father.Groups["id"].Value == "0")
                {
                    return z;
                }
                transform = FindTransform(@"^--- !u!4 &" + father.Groups["id"].Value + @"\r?$");
            }

            /// <summary>Finds one Transform document that matches a pattern.</summary>
            /// <param name="pattern">The pattern matched against the document header and body.</param>
            /// <returns>The matching Transform document.</returns>
            /// <exception cref="InvalidOperationException">No unique Transform matches.</exception>
            Match FindTransform(string pattern)
            {
                Match? found = null;
                foreach (Match document in Regex.Matches(scene, @"(?ms)^--- !u!4 &\d+\r?\nTransform:\r?\n(?<body>.*?)(?=^--- !u!|\z)"))
                {
                    if (Regex.IsMatch(document.Value, pattern, RegexOptions.Multiline))
                    {
                        Check(found == null, $"Unique transform for {name}");
                        found = document;
                    }
                }
                Check(found != null, $"Transform for {name}");
                return found!;
            }
        }

        /// <summary>
        /// Checks local depth assignment and movement under transformed parents.
        /// </summary>
        /// <exception cref="InvalidOperationException">A renderer or transform check fails.</exception>
        private static void ValidateTransforms()
        {
            var parent = new GameObject("Sorting parent");
            var alternateParent = new GameObject("Alternate sorting parent");
            var child = new GameObject("Sorting child");
            try
            {
                parent.transform.position = new Vector3(5, -4, 12);
                parent.transform.rotation = Quaternion.Euler(0, 0, 37);
                parent.transform.localScale = new Vector3(2, 3, 2);
                child.transform.SetParent(parent.transform, false);
                child.transform.localPosition = new Vector3(1, 2, 3);
                var renderer = child.AddComponent<SpriteRenderer>();
                renderer.sortingLayerName = "Tap";
                renderer.sortingOrder = 123;
                renderer.spriteSortPoint = SpriteSortPoint.Center;
                var original = child.transform.localPosition;
                Sorting.Apply(renderer, Sorting.NoteLayer.Touch, -32769);
                Check(renderer.sortingLayerID == 0 && renderer.sortingLayerName == "Default", "Apply resets sorting layer");
                Check(renderer.sortingOrder == 0 && renderer.spriteSortPoint == SpriteSortPoint.Pivot, "Apply resets order and pivot");
                Near(child.transform.localPosition.x, original.x, "Apply preserves local X");
                Near(child.transform.localPosition.y, original.y, "Apply preserves local Y");
                var z = Sorting.GetZIndex(Sorting.NoteLayer.Touch, -32769);
                Near(child.transform.localPosition.z, z, "Apply sets local Z");
                Near(child.transform.position.z, 12 + 2 * z, "Apply inherits parent depth and Z scale");
                var worldZ = child.transform.position.z;
                for (var frame = 0; frame < 120; frame++)
                {
                    var angle = frame * Mathf.PI / 60;
                    Sorting.SetPosition(child.transform, new Vector3(Mathf.Cos(angle) * 4, Mathf.Sin(angle) * 4, 99));
                    Near(child.transform.position.z, worldZ, "World XY movement preserves world Z");
                    Near(child.transform.localPosition.z, z, "Planar world XY movement preserves local Z");
                }
                Sorting.SetLocalPosition(child.transform, new Vector3(-2, 7, 99));
                Near(child.transform.localPosition.x, -2, "SetLocalPosition X");
                Near(child.transform.localPosition.y, 7, "SetLocalPosition Y");
                Near(child.transform.localPosition.z, z, "SetLocalPosition preserves local Z");
                original = child.transform.localPosition;
                Sorting.SetLocalZPosition(child.transform, -7);
                Near(child.transform.localPosition.x, original.x, "SetLocalZPosition preserves local X");
                Near(child.transform.localPosition.y, original.y, "SetLocalZPosition preserves local Y");
                Near(child.transform.localPosition.z, -7, "SetLocalZPosition sets local Z");
                Near(child.transform.position.z, -2, "SetLocalZPosition inherits parent depth and Z scale");
                Sorting.Apply(renderer, Sorting.NoteLayer.Slides, 32769);
                z = Sorting.GetZIndex(Sorting.NoteLayer.Slides, 32769);
                Near(child.transform.localPosition.z, z, "Pooled renderer changes local category");
                worldZ = child.transform.position.z;
                foreach (var scale in new[] { 0f, 0.5f, 1f, 3f })
                {
                    parent.transform.localScale = new Vector3(scale, scale, 2);
                    Sorting.SetLocalPosition(child.transform, new Vector3(scale, -scale, 0));
                    Near(child.transform.localPosition.z, z, $"XY scale {scale} preserves local depth");
                    Near(child.transform.position.z, worldZ, $"XY scale {scale} preserves inherited world depth");
                }
                parent.transform.position += new Vector3(0, 0, 5);
                Near(child.transform.localPosition.z, z, "Parent movement preserves local index");
                Near(child.transform.position.z, worldZ + 5, "Child inherits parent depth movement");

                alternateParent.transform.position = new Vector3(1, 8, -7);
                alternateParent.transform.rotation = Quaternion.Euler(0, 0, -12);
                alternateParent.transform.localScale = new Vector3(0.5f, 2, 3);
                original = child.transform.localPosition;
                child.transform.SetParent(alternateParent.transform, false);
                Near(child.transform.localPosition.x, original.x, "Local reparent preserves local X");
                Near(child.transform.localPosition.y, original.y, "Local reparent preserves local Y");
                Near(child.transform.localPosition.z, z, "Local reparent preserves assigned index");
                Near(child.transform.position.z, -7 + 3 * z, "Local reparent inherits new parent depth and Z scale");

                alternateParent.transform.rotation = Quaternion.Euler(20, 30, -12);
                original = child.transform.localPosition;
                Sorting.SetLocalZPosition(child.transform, -4);
                Near(child.transform.localPosition.x, original.x, "Tilted parent preserves local X");
                Near(child.transform.localPosition.y, original.y, "Tilted parent preserves local Y");
                Near(child.transform.localPosition.z, -4, "Tilted parent sets local Z");
            }
            finally
            {
                Object.DestroyImmediate(child);
                Object.DestroyImmediate(alternateParent);
                Object.DestroyImmediate(parent);
            }
            Debug.Log("NOTE_SORTING_TRANSFORMS_PASSED: local indices, pooled reset, movement and transformed parents");
        }

        /// <summary>
        /// Checks nested Tap and Hold components use relative local depth without duplicating the body index.
        /// </summary>
        /// <exception cref="InvalidOperationException">A nested renderer priority check fails.</exception>
        private static void ValidateNestedRenderers()
        {
            var parent = new GameObject("Nested sorting parent");
            var bodyObject = new GameObject("Tap or Hold body");
            var exObject = new GameObject("Tap or Hold EX");
            var endObject = new GameObject("Hold end");
            try
            {
                parent.transform.position = new Vector3(5, -4, 12);
                parent.transform.rotation = Quaternion.Euler(0, 0, 37);
                parent.transform.localScale = new Vector3(2, 3, 2);
                bodyObject.transform.SetParent(parent.transform, false);
                bodyObject.transform.localScale = new Vector3(1.5f, 2, 1);
                exObject.transform.SetParent(bodyObject.transform, false);
                endObject.transform.SetParent(bodyObject.transform, false);
                var body = bodyObject.AddComponent<SpriteRenderer>();
                var ex = exObject.AddComponent<SpriteRenderer>();
                var end = endObject.AddComponent<SpriteRenderer>();
                foreach (var order in new[] { -32766, 0, 32767 })
                {
                    var bodyZ = Sorting.GetZIndex(Sorting.NoteLayer.Tap, order - 1);
                    var exZ = Sorting.GetZIndex(Sorting.NoteLayer.Tap, order);
                    var endZ = Sorting.GetZIndex(Sorting.NoteLayer.Tap, order - 2);
                    Sorting.Apply(body, Sorting.NoteLayer.Tap, order - 1);
                    Sorting.Apply(ex, Sorting.NoteLayer.Tap, order, bodyZ);
                    Sorting.Apply(end, Sorting.NoteLayer.Tap, order - 2, bodyZ);
                    Near(body.transform.localPosition.z, bodyZ, $"Nested body local index {order}");
                    Near(ex.transform.localPosition.z, exZ - bodyZ, $"Nested EX relative local index {order}");
                    Near(end.transform.localPosition.z, endZ - bodyZ, $"Nested end relative local index {order}");
                    Check(ex.transform.localPosition.z < 0 && end.transform.localPosition.z > 0, $"Nested EX/body/end local priority {order}");
                    Near(ex.transform.position.z, 12 + 2 * exZ, $"Nested EX inherits one category index {order}");
                    Near(end.transform.position.z, 12 + 2 * endZ, $"Nested end inherits one category index {order}");
                    Check(ex.transform.position.z < body.transform.position.z && body.transform.position.z < end.transform.position.z,
                        $"Nested EX/body/end world priority {order}");
                }
                var bodyLocalZ = body.transform.localPosition.z;
                var exLocalZ = ex.transform.localPosition.z;
                var endLocalZ = end.transform.localPosition.z;
                var bodyWorldZ = body.transform.position.z;
                var exWorldZ = ex.transform.position.z;
                var endWorldZ = end.transform.position.z;
                parent.transform.position += new Vector3(0, 0, 5);
                Near(body.transform.localPosition.z, bodyLocalZ, "Parent movement preserves nested body local index");
                Near(ex.transform.localPosition.z, exLocalZ, "Parent movement preserves nested EX local index");
                Near(end.transform.localPosition.z, endLocalZ, "Parent movement preserves nested end local index");
                Near(body.transform.position.z, bodyWorldZ + 5, "Nested body inherits parent depth movement");
                Near(ex.transform.position.z, exWorldZ + 5, "Nested EX inherits parent depth movement");
                Near(end.transform.position.z, endWorldZ + 5, "Nested end inherits parent depth movement");
            }
            finally
            {
                Object.DestroyImmediate(endObject);
                Object.DestroyImmediate(exObject);
                Object.DestroyImmediate(bodyObject);
                Object.DestroyImmediate(parent);
            }
            Debug.Log("NOTE_SORTING_NESTED_PASSED: Tap and Hold relative component depths preserve EX/body/end priority");
        }

        /// <summary>
        /// Scans complete SpriteRenderer documents and variant/scene sorting overrides.
        /// </summary>
        /// <exception cref="InvalidOperationException">An asset check fails.</exception>
        private static void ValidateAssets()
        {
            var root = RepositoryRoot;
            var files = Directory.GetFiles(Path.Combine(root, "Assets/Prefabs/Game"), "*.prefab", SearchOption.AllDirectories);
            var prefabPaths = new Dictionary<string, string>();
            foreach (var meta in Directory.GetFiles(Path.Combine(root, "Assets"), "*.prefab.meta", SearchOption.AllDirectories))
            {
                var guid = Regex.Match(File.ReadAllText(meta), @"(?m)^guid: ([a-f0-9]{32})\r?$");
                if (guid.Success)
                {
                    prefabPaths.Add(guid.Groups[1].Value, meta.Substring(0, meta.Length - ".meta".Length));
                }
            }
            var renderers = 0;
            var overrides = 0;
            foreach (var path in files)
            {
                ValidateFile(path);
            }
            ValidateFile(Path.Combine(root, "Assets/Prefabs/UI/TouchFeedback.prefab"));
            ValidateFile(Path.Combine(root, "Assets/Scenes/Game.unity"));
            var holdEffect = File.ReadAllText(Path.Combine(root, "Assets/Prefabs/Game/Hold_Effect.prefab"));
            var particle = Regex.Match(holdEffect, @"(?ms)^--- !u!199 &[^\r\n]+\r?\nParticleSystemRenderer:\r?\n(?<body>.*?)(?=^--- !u!|\z)");
            Check(particle.Success, "Hold effect particle renderer exists");
            Check(ReadInteger(particle.Groups["body"].Value, "m_SortingLayerID") == 0, "Hold effect particle uses Default");
            Check(ReadInteger(particle.Groups["body"].Value, "m_SortingOrder") == 0, "Hold effect particle uses zero order");
            Check(ReadInteger(particle.Groups["body"].Value, "m_SortingFudge") == 0, "Hold effect particle has no depth bias");
            var touchHold = File.ReadAllText(Path.Combine(root, "Assets/Prefabs/Game/TouchHold.prefab"));
            Check(Regex.IsMatch(touchHold, @"(?m)^  _sortingLayerName: Default\r?$"), "TouchHold RawSprite border uses Default");
            Check(ReadInteger(touchHold, "_sortingOrder") == 0, "TouchHold RawSprite border uses zero order");
            var game = File.ReadAllText(Path.Combine(root, "Assets/Scenes/Game.unity"));
            foreach (var canvasId in new[] { 1076127510, 1353679705, 1643501205 })
            {
                var canvas = Regex.Match(game, @"(?ms)^--- !u!223 &" + canvasId + @"\r?\nCanvas:\r?\n(?<body>.*?)(?=^--- !u!|\z)");
                Check(canvas.Success, $"World canvas {canvasId} exists");
                Check(ReadInteger(canvas.Groups["body"].Value, "m_SortingLayerID") == 0, $"World canvas {canvasId} uses Default");
                Check(ReadInteger(canvas.Groups["body"].Value, "m_SortingOrder") == 0, $"World canvas {canvasId} uses zero order");
            }
            Check(renderers > 100, "Gameplay SpriteRenderer scan is populated");
            Check(overrides > 0, "Prefab variant and scene override scan is populated");
            Debug.Log($"NOTE_SORTING_ASSETS_PASSED: {renderers} SpriteRenderers, {overrides} sorting overrides, {files.Length + 2} files");
            var animations = 0;
            foreach (var directory in new[] { "NoteEffects", "TouchEffects", "OutlineEffect" })
            {
                foreach (var path in Directory.GetFiles(Path.Combine(root, "Assets/Animation", directory), "*.anim", SearchOption.AllDirectories))
                {
                    ValidateAnimation(path);
                }
            }
            ValidateAnimation(Path.Combine(root, "Assets/Animation/UI/TouchFeedbackAnim.anim"));
            Check(animations > 20, "Gameplay animation scan is populated");
            Debug.Log($"NOTE_SORTING_ANIMATIONS_PASSED: {animations} clips preserve local Z, Z scale and renderer sorting");

            /// <summary>Rejects animation bindings that restore a renderer sorting property.</summary>
            /// <param name="path">The animation path.</param>
            /// <exception cref="InvalidOperationException">A sorting-property curve is present.</exception>
            void ValidateAnimation(string path)
            {
                var source = File.ReadAllText(path);
                Check(!Regex.IsMatch(source, @"attribute: m_(?:SortingLayerID|SortingOrder|SortingLayer|SpriteSortPoint)\r?\n"), $"No renderer sorting animation in {path}");
                var relative = path.Substring(Path.Combine(root, "Assets/Animation").Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animation/" + relative.Replace('\\', '/'));
                Check(clip != null, $"Imported animation {path}");
                var bindings = new List<EditorCurveBinding>();
                var animationRoot = new GameObject("Animation depth root");
                animationRoot.transform.position = new Vector3(0, 0, -6.4f);
                try
                {
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.type != typeof(Transform) ||
                            (binding.propertyName != "m_LocalPosition.z" && binding.propertyName != "m_LocalScale.z"))
                        {
                            continue;
                        }
                        bindings.Add(binding);
                        var parent = animationRoot.transform;
                        foreach (var segment in binding.path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var child = parent.Find(segment);
                            if (child == null)
                            {
                                child = new GameObject(segment).transform;
                                child.SetParent(parent, false);
                            }
                            parent = child;
                        }
                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        var expected = binding.propertyName == "m_LocalPosition.z" ? 0f : 1f;
                        for (var frame = 0; frame <= 30; frame++)
                        {
                            Near(curve.Evaluate(clip.length * frame / 30), expected, $"Editor curve {path}, {binding.path}/{binding.propertyName}");
                        }
                    }
                    for (var frame = 0; frame <= 30; frame++)
                    {
                        clip.SampleAnimation(animationRoot, clip.length * frame / 30);
                        Near(animationRoot.transform.position.z, -6.4f, $"Animation {path} preserves mapped root depth");
                        foreach (var binding in bindings)
                        {
                            var transform = binding.path.Length == 0 ? animationRoot.transform : animationRoot.transform.Find(binding.path);
                            var actual = binding.propertyName == "m_LocalPosition.z" ? transform.localPosition.z : transform.localScale.z;
                            var expected = binding.propertyName == "m_LocalPosition.z" ? 0f : 1f;
                            Near(actual, expected, $"Sampled animation {path}, {binding.path}/{binding.propertyName}");
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(animationRoot);
                }
                animations++;
            }

            /// <summary>Checks one serialized scene or prefab without importing gameplay scripts.</summary>
            /// <param name="path">The scene or prefab path.</param>
            /// <exception cref="InvalidOperationException">A serialized sorting value is incorrect.</exception>
            void ValidateFile(string path)
            {
                var source = File.ReadAllText(path);
                foreach (Match document in Regex.Matches(source, @"(?ms)^--- !u!212 &[^\r\n]+\r?\nSpriteRenderer:\r?\n(?<body>.*?)(?=^--- !u!|\z)"))
                {
                    var body = document.Groups["body"].Value;
                    if (!body.Contains("m_SortingLayerID:"))
                    {
                        Check(document.Value.Contains("stripped"), $"Unexpected incomplete SpriteRenderer in {path}");
                        continue;
                    }
                    Check(ReadInteger(body, "m_SortingLayerID") == 0, $"Default layer in {path}");
                    Check(ReadInteger(body, "m_SortingOrder") == 0, $"Zero renderer order in {path}");
                    Check(ReadInteger(body, "m_SpriteSortPoint") == 1, $"Pivot sort point in {path}");
                    renderers++;
                }
                foreach (Match entry in Regex.Matches(source, @"target: \{fileID: (?<id>-?\d+), guid: (?<guid>[a-f0-9]{32}), type: 3\}\r?\n\s+propertyPath: m_(?<property>SortingLayerID|SortingOrder|SpriteSortPoint)\r?\n\s+value: (?<value>-?\d+)"))
                {
                    var guid = entry.Groups["guid"].Value;
                    Check(prefabPaths.TryGetValue(guid, out var prefab), $"Prefab override source {guid} exists");
                    var prefabSource = File.ReadAllText(prefab);
                    if (!Regex.IsMatch(prefabSource, @"(?m)^--- !u!212 &" + entry.Groups["id"].Value + @"(?: stripped)?\r?$"))
                    {
                        continue;
                    }
                    var expected = entry.Groups["property"].Value == "SpriteSortPoint" ? 1 : 0;
                    Check(int.Parse(entry.Groups["value"].Value, CultureInfo.InvariantCulture) == expected, $"Default sorting override in {path}");
                    overrides++;
                }
            }
        }

        /// <summary>
        /// Verifies that Z takes priority over XY distance and creation order in real rendering.
        /// </summary>
        /// <exception cref="InvalidOperationException">Rendering or pixel checks fail.</exception>
        private static void ValidateRendering()
        {
            Check(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "A graphics device is required for the rendering check");
            var cameraObject = new GameObject("Sorting camera");
            var rearObject = new GameObject("Rear red sprite");
            var frontObject = new GameObject("Front green sprite");
            var depthParentObject = new GameObject("Gameplay pool depth");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var target = new RenderTexture(64, 64, 16, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            Sprite? sprite = null;
            var previousTarget = RenderTexture.active;
            try
            {
                texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                texture.Apply();
                sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f, 1);
                var rear = rearObject.AddComponent<SpriteRenderer>();
                var front = frontObject.AddComponent<SpriteRenderer>();
                rear.sprite = sprite;
                front.sprite = sprite;
                rear.color = Color.red;
                front.color = Color.green;
                rear.transform.localScale = Vector3.one * 10;
                front.transform.localScale = Vector3.one * 10;
                rear.transform.position = new Vector3(0, 0, 0);
                front.transform.position = new Vector3(7, 7, 0);
                var camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 0, -10);
                camera.orthographic = true;
                camera.orthographicSize = 10;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 30;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.transparencySortMode = TransparencySortMode.CustomAxis;
                camera.transparencySortAxis = Vector3.forward;
                camera.targetTexture = target;
                foreach (var reverse in new[] { false, true })
                {
                    Sorting.Apply(rear, reverse ? Sorting.NoteLayer.Touch : Sorting.NoteLayer.Tap);
                    Sorting.Apply(front, reverse ? Sorting.NoteLayer.Tap : Sorting.NoteLayer.Touch);
                    camera.Render();
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                    readback.Apply();
                    var sample = readback.GetPixel(40, 40);
                    Check(reverse ? sample.r > 0.9f && sample.g < 0.1f : sample.g > 0.9f && sample.r < 0.1f,
                        $"Z wins at nonzero XY with reversed category priority ({reverse}): {sample}");
                }
                Check((front.transform.position - camera.transform.position).sqrMagnitude > (rear.transform.position - camera.transform.position).sqrMagnitude,
                    "The XY fixture opposes distance sorting");

                // Adjacent orders are one 2^-17 step apart; render them at the serialized pool and camera depths.
                var depths = ReadGameplayDepths();
                depthParentObject.transform.position = new Vector3(0, 0, depths.PoolZ[0]);
                rear.transform.SetParent(depthParentObject.transform, false);
                front.transform.SetParent(depthParentObject.transform, false);
                camera.transform.position = new Vector3(0, 0, depths.CameraZ);
                camera.farClipPlane = 200;
                var pairs = new[]
                {
                    (FrontLayer: Sorting.NoteLayer.Tap, FrontOrder: 32767, RearLayer: Sorting.NoteLayer.Tap, RearOrder: 32766),
                    (FrontLayer: Sorting.NoteLayer.Tap, FrontOrder: -32767, RearLayer: Sorting.NoteLayer.Tap, RearOrder: -32768),
                    (FrontLayer: Sorting.NoteLayer.Touch, FrontOrder: 1, RearLayer: Sorting.NoteLayer.Touch, RearOrder: 0),
                    (FrontLayer: Sorting.NoteLayer.Tap, FrontOrder: (int)short.MinValue, RearLayer: Sorting.NoteLayer.TapLines, RearOrder: (int)short.MaxValue),
                };
                foreach (var pair in pairs)
                {
                    foreach (var reverse in new[] { false, true })
                    {
                        Sorting.Apply(front, reverse ? pair.RearLayer : pair.FrontLayer, reverse ? pair.RearOrder : pair.FrontOrder);
                        Sorting.Apply(rear, reverse ? pair.FrontLayer : pair.RearLayer, reverse ? pair.FrontOrder : pair.RearOrder);
                        Check(Math.Abs(front.transform.position.z - rear.transform.position.z) == OrderStep, $"Adjacent render fixture depth {pair}");
                        camera.Render();
                        RenderTexture.active = target;
                        readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                        readback.Apply();
                        var sample = readback.GetPixel(40, 40);
                        Check(reverse ? sample.r > 0.9f && sample.g < 0.1f : sample.g > 0.9f && sample.r < 0.1f,
                            $"Adjacent order wins at pool depth ({pair}, reversed {reverse}): {sample}");
                    }
                }
                Debug.Log($"NOTE_SORTING_RENDER_ADJACENT_PASSED: {pairs.Length} one-step pairs at pool depth {depths.PoolZ[0]}, camera {depths.CameraZ}");
                Debug.Log($"NOTE_SORTING_RENDER_DEVICE: {SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsDeviceName}");
            }
            finally
            {
                RenderTexture.active = previousTarget;
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(rearObject);
                Object.DestroyImmediate(frontObject);
                Object.DestroyImmediate(depthParentObject);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(target);
            }
        }

        /// <summary>
        /// Reads a required integer field from a serialized component.
        /// </summary>
        /// <param name="source">The component text.</param>
        /// <param name="name">The serialized field name.</param>
        /// <returns>The required integer value.</returns>
        /// <exception cref="InvalidOperationException">The field is missing.</exception>
        private static int ReadInteger(string source, string name)
        {
            var match = Regex.Match(source, @"(?m)^  " + name + @": (-?\d+)\r?$");
            Check(match.Success, $"Missing serialized field {name}");
            return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Checks a world or local coordinate within Unity transform precision.
        /// </summary>
        /// <param name="actual">The observed value.</param>
        /// <param name="expected">The expected value.</param>
        /// <param name="context">The check description.</param>
        /// <exception cref="InvalidOperationException">The values differ.</exception>
        private static void Near(float actual, float expected, string context)
        {
            Check(Mathf.Abs(actual - expected) < 0.00001f, $"{context}: expected {expected}, got {actual}");
        }

        /// <summary>
        /// Rejects an unsatisfied regression condition.
        /// </summary>
        /// <param name="condition">Whether the check passed.</param>
        /// <param name="context">The check description.</param>
        /// <exception cref="InvalidOperationException">The condition is false.</exception>
        private static void Check(bool condition, string context)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Note sorting validation failed: " + context);
            }
        }
    }
}
#endif
