using MajdataPlay.IO;
using MajdataPlay.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
#nullable enable

namespace MajdataPlay.Tests.SensorDebugValidation
{
    /// <summary>Exercises production visualization against real Unity meshes, colliders, and property blocks.</summary>
    public static class Validation
    {
        /// <summary>Counts successful assertions.</summary>
        private static int s_assertions;

        /// <summary>Runs the isolated Editor regression without loading the main game's scenes.</summary>
        /// <exception cref="InvalidOperationException">An asset or visualization assertion fails.</exception>
        public static void Run()
        {
            ValidateJson();
            Check((int)StaticEditorFlags.BatchingStatic == 4, "The scene validator clears the actual Unity batching flag.");
            var areas = AssetDatabase.LoadAllAssetsAtPath("Assets/Models/JudgeAreas.fbx").OfType<Mesh>().OrderBy(mesh => mesh.name, StringComparer.Ordinal).ToArray();
            Check(areas.Length >= 5, "The original full-area meshes must import.");
            var borders = "ABCDE".Select(letter => AssetDatabase.LoadAssetAtPath<Mesh>($"Assets/Models/JudgeAreaBorders/{letter}.asset")).ToArray();
            Check(borders.All(mesh => mesh != null), "All five baked borders must import.");
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/JudgeAreas.mat");
            Check(material != null && material.shader.name == "Maj/Debug/SensorBorder", "The production material and shader must resolve.");
            Check(!ShaderUtil.ShaderHasError(material!.shader), "The production shader must import without errors.");
            Check(material!.renderQueue == 3000, "Every sensor style retains transparent rendering.");

            var root = new GameObject("Sensor visualization validation");
            root.SetActive(false);
            var filters = new MeshFilter[34];
            var renderers = new MeshRenderer[34];
            var colliders = new MeshCollider[34];
            var originalColliderMeshes = new Mesh[34];
            try
            {
                for (var index = 0; index < filters.Length; index++)
                {
                    var child = new GameObject("Sensor " + index);
                    child.transform.SetParent(root.transform, false);
                    child.transform.localPosition = new Vector3((index % 8) * 0.4f, (index / 8) * 0.3f, index * 0.001f);
                    child.transform.localScale = new Vector3(1.2f, 0.8f, 1f);
                    filters[index] = child.AddComponent<MeshFilter>();
                    filters[index].sharedMesh = borders[index % borders.Length];
                    renderers[index] = child.AddComponent<MeshRenderer>();
                    colliders[index] = child.AddComponent<MeshCollider>();
                    originalColliderMeshes[index] = areas[index % areas.Length];
                    colliders[index].sharedMesh = originalColliderMeshes[index];
                    InputManager.TouchPanelRawData[index] = index % 2 == 1;
                }
                InputManager.TouchPanelRawData[34] = true;
                var component = root.AddComponent<DummyTouchPanelRenderer>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(DummyTouchPanelRenderer).GetField("_sharedInstancedMaterial", flags)!.SetValue(component, material);
                typeof(DummyTouchPanelRenderer).GetMethod("Awake", flags)!.Invoke(component, null);
                Check(renderers.All(renderer => !renderer.enabled), "Awake disables every renderer before the first update.");
                Check(component.InstanceID2SensorIndexMappingTable.Count == 34, "All input mappings survive initialization.");
                var settings = MajEnv.Settings.Debug;
                var block = new MaterialPropertyBlock();
                var colorId = Shader.PropertyToID("_BaseColor");
                var materialColor = material.GetColor(colorId);
                var originalMatrices = filters.Select(filter => filter.transform.localToWorldMatrix).ToArray();
                foreach (var style in new[] { SensorDebugStyle.RedGreen, SensorDebugStyle.Brightness, SensorDebugStyle.Classic, SensorDebugStyle.RedGreen })
                {
                    settings.SensorStyle = style;
                    settings.DisplaySensor = true;
                    for (var opacity = 10; opacity <= 90; opacity += 10)
                    {
                        settings.SensorOpacity = opacity;
                        Step(component);
                        for (var index = 0; index < filters.Length; index++)
                        {
                            Check(renderers[index].enabled, "Visible sensors enable their renderers.");
                            var expectedMesh = style == SensorDebugStyle.RedGreen ? borders[index % borders.Length] : originalColliderMeshes[index];
                            Check(filters[index].sharedMesh == expectedMesh, "Style chooses the existing full area or baked border.");
                            Check(colliders[index].sharedMesh == originalColliderMeshes[index] && colliders[index].enabled, "Visualization preserves collision.");
                            Check(component.InstanceID2SensorIndexMappingTable[colliders[index].GetInstanceID()] == index, "Style preserves collider input indices.");
                            var active = InputManager.TouchPanelRawData[index];
                            var alpha = opacity / 100f;
                            var expected = active ? new Color(0, 1, 0, alpha) : new Color(1, 0, 0, alpha);
                            if (style == SensorDebugStyle.Brightness)
                            {
                                expected = new Color(0, 0, 0, active ? Math.Min(1f, alpha * 2f) : alpha * 0.5f);
                            }
                            else if (style == SensorDebugStyle.Classic)
                            {
                                expected = active ? new Color(93f / 255f, 13f / 255f, 6f / 255f, alpha) : new Color(0, 13f / 255f, 105f / 255f, alpha);
                            }
                            renderers[index].GetPropertyBlock(block);
                            var actual = block.GetColor(colorId);
                            Check(Vector4.Distance(actual, expected) < 0.00001f, "The real property block contains the state color and opacity.");
                            Check(renderers[index].sharedMaterial == material, "No per-sensor material is instantiated.");
                        }
                    }
                    settings.DisplaySensor = false;
                    Step(component);
                    for (var frame = 0; frame < 5; frame++)
                    {
                        Step(component);
                    }
                    Check(renderers.All(renderer => !renderer.enabled), "Disabled debug stops all renderer submission.");
                    Check(colliders.All(collider => collider.enabled && collider.gameObject.activeSelf), "Disabled debug preserves input objects.");
                }
                settings.SensorStyle = SensorDebugStyle.Brightness;
                settings.DisplaySensor = false;
                Step(component);
                settings.DisplaySensor = true;
                Step(component);
                Check(filters.Select((filter, index) => filter.sharedMesh == originalColliderMeshes[index]).All(value => value), "A style selected while hidden applies on reshow.");
                Check(material.GetColor(colorId) == materialColor, "Renderer colors never mutate the shared material.");
                ValidatePositions(component, filters, renderers, colliders, originalColliderMeshes, originalMatrices);
                Debug.Log($"SENSOR_DEBUG_VALIDATION_PASSED ({s_assertions} assertions)");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>Checks the production options using the game's Newtonsoft converter policy.</summary>
        private static void ValidateJson()
        {
            var serializer = new JsonSerializerSettings
            {
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                Converters = { new StringEnumConverter() }
            };
            var defaults = JsonConvert.DeserializeObject<DebugOptions>("{}", serializer)!;
            Check(defaults.SensorStyle == SensorDebugStyle.RedGreen && defaults.SensorOpacity == 20 && defaults.SensorPosition == SensorDebugPosition.MainScreen, "Missing new keys preserve the main-screen red/green default.");
            foreach (var style in new[] { SensorDebugStyle.Brightness, SensorDebugStyle.RedGreen, SensorDebugStyle.Classic })
            {
                var options = new DebugOptions { SensorStyle = style, SensorOpacity = 30, DisplaySensor = true, DisplayOffset = 0.125f };
                var json = JsonConvert.SerializeObject(options, serializer);
                Check(json.Contains("\"SensorStyle\":\"" + style + "\""), "The saved enum uses its stable string name.");
                var restored = JsonConvert.DeserializeObject<DebugOptions>(json, serializer)!;
                Check(restored.SensorStyle == style && restored.SensorOpacity == 30 && restored.DisplaySensor && restored.DisplayOffset == 0.125f, "New and existing debug fields round-trip together.");
            }
            var invalid = JsonConvert.DeserializeObject<DebugOptions>("{\"SensorStyle\":1234}", serializer)!;
            Check(invalid.SensorStyle == SensorDebugStyle.RedGreen, "Unknown numeric styles fall back safely.");
            foreach (var position in new[] { SensorDebugPosition.MainScreen, SensorDebugPosition.SubScreen })
            {
                var json = JsonConvert.SerializeObject(new DebugOptions { SensorPosition = position }, serializer);
                Check(json.Contains("\"SensorPosition\":\"" + position + "\""), "The position enum uses a stable JSON string.");
                Check(JsonConvert.DeserializeObject<DebugOptions>(json, serializer)!.SensorPosition == position, "The sensor display position round-trips.");
            }
            invalid = JsonConvert.DeserializeObject<DebugOptions>("{\"SensorPosition\":1234}", serializer)!;
            Check(invalid.SensorPosition == SensorDebugPosition.MainScreen, "Unknown numeric positions fall back to the main screen.");
        }

        /// <summary>Runs the production pre-update and late layout stages in normal frame order.</summary>
        /// <param name="component">The production sensor visualization component.</param>
        private static void Step(DummyTouchPanelRenderer component)
        {
            component.OnPreUpdate();
            typeof(DummyTouchPanelRenderer).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);
        }

        /// <summary>Validates screen placement while preserving every input collider and world transform.</summary>
        /// <param name="component">The production sensor visualization component.</param>
        /// <param name="filters">All sensor filters.</param>
        /// <param name="renderers">All sensor renderers.</param>
        /// <param name="colliders">All sensor colliders.</param>
        /// <param name="colliderMeshes">The retained collider meshes.</param>
        /// <param name="originalMatrices">The original input transforms.</param>
        private static void ValidatePositions(DummyTouchPanelRenderer component, MeshFilter[] filters, MeshRenderer[] renderers, MeshCollider[] colliders, Mesh[] colliderMeshes, Matrix4x4[] originalMatrices)
        {
            var settings = MajEnv.Settings.Debug;
            var transformId = Shader.PropertyToID("_SensorDisplayTransform");
            var colorId = Shader.PropertyToID("_BaseColor");
            var block = new MaterialPropertyBlock();
            var source = WorldGeometryBounds(filters, colliderMeshes);
            settings.DisplaySensor = true;
            settings.SensorPosition = SensorDebugPosition.SubScreen;
            foreach (var edge in new[] { new Vector4(-4, 13, 4, 9), new Vector4(1, 8, 11, 6), new Vector4(-8, 10, -4, 3) })
            {
                InputManager.SubScreenEdge = edge;
                foreach (var style in new[] { SensorDebugStyle.Brightness, SensorDebugStyle.RedGreen, SensorDebugStyle.Classic })
                {
                    settings.SensorStyle = style;
                    settings.SensorOpacity = 90;
                    Step(component);
                    var expectedScale = (edge.y - edge.w) / source.size.y;
                    var expected = new Vector4(expectedScale, expectedScale, (edge.x + edge.z) * 0.5f - source.center.x * expectedScale, (edge.y + edge.w) * 0.5f - source.center.y * expectedScale);
                    var displayed = new Bounds();
                    var hasPoint = false;
                    for (var index = 0; index < filters.Length; index++)
                    {
                        Check(renderers[index].enabled, "A valid secondary display enables every sensor.");
                        renderers[index].GetPropertyBlock(block);
                        var actual = block.GetVector(transformId);
                        Check(Vector4.Distance(actual, expected) < 0.00001f, "Placement uses one uniform world transform shared by every sensor.");
                        var geometry = filters[index].sharedMesh.vertices;
                        foreach (var point in geometry)
                        {
                            var world = filters[index].transform.TransformPoint(point);
                            world = new Vector3(world.x * actual.x + actual.z, world.y * actual.y + actual.w, world.z);
                            if (!hasPoint)
                            {
                                displayed = new Bounds(world, Vector3.zero);
                                hasPoint = true;
                            }
                            displayed.Encapsulate(world);
                            Check(ContainsWithTolerance(renderers[index].bounds, world), "Custom renderer bounds contain the shader-transformed geometry for culling.");
                        }
                        Check(colliders[index].sharedMesh == colliderMeshes[index] && colliders[index].enabled, "Secondary placement keeps collision geometry and state intact.");
                        Check(filters[index].transform.localToWorldMatrix == originalMatrices[index], "Secondary placement never moves or scales input transforms.");
                        Check(block.GetColor(colorId).a > 0f, "Layout updates preserve the sensor color property.");
                    }
                    Check(Math.Abs(displayed.min.y - edge.w) < 0.001f && Math.Abs(displayed.max.y - edge.y) < 0.001f, $"The entire selected {style} sensor drawing aligns with the secondary display top and bottom: got {displayed}, wanted {edge}.");
                    Check(Math.Abs(displayed.center.x - (edge.x + edge.z) * 0.5f) < 0.001f, "The entire sensor drawing is horizontally centered in the secondary display.");
                }
            }
            foreach (var invalid in new[] { Vector4.zero, new Vector4(1, 2, 1, 0), new Vector4(2, 2, 1, 0), new Vector4(0, 2, 1, 2), new Vector4(float.NaN, 2, 1, 0), new Vector4(0, float.PositiveInfinity, 1, 0) })
            {
                InputManager.SubScreenEdge = invalid;
                Step(component);
                Check(renderers.All(renderer => !renderer.enabled), "Uninitialized or invalid secondary edges hide every visualization.");
                InputManager.SubScreenEdge = new Vector4(-4, 13, 4, 9);
                Step(component);
                Check(renderers.All(renderer => renderer.enabled), "Secondary visualization recovers automatically when valid edges arrive.");
            }
            var rootTransform = component.transform;
            rootTransform.SetPositionAndRotation(new Vector3(2, -1, 0), Quaternion.Euler(0, 0, 17));
            rootTransform.localScale = new Vector3(0.8f, 1.1f, 1f);
            var movedMatrices = filters.Select(filter => filter.transform.localToWorldMatrix).ToArray();
            Step(component);
            var movedSource = WorldGeometryBounds(filters, colliderMeshes);
            var movedScale = 4f / movedSource.size.y;
            var movedExpected = new Vector4(movedScale, movedScale, -movedSource.center.x * movedScale, 11f - movedSource.center.y * movedScale);
            for (var index = 0; index < filters.Length; index++)
            {
                renderers[index].GetPropertyBlock(block);
                var actual = block.GetVector(transformId);
                Check(Vector4.Distance(actual, movedExpected) < 0.00001f, "Parent movement, rotation, and scale refresh the mapping from actual vertices.");
                Check(filters[index].transform.localToWorldMatrix == movedMatrices[index], "A layout refresh preserves externally changed input transforms.");
                foreach (var point in filters[index].sharedMesh.vertices)
                {
                    var world = filters[index].transform.TransformPoint(point);
                    world = new Vector3(world.x * actual.x + actual.z, world.y * actual.y + actual.w, world.z);
                    Check(ContainsWithTolerance(renderers[index].bounds, world), "Culling bounds refresh with transformed source geometry.");
                }
            }
            ValidateProductionScalePlacement(component, filters, renderers, colliders, colliderMeshes);
            rootTransform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            rootTransform.localScale = Vector3.one;
            Step(component);
            settings.DisplaySensor = false;
            Step(component);
            settings.SensorPosition = SensorDebugPosition.MainScreen;
            settings.SensorStyle = SensorDebugStyle.Classic;
            InputManager.SubScreenEdge = Vector4.zero;
            Step(component);
            Check(renderers.All(renderer => !renderer.enabled), "Changing position or style while hidden never submits geometry.");
            settings.DisplaySensor = true;
            Step(component);
            for (var index = 0; index < filters.Length; index++)
            {
                renderers[index].GetPropertyBlock(block);
                Check(renderers[index].enabled && filters[index].sharedMesh == colliderMeshes[index], "A hidden position/style change applies on reshow.");
                Check(block.GetVector(transformId) == new Vector4(1, 1, 0, 0), "Main-screen placement resets the shader transform to identity.");
                var expectedBounds = WorldGeometryBounds(new[] { filters[index] }, new[] { filters[index].sharedMesh });
                Check(Vector3.Distance(renderers[index].bounds.center, expectedBounds.center) < 0.001f && Vector3.Distance(renderers[index].bounds.size, expectedBounds.size) < 0.001f, "Returning to the main screen restores automatic mesh bounds.");
            }
            filters[0].transform.localPosition += new Vector3(2, 3, 0);
            Step(component);
            var moved = WorldGeometryBounds(new[] { filters[0] }, new[] { filters[0].sharedMesh });
            Check(Vector3.Distance(renderers[0].bounds.center, moved.center) < 0.001f, "ResetBounds allows ordinary transform tracking after returning to the main screen.");
        }

        /// <summary>Checks the real scene's small root scale combined with large, independently rotated sensor children.</summary>
        /// <param name="component">The production sensor visualization component.</param>
        /// <param name="filters">The filters providing source meshes and transforms.</param>
        /// <param name="renderers">The renderers providing visual bounds and property blocks.</param>
        /// <param name="colliders">The input colliders that visualization must preserve.</param>
        /// <param name="colliderMeshes">The retained full-area collider meshes.</param>
        private static void ValidateProductionScalePlacement(DummyTouchPanelRenderer component, MeshFilter[] filters, MeshRenderer[] renderers, MeshCollider[] colliders, Mesh[] colliderMeshes)
        {
            var root = component.transform;
            var rootPosition = root.localPosition;
            var rootRotation = root.localRotation;
            var rootScale = root.localScale;
            var positions = filters.Select(filter => filter.transform.localPosition).ToArray();
            var rotations = filters.Select(filter => filter.transform.localRotation).ToArray();
            var scales = filters.Select(filter => filter.transform.localScale).ToArray();
            var settings = MajEnv.Settings.Debug;
            var edge = new Vector4(-6, 12, 6, 7);
            var block = new MaterialPropertyBlock();
            var transformId = Shader.PropertyToID("_SensorDisplayTransform");
            try
            {
                for (var index = 0; index < filters.Length; index++)
                {
                    var child = filters[index].transform;
                    child.localPosition = new Vector3((index % 8) * 0.04f, (index / 8) * 0.03f, index * 0.001f);
                    child.localRotation = Quaternion.Euler(0, 0, (index % 8) * 45);
                    child.localScale = Vector3.one * 100f;
                }
                settings.SensorPosition = SensorDebugPosition.SubScreen;
                settings.DisplaySensor = true;
                InputManager.SubScreenEdge = edge;
                for (var rootVariant = 0; rootVariant < 2; rootVariant++)
                {
                    root.SetLocalPositionAndRotation(new Vector3(0.3f + rootVariant, -6.2f + rootVariant, -3f), Quaternion.Euler(0, 0, rootVariant * 11));
                    root.localScale = Vector3.one * 0.021432761f;
                    var matrices = filters.Select(filter => filter.transform.localToWorldMatrix).ToArray();
                    var source = WorldGeometryBounds(filters, colliderMeshes);
                    var scale = (edge.y - edge.w) / source.size.y;
                    var expected = new Vector4(scale, scale, (edge.x + edge.z) * 0.5f - source.center.x * scale, (edge.y + edge.w) * 0.5f - source.center.y * scale);
                    foreach (var style in new[] { SensorDebugStyle.RedGreen, SensorDebugStyle.Brightness, SensorDebugStyle.Classic })
                    {
                        settings.SensorStyle = style;
                        Step(component);
                        var displayed = new Bounds();
                        var initialized = false;
                        for (var index = 0; index < filters.Length; index++)
                        {
                            Check(renderers[index].enabled, "The production root/child scale hierarchy remains visible in the secondary region.");
                            renderers[index].GetPropertyBlock(block);
                            var actual = block.GetVector(transformId);
                            Check(Vector4.Distance(actual, expected) < 0.00001f, "The small root/large child hierarchy maps from its actual vertex union.");
                            Check(filters[index].transform.localToWorldMatrix == matrices[index], "Secondary layout never modifies the production-scale input transforms.");
                            Check(colliders[index].sharedMesh == colliderMeshes[index] && colliders[index].enabled, "Secondary layout preserves collision at the production scale.");
                            foreach (var point in filters[index].sharedMesh.vertices)
                            {
                                var world = filters[index].transform.TransformPoint(point);
                                world = new Vector3(world.x * actual.x + actual.z, world.y * actual.y + actual.w, world.z);
                                if (!initialized)
                                {
                                    displayed = new Bounds(world, Vector3.zero);
                                    initialized = true;
                                }
                                displayed.Encapsulate(world);
                                Check(ContainsWithTolerance(renderers[index].bounds, world), "Culling bounds refresh after changing style with rotated production-scale children.");
                            }
                        }
                        Check(Math.Abs(displayed.min.y - edge.w) < 0.001f && Math.Abs(displayed.max.y - edge.y) < 0.001f, "Production-scale geometry aligns with the secondary region's top and bottom.");
                        Check(Math.Abs(displayed.center.x - (edge.x + edge.z) * 0.5f) < 0.001f, "Production-scale geometry stays centered after root transforms and style changes.");
                    }
                }
            }
            finally
            {
                for (var index = 0; index < filters.Length; index++)
                {
                    var child = filters[index].transform;
                    child.SetLocalPositionAndRotation(positions[index], rotations[index]);
                    child.localScale = scales[index];
                }
                root.SetLocalPositionAndRotation(rootPosition, rootRotation);
                root.localScale = rootScale;
            }
        }

        /// <summary>Computes the actual vertex union independently from the renderer layout implementation.</summary>
        /// <param name="filters">The filters providing world transforms.</param>
        /// <param name="meshes">The source meshes to include.</param>
        /// <returns>The world-space union of all source vertices.</returns>
        private static Bounds WorldGeometryBounds(MeshFilter[] filters, Mesh[] meshes)
        {
            var result = new Bounds();
            var initialized = false;
            for (var index = 0; index < filters.Length; index++)
            {
                foreach (var point in meshes[index].vertices)
                {
                    var world = filters[index].transform.TransformPoint(point);
                    if (!initialized)
                    {
                        result = new Bounds(world, Vector3.zero);
                        initialized = true;
                    }
                    result.Encapsulate(world);
                }
            }
            return result;
        }

        /// <summary>Allows only floating-point rounding at transformed bound edges.</summary>
        /// <param name="bounds">The renderer culling bounds.</param>
        /// <param name="point">A transformed mesh vertex.</param>
        /// <returns>Whether the bounds contain the point with a small tolerance.</returns>
        private static bool ContainsWithTolerance(Bounds bounds, Vector3 point)
        {
            bounds.Expand(0.001f);
            return bounds.Contains(point);
        }

        /// <summary>Records an assertion and fails immediately when its condition is false.</summary>
        /// <param name="condition">The result being checked.</param>
        /// <param name="message">Context for the failed condition.</param>
        /// <exception cref="InvalidOperationException">The condition is false.</exception>
        private static void Check(bool condition, string message)
        {
            s_assertions++;
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
