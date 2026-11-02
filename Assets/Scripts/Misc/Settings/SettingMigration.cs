using System;
using System.Collections.Generic;
using MajdataPlay.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Upgrades the settings.json and system-settings.json documents written by older versions to the current
    /// <see cref="UserSetting"/> and <see cref="SystemSetting"/> layouts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The migration edits the JSON documents before they are deserialized, so the setting classes only describe the
    /// current layout and never carry members that exist only for compatibility. Keys of the current layout always win
    /// over legacy keys, and every migrated legacy key is removed, so the next save writes the current layout only.
    /// Migrating current documents changes nothing.
    /// </para>
    /// <para>
    /// Older versions stored every category in settings.json. The categories that now belong to
    /// <see cref="SystemSetting"/> move to system-settings.json; a category that system-settings.json already has only
    /// receives the keys it lacks. A setting category written as <c>null</c> is removed, so it falls back to its
    /// defaults instead of leaving its setting root without it.
    /// </para>
    /// <para>
    /// The options that changed category then move to their current category, which may belong to the other document,
    /// and the options renamed by the move, such as <c>LowLatencyVSync</c> of <c>Debug</c>, take their current key, such
    /// as <see cref="RenderingOptions.VulkanLowLatencyVSync"/>.
    /// The rendering keys that older versions stored in the <c>Display</c> category, <c>RenderQuality</c>,
    /// <c>RenderScale</c>, <c>FPSLimit</c> and <c>VSync</c>, are converted to the <c>Rendering</c> category first.
    /// The <c>Online</c> category of system-settings.json becomes the <c>Online</c> options of the <c>Network</c>
    /// category, except its <c>UseProxy</c> and <c>Proxy</c> keys, which become the <c>Enable</c> and <c>Url</c> keys of
    /// the <c>Proxy</c> options. The <c>Player</c> key of the <c>InputDevice</c> options in the <c>IO</c> category moves
    /// up to the <c>IO</c> category itself.
    /// </para>
    /// </remarks>
    internal static class SettingMigration
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
        /// The legacy key of the volumes in the <c>Audio</c> category, which are now the <c>Volume</c> category.
        /// </summary>
        private const string LegacyVolumeKey = "Volume";

        /// <summary>
        /// The legacy category of the online and proxy options, which are now part of the <c>Network</c> category.
        /// </summary>
        private const string LegacyOnlineCategory = "Online";

        /// <summary>
        /// The legacy key of the proxy switch in the <c>Online</c> category, which is now <see cref="ProxyOptions.Enable"/>.
        /// </summary>
        private const string LegacyUseProxyKey = "UseProxy";

        /// <summary>
        /// The legacy key of the proxy URL in the <c>Online</c> category, which is now <see cref="ProxyOptions.Url"/>.
        /// </summary>
        private const string LegacyProxyKey = "Proxy";

        // IOOptions.Player 只在桌面平台存在，无法用 nameof 引用；SettingMigrationTests 会检查它仍是 IOOptions 的属性。
        /// <summary>
        /// The key of the player number, which older versions stored in the <c>InputDevice</c> options of the <c>IO</c>
        /// category and which is now a key of the <c>IO</c> category itself.
        /// </summary>
        internal const string PlayerKey = "Player";

        // 旧版 settings.json 中属于系统设置的分类名，是历史格式，不能跟随 SystemSetting 的属性重命名。
        /// <summary>
        /// Gets the categories that older versions stored in settings.json and that now belong to system-settings.json.
        /// </summary>
        private static readonly string[] s_legacySystemCategories =
        {
            "Rendering",
            "Audio",
            "Encoding",
            "Decoding",
            "Debug",
            "Online",
            "IO"
        };

        /// <summary>
        /// Gets the categories of <see cref="UserSetting"/>, which are stored in settings.json.
        /// </summary>
        private static readonly string[] s_userCategories =
        {
            nameof(UserSetting.Game),
            nameof(UserSetting.Judge),
            nameof(UserSetting.Display),
            nameof(UserSetting.Volume),
            nameof(UserSetting.Touch)
        };

        // 键名同时是旧位置与现行位置的名称，改名的选项写作 (旧键名, 现行键名)；SettingMigrationTests 会检查现行键名仍是目标分类的属性。
        /// <summary>
        /// Gets the options that moved to another category, by legacy category and current category.
        /// </summary>
        internal static readonly LegacyOptionMove[] LegacyOptionMoves =
        {
            new("Game", nameof(UserSetting.Display), "BGInfo", "SecondaryBGInfo", "SubScreenBGInfo", "TopInfo"),
            new("Game", nameof(UserSetting.Judge), "EnableJudgeTimingGauge"),
            new("Game", nameof(SystemSetting.Common), "RecordMode"),
            new("Display", nameof(UserSetting.Game), "SkipVideoDownload"),
            new("Display", nameof(UserSetting.Judge), "DisplayCriticalPerfect", "DisplayBreakScore", "FastLateType",
                "NoteJudgeType", "TouchJudgeType", "SlideJudgeType", "BreakJudgeType", "BreakFastLateType",
                "SlideSortOrder", "OuterJudgeDistance", "InnerJudgeDistance", "DisplayHoldHeadJudgeResult"),
            new("Display", nameof(SystemSetting.Rendering), "Resolution", "MainScreenTransform", "MainScreenScale",
                "MainScreenOffset", "MainScreenCachedScreenCenterY", "SubDisplayOffset", "SubDisplayScale",
                "GameplayScreenRotationAngle", "Topmost"),
            new("Debug", nameof(UserSetting.Game), "NoteAppearRate"),
            new("Debug", nameof(UserSetting.Judge), "OffsetUnit"),
            new("Debug", nameof(UserSetting.Display), "DisplaySensor", "SensorPosition", "SensorStyle", "SensorOpacity"),
            new("Debug", nameof(UserSetting.Touch), "TouchSimulationRadius", "TouchAAreaExtraRadius",
                "TouchBAreaExtraRadius", "TouchCAreaExtraRadius", "TouchDAreaExtraRadius", "TouchEAreaExtraRadius",
                "TouchRadiusAdjust"),
            new("Debug", nameof(SystemSetting.Rendering), "DisplayRuntimeInfo", "FullScreen"),
            new("Debug", nameof(SystemSetting.Common), "MenuOptionIterationSpeed", "HideCursorInGame", "NoteFolding",
                "DJAutoPolicy"),
            new("Debug", nameof(SystemSetting.Rendering), ("LowLatencyVSync", "VulkanLowLatencyVSync"),
                ("LowLatencyVSyncBackend", "VulkanLowLatencyVSyncBackend"))
        };

        /// <summary>
        /// Upgrades the settings.json and system-settings.json documents to the current layouts in place.
        /// </summary>
        /// <param name="user">The root object of settings.json; empty when the file does not exist.</param>
        /// <param name="system">The root object of system-settings.json; empty when the file does not exist.</param>
        /// <param name="serializer">The serializer that reads legacy values, configured like the settings readers.</param>
        /// <returns>True when a document was changed; false when both already had the current layout.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="user"/>, <paramref name="system"/> or <paramref name="serializer"/> is null.</exception>
        /// <exception cref="JsonException">
        /// A legacy value of settings.json that has to be converted cannot be read. <paramref name="system"/> may already
        /// have received categories of <paramref name="user"/> when this is thrown.
        /// </exception>
        internal static bool Migrate(JObject user, JObject system, JsonSerializer serializer)
        {
            if (user is null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (system is null)
            {
                throw new ArgumentNullException(nameof(system));
            }
            if (serializer is null)
            {
                throw new ArgumentNullException(nameof(serializer));
            }

            // A null Rendering category was always read as missing, which lets its legacy keys migrate below.
            var changed = RemoveNullCategories(user);
            changed |= RemoveNullCategories(system);
            changed |= MoveLegacySystemCategories(user, system);
            changed |= MoveLegacyRenderingKeys(user, system, serializer);
            changed |= MoveLegacyVolume(user, system);
            changed |= MoveLegacyOnlineCategory(system);
            changed |= MoveLegacyPlayer(system);
            foreach (var move in LegacyOptionMoves)
            {
                changed |= MoveLegacyOptions(user, system, move);
            }
            return changed;
        }

        /// <summary>
        /// Removes the setting categories whose value is <c>null</c>.
        /// </summary>
        /// <param name="root">The root object of a document.</param>
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
                MajDebug.LogWarning(nameof(SettingMigration), $"The {category.Name} settings were null and use their defaults");
            }
            return nullCategories.Count != 0;
        }

        /// <summary>
        /// Moves the categories that older versions stored in settings.json to system-settings.json.
        /// </summary>
        /// <remarks>
        /// A category missing from system-settings.json is moved as it is. An existing one keeps its values and only
        /// receives the keys it lacks, which also carries the legacy keys that move on to another category.
        /// </remarks>
        /// <param name="user">The root object of settings.json.</param>
        /// <param name="system">The root object of system-settings.json.</param>
        /// <returns>True when a category was found in settings.json and removed from it.</returns>
        private static bool MoveLegacySystemCategories(JObject user, JObject system)
        {
            var changed = false;
            foreach (var name in s_legacySystemCategories)
            {
                if (!user.TryGetValue(name, out var legacyCategory))
                {
                    continue;
                }
                user.Remove(name);
                changed = true;
                if (!system.TryGetValue(name, out var currentCategory))
                {
                    system[name] = legacyCategory;
                }
                else if (currentCategory is JObject current && legacyCategory is JObject legacy)
                {
                    foreach (var key in legacy.Properties())
                    {
                        if (!current.ContainsKey(key.Name))
                        {
                            current[key.Name] = key.Value;
                        }
                    }
                }
                MajDebug.LogInfo(nameof(SettingMigration), $"Moved the {name} settings from settings.json to system-settings.json");
            }
            return changed;
        }

        /// <summary>
        /// Moves the legacy rendering keys of the <c>Display</c> category to the <c>Rendering</c> category.
        /// </summary>
        /// <remarks>
        /// A missing <c>Rendering</c> category is created from the legacy quality level and render scale. An existing
        /// one keeps its values, and the legacy frame pacing values only fill the keys it lacks.
        /// </remarks>
        /// <param name="user">The root object of settings.json, which holds the <c>Display</c> category.</param>
        /// <param name="system">The root object of system-settings.json, which holds the <c>Rendering</c> category.</param>
        /// <param name="serializer">The serializer that reads the legacy quality level and render scale.</param>
        /// <returns>True when a legacy key was found and removed.</returns>
        /// <exception cref="JsonException">The legacy quality level or render scale cannot be read.</exception>
        private static bool MoveLegacyRenderingKeys(JObject user, JObject system, JsonSerializer serializer)
        {
            if (user[nameof(UserSetting.Display)] is not JObject display)
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
            if (system.TryGetValue(nameof(SystemSetting.Rendering), out var renderingToken))
            {
                // A Rendering category that is not an object is reported by the deserializer like any other invalid value.
                rendering = renderingToken as JObject;
            }
            else
            {
                rendering = CreateRenderingCategory(legacyQuality?.ToObject<RenderQualityOption>(serializer),
                                                    legacyRenderScale?.ToObject<int>(serializer));
                system[nameof(SystemSetting.Rendering)] = rendering;
            }
            if (rendering is not null)
            {
                CopyIfMissing(rendering, nameof(RenderingOptions.FPSLimit), legacyFPSLimit);
                CopyIfMissing(rendering, nameof(RenderingOptions.VSync), legacyVSync);
            }
            MajDebug.LogInfo(nameof(SettingMigration), "Moved the legacy rendering settings from Display to Rendering");
            return true;
        }

        /// <summary>
        /// Moves the legacy volumes of the <c>Audio</c> category to the <c>Volume</c> category of settings.json.
        /// </summary>
        /// <param name="user">The root object of settings.json, which receives the <c>Volume</c> category.</param>
        /// <param name="system">The root object of system-settings.json, which holds the <c>Audio</c> category.</param>
        /// <returns>True when the legacy key was found and removed.</returns>
        private static bool MoveLegacyVolume(JObject user, JObject system)
        {
            if (system[nameof(SystemSetting.Audio)] is not JObject audio)
            {
                return false;
            }
            var hasLegacyKey = false;
            var legacyVolume = TakeLegacyValue(audio, LegacyVolumeKey, ref hasLegacyKey);
            if (!hasLegacyKey)
            {
                return false;
            }
            CopyIfMissing(user, nameof(UserSetting.Volume), legacyVolume);
            MajDebug.LogInfo(nameof(SettingMigration), "Moved the legacy volume settings from Audio to Volume");
            return true;
        }

        /// <summary>
        /// Moves the legacy <c>Online</c> category of system-settings.json into the <c>Network</c> category.
        /// </summary>
        /// <remarks>
        /// The proxy keys become the <see cref="ProxyOptions"/> of <c>Network</c>, and every other key moves under the
        /// same name to its <see cref="NetworkOptions.Online"/> options. Existing <c>Network</c> options keep their
        /// values and only receive the keys they lack. Options that are not objects keep their value, which the
        /// deserializer reports like any other invalid value, and the legacy values meant for them are dropped.
        /// </remarks>
        /// <param name="system">The root object of system-settings.json, which holds both categories.</param>
        /// <returns>True when the legacy category was found and removed.</returns>
        private static bool MoveLegacyOnlineCategory(JObject system)
        {
            if (!system.TryGetValue(LegacyOnlineCategory, out var legacyToken))
            {
                return false;
            }
            system.Remove(LegacyOnlineCategory);
            if (legacyToken is JObject legacy && GetOrCreateObject(system, nameof(SystemSetting.Network)) is JObject network)
            {
                foreach (var option in legacy.Properties())
                {
                    var (options, key) = option.Name switch
                    {
                        LegacyUseProxyKey => (nameof(NetworkOptions.Proxy), nameof(ProxyOptions.Enable)),
                        LegacyProxyKey => (nameof(NetworkOptions.Proxy), nameof(ProxyOptions.Url)),
                        _ => (nameof(NetworkOptions.Online), option.Name)
                    };
                    if (GetOrCreateObject(network, options) is JObject target)
                    {
                        CopyIfMissing(target, key, option.Value);
                    }
                }
            }
            MajDebug.LogInfo(nameof(SettingMigration), "Moved the legacy Online settings to Network");
            return true;
        }

        /// <summary>
        /// Moves the legacy player number of the <c>InputDevice</c> options up to the <c>IO</c> category.
        /// </summary>
        /// <remarks>
        /// A player number the <c>IO</c> category already has keeps its value, and the legacy one is dropped.
        /// </remarks>
        /// <param name="system">The root object of system-settings.json, which holds the <c>IO</c> category.</param>
        /// <returns>True when the legacy key was found and removed.</returns>
        private static bool MoveLegacyPlayer(JObject system)
        {
            if (system[nameof(SystemSetting.IO)] is not JObject io ||
                io[nameof(IOOptions.InputDevice)] is not JObject inputDevice)
            {
                return false;
            }
            var hasLegacyKey = false;
            var legacyPlayer = TakeLegacyValue(inputDevice, PlayerKey, ref hasLegacyKey);
            if (!hasLegacyKey)
            {
                return false;
            }
            CopyIfMissing(io, PlayerKey, legacyPlayer);
            MajDebug.LogInfo(nameof(SettingMigration), "Moved the legacy player number from InputDevice to IO");
            return true;
        }

        /// <summary>
        /// Gets the object stored under a key, creating an empty one when the key is missing.
        /// </summary>
        /// <param name="parent">The object holding the key.</param>
        /// <param name="key">The key of the object.</param>
        /// <returns>The object; null when the key holds a value that is not an object.</returns>
        private static JObject? GetOrCreateObject(JObject parent, string key)
        {
            if (!parent.TryGetValue(key, out var token))
            {
                token = new JObject();
                parent[key] = token;
            }
            return token as JObject;
        }

        /// <summary>
        /// Moves the legacy keys of one category to their current category.
        /// </summary>
        /// <remarks>
        /// The current category is created when it is missing. A current category that is not an object keeps its
        /// value, which the deserializer reports like any other invalid value, and the legacy values are dropped.
        /// </remarks>
        /// <param name="user">The root object of settings.json.</param>
        /// <param name="system">The root object of system-settings.json.</param>
        /// <param name="move">The keys to move and their legacy and current categories.</param>
        /// <returns>True when a legacy key was found and removed.</returns>
        private static bool MoveLegacyOptions(JObject user, JObject system, LegacyOptionMove move)
        {
            if (GetDocument(user, system, move.SourceCategory)[move.SourceCategory] is not JObject source)
            {
                return false;
            }

            var movedCount = 0;
            JObject? target = null;
            var targetRoot = GetDocument(user, system, move.TargetCategory);
            foreach (var (legacyKey, currentKey) in move.Keys)
            {
                var hasLegacyKey = false;
                var legacyValue = TakeLegacyValue(source, legacyKey, ref hasLegacyKey);
                if (!hasLegacyKey)
                {
                    continue;
                }
                movedCount++;
                if (target is null)
                {
                    if (!targetRoot.TryGetValue(move.TargetCategory, out var targetToken))
                    {
                        targetToken = new JObject();
                        targetRoot[move.TargetCategory] = targetToken;
                    }
                    target = targetToken as JObject;
                }
                if (target is not null)
                {
                    CopyIfMissing(target, currentKey, legacyValue);
                }
            }
            if (movedCount == 0)
            {
                return false;
            }
            MajDebug.LogInfo(nameof(SettingMigration), $"Moved {movedCount} legacy settings from {move.SourceCategory} to {move.TargetCategory}");
            return true;
        }

        /// <summary>
        /// Gets the document that stores a category in the current layout.
        /// </summary>
        /// <param name="user">The root object of settings.json.</param>
        /// <param name="system">The root object of system-settings.json.</param>
        /// <param name="category">The category name.</param>
        /// <returns><paramref name="user"/> for a category of <see cref="UserSetting"/>; otherwise <paramref name="system"/>.</returns>
        private static JObject GetDocument(JObject user, JObject system, string category)
        {
            return Array.IndexOf(s_userCategories, category) >= 0 ? user : system;
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
        /// Adds a legacy value to an object unless the object already has the key.
        /// </summary>
        /// <param name="category">The object that receives the value.</param>
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

        /// <summary>
        /// Describes options that moved from a legacy category to their current category, under the same key or renamed.
        /// </summary>
        internal sealed class LegacyOptionMove
        {
            /// <summary>
            /// Gets the category that stored the options in older versions.
            /// </summary>
            internal string SourceCategory { get; }

            /// <summary>
            /// Gets the category that stores the options now.
            /// </summary>
            internal string TargetCategory { get; }

            /// <summary>
            /// Gets the keys of the moved options in the legacy category and in the current category.
            /// </summary>
            internal IReadOnlyList<(string LegacyKey, string CurrentKey)> Keys { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="LegacyOptionMove"/> class for options that keep their keys.
            /// </summary>
            /// <param name="sourceCategory">The category that stored the options in older versions.</param>
            /// <param name="targetCategory">The category that stores the options now.</param>
            /// <param name="keys">The keys of the moved options, which are the same in both categories.</param>
            internal LegacyOptionMove(string sourceCategory, string targetCategory, params string[] keys)
                : this(sourceCategory, targetCategory, Array.ConvertAll(keys, key => (key, key)))
            {
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="LegacyOptionMove"/> class for options renamed by the move.
            /// </summary>
            /// <param name="sourceCategory">The category that stored the options in older versions.</param>
            /// <param name="targetCategory">The category that stores the options now.</param>
            /// <param name="keys">The legacy key and the current key of each moved option.</param>
            internal LegacyOptionMove(string sourceCategory, string targetCategory,
                                      params (string LegacyKey, string CurrentKey)[] keys)
            {
                SourceCategory = sourceCategory;
                TargetCategory = targetCategory;
                Keys = keys;
            }
        }
    }
}
