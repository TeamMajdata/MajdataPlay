using System;
using System.Threading.Tasks;
using MajdataPlay.i18n;

#nullable enable
namespace MajdataPlay.Tests.Game.EditorTools
{
    /// <summary>
    /// Localization call shapes inspected by <see cref="TranslationCallAnalyzerTests"/>. The methods are analyzed as IL
    /// and never executed; every proven key starts with <c>MajTestCall.</c>, every string that must not be reported as a
    /// key starts with <c>MajTestNonKey.</c>.
    /// </summary>
    internal static class TranslationCallFixtures
    {
        /// <summary>A compile-time constant key.</summary>
        private const string ConstantKey = "MajTestCall.Constant";

        /// <summary>A readonly key whose initializer the analyzer decodes without running it.</summary>
        private static readonly string s_readonlyKey = "MajTestCall.Readonly";

        /// <summary>A mutable static key whose value cannot be proven.</summary>
        private static string s_mutableKey = "MajTestNonKey.Mutable";

        /// <summary>Uses every supported localization entry point with literal keys.</summary>
        internal static void EntryPoints()
        {
            "MajTestCall.Extension".i18n();
            StringExtensions.i18n("MajTestCall.Static", Array.Empty<object>());
            ConstantKey.i18n();
            "MajTestCall.Try".Tryi18n(out var first);
            Localization.TryGetLocalizedText("MajTestCall.Service", out var second);
            GC.KeepAlive(first);
            GC.KeepAlive(second);
        }

        /// <summary>Reassigns a local key between calls.</summary>
        internal static void LocalReassignment()
        {
            var key = "MajTestCall.LocalFirst";
            key.i18n();
            key = "MajTestNonKey.Overwritten";
            key = "MajTestCall.LocalSecond";
            key.i18n();
        }

        /// <summary>Joins two literal keys at an if/else merge.</summary>
        /// <param name="left">Selects the branch.</param>
        internal static void BranchMerge(bool left)
        {
            string key;
            if (left)
            {
                key = "MajTestCall.BranchLeft";
            }
            else
            {
                key = "MajTestCall.BranchRight";
            }
            key.i18n();
        }

        /// <summary>Joins three literal keys after a switch.</summary>
        /// <param name="selector">Selects the case.</param>
        internal static void SwitchMerge(int selector)
        {
            string key;
            switch (selector)
            {
                case 0:
                    key = "MajTestCall.SwitchZero";
                    break;
                case 1:
                    key = "MajTestCall.SwitchOne";
                    break;
                default:
                    key = "MajTestCall.SwitchOther";
                    break;
            }
            key.i18n();
        }

        /// <summary>Concatenates literal pieces held in locals.</summary>
        internal static void Concatenation()
        {
            var prefix = "MajTestCall.Concat";
            var suffix = "Pair";
            (prefix + suffix).i18n();
            var middle = "Tri";
            var end = "ple";
            string.Concat(prefix, middle, end).i18n();
        }

        /// <summary>Passes format payloads that must not be reported as keys.</summary>
        internal static void FormatPayload()
        {
            "MajTestCall.Format".i18n("MajTestNonKey.Payload", 3);
        }

        /// <summary>Reads keys from a readonly initializer and from a mutable field.</summary>
        internal static void Fields()
        {
            s_readonlyKey.i18n();
            s_mutableKey.i18n();
        }

        /// <summary>Reads array elements at constant indices.</summary>
        internal static void ArrayElements()
        {
            var keys = new[] { "MajTestNonKey.ArrayFirst", "MajTestCall.ArraySecond" };
            keys[1].i18n();
        }

        /// <summary>Localizes a runtime argument next to an unrelated literal.</summary>
        /// <param name="runtimeKey">The dynamic key.</param>
        internal static void Dynamic(string runtimeKey)
        {
            var nearby = "MajTestNonKey.Nearby";
            GC.KeepAlive(nearby);
            runtimeKey.i18n();
        }

        /// <summary>Joins a literal and a dynamic key.</summary>
        /// <param name="useLiteral">Selects the literal.</param>
        /// <param name="runtimeKey">The dynamic key.</param>
        internal static void MixedDynamic(bool useLiteral, string runtimeKey)
        {
            var key = useLiteral ? "MajTestCall.MixedLiteral" : runtimeKey;
            key.i18n();
        }

        /// <summary>Localizes inside an async state machine.</summary>
        /// <returns>The task.</returns>
        internal static async Task AsyncCall()
        {
            await Task.Yield();
            "MajTestCall.Async".i18n();
        }

        /// <summary>Localizes inside a lambda.</summary>
        /// <returns>The lambda.</returns>
        internal static Func<string> LambdaCall()
        {
            return () => "MajTestCall.Lambda".i18n();
        }

        /// <summary>Uses the same key from a second call site.</summary>
        internal static void SharedFirst()
        {
            "MajTestCall.Shared".i18n();
        }

        /// <summary>Uses the same key from another call site.</summary>
        internal static void SharedSecond()
        {
            "MajTestCall.Shared".i18n();
        }

        /// <summary>Localizes inside a try/finally block.</summary>
        internal static void TryFinally()
        {
            try
            {
                "MajTestCall.InTry".i18n();
            }
            finally
            {
                GC.KeepAlive("MajTestNonKey.Finally");
            }
        }

        /// <summary>Stores a key in a loop, which converges on a finite set.</summary>
        /// <param name="count">The loop count.</param>
        internal static void Loop(int count)
        {
            var key = "MajTestCall.LoopStart";
            for (var i = 0; i < count; i++)
            {
                key.i18n();
                key = "MajTestCall.LoopNext";
            }
        }
    }
}
