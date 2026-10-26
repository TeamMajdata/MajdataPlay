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
            /// <summary>Selects dependencies retained only in generic signatures.</summary>
            /// <param name="type">The selected Java declaration.</param>
            /// <param name="pending">The dependency work queue.</param>
            private void SelectGenericReferences(JavaType type, Queue<JavaType> pending)
            {
                if (type.Signature is not null)
                {
                    var signature = JavaGenericSignature.ParseClass(type.Signature);
                    foreach (var bound in signature.TypeParameters.SelectMany(value => value.Bounds)
                        .Concat(signature.Interfaces).Concat(new[] { signature.SuperType }))
                    {
                        SelectGenericReference(bound, pending);
                    }
                }
                foreach (var field in type.Fields.Where(IsVisibleField).Where(value => value.Signature is not null))
                {
                    SelectGenericReference(JavaGenericSignature.ParseType(field.Signature!), pending);
                }
                foreach (var method in type.Methods.Where(IsVisibleMethod).Where(value => value.Signature is not null))
                {
                    var signature = JavaGenericSignature.ParseMethod(method.Signature!);
                    foreach (var reference in signature.TypeParameters.SelectMany(value => value.Bounds)
                        .Concat(signature.ParameterTypes).Concat(signature.ThrowsTypes).Concat(new[] { signature.ReturnType }))
                    {
                        SelectGenericReference(reference, pending);
                    }
                }
            }

            /// <summary>Selects object dependencies recursively through generic type arguments.</summary>
            /// <param name="type">The generic signature node.</param>
            /// <param name="pending">The dependency work queue.</param>
            private void SelectGenericReference(JavaGenericType type, Queue<JavaType> pending)
            {
                if (type.Kind == 'L')
                {
                    AddSelected(type.Name, pending);
                    AddSelected(ReferenceName(type.Name), pending);
                }
                foreach (var argument in type.TypeArguments)
                {
                    SelectGenericReference(argument, pending);
                }
                if (type.DeclaringType is not null)
                {
                    SelectGenericReference(type.DeclaringType, pending);
                }
                if (type.ElementType is not null)
                {
                    SelectGenericReference(type.ElementType, pending);
                }
            }

            /// <summary>Gets declared Java class type parameters.</summary>
            /// <param name="type">The Java declaration.</param>
            /// <returns>The original ordered type parameters.</returns>
            private static IReadOnlyList<JavaGenericParameter> GenericParameters(JavaType type)
            {
                return type.Signature is null ? Array.Empty<JavaGenericParameter>() : JavaGenericSignature.ParseClass(type.Signature).TypeParameters;
            }

            /// <summary>Restores a concrete Java superclass parameterization on an erased nongeneric wrapper.</summary>
            /// <param name="plan">The wrapper declaration being emitted.</param>
            /// <returns>The constructed superclass wrapper, or null when lexical variables require erasure.</returns>
            private string? GenericSuperclass(TypePlan plan)
            {
                if (plan.Base is null || plan.Type.Signature is null || GenericParameters(plan.Type).Count != 0)
                {
                    return null;
                }
                var superclass = JavaGenericSignature.ParseClass(plan.Type.Signature).SuperType;
                if (superclass.Name != plan.Base.Type.Name || superclass.TypeArguments.Count == 0 || ContainsTypeVariable(superclass))
                {
                    return null;
                }
                return GenericTypeName(superclass, new Dictionary<string, string>(StringComparer.Ordinal), false, false, true);
            }

            /// <summary>Checks whether a generic reference depends on an unavailable lexical type variable.</summary>
            /// <param name="type">The Java generic reference.</param>
            /// <returns>Whether a variable occurs in this reference or any nested component.</returns>
            private static bool ContainsTypeVariable(JavaGenericType type)
            {
                return type.Kind == 'T' || type.TypeArguments.Any(ContainsTypeVariable)
                    || (type.DeclaringType is not null && ContainsTypeVariable(type.DeclaringType))
                    || (type.ElementType is not null && ContainsTypeVariable(type.ElementType));
            }

            /// <summary>Formats a C# type parameter declaration list.</summary>
            /// <param name="parameters">The declared Java type parameters.</param>
            /// <param name="variables">The resolved type variable names.</param>
            /// <returns>The angle-bracketed C# list, or empty text.</returns>
            private static string TypeParameterList(IReadOnlyList<JavaGenericParameter> parameters, IReadOnlyDictionary<string, string> variables)
            {
                return parameters.Count == 0 ? string.Empty : "<" + string.Join(", ", parameters.Select(value => variables[value.Name])) + ">";
            }

            /// <summary>Restores Java superclass, interface, intersection, and self bounds.</summary>
            /// <param name="parameters">The declared Java type parameters.</param>
            /// <param name="variables">The available C# type variables.</param>
            /// <returns>The C# generic constraint clauses.</returns>
            private string GenericConstraints(IReadOnlyList<JavaGenericParameter> parameters, IReadOnlyDictionary<string, string> variables)
            {
                var result = string.Empty;
                foreach (var parameter in parameters)
                {
                    var bounds = new List<string>();
                    var hasClass = false;
                    foreach (var bound in parameter.Bounds)
                    {
                        if (bound.Kind == 'L' && bound.Name == ObjectName)
                        {
                            continue;
                        }
                        var isInterface = bound.Kind == 'L' && _types.TryGetValue(bound.Name, out var type) && type.IsInterface;
                        bounds.Add(GenericTypeName(bound, variables, isInterface, false, true));
                        hasClass |= !isInterface;
                    }
                    if (!hasClass)
                    {
                        bounds.Insert(0, QualifiedWrapper(ObjectName));
                    }
                    result += " where " + variables[parameter.Name] + " : " + string.Join(", ", bounds.Distinct(StringComparer.Ordinal));
                }
                return result;
            }

            /// <summary>Maps a generic signature while keeping raw and wildcard references representable.</summary>
            /// <param name="type">The Java generic signature.</param>
            /// <param name="variables">The available type variables.</param>
            /// <param name="contract">Whether Java interface references use their C# contracts.</param>
            /// <param name="nullable">Whether to annotate reference and array values.</param>
            /// <param name="argument">Whether this node is a generic argument rather than a string value.</param>
            /// <returns>The C# type spelling.</returns>
            private string GenericTypeName(JavaGenericType type, IReadOnlyDictionary<string, string> variables, bool contract = true,
                bool nullable = true, bool argument = false)
            {
                var suffix = nullable ? "?" : string.Empty;
                if (type.Kind == 'T')
                {
                    return (variables.TryGetValue(type.Name, out var variable) ? variable : QualifiedWrapper(ObjectName)) + suffix;
                }
                if (type.Kind == '[')
                {
                    return GenericTypeName(type.ElementType!, variables, contract, true, argument) + "[]" + suffix;
                }
                if (type.Kind == '+' || type.Kind == '-')
                {
                    return GenericTypeName(type.ElementType!, variables, contract, nullable, argument);
                }
                if (type.Kind == '*')
                {
                    return QualifiedWrapper(ObjectName) + suffix;
                }
                if (type.Kind != 'L')
                {
                    return TypeName(new JavaTypeDescriptor(type.Kind));
                }
                if (type.Name == "java.lang.String" && !argument)
                {
                    return "string" + suffix;
                }
                var reference = ReferenceName(type.Name);
                var declaration = _types[reference];
                var name = contract && declaration.IsInterface ? _interfaceNames[reference] : _wrapperNames[reference];
                var scope = type.DeclaringType is not null && declaration.EnclosingType is not null
                    ? GenericTypeName(type.DeclaringType, variables, false, false, true) : "global::" + TypeScope(reference);
                var result = scope + "." + name;
                if (reference == type.Name && type.TypeArguments.Count != 0
                    && type.TypeArguments.Count == GenericParameters(declaration).Count
                    && CanConstructGenericType(declaration, type.TypeArguments, variables))
                {
                    result += "<" + string.Join(", ", type.TypeArguments.Select(value => GenericTypeName(value, variables, false, false, true))) + ">";
                }
                return result + suffix;
            }

            /// <summary>Checks whether a Java argument has a CLR representation satisfying wrapper constraints.</summary>
            /// <param name="type">The Java generic argument.</param>
            /// <returns>Whether the argument can be used without inventing an existential or array wrapper.</returns>
            private bool CanUseGenericArgument(JavaGenericType type)
            {
                return type.Kind == 'T' || type.Kind == 'L';
            }

            /// <summary>Checks dependent class constraints against the CLR wrapper inheritance representation.</summary>
            /// <param name="declaration">The generic Java declaration being constructed.</param>
            /// <param name="arguments">The requested Java generic arguments.</param>
            /// <param name="variables">The lexical variable names and bound classifications.</param>
            /// <returns>Whether the CLR construction can preserve the arguments without violating a class constraint.</returns>
            private bool CanConstructGenericType(JavaType declaration, IReadOnlyList<JavaGenericType> arguments, IReadOnlyDictionary<string, string> variables)
            {
                if (!arguments.All(CanUseGenericArgument))
                {
                    return false;
                }
                var parameters = GenericParameters(declaration);
                for (var index = 0; index < parameters.Count; index++)
                {
                    if (IsThrowableArgument(arguments[index], variables)
                        && !HasThrowableBound(parameters[index], parameters, new Dictionary<string, string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal)))
                    {
                        return false;
                    }
                    foreach (var bound in parameters[index].Bounds.Where(value => value.Kind == 'T'))
                    {
                        var boundIndex = -1;
                        for (var candidate = 0; candidate < parameters.Count; candidate++)
                        {
                            if (parameters[candidate].Name == bound.Name)
                            {
                                boundIndex = candidate;
                                break;
                            }
                        }
                        if (boundIndex < 0 || arguments[index].Kind != 'L' || arguments[boundIndex].Kind != 'L')
                        {
                            continue;
                        }
                        var actual = arguments[index].Name;
                        var expected = arguments[boundIndex].Name;
                        if (actual != expected && _types.TryGetValue(expected, out var expectedType) && expectedType.IsInterface)
                        {
                            return false;
                        }
                    }
                }
                return true;
            }

            /// <summary>Finds parameterized interface contracts with each declaration's substituted variables.</summary>
            /// <param name="interfaces">The direct Java interface references.</param>
            /// <param name="variables">The containing declaration's variable substitutions.</param>
            /// <returns>The reachable constructed generic interface contracts.</returns>
            private IReadOnlyList<GenericInterfaceContract> GenericInterfaceContracts(IEnumerable<JavaGenericType> interfaces, IReadOnlyDictionary<string, string> variables)
            {
                var result = new List<GenericInterfaceContract>();
                var visited = new HashSet<string>(StringComparer.Ordinal);
                foreach (var implemented in interfaces)
                {
                    AddGenericInterfaceContract(implemented, variables, result, visited);
                }
                return result;
            }

            /// <summary>Finds concrete generic Java interfaces that can also be preserved on an erased wrapper.</summary>
            /// <param name="plan">The erased wrapper declaration.</param>
            /// <returns>The concrete generic interface names.</returns>
            private IEnumerable<string> ConcreteGenericInterfaces(TypePlan plan)
            {
                return ConcreteGenericContracts(plan).Select(value => value.QualifiedName);
            }

            /// <summary>Emits checked JNI implementations of concrete generic interfaces on an erased wrapper.</summary>
            /// <param name="source">The output source writer.</param>
            /// <param name="plan">The erased wrapper declaration.</param>
            private void EmitConcreteGenericContracts(SourceWriter source, TypePlan plan)
            {
                foreach (var contract in ConcreteGenericContracts(plan))
                {
                    foreach (var method in contract.Plan.InterfaceMethods.Where(value => value.Method.Signature is not null))
                    {
                        EmitGenericMethod(source, plan, method, contract.Variables, false, contract.QualifiedName);
                    }
                }
            }

            /// <summary>Resolves generic interfaces whose arguments contain no unavailable class variables.</summary>
            /// <param name="plan">The erased wrapper declaration.</param>
            /// <returns>The constructed interface contracts that survive class erasure.</returns>
            private IReadOnlyList<GenericInterfaceContract> ConcreteGenericContracts(TypePlan plan)
            {
                var interfaces = plan.Type.Signature is null ? Array.Empty<JavaGenericType>()
                    : JavaGenericSignature.ParseClass(plan.Type.Signature).Interfaces.Where(value => !ContainsTypeVariable(value)).ToArray();
                return GenericInterfaceContracts(interfaces, new Dictionary<string, string>(StringComparer.Ordinal));
            }

            /// <summary>Expands one generic interface and substitutes its arguments into inherited contracts.</summary>
            /// <param name="implemented">The constructed Java interface reference.</param>
            /// <param name="variables">The referring declaration's variable substitutions.</param>
            /// <param name="result">The reachable CLR contract list.</param>
            /// <param name="visited">The already expanded constructed interface names.</param>
            private void AddGenericInterfaceContract(JavaGenericType implemented, IReadOnlyDictionary<string, string> variables,
                List<GenericInterfaceContract> result, HashSet<string> visited)
            {
                if (!_plans.TryGetValue(implemented.Name, out var plan) || !plan.Type.IsInterface)
                {
                    return;
                }
                var parameters = GenericParameters(plan.Type);
                if (parameters.Count == 0 || parameters.Count != implemented.TypeArguments.Count || !CanConstructGenericType(plan.Type, implemented.TypeArguments, variables))
                {
                    return;
                }
                var name = GenericTypeName(implemented, variables, true, false);
                if (!visited.Add(name))
                {
                    return;
                }
                var substitutions = new GenericVariableScope(variables);
                for (var index = 0; index < parameters.Count; index++)
                {
                    substitutions[parameters[index].Name] = GenericTypeName(implemented.TypeArguments[index], variables, false, false, true);
                    if (IsThrowableArgument(implemented.TypeArguments[index], variables))
                    {
                        substitutions.ThrowableVariables.Add(parameters[index].Name);
                    }
                    else
                    {
                        substitutions.ThrowableVariables.Remove(parameters[index].Name);
                    }
                }
                result.Add(new GenericInterfaceContract(plan, name, substitutions));
                if (plan.Type.Signature is not null)
                {
                    foreach (var parent in JavaGenericSignature.ParseClass(plan.Type.Signature).Interfaces)
                    {
                        AddGenericInterfaceContract(parent, substitutions, result, visited);
                    }
                }
            }

            /// <summary>Keeps a constructed interface and its Java-to-CLR variable substitutions together.</summary>
            private sealed class GenericInterfaceContract
            {
                /// <summary>Gets the interface declaration plan.</summary>
                internal TypePlan Plan { get; }

                /// <summary>Gets the fully qualified constructed CLR interface name.</summary>
                internal string QualifiedName { get; }

                /// <summary>Gets the substituted Java variable names.</summary>
                internal IReadOnlyDictionary<string, string> Variables { get; }

                /// <summary>Creates one constructed generic interface contract.</summary>
                /// <param name="plan">The interface declaration plan.</param>
                /// <param name="qualifiedName">The constructed CLR interface name.</param>
                /// <param name="variables">The Java-to-CLR variable substitutions.</param>
                internal GenericInterfaceContract(TypePlan plan, string qualifiedName, IReadOnlyDictionary<string, string> variables)
                {
                    Plan = plan;
                    QualifiedName = qualifiedName;
                    Variables = variables;
                }
            }

            /// <summary>Emits the parameterized companion of a Java generic declaration.</summary>
            /// <param name="source">The output writer.</param>
            /// <param name="plan">The erased Java wrapper plan.</param>
            /// <param name="outerVariables">The type variables of generic containing wrappers.</param>
            private void EmitGenericDeclaration(SourceWriter source, TypePlan plan, IReadOnlyDictionary<string, string> outerVariables)
            {
                var parameters = GenericParameters(plan.Type);
                if (parameters.Count == 0 && outerVariables.Count == 0)
                {
                    return;
                }
                var variables = AddVariables(outerVariables, parameters);
                var typeParameters = TypeParameterList(parameters, variables);
                var className = plan.Name + typeParameters;
                var interfaceName = plan.InterfaceSimpleName + typeParameters;
                var constraints = GenericConstraints(parameters, variables);
                var methods = plan.PublicMethods.Where(value => value.Owner.Name == plan.Type.Name && !value.Method.IsStatic).ToArray();
                var classSignature = plan.Type.Signature is not null ? JavaGenericSignature.ParseClass(plan.Type.Signature)
                    : new JavaClassSignature(Array.Empty<JavaGenericParameter>(),
                        JavaGenericSignature.ParseType("L" + (plan.Type.SuperName ?? ObjectName).Replace('.', '/') + ";"),
                        plan.Type.Interfaces.Select(value => JavaGenericSignature.ParseType("L" + value.Replace('.', '/') + ";")).ToArray());
                var genericInterfaces = GenericInterfaceContracts(classSignature.Interfaces, variables);
                if (plan.Type.IsInterface)
                {
                    source.Line("/// <summary>Preserves the Java generic interface contract.</summary>");
                    foreach (var parameter in parameters)
                    {
                        source.Line("/// <typeparam name=\"" + variables[parameter.Name] + "\">The Java type argument.</typeparam>");
                    }
                    var interfaceBases = new[] { plan.InterfaceQualifiedName! }.Concat(genericInterfaces.Select(value => value.QualifiedName));
                    source.Open("public interface " + interfaceName + " : " + string.Join(", ", interfaceBases.Distinct(StringComparer.Ordinal)) + constraints);
                    foreach (var method in plan.InterfaceMethods.Where(value => value.Method.Signature is not null))
                    {
                        EmitGenericMethod(source, plan, method, variables, true);
                    }
                    source.Close();
                }
                source.Line("/// <summary>Preserves Java type arguments and their declared bounds.</summary>");
                foreach (var parameter in parameters)
                {
                    source.Line("/// <typeparam name=\"" + variables[parameter.Name] + "\">The Java type argument.</typeparam>");
                }
                var bases = new List<string> { plan.QualifiedName };
                if (plan.Type.IsInterface)
                {
                    bases.Add(interfaceName);
                }
                var equatable = DeclaresObjectMethod(plan.Type, "equals", "(Ljava/lang/Object;)Z");
                if (equatable)
                {
                    bases.Add("global::System.IEquatable<" + className + ">");
                }
                bases.AddRange(genericInterfaces.Select(value => value.QualifiedName));
                var comparisonName = FindManagedInterfaceArgument(plan.Type, "java.lang.Comparable", variables, new HashSet<string>(StringComparer.Ordinal)) ?? QualifiedWrapper(ObjectName);
                var comparatorName = FindManagedInterfaceArgument(plan.Type, "java.util.Comparator", variables, new HashSet<string>(StringComparer.Ordinal)) ?? QualifiedWrapper(ObjectName);
                if (ImplementsJava(plan.Type, "java.lang.Comparable"))
                {
                    bases.Add("global::System.IComparable<" + comparisonName + ">");
                }
                if (ImplementsJava(plan.Type, "java.util.Comparator"))
                {
                    bases.Add("global::System.Collections.Generic.IComparer<" + comparatorName + ">");
                }
                var iterable = genericInterfaces.FirstOrDefault(value => value.Plan.Type.Name == "java.lang.Iterable");
                var elementName = plan.Type.Name == "java.lang.Iterable" && parameters.Count == 1 ? variables[parameters[0].Name]
                    : iterable is null ? null : iterable.Variables[GenericParameters(iterable.Plan.Type)[0].Name];
                if (elementName is not null)
                {
                    bases.Add("global::System.Collections.Generic.IEnumerable<" + elementName + ">");
                }
                source.Open("public partial class " + className + " : " + string.Join(", ", bases.Distinct(StringComparer.Ordinal)) + constraints);
                EmitRegistration(source, plan.Name, className, plan.Type.Name);
                EmitReferenceConstructor(source, plan);
                var signatures = new HashSet<string>(StringComparer.Ordinal);
                foreach (var constructor in plan.Constructors)
                {
                    var types = GenericMethodParameters(constructor.Method, constructor.Signature, variables);
                    if (!signatures.Add(string.Join(",", types.Select(JavaNames.SignatureType))))
                    {
                        continue;
                    }
                    source.Line("/// <summary>Constructs a Java instance with typed arguments.</summary>");
                    DocumentParameters(source, constructor.ParameterNames, constructor.Method, constructor.Signature);
                    source.Line("public " + plan.Name + "(" + ParameterDeclarations(types, constructor.ParameterNames) + ")");
                    source.Open("    : this(" + _jni + ".Construct(" + JavaNames.StringLiteral(plan.Type.Name) + ", "
                        + JavaNames.StringLiteral(constructor.Method.Descriptor) + ", " + Arguments(constructor.ParameterNames) + "), ownsReference: true)");
                    source.Close();
                }
                source.Line("/// <summary>Gets the Java reflection class for this wrapper.</summary>");
                var classWrapper = QualifiedWrapper(ClassName) + (IsThrowable(plan.Type) ? string.Empty : "<" + className + ">");
                source.Open("public static new " + classWrapper + " " + plan.ClassPropertyName);
                source.Open("get");
                source.Line("return " + ConverterName + ".ToWrapper<" + classWrapper + ">(" + _jni + ".GetClass("
                    + JavaNames.StringLiteral(plan.Type.Name) + "), ownsReference: true)!;");
                source.Close();
                source.Close();
                foreach (var field in plan.Fields.Where(value => value.Field.Signature is not null))
                {
                    var generic = JavaGenericSignature.ParseType(field.Field.Signature!);
                    var mapped = GenericTypeName(generic, variables);
                    source.Line("/// <summary>Gets the Java field with its declared type arguments.</summary>");
                    source.Open("public " + (field.Field.IsStatic ? "static " : string.Empty) + "new " + mapped + " " + field.Name);
                    source.Open("get");
                    var invocation = _jni + ".GetField<" + GenericJniTypeName(field.Descriptor, generic) + ">(" + (field.Field.IsStatic ? "null" : "JavaReference")
                        + ", " + JavaNames.StringLiteral(field.Owner.Name) + ", " + JavaNames.StringLiteral(field.Field.Name) + ", " + JavaNames.StringLiteral(field.Field.Descriptor) + ")";
                    source.Line("return " + WrapGenericResult(field.Descriptor, generic, invocation, variables) + ";");
                    source.Close();
                    source.Close();
                }
                foreach (var method in methods.Where(value => value.Method.Signature is not null))
                {
                    EmitGenericMethod(source, plan, method, variables, false);
                }
                if (equatable)
                {
                    source.Line("/// <summary>Compares typed wrappers using Java equals(Object).</summary>");
                    source.Line("/// <param name=\"other\">The other Java wrapper, or null.</param>");
                    source.Line("/// <returns>Whether the Java objects are equal.</returns>");
                    source.Open("public bool Equals(" + className + "? other)");
                    source.Line("return Equals((object?)other);");
                    source.Close();
                }
                foreach (var contract in genericInterfaces)
                {
                    foreach (var method in contract.Plan.InterfaceMethods.Where(value => value.Method.Signature is not null))
                    {
                        EmitGenericMethod(source, plan, method, contract.Variables, false, contract.QualifiedName);
                    }
                }
                if (ImplementsJava(plan.Type, "java.lang.Comparable") || ImplementsJava(plan.Type, "java.util.Comparator"))
                {
                    EmitComparisonContracts(source, plan.Type, comparisonName, false, comparatorName);
                }
                if (elementName is not null)
                {
                    EmitEnumerableContract(source, elementName, false);
                }
                source.Close();
            }

            /// <summary>Maps generic method parameters while preserving synthetic constructor prefixes.</summary>
            /// <param name="method">The Java member.</param>
            /// <param name="descriptor">The erased JVM signature.</param>
            /// <param name="variables">The available type variables.</param>
            /// <returns>The typed CLR parameters in JVM descriptor order.</returns>
            private string[] GenericMethodParameters(JavaMethod method, JavaMethodDescriptor descriptor, IReadOnlyDictionary<string, string> variables)
            {
                var result = descriptor.Parameters.Select(value => TypeName(value)).ToArray();
                if (method.Signature is not null)
                {
                    var signature = JavaGenericSignature.ParseMethod(method.Signature);
                    var offset = descriptor.Parameters.Count - signature.ParameterTypes.Count;
                    for (var index = 0; index < signature.ParameterTypes.Count; index++)
                    {
                        result[index + offset] = GenericTypeName(signature.ParameterTypes[index], variables);
                    }
                }
                return result;
            }

            /// <summary>Emits a typed Java member retaining its exact erased JNI descriptor.</summary>
            /// <param name="source">The output writer.</param>
            /// <param name="plan">The containing declaration.</param>
            /// <param name="method">The Java member.</param>
            /// <param name="classVariables">The containing type variables.</param>
            /// <param name="isInterface">Whether only a contract declaration is needed.</param>
            /// <param name="explicitContract">The constructed interface to implement explicitly, if present.</param>
            private void EmitGenericMethod(SourceWriter source, TypePlan plan, MethodMember method, IReadOnlyDictionary<string, string> classVariables, bool isInterface, string? explicitContract = null)
            {
                var signature = JavaGenericSignature.ParseMethod(method.Method.Signature!);
                var variables = AddVariables(classVariables, signature.TypeParameters);
                var parameters = ParameterNames(method.Method, method.Signature);
                var types = GenericMethodParameters(method.Method, method.Signature, variables);
                var result = GenericTypeName(signature.ReturnType, variables);
                DocumentMethod(source, method, parameters);
                foreach (var parameter in signature.TypeParameters)
                {
                    source.Line("/// <typeparam name=\"" + variables[parameter.Name] + "\">The Java method type argument.</typeparam>");
                }
                var hidesErased = signature.TypeParameters.Count == 0
                    && types.Select(JavaNames.SignatureType).SequenceEqual(method.ParameterTypes.Select(JavaNames.SignatureType), StringComparer.Ordinal);
                var modifiers = explicitContract is not null ? string.Empty : (isInterface ? string.Empty : "public ") + (hidesErased ? "new " : string.Empty);
                var declaration = modifiers + result + " "
                    + (explicitContract is null ? string.Empty : explicitContract + ".") + method.Name
                    + TypeParameterList(signature.TypeParameters, variables) + "(" + ParameterDeclarations(types, parameters) + ")"
                    + (explicitContract is null ? GenericConstraints(signature.TypeParameters, variables)
                        : string.Concat(signature.TypeParameters.Select(value => " where " + variables[value.Name] + " : class")));
                if (isInterface)
                {
                    source.Line(declaration + ";");
                    return;
                }
                source.Open(declaration);
                var invocation = _jni + ".Call" + (method.Signature.ReturnType.Code == 'V' ? string.Empty : "<" + GenericJniTypeName(method.Signature.ReturnType, signature.ReturnType) + ">")
                    + "(JavaReference, " + JavaNames.StringLiteral(method.Owner.Name) + ", " + JavaNames.StringLiteral(method.Method.Name) + ", "
                    + JavaNames.StringLiteral(method.Method.Descriptor) + ", " + Arguments(parameters) + ")";
                source.Line(method.Signature.ReturnType.Code == 'V' ? invocation + ";" : "return " + WrapGenericResult(method.Signature.ReturnType, signature.ReturnType, invocation, variables) + ";");
                source.Close();
            }

            /// <summary>Selects JNI read types without converting a String-bounded type variable into a CLR string.</summary>
            /// <param name="descriptor">The exact erased JNI type.</param>
            /// <param name="generic">The retained generic type.</param>
            /// <returns>The raw JNI result type before checked wrapper conversion.</returns>
            private string GenericJniTypeName(JavaTypeDescriptor descriptor, JavaGenericType generic)
            {
                if (descriptor.Code == '[' && generic.Kind == '[')
                {
                    return GenericJniTypeName(descriptor.Element!, generic.ElementType!) + "[]?";
                }
                return descriptor.Code == 'L' && generic.Kind == 'T' ? UnityObject + "?" : TypeName(descriptor, true);
            }

            /// <summary>Converts JNI values to their generic reference wrapper types.</summary>
            /// <param name="descriptor">The exact erased return descriptor.</param>
            /// <param name="generic">The declared Java generic return type.</param>
            /// <param name="expression">The JNI invocation expression.</param>
            /// <param name="variables">The available type variables.</param>
            /// <param name="depth">The current array depth.</param>
            /// <returns>The checked generic result conversion expression.</returns>
            private string WrapGenericResult(JavaTypeDescriptor descriptor, JavaGenericType generic, string expression,
                IReadOnlyDictionary<string, string> variables, int depth = 0)
            {
                if (descriptor.Code == '[' && generic.Kind == '[')
                {
                    var parameter = "__genericElement" + depth;
                    return _jni + ".MapArray<" + GenericJniTypeName(descriptor.Element!, generic.ElementType!) + ", " + GenericTypeName(generic.ElementType!, variables)
                        + ">(" + expression + ", " + parameter + " => " + WrapGenericResult(descriptor.Element!, generic.ElementType!, parameter, variables, depth + 1) + ")";
                }
                if (descriptor.Code == 'L' && (descriptor.BinaryName != "java.lang.String" || generic.Kind == 'T'))
                {
                    var wrapper = GenericTypeName(generic, variables, false, false);
                    return ConverterName + ".ToWrapper<" + wrapper + ">(" + expression + ", ownsReference: true)";
                }
                return expression;
            }
        }
    }
}
