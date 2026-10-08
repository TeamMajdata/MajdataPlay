using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

#nullable enable
namespace MajdataPlay.Editor.Windows
{
    /// <summary>
    /// Discovers literal localization keys without executing code from the inspected assembly.
    /// </summary>
    internal static class TranslationCallAnalyzer
    {
        /// <summary>Limits finite string sets and concatenation products.</summary>
        private const int MaxStrings = 32;

        /// <summary>Bounds concatenated string size even in exponential-growth loops.</summary>
        private const int MaxStringLength = 16384;

        /// <summary>Bounds the number of tracked array allocation sites per method.</summary>
        private const int MaxArrays = 64;

        /// <summary>Limits arrays modeled by the interpreter.</summary>
        private const int MaxArrayLength = 256;

        /// <summary>Bounds work per method, including loop convergence.</summary>
        private const int MaxVisits = 65536;

        /// <summary>Includes only members declared on the inspected type.</summary>
        private const BindingFlags DeclaredMembers = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        /// <summary>Maps encoded opcode values without reflecting over static field values.</summary>
        private static readonly Dictionary<short, OpCode> s_opCodes = CreateOpCodes();

        /// <summary>
        /// Adds proven keys and their source locations, and reports unresolved localization calls.
        /// </summary>
        /// <param name="assembly">The assembly whose declared method and constructor bodies are inspected.</param>
        /// <param name="keys">An ordinal dictionary of keys to ordinal sets of source locations.</param>
        /// <param name="diagnostics">Receives unsupported IL, load failures, and dynamic key diagnostics.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        internal static void Collect(Assembly assembly, IDictionary<string, HashSet<string>> keys,
            IList<string> diagnostics)
        {
            if (assembly == null || keys == null || diagnostics == null)
            {
                throw new ArgumentNullException(assembly == null ? nameof(assembly) :
                    keys == null ? nameof(keys) : nameof(diagnostics));
            }
            var context = new AnalysisContext(diagnostics);
            foreach (var type in GetTypes(assembly, diagnostics))
            {
                try
                {
                    var methods = new HashSet<MethodBase>();
                    foreach (var method in type.GetMethods(DeclaredMembers))
                    {
                        methods.Add(method);
                    }
                    foreach (var constructor in type.GetConstructors(DeclaredMembers))
                    {
                        methods.Add(constructor);
                    }
                    if (type.TypeInitializer != null)
                    {
                        methods.Add(type.TypeInitializer);
                    }
                    foreach (var method in methods)
                    {
                        CollectMethod(method, context, keys);
                    }
                }
                catch (Exception exception) when (IsInspectionFailure(exception))
                {
                    diagnostics.Add($"Cannot inspect {type.FullName}: {exception.Message}");
                }
            }
        }

        /// <summary>
        /// Infers fully literal OptionValues arrays at InitValueTexts calls and normal InitInternal exits.
        /// Superseded stores are not collected. No constructor, getter, enumerator, or initializer runs;
        /// opaque calls invalidate escaped values and unknown custom options produce diagnostics.
        /// </summary>
        /// <param name="enumeratorType">The option enumerator type to inspect without instantiating it.</param>
        /// <param name="diagnostics">Receives unknown assignments, mutations, and inspection failures.</param>
        /// <returns>Distinct proven literal option strings, sorted using ordinal comparison.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        internal static IReadOnlyList<string> CollectOptionValueStrings(Type enumeratorType,
            IList<string> diagnostics)
        {
            if (enumeratorType == null || diagnostics == null)
            {
                throw new ArgumentNullException(enumeratorType == null ? nameof(enumeratorType) : nameof(diagnostics));
            }
            var result = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var method = enumeratorType.GetMethod("InitInternal", BindingFlags.Instance |
                    BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                var field = FindOptionField(enumeratorType);
                if (method == null || method.ReturnType != typeof(void) || field == null)
                {
                    diagnostics.Add($"Unknown custom option values: {enumeratorType.FullName} has no inspectable InitInternal/OptionValues pair.");
                    return Array.Empty<string>();
                }
                var context = new AnalysisContext(diagnostics);
                var analysis = context.TryAnalyze(method, field, null, false);
                if (analysis == null)
                {
                    diagnostics.Add($"Unknown custom option values: {enumeratorType.FullName}.InitInternal has no safely analyzable IL body.");
                    return Array.Empty<string>();
                }
                var observations = 0;
                for (var i = 0; i < analysis.Instructions.Count; i++)
                {
                    var instruction = analysis.Instructions[i];
                    var state = analysis.States[i];
                    if (state == null)
                    {
                        continue;
                    }
                    if (instruction.Code == OpCodes.Ret ||
                        (IsOptionTextCall(instruction, field) && state.Peek(0).Kind == ValueKind.This))
                    {
                        observations++;
                        var values = GetArrayStrings(state.OptionValue, state);
                        if (values == null)
                        {
                            diagnostics.Add($"Unknown custom option values: {Source(method, instruction.Offset)} has no proven current literal OptionValues array; dynamic writes or opaque calls require review.");
                            continue;
                        }
                        foreach (var text in values)
                        {
                            result.Add(text);
                        }
                    }
                }
                if (observations == 0)
                {
                    diagnostics.Add($"Unknown custom option values: {Source(method, 0)} has no inspectable option text use or normal exit; helper methods are not executed.");
                }
            }
            catch (Exception exception) when (IsInspectionFailure(exception))
            {
                diagnostics.Add($"Cannot inspect custom option values for {enumeratorType.FullName}: {exception.Message}");
            }
            var sorted = new List<string>(result);
            sorted.Sort(StringComparer.Ordinal);
            return sorted.ToArray();
        }

        /// <summary>Recognizes the value-text initialization hook declared by the option field's owner.</summary>
        /// <param name="instruction">The decoded instruction.</param>
        /// <param name="field">The inherited option storage field.</param>
        /// <returns>Whether the instruction invokes the parameterless instance localization hook.</returns>
        private static bool IsOptionTextCall(Instruction instruction, FieldInfo field)
        {
            return (instruction.Code == OpCodes.Call || instruction.Code == OpCodes.Callvirt) &&
                instruction.Operand is MethodInfo method && !method.IsStatic &&
                method.DeclaringType == field.DeclaringType && method.Name == "InitValueTexts" &&
                method.ReturnType == typeof(void) && method.GetParameters().Length == 0;
        }

        /// <summary>Finds the inherited instance field used by option enumerators.</summary>
        /// <param name="type">The enumerator type.</param>
        /// <returns>The matching field, or null when it cannot contain a string array.</returns>
        private static FieldInfo? FindOptionField(Type type)
        {
            for (Type? current = type; current != null; current = current.BaseType)
            {
                var field = current.GetField("OptionValues", DeclaredMembers);
                if (field != null && !field.IsStatic &&
                    (field.FieldType == typeof(object[]) || field.FieldType == typeof(string[])))
                {
                    return field;
                }
            }
            return null;
        }

        /// <summary>Retains loadable types when a dependency prevents full type enumeration.</summary>
        /// <param name="assembly">The assembly to inspect.</param>
        /// <param name="diagnostics">Receives type loading diagnostics.</param>
        /// <returns>The loadable types, including nested state machine types.</returns>
        private static IEnumerable<Type> GetTypes(Assembly assembly, IList<string> diagnostics)
        {
            Type?[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types;
                foreach (var failure in exception.LoaderExceptions)
                {
                    if (failure != null)
                    {
                        diagnostics.Add($"Assembly type loading: {failure.Message}");
                    }
                }
            }
            foreach (var type in types)
            {
                if (type != null)
                {
                    yield return type;
                }
            }
        }

        /// <summary>Analyzes a candidate method and publishes only its converged incoming call arguments.</summary>
        /// <param name="method">The declared method or constructor.</param>
        /// <param name="context">The shared reflection and readonly-field analysis context.</param>
        /// <param name="keys">Receives proven keys and their sources.</param>
        private static void CollectMethod(MethodBase method, AnalysisContext context,
            IDictionary<string, HashSet<string>> keys)
        {
            var analysis = context.TryAnalyze(method, null, null, true);
            if (analysis == null)
            {
                return;
            }
            for (var i = 0; i < analysis.Instructions.Count; i++)
            {
                var instruction = analysis.Instructions[i];
                var state = analysis.States[i];
                if (state == null || !IsLocalizationCall(instruction))
                {
                    continue;
                }
                var called = (MethodInfo)instruction.Operand!;
                var key = state.Peek(called.GetParameters().Length - 1);
                var source = Source(method, instruction.Offset);
                if (key.Kind != ValueKind.Strings)
                {
                    context.Diagnostics.Add($"Unresolved localization key: {source} -> {called.DeclaringType!.FullName}.{called.Name}; argument is dynamic or exceeds analysis limits.");
                    continue;
                }
                foreach (var text in key.Strings)
                {
                    if (!keys.TryGetValue(text, out var sources))
                    {
                        sources = new HashSet<string>(StringComparer.Ordinal);
                        keys.Add(text, sources);
                    }
                    sources.Add(source);
                }
            }
        }

        /// <summary>Matches only the actual localization entry points and their complete signatures.</summary>
        /// <param name="instruction">A decoded instruction.</param>
        /// <returns>Whether it calls a supported localization method.</returns>
        private static bool IsLocalizationCall(Instruction instruction)
        {
            if ((instruction.Code != OpCodes.Call && instruction.Code != OpCodes.Callvirt) ||
                instruction.Operand is not MethodInfo method || !method.IsStatic || method.IsGenericMethod)
            {
                return false;
            }
            var parameters = method.GetParameters();
            if (parameters.Length != 2 || parameters[0].ParameterType != typeof(string))
            {
                return false;
            }
            var stringOut = parameters[1].ParameterType == typeof(string).MakeByRefType() && parameters[1].IsOut;
            return (method.DeclaringType?.FullName == "MajdataPlay.StringExtensions" &&
                ((method.Name == "i18n" && method.ReturnType == typeof(string) &&
                    parameters[1].ParameterType == typeof(object[])) ||
                (method.Name == "Tryi18n" && method.ReturnType == typeof(bool) && stringOut))) ||
                (method.DeclaringType?.FullName == "MajdataPlay.i18n.Localization" &&
                method.Name == "TryGetLocalizedText" && method.ReturnType == typeof(bool) && stringOut);
        }

        /// <summary>Formats a readable declaring type, method, and IL offset.</summary>
        /// <param name="method">The source method.</param>
        /// <param name="offset">The zero-based IL byte offset.</param>
        /// <returns>A location suitable for display in the editor.</returns>
        private static string Source(MethodBase method, int offset)
        {
            return $"{method.DeclaringType?.FullName ?? "<global>"}.{method.Name} IL_{offset:X4}";
        }

        /// <summary>Restricts recovery to reflection, metadata, and analysis failures.</summary>
        /// <param name="exception">The caught exception.</param>
        /// <returns>Whether inspection can continue with other methods.</returns>
        private static bool IsInspectionFailure(Exception exception)
        {
            return exception is ArgumentException || exception is InvalidOperationException ||
                exception is NotSupportedException || exception is TypeLoadException ||
                exception is System.IO.IOException || exception is BadImageFormatException ||
                exception is MemberAccessException || exception is System.Security.SecurityException;
        }

        /// <summary>Extracts only fully known string array contents.</summary>
        /// <param name="value">The abstract array reference.</param>
        /// <param name="state">The array heap at the instruction.</param>
        /// <returns>All finite literal elements, or null if any element is unproven.</returns>
        private static List<string>? GetArrayStrings(Value value, State state)
        {
            if (value.Kind != ValueKind.Array)
            {
                return null;
            }
            var result = new List<string>();
            foreach (var site in value.ArraySites)
            {
                if (!state.Arrays.TryGetValue(site, out var array) || array.Elements == null)
                {
                    return null;
                }
                foreach (var element in array.Elements)
                {
                    if (element.Kind != ValueKind.Strings)
                    {
                        return null;
                    }
                    result.AddRange(element.Strings);
                }
            }
            return result;
        }

        /// <summary>The kinds of values whose identity or contents can be proven from IL.</summary>
        private enum ValueKind
        {
            /// <summary>A dynamic value or widened finite set.</summary>
            Unknown,
            /// <summary>A finite ordinal set of literal strings.</summary>
            Strings,
            /// <summary>A single 32-bit integer constant.</summary>
            Integer,
            /// <summary>The null reference.</summary>
            Null,
            /// <summary>A method-local array allocation site.</summary>
            Array,
            /// <summary>An address of a local variable.</summary>
            LocalAddress,
            /// <summary>An address of an argument slot.</summary>
            ArgumentAddress,
            /// <summary>The current instance, not an arbitrary object of the same type.</summary>
            This
        }

        /// <summary>An immutable stack or local value in the finite abstract domain.</summary>
        private sealed class Value
        {
            /// <summary>The top of the abstract domain.</summary>
            internal static Value Unknown { get; } = new Value(ValueKind.Unknown);
            /// <summary>The null literal.</summary>
            internal static Value Null { get; } = new Value(ValueKind.Null);
            /// <summary>The instance argument.</summary>
            internal static Value This { get; } = new Value(ValueKind.This);
            /// <summary>The value category.</summary>
            internal ValueKind Kind { get; }
            /// <summary>An integer, slot index, or allocation offset.</summary>
            internal int Number { get; }
            /// <summary>The sorted literal set; empty for non-string values.</summary>
            internal string[] Strings { get; }
            /// <summary>The bounded array allocation sites represented by a reference.</summary>
            internal int[] ArraySites { get; }

            /// <summary>Creates an immutable abstract value.</summary>
            /// <param name="kind">The value category.</param>
            /// <param name="number">An optional constant or identity.</param>
            /// <param name="strings">An optional sorted ordinal literal set.</param>
            /// <param name="arraySites">Optional sorted allocation identities for a merged array reference.</param>
            internal Value(ValueKind kind, int number = 0, string[]? strings = null, int[]? arraySites = null)
            {
                Kind = kind;
                Number = kind == ValueKind.Array ? 0 : number;
                Strings = strings ?? Array.Empty<string>();
                ArraySites = arraySites ?? (kind == ValueKind.Array ? new[] { number } : Array.Empty<int>());
            }

            /// <summary>Compares values structurally without executing inspected code.</summary>
            /// <param name="other">The value to compare.</param>
            /// <returns>Whether the abstract values are identical.</returns>
            internal bool SameAs(Value other)
            {
                if (Kind != other.Kind || Number != other.Number || Strings.Length != other.Strings.Length ||
                    ArraySites.Length != other.ArraySites.Length)
                {
                    return false;
                }
                for (var i = 0; i < Strings.Length; i++)
                {
                    if (!StringComparer.Ordinal.Equals(Strings[i], other.Strings[i]))
                    {
                        return false;
                    }
                }
                for (var i = 0; i < ArraySites.Length; i++)
                {
                    if (ArraySites[i] != other.ArraySites[i])
                    {
                        return false;
                    }
                }
                return true;
            }

            /// <summary>Joins possible values at a control-flow merge, widening oversized sets.</summary>
            /// <param name="other">The value on another incoming path.</param>
            /// <returns>The conservative union, or unknown for incompatible kinds.</returns>
            internal Value Merge(Value other)
            {
                if (SameAs(other))
                {
                    return this;
                }
                if (Kind == ValueKind.Array && other.Kind == ValueKind.Array)
                {
                    var sites = new HashSet<int>(ArraySites);
                    sites.UnionWith(other.ArraySites);
                    if (sites.Count > MaxStrings)
                    {
                        return Unknown;
                    }
                    var sorted = new List<int>(sites);
                    sorted.Sort();
                    return new Value(ValueKind.Array, arraySites: sorted.ToArray());
                }
                if (Kind != ValueKind.Strings || other.Kind != ValueKind.Strings)
                {
                    return Unknown;
                }
                var strings = new HashSet<string>(Strings, StringComparer.Ordinal);
                strings.UnionWith(other.Strings);
                return FromStrings(strings);
            }

            /// <summary>Builds a bounded, sorted literal set.</summary>
            /// <param name="strings">The candidate literals.</param>
            /// <returns>A finite string value, or unknown if its set is too large.</returns>
            internal static Value FromStrings(HashSet<string> strings)
            {
                if (strings.Count > MaxStrings)
                {
                    return Unknown;
                }
                var sorted = new List<string>(strings);
                foreach (var text in sorted)
                {
                    if (text.Length > MaxStringLength)
                    {
                        return Unknown;
                    }
                }
                sorted.Sort(StringComparer.Ordinal);
                return new Value(ValueKind.Strings, strings: sorted.ToArray());
            }
        }

        /// <summary>A small tracked array whose unknown contents invalidate the entire allocation.</summary>
        private sealed class ArrayValue
        {
            /// <summary>Known elements, or null for an escaped or unbounded allocation.</summary>
            internal Value[]? Elements { get; set; }

            /// <summary>Creates an array state.</summary>
            /// <param name="elements">The known elements, or null when unknown.</param>
            internal ArrayValue(Value[]? elements)
            {
                Elements = elements;
            }

            /// <summary>Copies the mutable element slots.</summary>
            /// <returns>An independent heap entry.</returns>
            internal ArrayValue Clone()
            {
                return new ArrayValue(Elements == null ? null : (Value[])Elements.Clone());
            }

            /// <summary>Joins heap contents from another incoming path.</summary>
            /// <param name="other">The other allocation state, or null when absent.</param>
            /// <returns>Whether any element became less precise.</returns>
            internal bool Merge(ArrayValue? other)
            {
                if (Elements == null)
                {
                    return false;
                }
                if (other?.Elements == null || Elements.Length != other.Elements.Length)
                {
                    Elements = null;
                    return true;
                }
                var changed = false;
                for (var i = 0; i < Elements.Length; i++)
                {
                    var merged = Elements[i].Merge(other.Elements[i]);
                    changed |= !Elements[i].SameAs(merged);
                    Elements[i] = merged;
                }
                return changed;
            }
        }

        /// <summary>The incoming evaluation stack, locals, arguments, and bounded local heap.</summary>
        private sealed class State
        {
            /// <summary>The evaluation stack, in push order.</summary>
            internal List<Value> Stack { get; } = new List<Value>();
            /// <summary>The current local values.</summary>
            internal Value[] Locals { get; }
            /// <summary>The current argument slots, including this for instance methods.</summary>
            internal Value[] Arguments { get; }
            /// <summary>Tracked arrays keyed by allocation IL offset.</summary>
            internal Dictionary<int, ArrayValue> Arrays { get; } = new Dictionary<int, ArrayValue>();
            /// <summary>The last array assigned to this.OptionValues, when that field is tracked.</summary>
            internal Value OptionValue { get; set; } = Value.Unknown;
            /// <summary>The last value written to the readonly field being inspected.</summary>
            internal Value StaticValue { get; set; } = Value.Unknown;

            /// <summary>Creates an unknown entry state without reading runtime arguments or locals.</summary>
            /// <param name="method">The method that defines argument slots.</param>
            /// <param name="body">The method body that defines local slots.</param>
            internal State(MethodBase method, MethodBody body)
            {
                Locals = new Value[body.LocalVariables.Count];
                Arguments = new Value[method.GetParameters().Length + (method.IsStatic ? 0 : 1)];
                ResetSlots();
                if (!method.IsStatic)
                {
                    Arguments[0] = Value.This;
                }
            }

            /// <summary>Copies a dataflow state.</summary>
            /// <param name="source">The state to copy.</param>
            private State(State source)
            {
                Locals = (Value[])source.Locals.Clone();
                Arguments = (Value[])source.Arguments.Clone();
                Stack.AddRange(source.Stack);
                foreach (var pair in source.Arrays)
                {
                    Arrays.Add(pair.Key, pair.Value.Clone());
                }
                OptionValue = source.OptionValue;
                StaticValue = source.StaticValue;
            }

            /// <summary>Creates an independent state for an outgoing edge.</summary>
            /// <returns>A deep copy of the mutable heap and slots.</returns>
            internal State Clone()
            {
                return new State(this);
            }

            /// <summary>Reads a stack slot relative to its top.</summary>
            /// <param name="depth">Zero for the topmost slot.</param>
            /// <returns>The slot value.</returns>
            /// <exception cref="InvalidOperationException">The IL stack is inconsistent.</exception>
            internal Value Peek(int depth)
            {
                if (depth < 0 || depth >= Stack.Count)
                {
                    throw new InvalidOperationException("Inconsistent IL evaluation stack.");
                }
                return Stack[Stack.Count - 1 - depth];
            }

            /// <summary>Removes the topmost stack value.</summary>
            /// <returns>The popped abstract value.</returns>
            /// <exception cref="InvalidOperationException">The IL stack is empty.</exception>
            internal Value Pop()
            {
                var value = Peek(0);
                Stack.RemoveAt(Stack.Count - 1);
                return value;
            }

            /// <summary>Joins incoming values, rejecting invalid stack depth merges.</summary>
            /// <param name="other">Another state reaching the same instruction.</param>
            /// <returns>Whether the fixed point changed.</returns>
            /// <exception cref="InvalidOperationException">Incoming stack depths differ.</exception>
            internal bool Merge(State other)
            {
                if (Stack.Count != other.Stack.Count)
                {
                    throw new InvalidOperationException("Inconsistent stack depths at an IL control-flow merge.");
                }
                var changed = MergeSlots(Locals, other.Locals) | MergeSlots(Arguments, other.Arguments);
                for (var i = 0; i < Stack.Count; i++)
                {
                    var merged = Stack[i].Merge(other.Stack[i]);
                    changed |= !Stack[i].SameAs(merged);
                    Stack[i] = merged;
                }
                foreach (var pair in Arrays)
                {
                    if (other.Arrays.TryGetValue(pair.Key, out var incoming))
                    {
                        changed |= pair.Value.Merge(incoming);
                    }
                }
                foreach (var pair in other.Arrays)
                {
                    if (!Arrays.ContainsKey(pair.Key))
                    {
                        Arrays.Add(pair.Key, pair.Value.Clone());
                        changed = true;
                    }
                }
                var option = OptionValue.Merge(other.OptionValue);
                var field = StaticValue.Merge(other.StaticValue);
                changed |= !OptionValue.SameAs(option) || !StaticValue.SameAs(field);
                OptionValue = option;
                StaticValue = field;
                return changed;
            }

            /// <summary>Joins corresponding argument or local slots.</summary>
            /// <param name="target">The existing slots.</param>
            /// <param name="incoming">The other incoming slots.</param>
            /// <returns>Whether any slot changed.</returns>
            private static bool MergeSlots(Value[] target, Value[] incoming)
            {
                var changed = false;
                for (var i = 0; i < target.Length; i++)
                {
                    var merged = target[i].Merge(incoming[i]);
                    changed |= !target[i].SameAs(merged);
                    target[i] = merged;
                }
                return changed;
            }

            /// <summary>Invalidates values that an unmodeled finally block can change.</summary>
            internal void InvalidateContinuation()
            {
                var receiver = Arguments.Length > 0 && Arguments[0].Kind == ValueKind.This;
                ResetSlots();
                if (receiver)
                {
                    Arguments[0] = Value.This;
                }
                foreach (var array in Arrays.Values)
                {
                    array.Elements = null;
                }
                OptionValue = Value.Unknown;
                StaticValue = Value.Unknown;
            }

            /// <summary>Sets argument and local slots to unknown.</summary>
            private void ResetSlots()
            {
                for (var i = 0; i < Locals.Length; i++)
                {
                    Locals[i] = Value.Unknown;
                }
                for (var i = 0; i < Arguments.Length; i++)
                {
                    Arguments[i] = Value.Unknown;
                }
            }

            /// <summary>Invalidates memory when an array or address escapes to opaque code.</summary>
            /// <param name="value">The escaping value.</param>
            internal void Escape(Value value)
            {
                if (value.Kind == ValueKind.LocalAddress)
                {
                    var previous = Locals[value.Number];
                    Locals[value.Number] = Value.Unknown;
                    if (previous.Kind == ValueKind.Array)
                    {
                        Escape(previous);
                    }
                }
                else if (value.Kind == ValueKind.ArgumentAddress)
                {
                    var previous = Arguments[value.Number];
                    Arguments[value.Number] = Value.Unknown;
                    if (previous.Kind == ValueKind.Array)
                    {
                        Escape(previous);
                    }
                }
                else if (value.Kind == ValueKind.This)
                {
                    var previous = OptionValue;
                    OptionValue = Value.Unknown;
                    if (previous.Kind == ValueKind.Array)
                    {
                        Escape(previous);
                    }
                }
                else if (value.Kind == ValueKind.Array)
                {
                    foreach (var site in value.ArraySites)
                    {
                        if (Arrays.TryGetValue(site, out var array))
                        {
                            array.Elements = null;
                        }
                    }
                }
            }
        }

        /// <summary>A resolved IL instruction with its original byte offset.</summary>
        private sealed class Instruction
        {
            /// <summary>The original byte offset.</summary>
            internal int Offset { get; }
            /// <summary>The decoded opcode.</summary>
            internal OpCode Code { get; }
            /// <summary>A resolved member, constant, slot index, or branch destination.</summary>
            internal object? Operand { get; }

            /// <summary>Creates a decoded instruction.</summary>
            /// <param name="offset">The IL byte offset.</param>
            /// <param name="code">The decoded opcode.</param>
            /// <param name="operand">The decoded or metadata-resolved operand.</param>
            internal Instruction(int offset, OpCode code, object? operand)
            {
                Offset = offset;
                Code = code;
                Operand = operand;
            }
        }

        /// <summary>A method's decoded instructions and converged incoming states.</summary>
        private sealed class Analysis
        {
            /// <summary>The ordered instruction list.</summary>
            internal List<Instruction> Instructions { get; }
            /// <summary>The incoming state at each instruction; null means unreachable.</summary>
            internal State?[] States { get; }

            /// <summary>Creates an analysis result.</summary>
            /// <param name="instructions">The decoded instructions.</param>
            /// <param name="states">The converged dataflow states.</param>
            internal Analysis(List<Instruction> instructions, State?[] states)
            {
                Instructions = instructions;
                States = states;
            }
        }

        /// <summary>Shares diagnostics and conservatively decoded readonly string fields across methods.</summary>
        private sealed class AnalysisContext
        {
            /// <summary>The diagnostic destination supplied by the caller.</summary>
            internal IList<string> Diagnostics { get; }
            /// <summary>Cached metadata-only constant and initializer results.</summary>
            private readonly Dictionary<FieldInfo, Value> _fieldValues = new Dictionary<FieldInfo, Value>();
            /// <summary>Fields currently being decoded; prevents circular initializer recursion.</summary>
            private readonly HashSet<FieldInfo> _readingFields = new HashSet<FieldInfo>();
            /// <summary>Initializer types currently inspected; prevents forward and circular initialization assumptions.</summary>
            private readonly HashSet<Type> _initializingTypes = new HashSet<Type>();

            /// <summary>Creates an inspection context.</summary>
            /// <param name="diagnostics">The caller's diagnostic destination.</param>
            internal AnalysisContext(IList<string> diagnostics)
            {
                Diagnostics = diagnostics;
            }

            /// <summary>Decodes and analyzes a body, recovering from unsupported metadata or IL.</summary>
            /// <param name="method">The body owner.</param>
            /// <param name="optionField">An optional instance field to track.</param>
            /// <param name="staticField">An optional readonly initializer field to track.</param>
            /// <param name="requireLocalization">Whether bodies without localization calls can be skipped.</param>
            /// <returns>The converged analysis, or null for absent, irrelevant, or unsupported bodies.</returns>
            internal Analysis? TryAnalyze(MethodBase method, FieldInfo? optionField,
                FieldInfo? staticField, bool requireLocalization)
            {
                try
                {
                    var body = method.GetMethodBody();
                    if (body == null)
                    {
                        return null;
                    }
                    var instructions = Decode(method, body);
                    if (requireLocalization && !instructions.Exists(IsLocalizationCall))
                    {
                        return null;
                    }
                    var initializerType = method is ConstructorInfo && method.IsStatic ? method.DeclaringType : null;
                    var enteredInitializer = initializerType != null && _initializingTypes.Add(initializerType);
                    try
                    {
                        return Analyze(method, body, instructions, optionField, staticField);
                    }
                    finally
                    {
                        if (enteredInitializer)
                        {
                            _initializingTypes.Remove(initializerType!);
                        }
                    }
                }
                catch (Exception exception) when (IsInspectionFailure(exception))
                {
                    Diagnostics.Add($"IL analysis skipped: {Source(method, 0)}: {exception.Message}");
                    return null;
                }
            }

            /// <summary>Reads metadata constants or decodes a readonly string's type initializer IL.</summary>
            /// <param name="field">The referenced field.</param>
            /// <returns>A proven literal value, or unknown; never uses FieldInfo.GetValue.</returns>
            internal Value ReadField(FieldInfo field)
            {
                if (!field.IsLiteral && field.DeclaringType != null && _initializingTypes.Contains(field.DeclaringType))
                {
                    return Value.Unknown;
                }
                if (_fieldValues.TryGetValue(field, out var cached))
                {
                    return cached;
                }
                if (field.IsLiteral)
                {
                    var constant = field.GetRawConstantValue();
                    var value = constant is string text ? new Value(ValueKind.Strings, strings: new[] { text }) :
                        constant is int number ? new Value(ValueKind.Integer, number) :
                        constant == null ? Value.Null : Value.Unknown;
                    _fieldValues[field] = value;
                    return value;
                }
                if (!field.IsStatic || !field.IsInitOnly || field.FieldType != typeof(string) ||
                    field.DeclaringType?.TypeInitializer == null || _readingFields.Count >= 8 ||
                    !_readingFields.Add(field))
                {
                    return Value.Unknown;
                }
                var result = Value.Unknown;
                try
                {
                    var analysis = TryAnalyze(field.DeclaringType.TypeInitializer, null, field, false);
                    Value? joined = null;
                    if (analysis != null)
                    {
                        for (var i = 0; i < analysis.Instructions.Count; i++)
                        {
                            var state = analysis.States[i];
                            if (analysis.Instructions[i].Code == OpCodes.Ret && state != null)
                            {
                                joined = joined == null ? state.StaticValue : joined.Merge(state.StaticValue);
                            }
                        }
                    }
                    result = joined ?? Value.Unknown;
                }
                finally
                {
                    _readingFields.Remove(field);
                }
                _fieldValues[field] = result;
                return result;
            }

            /// <summary>Computes a bounded fixed point over normal CFG edges and conservative handler roots.</summary>
            /// <param name="method">The body owner.</param>
            /// <param name="body">The reflected body and exception clauses.</param>
            /// <param name="instructions">The decoded instructions.</param>
            /// <param name="optionField">The optional instance field to track.</param>
            /// <param name="staticField">The optional readonly field to track.</param>
            /// <returns>The converged incoming states.</returns>
            /// <exception cref="InvalidOperationException">IL is inconsistent or the work budget is exhausted.</exception>
            private Analysis Analyze(MethodBase method, MethodBody body, List<Instruction> instructions,
                FieldInfo? optionField, FieldInfo? staticField)
            {
                var states = new State?[instructions.Count];
                if (instructions.Count == 0)
                {
                    return new Analysis(instructions, states);
                }
                var offsets = new Dictionary<int, int>();
                for (var i = 0; i < instructions.Count; i++)
                {
                    offsets.Add(instructions[i].Offset, i);
                }
                var pending = new Queue<int>();
                var queued = new bool[instructions.Count];
                Enqueue(0, new State(method, body), states, pending, queued);
                var hasFinally = false;
                foreach (var clause in body.ExceptionHandlingClauses)
                {
                    var handler = new State(method, body);
                    if (clause.Flags == ExceptionHandlingClauseOptions.Clause ||
                        clause.Flags == ExceptionHandlingClauseOptions.Filter)
                    {
                        handler.Stack.Add(Value.Unknown);
                    }
                    Enqueue(Target(clause.HandlerOffset, offsets), handler, states, pending, queued);
                    if (clause.Flags == ExceptionHandlingClauseOptions.Filter)
                    {
                        Enqueue(Target(clause.FilterOffset, offsets), handler, states, pending, queued);
                    }
                    hasFinally |= clause.Flags == ExceptionHandlingClauseOptions.Finally;
                }
                var visits = 0;
                while (pending.Count > 0)
                {
                    if (++visits > MaxVisits)
                    {
                        throw new InvalidOperationException("Control-flow analysis exceeded its bounded work budget.");
                    }
                    var index = pending.Dequeue();
                    queued[index] = false;
                    var instruction = instructions[index];
                    var input = states[index]!;
                    var output = input.Clone();
                    Transfer(method, instruction, output, optionField, staticField);
                    var flow = instruction.Code.FlowControl;
                    if (flow == FlowControl.Return || flow == FlowControl.Throw || instruction.Code == OpCodes.Jmp)
                    {
                        continue;
                    }
                    if (instruction.Code == OpCodes.Leave || instruction.Code == OpCodes.Leave_S)
                    {
                        output.Stack.Clear();
                        if (hasFinally)
                        {
                            output.InvalidateContinuation();
                        }
                    }
                    if (instruction.Code == OpCodes.Switch)
                    {
                        var destinations = (int[])instruction.Operand!;
                        var selector = input.Peek(0);
                        if (selector.Kind == ValueKind.Integer)
                        {
                            if (selector.Number >= 0 && selector.Number < destinations.Length)
                            {
                                Enqueue(Target(destinations[selector.Number], offsets), output, states, pending, queued);
                                continue;
                            }
                        }
                        else
                        {
                            foreach (var destination in destinations)
                            {
                                Enqueue(Target(destination, offsets), output, states, pending, queued);
                            }
                        }
                    }
                    else if (flow == FlowControl.Branch || flow == FlowControl.Cond_Branch)
                    {
                        var taken = BranchTaken(instruction, input);
                        if (taken != false)
                        {
                            Enqueue(Target((int)instruction.Operand!, offsets), output, states, pending, queued);
                        }
                        if (flow == FlowControl.Branch || taken == true)
                        {
                            continue;
                        }
                    }
                    if (index + 1 < instructions.Count)
                    {
                        Enqueue(index + 1, output, states, pending, queued);
                    }
                }
                return new Analysis(instructions, states);
            }

            /// <summary>Models stack effects and the limited side effects needed for literal analysis.</summary>
            /// <param name="owner">The method whose IL is being interpreted.</param>
            /// <param name="instruction">The instruction to interpret.</param>
            /// <param name="state">The mutable outgoing state.</param>
            /// <param name="optionField">The optional tracked option field.</param>
            /// <param name="staticField">The optional tracked readonly field.</param>
            /// <exception cref="InvalidOperationException">IL has an unsupported variable stack effect.</exception>
            private void Transfer(MethodBase owner, Instruction instruction, State state, FieldInfo? optionField, FieldInfo? staticField)
            {
                var code = instruction.Code;
                var operand = instruction.Operand;
                if (code == OpCodes.Ldstr)
                {
                    state.Stack.Add(new Value(ValueKind.Strings, strings: new[] { (string)operand! }));
                }
                else if (code == OpCodes.Ldnull)
                {
                    state.Stack.Add(Value.Null);
                }
                else if (code == OpCodes.Dup)
                {
                    state.Stack.Add(state.Peek(0));
                }
                else if (code == OpCodes.Pop)
                {
                    state.Pop();
                }
                else if (TrySlot(code, operand, true, out var local, out var store, out var address))
                {
                    if (store)
                    {
                        state.Locals[local] = state.Pop();
                    }
                    else
                    {
                        state.Stack.Add(address ? new Value(ValueKind.LocalAddress, local) : state.Locals[local]);
                    }
                }
                else if (TrySlot(code, operand, false, out var argument, out store, out address))
                {
                    if (store)
                    {
                        state.Arguments[argument] = state.Pop();
                    }
                    else
                    {
                        state.Stack.Add(address ? new Value(ValueKind.ArgumentAddress, argument) : state.Arguments[argument]);
                    }
                }
                else if (TryInteger(code, operand, out var integer))
                {
                    state.Stack.Add(new Value(ValueKind.Integer, integer));
                }
                else if (code == OpCodes.Call || code == OpCodes.Callvirt || code == OpCodes.Newobj)
                {
                    TransferCall(instruction, state);
                }
                else if (code == OpCodes.Ldsfld)
                {
                    var field = (FieldInfo)operand!;
                    // A final initializer value is not the value observed during forward initialization.
                    state.Stack.Add(owner is ConstructorInfo && owner.IsStatic && field.DeclaringType == owner.DeclaringType ?
                        Equals(field, staticField) ? state.StaticValue : Value.Unknown : ReadField(field));
                }
                else if (code == OpCodes.Ldfld)
                {
                    var receiver = state.Pop();
                    state.Stack.Add(Equals(operand, optionField) && receiver.Kind == ValueKind.This ?
                        state.OptionValue : Value.Unknown);
                }
                else if (code == OpCodes.Stfld || code == OpCodes.Stsfld)
                {
                    var value = state.Pop();
                    if (code == OpCodes.Stfld)
                    {
                        var receiver = state.Pop();
                        if (Equals(operand, optionField) && receiver.Kind == ValueKind.This)
                        {
                            // This tracked store does not escape until the instance or array reaches opaque code.
                            state.OptionValue = value;
                            return;
                        }
                    }
                    else if (Equals(operand, staticField))
                    {
                        state.StaticValue = value;
                    }
                    state.Escape(value);
                }
                else if (code == OpCodes.Newarr)
                {
                    var length = state.Pop();
                    AllocateArray(instruction.Offset, length, (Type)operand!, state);
                }
                else if (code == OpCodes.Ldlen)
                {
                    var reference = state.Pop();
                    state.Stack.Add(ReadArray(reference, null, state));
                }
                else if (code == OpCodes.Stelem_Ref)
                {
                    var value = state.Pop();
                    var index = state.Pop();
                    var reference = state.Pop();
                    WriteArray(reference, index, value, state);
                    state.Escape(value);
                }
                else if (code == OpCodes.Ldelem_Ref)
                {
                    var index = state.Pop();
                    var reference = state.Pop();
                    state.Stack.Add(ReadArray(reference, index, state));
                }
                else if (code == OpCodes.Ldind_Ref || code == OpCodes.Ldobj)
                {
                    var reference = state.Pop();
                    state.Stack.Add(reference.Kind == ValueKind.LocalAddress ? state.Locals[reference.Number] :
                        reference.Kind == ValueKind.ArgumentAddress ? state.Arguments[reference.Number] : Value.Unknown);
                }
                else if (code == OpCodes.Stind_Ref || code == OpCodes.Stobj)
                {
                    var value = state.Pop();
                    var reference = state.Pop();
                    if (reference.Kind == ValueKind.LocalAddress)
                    {
                        state.Locals[reference.Number] = value;
                    }
                    else if (reference.Kind == ValueKind.ArgumentAddress)
                    {
                        state.Arguments[reference.Number] = value;
                    }
                    else
                    {
                        state.Escape(value);
                    }
                }
                else if (code == OpCodes.Castclass)
                {
                    var value = state.Pop();
                    // Successful reference casts preserve identity, including aliases of this and arrays.
                    state.Stack.Add(value.Kind == ValueKind.Array || value.Kind == ValueKind.Strings ||
                        value.Kind == ValueKind.This || value.Kind == ValueKind.Null ? value : Value.Unknown);
                }
                else if (code != OpCodes.Ret && code != OpCodes.Jmp)
                {
                    var pops = StackCount(code.StackBehaviourPop);
                    var pushes = StackCount(code.StackBehaviourPush);
                    for (var i = 0; i < pops; i++)
                    {
                        state.Escape(state.Pop());
                    }
                    for (var i = 0; i < pushes; i++)
                    {
                        state.Stack.Add(Value.Unknown);
                    }
                }
            }

            /// <summary>Consumes complete call arguments and models only safe framework string operations.</summary>
            /// <param name="instruction">The call or object construction instruction.</param>
            /// <param name="state">The outgoing state.</param>
            private void TransferCall(Instruction instruction, State state)
            {
                var method = (MethodBase)instruction.Operand!;
                var parameters = method.GetParameters();
                var arguments = new Value[parameters.Length];
                for (var i = arguments.Length - 1; i >= 0; i--)
                {
                    arguments[i] = state.Pop();
                }
                if (!method.IsStatic && instruction.Code != OpCodes.Newobj)
                {
                    state.Escape(state.Pop());
                }
                var result = Value.Unknown;
                var safe = false;
                if (method is MethodInfo called && called.DeclaringType == typeof(string) &&
                    called.Name == nameof(string.Concat) && called.IsStatic)
                {
                    result = Concat(arguments, parameters, state);
                    safe = result.Kind == ValueKind.Strings;
                }
                else if (method is MethodInfo empty && empty.DeclaringType == typeof(Array) &&
                    empty.Name == nameof(Array.Empty) && empty.IsGenericMethod && parameters.Length == 0)
                {
                    AllocateArray(instruction.Offset, new Value(ValueKind.Integer, 0),
                        empty.GetGenericArguments()[0], state);
                    return;
                }
                if (!safe)
                {
                    foreach (var argument in arguments)
                    {
                        state.Escape(argument);
                    }
                }
                if (instruction.Code == OpCodes.Newobj || (method is MethodInfo function && function.ReturnType != typeof(void)))
                {
                    state.Stack.Add(result);
                }
            }
        }

        /// <summary>Reads a known element or length across every possible array allocation.</summary>
        /// <param name="reference">The possible local array identities.</param>
        /// <param name="index">The element index, or null to read array lengths.</param>
        /// <param name="state">The current tracked array heap.</param>
        /// <returns>The joined value, or unknown if any possible allocation is unproven.</returns>
        private static Value ReadArray(Value reference, Value? index, State state)
        {
            if (reference.Kind != ValueKind.Array || (index != null && index.Kind != ValueKind.Integer))
            {
                return Value.Unknown;
            }
            Value? joined = null;
            foreach (var site in reference.ArraySites)
            {
                if (!state.Arrays.TryGetValue(site, out var array) || array.Elements == null ||
                    (index != null && (index.Number < 0 || index.Number >= array.Elements.Length)))
                {
                    return Value.Unknown;
                }
                var value = index == null ? new Value(ValueKind.Integer, array.Elements.Length) : array.Elements[index.Number];
                joined = joined == null ? value : joined.Merge(value);
            }
            return joined ?? Value.Unknown;
        }

        /// <summary>Updates a definite element or invalidates ambiguous target allocations.</summary>
        /// <param name="reference">The possible target allocations.</param>
        /// <param name="index">The abstract element index.</param>
        /// <param name="value">The stored value.</param>
        /// <param name="state">The heap to update.</param>
        private static void WriteArray(Value reference, Value index, Value value, State state)
        {
            if (reference.Kind != ValueKind.Array)
            {
                return;
            }
            foreach (var site in reference.ArraySites)
            {
                if (!state.Arrays.TryGetValue(site, out var array))
                {
                    continue;
                }
                if (reference.ArraySites.Length != 1 || array.Elements == null || index.Kind != ValueKind.Integer ||
                    index.Number < 0 || index.Number >= array.Elements.Length)
                {
                    array.Elements = null;
                }
                else
                {
                    array.Elements[index.Number] = value;
                }
            }
        }

        /// <summary>Creates a bounded tracked reference array, invalidating repeated allocation sites.</summary>
        /// <param name="offset">The allocation site.</param>
        /// <param name="length">The abstract requested length.</param>
        /// <param name="elementType">The declared array element type.</param>
        /// <param name="state">The outgoing state.</param>
        private static void AllocateArray(int offset, Value length, Type elementType, State state)
        {
            if (state.Arrays.Count >= MaxArrays || length.Kind != ValueKind.Integer ||
                length.Number < 0 || length.Number > MaxArrayLength ||
                (elementType != typeof(string) && elementType != typeof(object)))
            {
                state.Stack.Add(Value.Unknown);
                return;
            }
            if (state.Arrays.ContainsKey(offset) && length.Number != 0)
            {
                state.Arrays[offset].Elements = null;
                state.Stack.Add(Value.Unknown);
                return;
            }
            var elements = new Value[length.Number];
            for (var i = 0; i < elements.Length; i++)
            {
                elements[i] = Value.Null;
            }
            state.Arrays[offset] = new ArrayValue(elements);
            state.Stack.Add(new Value(ValueKind.Array, offset));
        }

        /// <summary>Models string and object Concat overloads only when all inputs are strings or null.</summary>
        /// <param name="arguments">The arguments in declaration order.</param>
        /// <param name="parameters">The exact overload's parameter metadata.</param>
        /// <param name="state">The local heap used for params arrays.</param>
        /// <returns>The finite concatenated literals, or unknown for dynamic or enumerable inputs.</returns>
        private static Value Concat(Value[] arguments, ParameterInfo[] parameters, State state)
        {
            var pieces = arguments;
            if (parameters.Length == 1 && (parameters[0].ParameterType == typeof(string[]) ||
                parameters[0].ParameterType == typeof(object[])))
            {
                var reference = arguments[0];
                if (reference.Kind != ValueKind.Array)
                {
                    return Value.Unknown;
                }
                Value? joined = null;
                foreach (var site in reference.ArraySites)
                {
                    if (!state.Arrays.TryGetValue(site, out var array) || array.Elements == null)
                    {
                        return Value.Unknown;
                    }
                    var value = ConcatPieces(array.Elements);
                    joined = joined == null ? value : joined.Merge(value);
                }
                return joined ?? Value.Unknown;
            }
            else
            {
                foreach (var parameter in parameters)
                {
                    if (parameter.ParameterType != typeof(string) && parameter.ParameterType != typeof(object))
                    {
                        return Value.Unknown;
                    }
                }
            }
            return ConcatPieces(pieces);
        }

        /// <summary>Concatenates finite pieces without inventing cross-products of correlated branch sets.</summary>
        /// <param name="pieces">The constant or finite literal pieces.</param>
        /// <returns>The concatenated literals, or unknown for ambiguous correlations or oversized strings.</returns>
        private static Value ConcatPieces(Value[] pieces)
        {
            var varying = 0;
            foreach (var piece in pieces)
            {
                if (piece.Kind == ValueKind.Strings && piece.Strings.Length > 1 && ++varying > 1)
                {
                    // Path-insensitive joins cannot prove that these alternatives vary independently.
                    return Value.Unknown;
                }
            }
            var combinations = new HashSet<string>(StringComparer.Ordinal) { string.Empty };
            foreach (var piece in pieces)
            {
                if (piece.Kind == ValueKind.Null)
                {
                    continue;
                }
                if (piece.Kind != ValueKind.Strings)
                {
                    return Value.Unknown;
                }
                var next = new HashSet<string>(StringComparer.Ordinal);
                foreach (var prefix in combinations)
                {
                    foreach (var suffix in piece.Strings)
                    {
                        if (prefix.Length > MaxStringLength - suffix.Length)
                        {
                            return Value.Unknown;
                        }
                        next.Add(prefix + suffix);
                        if (next.Count > MaxStrings)
                        {
                            return Value.Unknown;
                        }
                    }
                }
                combinations = next;
            }
            return Value.FromStrings(combinations);
        }

        /// <summary>Updates a CFG node and schedules it only when the joined state changes.</summary>
        /// <param name="index">The target instruction index.</param>
        /// <param name="incoming">The incoming edge state.</param>
        /// <param name="states">The node states.</param>
        /// <param name="pending">The work queue.</param>
        /// <param name="queued">The queue membership flags.</param>
        private static void Enqueue(int index, State incoming, State?[] states, Queue<int> pending, bool[] queued)
        {
            var changed = states[index] == null;
            if (changed)
            {
                states[index] = incoming.Clone();
            }
            else
            {
                changed = states[index]!.Merge(incoming);
            }
            if (changed && !queued[index])
            {
                queued[index] = true;
                pending.Enqueue(index);
            }
        }

        /// <summary>Resolves a branch destination to an instruction boundary.</summary>
        /// <param name="offset">The destination IL byte offset.</param>
        /// <param name="offsets">The decoded instruction index map.</param>
        /// <returns>The destination instruction index.</returns>
        /// <exception cref="InvalidOperationException">The destination is not a valid instruction boundary.</exception>
        private static int Target(int offset, Dictionary<int, int> offsets)
        {
            if (!offsets.TryGetValue(offset, out var index))
            {
                throw new InvalidOperationException($"Invalid IL branch target IL_{offset:X4}.");
            }
            return index;
        }

        /// <summary>Prunes simple constant truth branches; other conditions retain both edges.</summary>
        /// <param name="instruction">The branch instruction.</param>
        /// <param name="state">Its incoming evaluation stack.</param>
        /// <returns>The proven branch decision, or null if it depends on runtime values.</returns>
        private static bool? BranchTaken(Instruction instruction, State state)
        {
            var code = instruction.Code;
            if (code != OpCodes.Brtrue && code != OpCodes.Brtrue_S &&
                code != OpCodes.Brfalse && code != OpCodes.Brfalse_S)
            {
                return null;
            }
            var value = state.Peek(0);
            bool? truth = value.Kind == ValueKind.Integer ? value.Number != 0 :
                value.Kind == ValueKind.Null ? false :
                value.Kind == ValueKind.Strings || value.Kind == ValueKind.Array || value.Kind == ValueKind.This ? true :
                (bool?)null;
            return code == OpCodes.Brfalse || code == OpCodes.Brfalse_S ? !truth : truth;
        }

        /// <summary>Recognizes the compact and general local/argument opcodes.</summary>
        /// <param name="code">The opcode.</param>
        /// <param name="operand">The optional slot index.</param>
        /// <param name="local">Whether to recognize locals rather than arguments.</param>
        /// <param name="index">Receives the slot index.</param>
        /// <param name="store">Receives whether the instruction stores into a slot.</param>
        /// <param name="address">Receives whether it takes a slot address.</param>
        /// <returns>Whether the instruction belongs to the requested slot family.</returns>
        private static bool TrySlot(OpCode code, object? operand, bool local,
            out int index, out bool store, out bool address)
        {
            index = 0;
            store = false;
            address = false;
            if (local)
            {
                if (code.Value >= OpCodes.Ldloc_0.Value && code.Value <= OpCodes.Ldloc_3.Value)
                {
                    index = code.Value - OpCodes.Ldloc_0.Value;
                    return true;
                }
                if (code.Value >= OpCodes.Stloc_0.Value && code.Value <= OpCodes.Stloc_3.Value)
                {
                    index = code.Value - OpCodes.Stloc_0.Value;
                    store = true;
                    return true;
                }
                store = code == OpCodes.Stloc || code == OpCodes.Stloc_S;
                address = code == OpCodes.Ldloca || code == OpCodes.Ldloca_S;
                if (store || address || code == OpCodes.Ldloc || code == OpCodes.Ldloc_S)
                {
                    index = (int)operand!;
                    return true;
                }
            }
            else
            {
                if (code.Value >= OpCodes.Ldarg_0.Value && code.Value <= OpCodes.Ldarg_3.Value)
                {
                    index = code.Value - OpCodes.Ldarg_0.Value;
                    return true;
                }
                store = code == OpCodes.Starg || code == OpCodes.Starg_S;
                address = code == OpCodes.Ldarga || code == OpCodes.Ldarga_S;
                if (store || address || code == OpCodes.Ldarg || code == OpCodes.Ldarg_S)
                {
                    index = (int)operand!;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Recognizes all 32-bit integer literal opcodes.</summary>
        /// <param name="code">The opcode.</param>
        /// <param name="operand">The encoded literal, if present.</param>
        /// <param name="number">Receives the literal integer.</param>
        /// <returns>Whether this is a supported integer load.</returns>
        private static bool TryInteger(OpCode code, object? operand, out int number)
        {
            number = 0;
            if (code.Value >= OpCodes.Ldc_I4_M1.Value && code.Value <= OpCodes.Ldc_I4_8.Value)
            {
                number = code.Value - OpCodes.Ldc_I4_0.Value;
                return true;
            }
            if (code == OpCodes.Ldc_I4 || code == OpCodes.Ldc_I4_S)
            {
                number = (int)operand!;
                return true;
            }
            return false;
        }

        /// <summary>Converts fixed opcode stack behavior to a slot count.</summary>
        /// <param name="behavior">The opcode's push or pop behavior.</param>
        /// <returns>The fixed number of stack slots.</returns>
        /// <exception cref="InvalidOperationException">A variable stack effect was not explicitly modeled.</exception>
        private static int StackCount(StackBehaviour behavior)
        {
            switch (behavior)
            {
                case StackBehaviour.Pop0:
                case StackBehaviour.Push0:
                    return 0;
                case StackBehaviour.Pop1:
                case StackBehaviour.Popi:
                case StackBehaviour.Popref:
                case StackBehaviour.Push1:
                case StackBehaviour.Pushi:
                case StackBehaviour.Pushi8:
                case StackBehaviour.Pushr4:
                case StackBehaviour.Pushr8:
                case StackBehaviour.Pushref:
                    return 1;
                case StackBehaviour.Pop1_pop1:
                case StackBehaviour.Popi_pop1:
                case StackBehaviour.Popi_popi:
                case StackBehaviour.Popi_popi8:
                case StackBehaviour.Popi_popr4:
                case StackBehaviour.Popi_popr8:
                case StackBehaviour.Popref_pop1:
                case StackBehaviour.Popref_popi:
                case StackBehaviour.Push1_push1:
                    return 2;
                case StackBehaviour.Popi_popi_popi:
                case StackBehaviour.Popref_popi_pop1:
                case StackBehaviour.Popref_popi_popi:
                case StackBehaviour.Popref_popi_popi8:
                case StackBehaviour.Popref_popi_popr4:
                case StackBehaviour.Popref_popi_popr8:
                case StackBehaviour.Popref_popi_popref:
                    return 3;
                default:
                    throw new InvalidOperationException($"Unsupported variable stack behavior: {behavior}.");
            }
        }

        /// <summary>Decodes ECMA IL and resolves tokens with the owning generic type/method context.</summary>
        /// <param name="method">The metadata owner.</param>
        /// <param name="body">The reflected IL body.</param>
        /// <returns>The ordered instructions, including absolute branch destinations.</returns>
        /// <exception cref="BadImageFormatException">IL bytes are truncated or use an invalid opcode.</exception>
        /// <exception cref="InvalidOperationException">The method exceeds the instruction budget.</exception>
        private static List<Instruction> Decode(MethodBase method, MethodBody body)
        {
            var bytes = body.GetILAsByteArray() ?? Array.Empty<byte>();
            var instructions = new List<Instruction>();
            var typeArguments = method.DeclaringType?.GetGenericArguments();
            var methodArguments = method is MethodInfo info && info.IsGenericMethod ? info.GetGenericArguments() : null;
            var module = method.Module;
            var position = 0;
            while (position < bytes.Length)
            {
                if (instructions.Count >= MaxVisits)
                {
                    throw new InvalidOperationException("Method exceeds the bounded instruction budget.");
                }
                var offset = position;
                var encoded = (short)ReadByte(bytes, ref position);
                if (encoded == 0xFE)
                {
                    encoded = unchecked((short)(0xFE00 | ReadByte(bytes, ref position)));
                }
                if (!s_opCodes.TryGetValue(encoded, out var code))
                {
                    throw new BadImageFormatException($"Unknown opcode at IL_{offset:X4}.");
                }
                object? operand = null;
                switch (code.OperandType)
                {
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineI:
                        operand = (int)unchecked((sbyte)ReadByte(bytes, ref position));
                        break;
                    case OperandType.ShortInlineVar:
                        operand = (int)ReadByte(bytes, ref position);
                        break;
                    case OperandType.InlineVar:
                        operand = (int)(ReadByte(bytes, ref position) | (ReadByte(bytes, ref position) << 8));
                        break;
                    case OperandType.InlineI:
                    case OperandType.InlineSig:
                        operand = ReadInt32(bytes, ref position);
                        break;
                    case OperandType.InlineI8:
                        EnsureBytes(bytes, position, 8);
                        operand = BitConverter.ToInt64(bytes, position);
                        position += 8;
                        break;
                    case OperandType.ShortInlineR:
                        EnsureBytes(bytes, position, 4);
                        operand = BitConverter.ToSingle(bytes, position);
                        position += 4;
                        break;
                    case OperandType.InlineR:
                        EnsureBytes(bytes, position, 8);
                        operand = BitConverter.ToDouble(bytes, position);
                        position += 8;
                        break;
                    case OperandType.ShortInlineBrTarget:
                        var shortDelta = unchecked((sbyte)ReadByte(bytes, ref position));
                        operand = position + shortDelta;
                        break;
                    case OperandType.InlineBrTarget:
                        var delta = ReadInt32(bytes, ref position);
                        operand = position + delta;
                        break;
                    case OperandType.InlineSwitch:
                        var count = ReadInt32(bytes, ref position);
                        if (count < 0 || count > (bytes.Length - position) / 4)
                        {
                            throw new BadImageFormatException("Invalid IL switch table.");
                        }
                        var targets = new int[count];
                        var end = position + (count * 4);
                        for (var i = 0; i < count; i++)
                        {
                            targets[i] = end + ReadInt32(bytes, ref position);
                        }
                        operand = targets;
                        break;
                    case OperandType.InlineString:
                        operand = module.ResolveString(ReadInt32(bytes, ref position));
                        break;
                    case OperandType.InlineField:
                        operand = module.ResolveField(ReadInt32(bytes, ref position), typeArguments, methodArguments);
                        break;
                    case OperandType.InlineMethod:
                        operand = module.ResolveMethod(ReadInt32(bytes, ref position), typeArguments, methodArguments);
                        break;
                    case OperandType.InlineType:
                        operand = module.ResolveType(ReadInt32(bytes, ref position), typeArguments, methodArguments);
                        break;
                    case OperandType.InlineTok:
                        operand = module.ResolveMember(ReadInt32(bytes, ref position), typeArguments, methodArguments);
                        break;
                    default:
                        throw new BadImageFormatException($"Unsupported operand at IL_{offset:X4}.");
                }
                instructions.Add(new Instruction(offset, code, operand));
            }
            return instructions;
        }

        /// <summary>Reads a single IL byte with truncation checking.</summary>
        /// <param name="bytes">The IL buffer.</param>
        /// <param name="position">The cursor, advanced by one byte.</param>
        /// <returns>The encoded byte.</returns>
        /// <exception cref="BadImageFormatException">The buffer is truncated.</exception>
        private static byte ReadByte(byte[] bytes, ref int position)
        {
            EnsureBytes(bytes, position, 1);
            return bytes[position++];
        }

        /// <summary>Reads a little-endian 32-bit IL operand independently of the host byte order.</summary>
        /// <param name="bytes">The IL buffer.</param>
        /// <param name="position">The cursor, advanced by four bytes.</param>
        /// <returns>The signed operand.</returns>
        /// <exception cref="BadImageFormatException">The buffer is truncated.</exception>
        private static int ReadInt32(byte[] bytes, ref int position)
        {
            EnsureBytes(bytes, position, 4);
            var result = bytes[position] | (bytes[position + 1] << 8) |
                (bytes[position + 2] << 16) | (bytes[position + 3] << 24);
            position += 4;
            return result;
        }

        /// <summary>Validates the available IL operand length.</summary>
        /// <param name="bytes">The IL buffer.</param>
        /// <param name="position">The current cursor.</param>
        /// <param name="count">The required byte count.</param>
        /// <exception cref="BadImageFormatException">The requested range is outside the buffer.</exception>
        private static void EnsureBytes(byte[] bytes, int position, int count)
        {
            if (position < 0 || count > bytes.Length - position)
            {
                throw new BadImageFormatException("Truncated IL operand.");
            }
        }

        /// <summary>Creates the standard opcode table using direct framework references, not GetValue.</summary>
        /// <returns>The standard opcode map for one-byte and 0xFE-prefixed encodings.</returns>
        private static Dictionary<short, OpCode> CreateOpCodes()
        {
            var result = new Dictionary<short, OpCode>();
            var codes = new[]
            {
                OpCodes.Nop, OpCodes.Break, OpCodes.Ldarg_0, OpCodes.Ldarg_1, OpCodes.Ldarg_2, OpCodes.Ldarg_3,
                OpCodes.Ldloc_0, OpCodes.Ldloc_1, OpCodes.Ldloc_2, OpCodes.Ldloc_3,
                OpCodes.Stloc_0, OpCodes.Stloc_1, OpCodes.Stloc_2, OpCodes.Stloc_3,
                OpCodes.Ldarg_S, OpCodes.Ldarga_S, OpCodes.Starg_S, OpCodes.Ldloc_S, OpCodes.Ldloca_S, OpCodes.Stloc_S,
                OpCodes.Ldnull, OpCodes.Ldc_I4_M1, OpCodes.Ldc_I4_0, OpCodes.Ldc_I4_1, OpCodes.Ldc_I4_2,
                OpCodes.Ldc_I4_3, OpCodes.Ldc_I4_4, OpCodes.Ldc_I4_5, OpCodes.Ldc_I4_6, OpCodes.Ldc_I4_7,
                OpCodes.Ldc_I4_8, OpCodes.Ldc_I4_S, OpCodes.Ldc_I4, OpCodes.Ldc_I8, OpCodes.Ldc_R4, OpCodes.Ldc_R8,
                OpCodes.Dup, OpCodes.Pop, OpCodes.Jmp, OpCodes.Call, OpCodes.Calli, OpCodes.Ret,
                OpCodes.Br_S, OpCodes.Brfalse_S, OpCodes.Brtrue_S, OpCodes.Beq_S, OpCodes.Bge_S, OpCodes.Bgt_S,
                OpCodes.Ble_S, OpCodes.Blt_S, OpCodes.Bne_Un_S, OpCodes.Bge_Un_S, OpCodes.Bgt_Un_S,
                OpCodes.Ble_Un_S, OpCodes.Blt_Un_S, OpCodes.Br, OpCodes.Brfalse, OpCodes.Brtrue,
                OpCodes.Beq, OpCodes.Bge, OpCodes.Bgt, OpCodes.Ble, OpCodes.Blt, OpCodes.Bne_Un,
                OpCodes.Bge_Un, OpCodes.Bgt_Un, OpCodes.Ble_Un, OpCodes.Blt_Un, OpCodes.Switch,
                OpCodes.Ldind_I1, OpCodes.Ldind_U1, OpCodes.Ldind_I2, OpCodes.Ldind_U2, OpCodes.Ldind_I4,
                OpCodes.Ldind_U4, OpCodes.Ldind_I8, OpCodes.Ldind_I, OpCodes.Ldind_R4, OpCodes.Ldind_R8, OpCodes.Ldind_Ref,
                OpCodes.Stind_Ref, OpCodes.Stind_I1, OpCodes.Stind_I2, OpCodes.Stind_I4, OpCodes.Stind_I8,
                OpCodes.Stind_R4, OpCodes.Stind_R8, OpCodes.Add, OpCodes.Sub, OpCodes.Mul, OpCodes.Div,
                OpCodes.Div_Un, OpCodes.Rem, OpCodes.Rem_Un, OpCodes.And, OpCodes.Or, OpCodes.Xor,
                OpCodes.Shl, OpCodes.Shr, OpCodes.Shr_Un, OpCodes.Neg, OpCodes.Not, OpCodes.Conv_I1,
                OpCodes.Conv_I2, OpCodes.Conv_I4, OpCodes.Conv_I8, OpCodes.Conv_R4, OpCodes.Conv_R8,
                OpCodes.Conv_U4, OpCodes.Conv_U8, OpCodes.Callvirt, OpCodes.Cpobj, OpCodes.Ldobj, OpCodes.Ldstr,
                OpCodes.Newobj, OpCodes.Castclass, OpCodes.Isinst, OpCodes.Conv_R_Un, OpCodes.Unbox, OpCodes.Throw,
                OpCodes.Ldfld, OpCodes.Ldflda, OpCodes.Stfld, OpCodes.Ldsfld, OpCodes.Ldsflda, OpCodes.Stsfld,
                OpCodes.Stobj, OpCodes.Conv_Ovf_I1_Un, OpCodes.Conv_Ovf_I2_Un, OpCodes.Conv_Ovf_I4_Un,
                OpCodes.Conv_Ovf_I8_Un, OpCodes.Conv_Ovf_U1_Un, OpCodes.Conv_Ovf_U2_Un, OpCodes.Conv_Ovf_U4_Un,
                OpCodes.Conv_Ovf_U8_Un, OpCodes.Conv_Ovf_I_Un, OpCodes.Conv_Ovf_U_Un,
                OpCodes.Box, OpCodes.Newarr, OpCodes.Ldlen, OpCodes.Ldelema, OpCodes.Ldelem_I1, OpCodes.Ldelem_U1,
                OpCodes.Ldelem_I2, OpCodes.Ldelem_U2, OpCodes.Ldelem_I4, OpCodes.Ldelem_U4, OpCodes.Ldelem_I8,
                OpCodes.Ldelem_I, OpCodes.Ldelem_R4, OpCodes.Ldelem_R8, OpCodes.Ldelem_Ref,
                OpCodes.Stelem_I, OpCodes.Stelem_I1, OpCodes.Stelem_I2, OpCodes.Stelem_I4, OpCodes.Stelem_I8,
                OpCodes.Stelem_R4, OpCodes.Stelem_R8, OpCodes.Stelem_Ref, OpCodes.Ldelem, OpCodes.Stelem,
                OpCodes.Unbox_Any, OpCodes.Conv_Ovf_I1, OpCodes.Conv_Ovf_U1, OpCodes.Conv_Ovf_I2,
                OpCodes.Conv_Ovf_U2, OpCodes.Conv_Ovf_I4, OpCodes.Conv_Ovf_U4, OpCodes.Conv_Ovf_I8,
                OpCodes.Conv_Ovf_U8, OpCodes.Refanyval, OpCodes.Ckfinite, OpCodes.Mkrefany, OpCodes.Ldtoken,
                OpCodes.Conv_U2, OpCodes.Conv_U1, OpCodes.Conv_I, OpCodes.Conv_Ovf_I, OpCodes.Conv_Ovf_U,
                OpCodes.Add_Ovf, OpCodes.Add_Ovf_Un, OpCodes.Mul_Ovf, OpCodes.Mul_Ovf_Un, OpCodes.Sub_Ovf,
                OpCodes.Sub_Ovf_Un, OpCodes.Endfinally, OpCodes.Leave, OpCodes.Leave_S, OpCodes.Stind_I, OpCodes.Conv_U,
                OpCodes.Arglist, OpCodes.Ceq, OpCodes.Cgt, OpCodes.Cgt_Un, OpCodes.Clt, OpCodes.Clt_Un,
                OpCodes.Ldftn, OpCodes.Ldvirtftn, OpCodes.Ldarg, OpCodes.Ldarga, OpCodes.Starg,
                OpCodes.Ldloc, OpCodes.Ldloca, OpCodes.Stloc, OpCodes.Localloc, OpCodes.Endfilter,
                OpCodes.Unaligned, OpCodes.Volatile, OpCodes.Tailcall, OpCodes.Initobj, OpCodes.Constrained,
                OpCodes.Cpblk, OpCodes.Initblk, OpCodes.Rethrow, OpCodes.Sizeof, OpCodes.Refanytype, OpCodes.Readonly
            };
            foreach (var code in codes)
            {
                result.Add(code.Value, code);
            }
            return result;
        }
    }
}
