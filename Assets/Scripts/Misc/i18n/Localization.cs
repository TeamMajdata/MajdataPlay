using MajdataPlay.Buffers;
using MajdataPlay.Collections;
using MajdataPlay.Diagnostics;
using MajdataPlay.Extensions;
using MajdataPlay.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;

#nullable enable
namespace MajdataPlay.i18n
{
    public static class Localization
    {
        public static event EventHandler<Language>? OnLanguageChanged;
        public static Language Current
        {
            get => _current;
            set
            {
                _current = value;
                if (OnLanguageChanged is not null)
                {
                    OnLanguageChanged(null, value);
                }
            }
        }
        readonly static JsonSerializerSettings JsonReaderSettings = new()
        {
            Formatting = Formatting.Indented,
            Converters = 
            { 
                new StringEnumConverter() 
            }
        };
        public static Language[] Available { get; private set; } = Array.Empty<Language>();

        static bool _isInited = false;
        readonly static object _initLock = new();

        internal static void Init()
        {
            if (_isInited)
            {
                return;
            }
            lock (_initLock)
            {
                if (_isInited)
                {
                    return;
                }
                _isInited = true;
            }

            try
            {
                var files = new List<string>
                {
                    "Langs/en-US",
                    "Langs/ja-JP",
                    "Langs/zh-CN",
                    "Langs/zh-TW",
                };
                List<Language> loadedLangs = new();
                foreach (var file in files)
                {
                    var ta = Resources.Load<TextAsset>(file);
                    if (ta == null)
                    {
                        MajDebug.LogError($"Lang file not found: {file}");
                        continue;
                    }
                    MajDebug.LogDebug("Lang file loaded: " + file);
                    var json = ta.text;
                    var lang = Parse(json);
                    if (lang is null)
                    {
                        MajDebug.LogError($"Failed to parse lang file: {file}");
                        continue;
                    }
                    loadedLangs.Add(lang);
                }
                if (loadedLangs.Count == 0)
                {
                    return;
                }
                var grouped = loadedLangs.GroupBy(x => x.ToString());
                Available = new Language[grouped.Count()];
                foreach (var (i, grouping) in grouped.WithIndex())
                {
                    Available[i] = grouping.First();
                }
            }
            catch(Exception e)
            {
                MajDebug.LogException(e);
            }
        }
        /// <summary>
        /// Set language by code and author<para>such like: "zh-CN - Majdata"</para>
        /// </summary>
        /// <param name="langInfo"></param>
        public static bool SetLang(string langInfo)
        {
            if (Available.IsEmpty())
            {
                return false;
            }
            var result = Available.Find(x => x.ToString() == langInfo);
            if (result is null)
            {
                return false;
            }
            Current = result;
            return true;
        }
        public static bool SetLangByCode(string code)
        {
            if (Available.IsEmpty())
            {
                return false;
            }
            var result = Available.Find(x => x.Code == code);
            if (result is null)
            {
                return false;
            }
            Current = result;
            return true;
        }
        public static Language? Parse(string json)
        {
            if (Serializer.Json.TryDeserialize<Language>(json, out var lang, out var exception, JsonReaderSettings) && lang is not null)
            {
                return lang;
            }
            else
            {
                MajDebug.LogException(exception);
                return default;
            }
        }
        public static Language[] Parse(IEnumerable<string> jsons)
        {
            using var loadedLangs = new PooledList<Language>();
            foreach (var json in jsons)
            {
                var lang = Parse(json);
                if(lang is null)
                {
                    continue;
                }
                loadedLangs.Add(lang);
            }
            if (loadedLangs.Count == 0)
            {
                return Array.Empty<Language>();
            }
            var grouped = loadedLangs.GroupBy(x => x.ToString());
            var available = new Language[grouped.Count()];
            foreach (var (i, grouping) in grouped.WithIndex())
            {
                available[i] = grouping.First();
            }
            return available;
        }
        /// <summary>
        /// Looks up the translation of a key in the current language and reports the key when it is missing.
        /// </summary>
        /// <remarks>
        /// A missing key is reported once per language through <see cref="ReportMissingTranslation"/>, so repeated
        /// lookups of the same key do not flood the log.
        /// </remarks>
        /// <param name="origin">The localization key.</param>
        /// <param name="strOut">Receives the translation, or <paramref name="origin"/> when the key is missing.</param>
        /// <returns>True when the current language translates <paramref name="origin"/>; otherwise false.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="origin"/> is null.</exception>
        public static bool TryGetLocalizedText(string origin, out string strOut)
        {
            if (TryFindLocalizedText(origin, out strOut))
            {
                return true;
            }
            ReportMissingTranslation(origin);
            return false;
        }
        /// <summary>
        /// Looks up the translation of a key in the current language without reporting a missing key.
        /// </summary>
        /// <remarks>
        /// Intended for fallback chains, where a miss is expected as long as a later key is translated; the caller
        /// reports the chain through <see cref="ReportMissingTranslation"/> when every key misses.
        /// </remarks>
        /// <param name="key">The localization key.</param>
        /// <param name="text">Receives the translation, or <paramref name="key"/> when the key is missing.</param>
        /// <returns>True when the current language translates <paramref name="key"/>; otherwise false.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
        internal static bool TryFindLocalizedText(string key, out string text)
        {
            var translations = Current.GetTranslations();
            if (translations.TryGetValue(key, out var content))
            {
                text = content;
                return true;
            }
            text = key;
            return false;
        }
        /// <summary>
        /// Reports a missing translation the first time it is seen for the current language.
        /// </summary>
        /// <remarks>
        /// Reported keys are remembered per language and forgotten when another language becomes current, so each
        /// language reports each missing key once. This method is thread-safe.
        /// </remarks>
        /// <param name="key">The missing localization key; for a fallback chain, its primary key.</param>
        /// <param name="fallbackKeys">The fallback keys that were also missing, for the log message, or null when there were none.</param>
        /// <param name="isExpected">
        /// True when the text is commonly left untranslated, such as a numeric option value; the miss is then
        /// reported at debug level instead of as a warning.
        /// </param>
        internal static void ReportMissingTranslation(string key, string? fallbackKeys = null, bool isExpected = false)
        {
            var language = _current;
            lock (_missingTranslationLock)
            {
                if (!ReferenceEquals(_missingTranslationLanguage, language))
                {
                    _reportedMissingTranslations.Clear();
                    _missingTranslationLanguage = language;
                }
                if (!_reportedMissingTranslations.Add(key))
                {
                    return;
                }
            }

            var message = fallbackKeys is null
                ? $"Missing translation for \"{key}\" in language \"{language}\""
                : $"Missing translation for \"{key}\" and its fallbacks ({fallbackKeys}) in language \"{language}\"";
            if (isExpected)
            {
                MajDebug.LogDebug(nameof(Localization), message);
            }
            else
            {
                MajDebug.LogWarning(nameof(Localization), message);
            }
        }
        static Language _current = Language.Default;
        /// <summary>
        /// The missing keys already reported for <see cref="_missingTranslationLanguage"/>.
        /// </summary>
        readonly static HashSet<string> _reportedMissingTranslations = new(StringComparer.Ordinal);
        /// <summary>
        /// The language whose missing keys <see cref="_reportedMissingTranslations"/> holds.
        /// </summary>
        static Language? _missingTranslationLanguage;
        /// <summary>
        /// Guards the missing-translation report state, which background loading tasks may also update.
        /// </summary>
        readonly static object _missingTranslationLock = new();
    }
}

