using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MajdataPlay.i18n;
#nullable enable
namespace MajdataPlay.TranslationValidation
{
    /// <summary>
    /// Contains localization call shapes that are inspected but never executed by the harness.
    /// </summary>
    internal static class CallFixtures
    {
        /// <summary>
        /// Supplies a compile-time constant that should be emitted as a literal key.
        /// </summary>
        private const string ConstantKey = "Call.Const";

        /// <summary>
        /// Exercises literal, extension-call, constant, and try-localization targets.
        /// </summary>
        internal static void LiteralTargets()
        {
            StringExtensions.i18n("Call.Literal", Array.Empty<object>());
            "Call.Extension".i18n();
            ConstantKey.i18n();
            "Call.Try".Tryi18n(out var extensionOutput);
            Localization.TryGetLocalizedText("Call.Localization", out var serviceOutput);
            GC.KeepAlive(extensionOutput);
            GC.KeepAlive(serviceOutput);
        }

        /// <summary>
        /// Exercises local propagation and overwrites before and after localization calls.
        /// </summary>
        internal static void LocalReassignment()
        {
            var key = "Call.Local";
            key.i18n();
            key = "Call.Assigned";
            key.i18n();
            key = "NonKey.OverwrittenLocal";
            key = "Call.Reassigned";
            key.i18n();
        }

        /// <summary>
        /// Exercises a control-flow join with two fixed localization keys.
        /// </summary>
        /// <param name="useLeft">Selects one of the possible runtime branches.</param>
        internal static void BranchMerge(bool useLeft)
        {
            string key;
            if (useLeft)
            {
                key = "Call.Branch.Left";
            }
            else
            {
                key = "Call.Branch.Right";
            }
            key.i18n();
        }

        /// <summary>
        /// Exercises switch branches that converge on a shared localization call.
        /// </summary>
        /// <param name="selection">Selects one of three finite key alternatives.</param>
        internal static void SwitchMerge(int selection)
        {
            string key;
            switch (selection)
            {
                case 0:
                    key = "Call.Switch.Zero";
                    break;
                case 1:
                    key = "Call.Switch.One";
                    break;
                default:
                    key = "Call.Switch.Other";
                    break;
            }
            key.i18n();
        }

        /// <summary>
        /// Exercises runtime concatenation of statically known local string values.
        /// </summary>
        internal static void Concatenation()
        {
            var prefix = "Call.Concat.";
            var pairSuffix = "Pair";
            string.Concat(prefix, pairSuffix).i18n();
            var middle = "Tri";
            var end = "ple";
            string.Concat(prefix, middle, end).i18n();
            var left = "Call.Operator.";
            var right = "Concat";
            (left + right).i18n();
        }

        /// <summary>
        /// Exercises formatting payloads and preexisting out values that are not keys.
        /// </summary>
        internal static void UnrelatedArguments()
        {
            var unused = "NonKey.UnusedNearby";
            GC.KeepAlive(unused);
            "Call.FormatKey".i18n("NonKey.FormatPayload", 7);
            var output = "NonKey.OutputSeed";
            Localization.TryGetLocalizedText("Call.OutKey", out output);
            GC.KeepAlive(output);
            var tryOutput = "NonKey.TryOutputSeed";
            "Call.TryOutKey".Tryi18n(out tryOutput);
            GC.KeepAlive(tryOutput);
        }

        /// <summary>
        /// Exercises a genuinely dynamic key with an unrelated nearby string literal.
        /// </summary>
        /// <param name="runtimeKey">A runtime value that cannot be statically determined.</param>
        internal static void DynamicArgument(string runtimeKey)
        {
            var nearby = "NonKey.DynamicNearby";
            GC.KeepAlive(nearby);
            runtimeKey.i18n();
        }

        /// <summary>
        /// Exercises a mixed fixed and dynamic control-flow join requiring a diagnostic.
        /// </summary>
        /// <param name="useFixed">Selects the constant or the dynamic branch.</param>
        /// <param name="runtimeKey">The key supplied by the dynamic branch.</param>
        internal static void MixedDynamicBranch(bool useFixed, string runtimeKey)
        {
            var key = useFixed ? "Call.Mixed.Fixed" : runtimeKey;
            key.i18n();
        }

        /// <summary>
        /// Ensures an out write invalidates the previous constant stored in a local variable.
        /// </summary>
        internal static void OutValueBecomesDynamic()
        {
            var key = "NonKey.StaleOutLocal";
            Localization.TryGetLocalizedText("Call.OutProducesDynamic", out key);
            key.i18n();
        }

        /// <summary>
        /// Exercises a field with a throwing static initializer that must not be evaluated.
        /// </summary>
        internal static void PoisonedReadonly()
        {
            var nearby = "NonKey.PoisonedNearby";
            GC.KeepAlive(nearby);
            PoisonedReadonlyKeys.Key.i18n();
        }

        /// <summary>
        /// Exercises a literal field initializer followed by a throwing static constructor.
        /// </summary>
        internal static void LiteralReadonly()
        {
            LiteralReadonlyKeys.Key.i18n();
        }

        /// <summary>
        /// Supplies one origin for a key used at multiple call sites.
        /// </summary>
        internal static void SharedKeyFirst()
        {
            "Call.Shared".i18n();
        }

        /// <summary>
        /// Supplies a second origin that must not erase the first key source.
        /// </summary>
        internal static void SharedKeySecond()
        {
            "Call.Shared".i18n();
        }

        /// <summary>
        /// Exercises keys that differ only by case and must remain distinct inventory entries.
        /// </summary>
        internal static void CaseSensitiveKeys()
        {
            "Call.Case".i18n();
            "call.case".i18n();
        }

        /// <summary>
        /// Exercises decoys whose short method names match but declaring types do not.
        /// </summary>
        internal static void WrongDeclaringTypes()
        {
            MajdataPlay.Extensions.StringExtensions.i18n("NonKey.WrongNamespace");
            MajdataPlay.Extensions.StringExtensions.Tryi18n("NonKey.WrongTryNamespace", out var first);
            Decoys.Localization.TryGetLocalizedText("NonKey.WrongLocalizationType", out var second);
            GC.KeepAlive(first);
            GC.KeepAlive(second);
        }

        /// <summary>
        /// Creates a compiler-generated lambda body containing a fixed localization key.
        /// </summary>
        /// <returns>A delegate that would localize a fixed key if executed.</returns>
        internal static Action Lambda()
        {
            return () =>
            {
                "Call.Lambda".i18n();
            };
        }

        /// <summary>
        /// Holds nested ordinary, async, and iterator method bodies.
        /// </summary>
        internal static class Nested
        {
            /// <summary>
            /// Exercises a method declared on a nested type.
            /// </summary>
            internal static void Direct()
            {
                "Call.Nested".i18n();
            }

            /// <summary>
            /// Exercises localization emitted into an async state machine after suspension.
            /// </summary>
            /// <returns>A task that would complete after localizing a fixed key.</returns>
            internal static async Task Async()
            {
                await Task.Yield();
                "Call.Async.AfterAwait".i18n();
            }

            /// <summary>
            /// Exercises fixed keys on both sides of an iterator suspension point.
            /// </summary>
            /// <returns>An iterator yielding one value before a second localization call.</returns>
            internal static IEnumerable<int> Coroutine()
            {
                "Call.Coroutine.BeforeYield".i18n();
                yield return 1;
                "Call.Coroutine.AfterYield".i18n();
            }
        }
    }

    /// <summary>
    /// Contains a static readonly field whose value cannot safely be obtained by reflection.
    /// </summary>
    internal static class PoisonedReadonlyKeys
    {
        /// <summary>
        /// Stores the result of an initializer that must never be executed.
        /// </summary>
        internal static readonly string Key = Initialize();

        /// <summary>
        /// Detects any attempt to evaluate the readonly initializer.
        /// </summary>
        /// <returns>No value because fixture initialization is forbidden.</returns>
        /// <exception cref="InvalidOperationException">Always thrown if fixture initialization runs.</exception>
        private static string Initialize()
        {
            throw ExecutionGuard.Executed(nameof(PoisonedReadonlyKeys));
        }
    }

    /// <summary>
    /// Provides a literal initializer without allowing its owning type to initialize at runtime.
    /// </summary>
    internal static class LiteralReadonlyKeys
    {
        /// <summary>
        /// Stores a literal that may be resolved from IL but must never be read by reflection.
        /// </summary>
        internal static readonly string Key = "Call.Readonly.Known";

        /// <summary>
        /// Prevents runtime initialization while allowing metadata and IL inspection.
        /// </summary>
        /// <exception cref="InvalidOperationException">Always thrown if the type is initialized.</exception>
        static LiteralReadonlyKeys()
        {
            throw ExecutionGuard.Executed(nameof(LiteralReadonlyKeys));
        }
    }
}
