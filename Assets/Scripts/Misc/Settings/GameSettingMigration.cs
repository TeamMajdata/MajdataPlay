using System;
using System.Collections.Generic;
using MajdataPlay.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Upgrades settings.json documents written by older versions to the current <see cref="GameSetting"/> layout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The migration edits the JSON document before it is deserialized, so the setting classes only describe the
    /// current layout and never carry members that exist only for compatibility. Keys of the current layout always win
    /// over legacy keys, and every migrated legacy key is removed, so the next save writes the current layout only.
    /// Migrating a current document changes nothing.
    /// </para>
    /// <para>
    /// A setting category written as <c>null</c> is removed, so it falls back to its defaults instead of leaving
    /// <see cref="GameSetting"/> without it. The rendering keys that older versions stored in the <c>Display</c>
    /// category, <c>RenderQuality</c>, <c>RenderScale</c>, <c>FPSLimit</c> and <c>VSync</c>, move to the
    /// <c>Rendering</c> category.
    /// </para>
    /// </remarks>
    internal static class GameSettingMigration
    {
        // 旧版 Display 中的键名是历史格式，不能跟随现行 RenderingOptions 的属性重命名。
        /// <summary>
        /// The legacy key of the rendering quality level in the <c>Display</c> category.
        /// </summary>
        private const string LegacyRenderQualityKey = "RenderQuality";

        /// <summary>
        /// The legacy key of the render scale in the <c>Display</c> category.
        /// </summary>
        private const string LegacyRenderScaleKey = "RenderScale";

        /// <summary>
        /// The legacy key of the frame rate limit in the <c>Display</c> category.
        /// </summary>
        private const string LegacyFPSLimitKey = "FPSLimit";

        /// <summary>
        /// The legacy key of the vertical sync switch in the <c>Display</c> category.
        /// </summary>
        private const string LegacyVSyncKey = "VSync";

        /// <summary>
        /// Upgrades a settings.json document to the current layout in place.
        /// </summary>
        /// <param name="root">The root object of the document.</param>
        /// <param name="serializer">The serializer that reads legacy values, configured like the settings.json reader.</param>
        /// <returns>True when the document was changed; false when it already had the current layout.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="root"/> or <paramref name="serializer"/> is null.</exception>
        /// <exception cref="JsonException">A legacy value that has to be converted cannot be read.</exception>
        internal static bool Migrate(JObject root, JsonSerializer serializer)
        {
            if (root is null)
            {
                throw new ArgumentNullException(nameof(root));
            }
            if (serializer is null)
            {
                throw new ArgumentNullException(nameof(serializer));
            }

            // A null Rendering category was always read as missing, which lets its legacy keys migrate below.
            var removedNullCategories = RemoveNullCategories(root);
            var movedRenderingKeys = MoveLegacyRenderingKeys(root, serializer);
            return removedNullCategories || movedRenderingKeys;
        }

        /// <summary>
        /// Removes the setting categories whose value is <c>null</c>.
        /// </summary>
        /// <param name="root">The root object of the document.</param>
        /// <returns>True when a category was removed.</returns>
        private static bool RemoveNullCategories(JObject root)
        {
            var nullCategories = new List<JProperty>();
            foreach (var category in root.Properties())
            {
                if (category.Value.Type == JTokenType.Null)
                {
                    nullCategories.Add(category);
                }
            }
            foreach (var category in nullCategories)
            {
                category.Remove();
                MajDebug.LogWarning(nameof(GameSettingMigration), $"The {category.Name} settings were null and use their defaults");
            }
            return nullCategories.Count != 0;
        }

        /// <summary>
        /// Moves the legacy rendering keys of the <c>Display</c> category to the <c>Rendering</c> category.
        /// </summary>
        /// <remarks>
        /// A missing <c>Rendering</c> category is created from the legacy quality level and render scale. An existing
        /// one keeps its values, and the legacy frame pacing values only fill the keys it lacks.
        /// </remarks>
        /// <param name="root">The root object of the document.</param>
        /// <param name="serializer">The serializer that reads the legacy quality level and render scale.</param>
        /// <returns>True when a legacy key was found and removed.</returns>
        /// <exception cref="JsonException">The legacy quality level or render scale cannot be read.</exception>
        private static bool MoveLegacyRenderingKeys(JObject root, JsonSerializer serializer)
        {
            if (root[nameof(GameSetting.Display)] is not JObject display)
            {
                return false;
            }

            var hasLegacyKeys = false;
            var legacyQuality = TakeLegacyValue(display, LegacyRenderQualityKey, ref hasLegacyKeys);
            var legacyRenderScale = TakeLegacyValue(display, LegacyRenderScaleKey, ref hasLegacyKeys);
            var legacyFPSLimit = TakeLegacyValue(display, LegacyFPSLimitKey, ref hasLegacyKeys);
            var legacyVSync = TakeLegacyValue(display, LegacyVSyncKey, ref hasLegacyKeys);
            if (!hasLegacyKeys)
            {
                return false;
            }

            JObject? rendering;
            if (root.TryGetValue(nameof(GameSetting.Rendering), out var renderingToken))
            {
                // A Rendering category that is not an object is reported by the deserializer like any other invalid value.
                rendering = renderingToken as JObject;
            }
            else
            {
                rendering = CreateRenderingCategory(legacyQuality?.ToObject<RenderQualityOption>(serializer),
                                                    legacyRenderScale?.ToObject<int>(serializer));
                root[nameof(GameSetting.Rendering)] = rendering;
            }
            if (rendering is not null)
            {
                CopyIfMissing(rendering, nameof(RenderingOptions.FPSLimit), legacyFPSLimit);
                CopyIfMissing(rendering, nameof(RenderingOptions.VSync), legacyVSync);
            }
            MajDebug.LogInfo(nameof(GameSettingMigration), "Moved the legacy rendering settings from Display to Rendering");
            return true;
        }

        /// <summary>
        /// Removes a legacy key from a category.
        /// </summary>
        /// <param name="category">The category holding the legacy key.</param>
        /// <param name="key">The legacy key.</param>
        /// <param name="removed">Set to true when the key existed; left unchanged otherwise.</param>
        /// <returns>The value of the key, or null when the key is missing or its value is null, which older versions also read as missing.</returns>
        private static JToken? TakeLegacyValue(JObject category, string key, ref bool removed)
        {
            if (!category.TryGetValue(key, out var value))
            {
                return null;
            }
            category.Remove(key);
            removed = true;
            return value.Type == JTokenType.Null ? null : value;
        }

        /// <summary>
        /// Adds a legacy value to a category unless the category already has the key.
        /// </summary>
        /// <param name="category">The category that receives the value.</param>
        /// <param name="key">The current key of the value.</param>
        /// <param name="legacyValue">The legacy value, or null when it was not stored.</param>
        private static void CopyIfMissing(JObject category, string key, JToken? legacyValue)
        {
            if (legacyValue is not null && !category.ContainsKey(key))
            {
                category[key] = legacyValue;
            }
        }

        /// <summary>
        /// Creates the <c>Rendering</c> category that reproduces a legacy quality level and render scale.
        /// </summary>
        /// <param name="legacyQuality">The legacy quality level, or null when it was not stored.</param>
        /// <param name="legacyRenderScale">The legacy render scale in percent, or null when it was not stored.</param>
        /// <returns>The category; empty, which selects the current defaults, when neither value was stored.</returns>
        private static JObject CreateRenderingCategory(RenderQualityOption? legacyQuality, int? legacyRenderScale)
        {
            var rendering = new JObject();
            // 只有旧键实际提供了值才迁移，避免桌面旧 Medium 默认把新配置变成 Custom / 2x。
            if (!legacyQuality.HasValue && !legacyRenderScale.HasValue)
            {
                return rendering;
            }

            // 部分旧配置仍按当时的平台默认补齐缺项。
#if UNITY_ANDROID || UNITY_IOS
            var quality = legacyQuality ?? RenderQualityOption.Low;
#else
            var quality = legacyQuality ?? RenderQualityOption.Medium;
#endif
            var renderScale = Math.Clamp(legacyRenderScale ?? 100, 10, 200);
            var preset = GetMatchingPreset(quality, renderScale);
            rendering[nameof(RenderingOptions.RenderQuality)] = preset.ToString();
            if (preset == RenderingQualityOption.Custom)
            {
                // A preset applies its own parameters when it is read. RenderingOptions also switches to intermediate
                // output by itself whenever the render scale is not 100%.
                rendering[nameof(RenderingOptions.RenderScale)] = renderScale;
                rendering[nameof(RenderingOptions.AntiAliasing)] = GetAntiAliasing(quality).ToString();
                rendering[nameof(RenderingOptions.TextureQuality)] = GetTextureQuality(quality).ToString();
            }
            return rendering;
        }

        /// <summary>
        /// Finds the current preset whose parameters equal those of a legacy quality level.
        /// </summary>
        /// <param name="quality">The legacy quality level.</param>
        /// <param name="renderScale">The legacy render scale in percent.</param>
        /// <returns>The matching preset, or <see cref="RenderingQualityOption.Custom"/> when no preset matches exactly.</returns>
        private static RenderingQualityOption GetMatchingPreset(RenderQualityOption quality, int renderScale)
        {
            if (renderScale != 100)
            {
                return RenderingQualityOption.Custom;
            }
            return quality switch
            {
                RenderQualityOption.VeryLow => RenderingQualityOption.EnergySaving,
                RenderQualityOption.Low => RenderingQualityOption.Performance,
                RenderQualityOption.High => RenderingQualityOption.Beautiful,
                _ => RenderingQualityOption.Custom
            };
        }

        /// <summary>
        /// Gets the anti-aliasing mode of a legacy quality level.
        /// </summary>
        /// <param name="quality">The legacy quality level.</param>
        /// <returns>The anti-aliasing mode; disabled for unknown levels.</returns>
        private static RenderingAntialiasingOption GetAntiAliasing(RenderQualityOption quality)
        {
            return quality switch
            {
                RenderQualityOption.Medium => RenderingAntialiasingOption.MSAA2x,
                RenderQualityOption.High => RenderingAntialiasingOption.MSAA4x,
                RenderQualityOption.VeryHigh or RenderQualityOption.Ultra => RenderingAntialiasingOption.MSAA8x,
                _ => RenderingAntialiasingOption.Disabled
            };
        }

        /// <summary>
        /// Gets the texture detail level of a legacy quality level.
        /// </summary>
        /// <param name="quality">The legacy quality level.</param>
        /// <returns>Medium for the lowest level; high otherwise.</returns>
        private static RenderingTextureQualityOption GetTextureQuality(RenderQualityOption quality)
        {
            return quality == RenderQualityOption.VeryLow
                ? RenderingTextureQualityOption.Medium
                : RenderingTextureQualityOption.High;
        }
    }
}
