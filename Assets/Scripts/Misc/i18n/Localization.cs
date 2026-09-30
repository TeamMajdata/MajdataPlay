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
        public static bool TryGetLocalizedText(string origin,out string strOut)
        {
            var translations = Current.GetTranslations();
            if (translations.TryGetValue(origin, out var content))
            {
                strOut = content;
                return true;
            }
            
            strOut = origin;
            var currentLang = _current;
            MajDebug.LogWarning($"[i18n]Missing translation for: {origin}\nCode: {currentLang.Code}, Author: {currentLang.Author}");
            return false;
        }
        static Language _current = Language.Default;
    }
}

