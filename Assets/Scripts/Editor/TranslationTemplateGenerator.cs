using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor.Build.Reporting;

namespace MajdataPlay.Editor
{
    internal static class TranslationTemplateGenerator
    {
        static Dictionary<string, string[]> _scene_translations = new()
        {
            { "Login", new string []
                {
                    "CONNECT_TIMEOUT",
                    "CONNECT_UNREACHABLE",
                    "LOGIN_FAILED",
                    "UNKNOWN_ERROR"
                } 
            },
            { "SortFind", new string[]
                {
                    "Search",
                    "Sort",
                    "SortbyDefault",
                    "SortbyDes",
                    "SortbyDiff",
                    "SortbyRank",
                    "SortbyTime",
                    "UseKeyboardHint"
                } 
            },
            { "List", new string[]
                {
                    "INPUT_HINT",
                    "EMPTY_COLLECTION",
                    "NO_PLAY_RECORD"
                }
            }
        };
        static string[] _common_translation = new string[]
        {
            "MAJTEXT_ONLINE_METHOD_NOT_ALLOWED",
            "MAJTEXT_ONLINE_USERNAME_OR_PASSWORD_INCORRECT",
            "MAJTEXT_SCORE_FAILED",
            "MAJTEXT_SCORE_SENDED",
            "MAJTEXT_SCORE_SENDING",
            "MAJTEXT_LOADING_CHART",
            "MAJTEXT_WAITING_FOR_BACKGROUND_TASKS_SUSPEND",
            "MAJTEXT_WAITING_FOR_RECORDER",
            "MAJTEXT_LOGGING_OUT",
            "MAJTEXT_LOADING_SCORE_STORAGE",
            "MAJTEXT_LOADING_SKIN",
            "MAJTEXT_DESERIALIZATION",
            "MAJTEXT_DOWNLOADING",
            "MAJTEXT_DOWNLOADING_AUDIO_TRACK",
            "MAJTEXT_DOWNLOADING_MAIDATA",
            "MAJTEXT_DOWNLOADING_PICTURE",
            "MAJTEXT_DOWNLOADING_VIDEO",
            "MAJTEXT_THUMBUP_ALREADY",
            "MAJTEXT_THUMBUP_FAILED",
            "MAJTEXT_THUMBUP_INFO",
            "MAJTEXT_THUMBUP_SENDED",
            "MAJTEXT_THUMBUP_SENDING",
            "MAJTEXT_PRESS_ANY_KEY",
            "MAJTEXT_SAY",
            "MAJTEXT_SCANNING_CHARTS",
            "MAJTEXT_SCANNING_CHARTS_FROM_{0}",
            "MAJTEXT_CLEANING_UP",
            "MAJTEXT_ONLINE_MACHINE_AUTH_REQUEST_FAILED",
            "MAJTEXT_ONLINE_MACHINE_REGISTRATION_UNSUPPORTED"
        };
        static string[] _err_translations = new string[]
        {
            "MAJTEXT_ERR_UNKNOWN",
            "MAJTEXT_ERR_OBSERROR",
            "MAJTEXT_ERR_VIDEO_PLAYER_PREPARE_TIMEOUT",
            "MAJTEXT_ERR_DOWNLOAD_FAILED",
            "MAJTEXT_ERR_LOAD_CHART_FAILED",
            "MAJTEXT_ERR_EMPTY_CHART",
            "MAJTEXT_ERR_INVALID_SYNTAX",
            "MAJTEXT_ERR_SCAN_CHARTS_FAILED",
            "MAJTEXT_ERR_NO_CHART",
        };
        public static void OnPreprocessBuild(BuildReport report)
        {

        }
    }
}
