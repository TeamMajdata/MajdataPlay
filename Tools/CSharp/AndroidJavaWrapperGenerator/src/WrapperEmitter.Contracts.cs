using System;
using System.Collections.Generic;
using System.Linq;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    internal static partial class WrapperEmitter
    {
        private sealed partial class Emitter
        {
            /// <summary>Checks an exact, nonstatic Java object override signature.</summary>
            /// <param name="type">The Java declaration.</param>
            /// <param name="name">The Java method name.</param>
            /// <param name="descriptor">The required JVM signature.</param>
            /// <returns>Whether the declaration supplies the override.</returns>
            private static bool DeclaresObjectMethod(JavaType type, string name, string descriptor)
            {
                return !type.IsInterface && type.Methods.Any(method => method.IsPublic && !method.IsStatic
                    && method.Name == name && method.Descriptor == descriptor);
            }

            /// <summary>Adds CLR contracts with the same Java behavior.</summary>
            /// <param name="plan">The wrapper declaration.</param>
            /// <param name="bases">The base type and interface names.</param>
            private void AddManagedInterfaces(TypePlan plan, List<string> bases)
            {
                if (DeclaresObjectMethod(plan.Type, "equals", "(Ljava/lang/Object;)Z"))
                {
                    bases.Add("global::System.IEquatable<" + plan.Name + ">");
                }
                if (ImplementsJava(plan.Type, "java.lang.Comparable"))
                {
                    bases.Add("global::System.IComparable");
                    bases.Add("global::System.IComparable<" + ManagedInterfaceArgument(plan.Type, "java.lang.Comparable") + ">");
                }
                if (ImplementsJava(plan.Type, "java.util.Comparator"))
                {
                    bases.Add("global::System.Collections.Generic.IComparer<" + ManagedInterfaceArgument(plan.Type, "java.util.Comparator") + ">");
                }
                if (ImplementsJava(plan.Type, "java.lang.Iterable"))
                {
                    bases.Add("global::System.Collections.Generic.IEnumerable<" + ManagedInterfaceArgument(plan.Type, "java.lang.Iterable") + ">");
                }
            }

            /// <summary>Checks a Java interface including indirect implementations.</summary>
            /// <param name="type">The Java declaration.</param>
            /// <param name="name">The Java interface binary name.</param>
            /// <returns>Whether the declaration implements the interface.</returns>
            private bool ImplementsJava(JavaType type, string name)
            {
                return Hierarchy(type, true).Any(value => value.Name == name || value.Interfaces.Contains(name));
            }

            /// <summary>Resolves a closed Java interface argument through class and interface inheritance.</summary>
            /// <param name="type">The Java wrapper declaration.</param>
            /// <param name="interfaceName">The Java interface whose single argument is being restored.</param>
            /// <returns>The concrete wrapper argument, or JavaObject when only erasure is representable.</returns>
            private string ManagedInterfaceArgument(JavaType type, string interfaceName)
            {
                return FindManagedInterfaceArgument(type, interfaceName, new Dictionary<string, string>(StringComparer.Ordinal),
                    new HashSet<string>(StringComparer.Ordinal)) ?? QualifiedWrapper(ObjectName);
            }

            /// <summary>Substitutes concrete generic arguments while traversing Java inheritance edges.</summary>
            /// <param name="type">The current class or interface declaration.</param>
            /// <param name="interfaceName">The target Java interface.</param>
            /// <param name="variables">The concrete substitutions available in this declaration.</param>
            /// <param name="visited">The already inspected declarations and substitution combinations.</param>
            /// <returns>The target argument's wrapper spelling, or null when no concrete argument is available.</returns>
            private string? FindManagedInterfaceArgument(JavaType type, string interfaceName, IReadOnlyDictionary<string, string> variables, HashSet<string> visited)
            {
                var identity = type.Name + ":" + string.Join(";", variables.OrderBy(value => value.Key, StringComparer.Ordinal)
                    .Select(value => value.Key + "=" + value.Value));
                if (!visited.Add(identity))
                {
                    return null;
                }

                var parameters = GenericParameters(type);
                if (type.Name == interfaceName && parameters.Count == 1 && variables.TryGetValue(parameters[0].Name, out var argument))
                {
                    return argument;
                }

                var signature = type.Signature is null ? null : JavaGenericSignature.ParseClass(type.Signature);
                var references = signature is null
                    ? type.Interfaces.Select(value => new JavaGenericType('L', value)).ToList()
                    : signature.Interfaces.ToList();
                if (signature is not null)
                {
                    references.Add(signature.SuperType);
                }
                else if (!string.IsNullOrEmpty(type.SuperName))
                {
                    references.Add(new JavaGenericType('L', type.SuperName));
                }

                foreach (var reference in references)
                {
                    if (reference.Name == interfaceName && reference.TypeArguments.Count == 1
                        && IsConcreteManagedArgument(reference.TypeArguments[0], variables))
                    {
                        return GenericTypeName(reference.TypeArguments[0], variables, false, false, true);
                    }
                    if (!_types.TryGetValue(reference.Name, out var inherited))
                    {
                        continue;
                    }

                    var substitutions = new Dictionary<string, string>(StringComparer.Ordinal);
                    var inheritedParameters = GenericParameters(inherited);
                    if (inheritedParameters.Count == reference.TypeArguments.Count)
                    {
                        for (var index = 0; index < inheritedParameters.Count; index++)
                        {
                            var inheritedArgument = reference.TypeArguments[index];
                            if (IsConcreteManagedArgument(inheritedArgument, variables))
                            {
                                substitutions[inheritedParameters[index].Name] = GenericTypeName(inheritedArgument, variables, false, false, true);
                            }
                        }
                    }
                    var result = FindManagedInterfaceArgument(inherited, interfaceName, substitutions, visited);
                    if (result is not null)
                    {
                        return result;
                    }
                }

                return null;
            }

            /// <summary>Checks whether a Java generic argument has a closed CLR wrapper representation.</summary>
            /// <param name="type">The Java argument or one of its nested arguments.</param>
            /// <param name="variables">The concrete type-variable substitutions.</param>
            /// <returns>Whether the argument avoids unbound variables, arrays, wildcards, and throwable roots.</returns>
            private bool IsConcreteManagedArgument(JavaGenericType type, IReadOnlyDictionary<string, string> variables)
            {
                if (type.Kind == 'T')
                {
                    return variables.ContainsKey(type.Name);
                }
                if (type.Kind != 'L' || _types.TryGetValue(type.Name, out var declaration) && IsThrowable(declaration))
                {
                    return false;
                }
                return type.TypeArguments.All(value => IsConcreteManagedArgument(value, variables))
                    && (type.DeclaringType is null || IsConcreteManagedArgument(type.DeclaringType, variables));
            }

            /// <summary>Emits Java object overrides and matching CLR interface adapters.</summary>
            /// <param name="source">The output writer.</param>
            /// <param name="plan">The Java declaration.</param>
            private void EmitManagedContracts(SourceWriter source, TypePlan plan)
            {
                var equatable = DeclaresObjectMethod(plan.Type, "equals", "(Ljava/lang/Object;)Z");
                if (equatable)
                {
                    source.Line("/// <summary>Compares this wrapper using Java equals(Object).</summary>");
                    source.Line("/// <param name=\"obj\">Another Java wrapper, or a managed value.</param>");
                    source.Line("/// <returns>Whether the Java objects are equal.</returns>");
                    DocumentJniExceptions(source, true);
                    source.Open("public override bool Equals(object? obj)");
                    source.Open("if (global::System.Object.ReferenceEquals(this, obj))");
                    source.Line("return true;");
                    source.Close();
                    source.Line("return obj is global::" + _options.RootNamespace + ".IJavaObject __other && " + _jni
                        + ".Call<bool>(JavaReference, \"java.lang.Object\", \"equals\", \"(Ljava/lang/Object;)Z\", __other.JavaReference);");
                    source.Close();
                    source.Line("/// <summary>Compares typed wrappers using Java equality.</summary>");
                    source.Line("/// <param name=\"other\">The other wrapper, or null.</param>");
                    source.Line("/// <returns>Whether the wrappers represent equal Java objects.</returns>");
                    DocumentJniExceptions(source, true);
                    source.Open("public bool Equals(" + plan.Name + "? other)");
                    source.Line("return Equals((object?)other);");
                    source.Close();
                }
                if (equatable || DeclaresObjectMethod(plan.Type, "hashCode", "()I"))
                {
                    source.Line("/// <summary>Returns the Java object's virtual hash code.</summary>");
                    source.Line("/// <returns>The Java hashCode() result.</returns>");
                    DocumentJniExceptions(source, true);
                    source.Open("public override int GetHashCode()");
                    source.Line("return " + _jni + ".Call<int>(JavaReference, \"java.lang.Object\", \"hashCode\", \"()I\");");
                    source.Close();
                }
                if (DeclaresObjectMethod(plan.Type, "toString", "()Ljava/lang/String;"))
                {
                    var throwable = IsThrowable(plan.Type);
                    source.Line("/// <summary>Returns the Java object's virtual string representation.</summary>");
                    source.Line(throwable ? "/// <returns>The Java string, or an empty string when Java returns null.</returns>"
                        : "/// <returns>The Java string, or null.</returns>");
                    DocumentJniExceptions(source, true);
                    source.Open("public override string" + (throwable ? string.Empty : "?") + " ToString()");
                    source.Line("return " + _jni + ".Call<string?>(JavaReference, \"java.lang.Object\", \"toString\", \"()Ljava/lang/String;\")"
                        + (throwable ? " ?? string.Empty" : string.Empty) + ";");
                    source.Close();
                }
                EmitComparisonContracts(source, plan.Type, ManagedInterfaceArgument(plan.Type, "java.lang.Comparable"),
                    comparatorArgumentType: ManagedInterfaceArgument(plan.Type, "java.util.Comparator"));
                if (ImplementsJava(plan.Type, "java.lang.Iterable"))
                {
                    EmitEnumerableContract(source, ManagedInterfaceArgument(plan.Type, "java.lang.Iterable"));
                }
            }

            /// <summary>Emits comparable and comparator adapters using their erased JNI contracts.</summary>
            /// <param name="source">The output writer.</param>
            /// <param name="type">The Java declaration.</param>
            /// <param name="argumentType">The CLR comparison argument wrapper.</param>
            /// <param name="includeNonGeneric">Whether to emit the non-generic comparison adapter on this declaration.</param>
            /// <param name="comparatorArgumentType">The comparator's separate argument type, or null to reuse the comparable argument.</param>
            private void EmitComparisonContracts(SourceWriter source, JavaType type, string argumentType, bool includeNonGeneric = true, string? comparatorArgumentType = null)
            {
                if (ImplementsJava(type, "java.lang.Comparable"))
                {
                    source.Line("/// <summary>Compares Java values using Comparable.compareTo.</summary>");
                    source.Line("/// <param name=\"other\">The Java wrapper to compare, or null.</param>");
                    source.Line("/// <returns>One for null, or the signed Java ordering result for a non-null value.</returns>");
                    DocumentJniExceptions(source, true);
                    source.Open("int global::System.IComparable<" + argumentType + ">.CompareTo(" + argumentType + "? other)");
                    source.Open("if (other is null)");
                    source.Line("return 1;");
                    source.Close();
                    source.Line("return " + _jni + ".Call<int>(JavaReference, \"java.lang.Comparable\", \"compareTo\", \"(Ljava/lang/Object;)I\", other);");
                    source.Close();
                    if (includeNonGeneric)
                    {
                        source.Line("/// <summary>Compares a managed value through the Java comparison contract.</summary>");
                        source.Line("/// <param name=\"obj\">A Java wrapper or null.</param>");
                        source.Line("/// <returns>One for null, or the signed Java ordering result for a non-null value.</returns>");
                        source.Line("/// <exception cref=\"global::System.ArgumentException\">The non-null value is not a Java wrapper.</exception>");
                        DocumentJniExceptions(source, true);
                        source.Open("int global::System.IComparable.CompareTo(object? obj)");
                        source.Open("if (obj is null)");
                        source.Line("return 1;");
                        source.Close();
                        source.Open("if (obj is not global::" + _options.RootNamespace + ".IJavaObject)");
                        source.Line("throw new global::System.ArgumentException(\"A Java wrapper is required.\", nameof(obj));");
                        source.Close();
                        source.Line("return " + _jni + ".Call<int>(JavaReference, \"java.lang.Comparable\", \"compareTo\", \"(Ljava/lang/Object;)I\", obj);");
                        source.Close();
                    }
                }
                if (ImplementsJava(type, "java.util.Comparator"))
                {
                    var comparatorType = comparatorArgumentType ?? argumentType;
                    source.Line("/// <summary>Orders Java values using Comparator.compare.</summary>");
                    source.Line("/// <param name=\"x\">The first Java value.</param>");
                    source.Line("/// <param name=\"y\">The second Java value.</param>");
                    source.Line("/// <returns>The signed Java ordering result.</returns>");
                    DocumentJniExceptions(source, true);
                    source.Open("int global::System.Collections.Generic.IComparer<" + comparatorType + ">.Compare(" + comparatorType + "? x, " + comparatorType + "? y)");
                    source.Line("return " + _jni + ".Call<int>(JavaReference, \"java.util.Comparator\", \"compare\", \"(Ljava/lang/Object;Ljava/lang/Object;)I\", x, y);");
                    source.Close();
                }
            }

            /// <summary>Adapts Java Iterable to a disposing CLR enumerator.</summary>
            /// <param name="source">The output writer.</param>
            /// <param name="elementType">The generated element wrapper type.</param>
            /// <param name="includeNonGeneric">Whether to emit the non-generic adapter on this declaration.</param>
            private void EmitEnumerableContract(SourceWriter source, string elementType, bool includeNonGeneric = true)
            {
                source.Line("/// <summary>Enumerates Java values using Iterable.iterator.</summary>");
                source.Line("/// <returns>An enumerator owning the Java iterator reference.</returns>");
                DocumentJniExceptions(source, true);
                source.Open("global::System.Collections.Generic.IEnumerator<" + elementType + "> global::System.Collections.Generic.IEnumerable<" + elementType + ">.GetEnumerator()");
                source.Line("using var __iterator = " + _jni + ".Call<" + UnityObject + ">(JavaReference, \"java.lang.Iterable\", \"iterator\", \"()Ljava/util/Iterator;\")");
                source.Line("    ?? throw new global::System.InvalidOperationException(\"Java returned a null iterator.\");");
                source.Open("while (" + _jni + ".Call<bool>(__iterator, \"java.util.Iterator\", \"hasNext\", \"()Z\"))");
                source.Line("yield return " + ConverterName + ".ToWrapper<" + elementType + ">(" + _jni + ".Call<" + UnityObject
                    + "?>(__iterator, \"java.util.Iterator\", \"next\", \"()Ljava/lang/Object;\"), ownsReference: true)!;");
                source.Close();
                source.Close();
                if (includeNonGeneric)
                {
                    source.Line("/// <summary>Enumerates the Java values through the CLR collection contract.</summary>");
                    source.Line("/// <returns>The Java-backed enumerator.</returns>");
                    source.Open("global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator()");
                    source.Line("return ((global::System.Collections.Generic.IEnumerable<" + elementType + ">)this).GetEnumerator();");
                    source.Close();
                }
            }
        }
    }
}
