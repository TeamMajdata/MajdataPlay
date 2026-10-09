#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MajdataPlay.Tests.AndroidJavaGeneratorValidation
{
    /// <summary>
    /// Checks the generated API semantically and its exact descriptor-forwarding syntax.
    /// </summary>
    internal static class ApiAssertions
    {
        /// <summary>
        /// Checks the same complete fixture API regardless of Java input representation.
        /// </summary>
        /// <param name="run">The completed generator execution.</param>
        /// <param name="includeInherited">Whether inherited widget members should be present.</param>
        public static void AssertRootApi(GeneratorRun run, bool includeInherited = true)
        {
            run.AssertCompiles();
            using var image = new MemoryStream();
            var emit = run.Output.Emit(image);
            Check.True(emit.Success, "Generated DLL emission failed: " + string.Join("\r\n", emit.Diagnostics));
            foreach (var source in run.GeneratedSources.Values)
            {
                Check.True(source.Contains("#nullable enable", StringComparison.Ordinal), "Generated source must explicitly enable nullable annotations.");
            }
            var widget = Type(run, "WidgetWrapper");
            var contract = Type(run, "ContractWrapper");
            var mode = Type(run, "ModeWrapper");
            var nested = Type(run, "NestedWrapper");
            var inner = Type(run, "InnerWrapper");
            foreach (var type in new[] { widget, contract, mode, nested, inner })
            {
                Check.Equal("MajdataPlay.Platform.Android.Runtime.Java.Lang.JavaObject", type.BaseType?.ToDisplayString(), type.Name + " base type");
                AssertWrappingConstructor(type);
            }
            AssertConstructors(widget);
            AssertPrimitiveMethods(widget);
            AssertFields(widget);
            AssertReferenceMethods(widget);
            AssertArrays(widget);
            AssertConstants(widget);
            AssertInheritance(widget, includeInherited);
            AssertInterface(contract);
            AssertEnum(mode);
            AssertNested(nested);
            AssertInner(inner);
            AssertDocumentation(widget);
            Check.True(!run.Diagnostics.Any(diagnostic => diagnostic.GetMessage().Contains("EXTRACTOR_MUST_NOT_INITIALIZE", StringComparison.Ordinal)),
                "Extraction must inspect compiler metadata without running throwing static initializers.");
        }

        /// <summary>
        /// Requires every erased overload to survive through compilable deterministic aliases and factories.
        /// </summary>
        /// <param name="run">The completed collision-fixture generation.</param>
        public static void AssertCollisionApi(GeneratorRun run)
        {
            run.AssertCompiles();
            run.AssertDiagnostic("AJG006");
            run.AssertDiagnostic("AJG007");
            var warnings = run.Diagnostics.Where(diagnostic => diagnostic.Id == "AJG006").ToArray();
            Check.True(warnings.Length >= 4 && warnings.All(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning),
                "All erased constructor/method overloads must report AJG006 warnings, not compilation-stopping errors.");
            var type = Type(run, "CollisionWrapper");
            AssertWrappingConstructor(type);
            Check.Equal(1, type.InstanceConstructors.Length, "Erased Java constructors must use factories rather than overwrite the wrapping constructor.");
            var factoryNames = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            var aliasNames = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in new[] { "Ljava/util/List;", "Ljava/util/Set;" })
            {
                var descriptor = "(" + parameter + ")V";
                var factory = FindForwardingMethod(type, "Construct", null, descriptor);
                Check.True(factory.IsStatic && factory.DeclaredAccessibility == Accessibility.Public, descriptor + " constructor factory must be public static.");
                Check.Equal(type.Name, factory.ReturnType.Name, descriptor + " factory result type");
                Check.True(factoryNames.Add(factory.Name), "Each erased constructor must have a distinct deterministic factory name.");
                Check.Equal("AndroidJavaObject", factory.Parameters.Single().Type.Name, descriptor + " erased factory argument");
                AssertNullable(factory.Parameters[0].Type, descriptor + " nullable factory argument");
                var alias = FindForwardingMethod(type, "Call", "select", descriptor);
                Check.True(!alias.IsStatic && alias.ReturnsVoid, descriptor + " alias must retain the instance void method.");
                Check.True(aliasNames.Add(alias.Name), "Each erased method overload must have a distinct alias.");
                Check.Equal("AndroidJavaObject", alias.Parameters.Single().Type.Name, descriptor + " erased alias argument");
                AssertForwarding(alias, "Call", "select", descriptor);
            }
            var lower = FindForwardingMethod(type, "Call", "ping", "()V");
            var upper = FindForwardingMethod(type, "Call", "Ping", "()V");
            Check.True(lower.Name != upper.Name, "Java methods differing only after PascalCase conversion must both survive under distinct names.");
            AssertField(type, "Sample", "sample", "I", "Int32");
            var sample = FindForwardingMethod(type, "Call", "Sample", "()I");
            Check.True(sample.Name != "Sample", "A Java method colliding with a field must be renamed, not discarded.");
        }

        /// <summary>
        /// Finds the unique alias or factory forwarding a particular original descriptor.
        /// </summary>
        /// <param name="type">The generated collision wrapper.</param>
        /// <param name="operation">The AndroidJni operation under inspection.</param>
        /// <param name="javaName">The Java name, or null for constructor factories.</param>
        /// <param name="descriptor">The exact JVM descriptor that must survive.</param>
        /// <returns>The preserved generated alias or factory.</returns>
        private static IMethodSymbol FindForwardingMethod(INamedTypeSymbol type, string operation, string? javaName, string descriptor)
        {
            var matches = type.GetMembers().OfType<IMethodSymbol>().Where(method => method.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax())
                .SelectMany(syntax => syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
                .Any(invocation => invocation.Expression.ToString().Contains("AndroidJni." + operation, StringComparison.Ordinal)
                    && invocation.ArgumentList.Arguments.Any(argument => argument.Expression is LiteralExpressionSyntax literal && literal.Token.ValueText == descriptor)
                    && (javaName is null || invocation.ArgumentList.Arguments.Any(argument => argument.Expression is LiteralExpressionSyntax literal && literal.Token.ValueText == javaName)))).ToArray();
            Check.Equal(1, matches.Length, type.Name + " preserved " + (javaName ?? "constructor") + " " + descriptor);
            return matches[0];
        }

        /// <summary>
        /// Finds a requested generated wrapper type by its full fixture namespace.
        /// </summary>
        /// <param name="run">The completed compilation.</param>
        /// <param name="name">The short wrapper name.</param>
        /// <returns>The generated named type symbol.</returns>
        public static INamedTypeSymbol Type(GeneratorRun run, string name)
        {
            return Check.NotNull(run.Output.GetTypeByMetadataName("Fixtures.Wrappers." + name), "wrapper " + name);
        }

        /// <summary>
        /// Finds one generated method using its name and optional parameter type.
        /// </summary>
        /// <param name="type">The wrapper containing the method.</param>
        /// <param name="name">The expected PascalCase name.</param>
        /// <param name="parameterType">The optional short or fully qualified overload parameter type.</param>
        /// <returns>The unique expected method symbol.</returns>
        public static IMethodSymbol Method(INamedTypeSymbol type, string name, string? parameterType = null)
        {
            var methods = type.GetMembers(name).OfType<IMethodSymbol>();
            if (parameterType is not null)
            {
                methods = methods.Where(method => method.Parameters.Length == 1
                    && (method.Parameters[0].Type.Name == parameterType || method.Parameters[0].Type.ToDisplayString().TrimEnd('?') == parameterType));
            }
            var matches = methods.ToArray();
            Check.Equal(1, matches.Length, type.Name + "." + name + " matching method count");
            return matches[0];
        }

        /// <summary>
        /// Requires the ordinary Java-object wrapping constructor and its ownership default.
        /// </summary>
        /// <param name="type">The generated wrapper type.</param>
        private static void AssertWrappingConstructor(INamedTypeSymbol type)
        {
            var constructors = type.InstanceConstructors.Where(constructor => constructor.Parameters.Length == 2
                && constructor.Parameters[0].Type.ToDisplayString() == "UnityEngine.AndroidJavaObject"
                && constructor.Parameters[1].Type.SpecialType == SpecialType.System_Boolean).ToArray();
            Check.Equal(1, constructors.Length, type.Name + " wrapping constructor count");
            var constructor = constructors[0];
            Check.Equal(Accessibility.Public, constructor.DeclaredAccessibility, type.Name + " wrapping accessibility");
            Check.True(constructor.Parameters[1].HasExplicitDefaultValue, type.Name + " wrapping ownership argument must be optional.");
            Check.Equal((object)true, constructor.Parameters[1].ExplicitDefaultValue, type.Name + " ownership default");
        }

        /// <summary>
        /// Requires a Java type without instantiable public constructors to expose only reference wrapping.
        /// </summary>
        /// <param name="type">The generated wrapper for a Java type that cannot be constructed.</param>
        /// <exception cref="InvalidOperationException">The wrapper exposes Java instantiation or an invalid wrapping constructor.</exception>
        public static void AssertReferenceOnlyConstruction(INamedTypeSymbol type)
        {
            Check.Equal(1, type.InstanceConstructors.Length,
                type.Name + " must expose only the reference-wrapping constructor");
            AssertWrappingConstructor(type);
            Check.True(!type.GetMembers().OfType<IMethodSymbol>().SelectMany(method => method.DeclaringSyntaxReferences)
                .SelectMany(reference => reference.GetSyntax().DescendantNodes().OfType<InvocationExpressionSyntax>())
                .Any(invocation => invocation.Expression.ToString().Contains("AndroidJni.Construct", StringComparison.Ordinal)),
                type.Name + " must not expose a Java constructor factory.");
        }

        /// <summary>
        /// Verifies constructor visibility and default-constructor behavior across Java input representations.
        /// </summary>
        /// <param name="run">The completed constructor-fixture generation.</param>
        /// <exception cref="InvalidOperationException">Generation fails or the constructor surface differs from the public Java API.</exception>
        public static void AssertConstructorVisibility(GeneratorRun run)
        {
            run.AssertCompiles();
            var mixed = Type(run, "MixedWrapper");
            AssertInstantiationConstructors(mixed, "(I)V", "(Ljava/lang/String;)V");
            var text = mixed.InstanceConstructors.Single(constructor => constructor.Parameters.Length == 1
                && constructor.Parameters[0].Type.SpecialType == SpecialType.System_String);
            AssertNullable(text.Parameters[0].Type, "nullable public constructor string argument");
            AssertInstantiationConstructors(Type(run, "ParameterizedWrapper"), "(C)V");
            AssertInstantiationConstructors(Type(run, "DefaultWrapper"), "()V");
            foreach (var name in new[] { "NoPublicWrapper", "PublicAbstractWrapper", "ProtectedAbstractWrapper", "InterfaceWrapper", "EnumWrapper" })
            {
                AssertReferenceOnlyConstruction(Type(run, name));
            }
        }

        /// <summary>
        /// Requires actual SDK public constructor overloads while rejecting nonconstructible SDK types.
        /// </summary>
        /// <param name="run">The completed SDK-only generation.</param>
        /// <exception cref="InvalidOperationException">Generation fails or the SDK constructor contract is not preserved.</exception>
        public static void AssertSdkConstructors(GeneratorRun run)
        {
            run.AssertCompiles();
            var intent = Type(run, "IntentWrapper");
            AssertInstantiationConstructors(intent, "()V", "(Landroid/content/Context;Ljava/lang/Class;)V",
                "(Landroid/content/Intent;)V", "(Ljava/lang/String;)V", "(Ljava/lang/String;Landroid/net/Uri;)V",
                "(Ljava/lang/String;Landroid/net/Uri;Landroid/content/Context;Ljava/lang/Class;)V");
            var copy = intent.InstanceConstructors.Single(constructor => constructor.Parameters.Length == 1
                && constructor.Parameters[0].Type.Name == "IntentWrapper");
            AssertNullable(copy.Parameters[0].Type, "nullable typed SDK copy-constructor argument");
            var text = intent.InstanceConstructors.Single(constructor => constructor.Parameters.Length == 1
                && constructor.Parameters[0].Type.SpecialType == SpecialType.System_String);
            AssertNullable(text.Parameters[0].Type, "nullable SDK constructor string argument");
            foreach (var name in new[] { "LooperWrapper", "RunnableWrapper", "InputStreamWrapper" })
            {
                AssertReferenceOnlyConstruction(Type(run, name));
            }
        }

        /// <summary>
        /// Requires exactly the listed Java instantiation descriptors plus the reference-wrapping constructor.
        /// </summary>
        /// <param name="type">The generated concrete wrapper.</param>
        /// <param name="descriptors">The complete expected set of public Java constructor descriptors.</param>
        /// <exception cref="InvalidOperationException">The generated constructors do not exactly match the expected descriptors.</exception>
        private static void AssertInstantiationConstructors(INamedTypeSymbol type, params string[] descriptors)
        {
            AssertWrappingConstructor(type);
            Check.Equal(descriptors.Length + 1, type.InstanceConstructors.Length, type.Name + " constructor count");
            foreach (var descriptor in descriptors)
            {
                var constructors = type.InstanceConstructors.Where(constructor => constructor.DeclaringSyntaxReferences
                    .Select(reference => reference.GetSyntax()).OfType<ConstructorDeclarationSyntax>()
                    .Any(syntax => syntax.Initializer is not null && syntax.Initializer.ArgumentList.Arguments.Any(argument =>
                        argument.Expression is LiteralExpressionSyntax literal && literal.Token.ValueText == descriptor))).ToArray();
                Check.Equal(1, constructors.Length, type.Name + " public constructor " + descriptor);
                Check.Equal(Accessibility.Public, constructors[0].DeclaredAccessibility, type.Name + " constructor accessibility");
                AssertConstructorDescriptor(constructors[0], descriptor);
            }
        }

        /// <summary>
        /// Verifies primitive and typed nullable constructor overloads and exact JVM signatures.
        /// </summary>
        /// <param name="widget">The generated widget wrapper.</param>
        private static void AssertConstructors(INamedTypeSymbol widget)
        {
            var empty = widget.InstanceConstructors.Single(constructor => constructor.Parameters.Length == 0);
            AssertConstructorDescriptor(empty, "()V");
            var primitive = widget.InstanceConstructors.Single(constructor => constructor.Parameters.Length == 8);
            var types = new[]
            {
                SpecialType.System_Boolean, SpecialType.System_SByte, SpecialType.System_Char, SpecialType.System_Int16,
                SpecialType.System_Int32, SpecialType.System_Int64, SpecialType.System_Single, SpecialType.System_Double
            };
            for (var index = 0; index < types.Length; index++)
            {
                Check.Equal(types[index], primitive.Parameters[index].Type.SpecialType, "primitive constructor parameter " + index);
            }
            AssertConstructorDescriptor(primitive, "(ZBCSIJFD)V");
            var contract = widget.InstanceConstructors.Single(constructor => constructor.Parameters.Length == 1 && constructor.Parameters[0].Type.Name == "ContractWrapper");
            AssertNullable(contract.Parameters[0].Type, "nullable interface constructor argument");
            AssertConstructorDescriptor(contract, "(Lfixtures/Contract;)V");
            var peer = widget.InstanceConstructors.Single(constructor => constructor.Parameters.Length == 1 && constructor.Parameters[0].Type.Name == "WidgetWrapper");
            AssertNullable(peer.Parameters[0].Type, "nullable concrete constructor argument");
            AssertConstructorDescriptor(peer, "(Lfixtures/Widget;)V");
        }

        /// <summary>
        /// Verifies the primitive mapping, including signed byte and UTF-16 char.
        /// </summary>
        /// <param name="widget">The generated widget wrapper.</param>
        private static void AssertPrimitiveMethods(INamedTypeSymbol widget)
        {
            var names = new[] { "Boolean", "Byte", "Char", "Short", "Int", "Long", "Float", "Double" };
            var descriptors = new[] { "Z", "B", "C", "S", "I", "J", "F", "D" };
            var types = new[]
            {
                SpecialType.System_Boolean, SpecialType.System_SByte, SpecialType.System_Char, SpecialType.System_Int16,
                SpecialType.System_Int32, SpecialType.System_Int64, SpecialType.System_Single, SpecialType.System_Double
            };
            for (var index = 0; index < names.Length; index++)
            {
                var method = Method(widget, names[index] + "Value");
                Check.Equal(types[index], method.ReturnType.SpecialType, method.Name + " result type");
                Check.Equal(types[index], method.Parameters.Single().Type.SpecialType, method.Name + " argument type");
                var javaName = char.ToLowerInvariant(names[index][0]) + names[index].Substring(1) + "Value";
                AssertForwarding(method, "Call", javaName, "(" + descriptors[index] + ")" + descriptors[index]);
            }
            var consume = Method(widget, "Consume");
            Check.True(consume.ReturnsVoid, "Java void must map to C# void.");
            AssertForwarding(consume, "Call", "consume", "(I)V");
        }

        /// <summary>
        /// Verifies getter-only fields, field hiding, and nullable wrapper field types.
        /// </summary>
        /// <param name="widget">The generated widget wrapper.</param>
        private static void AssertFields(INamedTypeSymbol widget)
        {
            AssertField(widget, "MutableCount", "mutableCount", "I", "Int32");
            AssertField(widget, "HiddenValue", "hiddenValue", "Ljava/lang/String;", "String");
            AssertField(widget, "Peer", "peer", "Lfixtures/Widget;", "WidgetWrapper");
            AssertField(widget, "Unknown", "unknown", "Lfixtures/Unknown;", "AndroidJavaObject");
            AssertField(widget, "Names", "names", "Ljava/util/List;", "AndroidJavaObject");
            AssertField(widget, "Bytes", "bytes", "[B", null);
            AssertField(widget, "Chars", "chars", "[C", null);
            AssertField(widget, "Peers", "peers", "[[Lfixtures/Widget;", null);
            var instance = AssertField(widget, "Instance", "INSTANCE", "Lfixtures/Widget;", "WidgetWrapper");
            Check.True(instance.IsStatic, "Static final nonconstant references must be static getter properties, not const fields.");
            foreach (var name in new[] { "HiddenValue", "Peer", "Unknown", "Names", "Peers", "Instance" })
            {
                AssertNullable(Property(widget, name).Type, name + " field reference");
            }
        }

        /// <summary>
        /// Verifies annotated references, unknown objects, generic erasure, and typed overloads.
        /// </summary>
        /// <param name="widget">The generated widget wrapper.</param>
        private static void AssertReferenceMethods(INamedTypeSymbol widget)
        {
            AssertReferenceMethod(widget, "Echo", "echo", "(Ljava/lang/String;)Ljava/lang/String;", "String");
            AssertReferenceMethod(widget, "Typed", "typed", "(Lfixtures/Widget;)Lfixtures/Widget;", "WidgetWrapper");
            AssertReferenceMethod(widget, "Contract", "contract", "(Lfixtures/Contract;)Lfixtures/Contract;", "ContractWrapper");
            AssertReferenceMethod(widget, "UnknownValue", "unknownValue", "(Lfixtures/Unknown;)Lfixtures/Unknown;", "AndroidJavaObject");
            AssertReferenceMethod(widget, "Erase", "erase", "(Ljava/lang/Object;)Ljava/lang/Object;", "AndroidJavaObject");
            AssertReferenceMethod(widget, "Bounded", "bounded", "(Lfixtures/Widget;)Lfixtures/Widget;", "WidgetWrapper");
            AssertReferenceMethod(widget, "List", "list", "(Ljava/util/List;)Ljava/util/List;", "AndroidJavaObject");
            var contract = Method(widget, "Choose", "ContractWrapper");
            var concrete = Method(widget, "Choose", "WidgetWrapper");
            AssertForwarding(contract, "Call", "choose", "(Lfixtures/Contract;)Lfixtures/Contract;");
            AssertForwarding(concrete, "Call", "choose", "(Lfixtures/Widget;)Lfixtures/Widget;");
            AssertNullable(contract.Parameters[0].Type, "choose nullable contract");
            AssertNullable(concrete.Parameters[0].Type, "choose nullable concrete argument");
        }

        /// <summary>
        /// Verifies primitive, typed, unknown, string, vararg, and jagged array signatures.
        /// </summary>
        /// <param name="widget">The generated widget wrapper.</param>
        private static void AssertArrays(INamedTypeSymbol widget)
        {
            var names = new[] { "Boolean", "Byte", "Char", "Short", "Int", "Long", "Float", "Double" };
            var descriptors = new[] { "Z", "B", "C", "S", "I", "J", "F", "D" };
            var types = new[]
            {
                SpecialType.System_Boolean, SpecialType.System_SByte, SpecialType.System_Char, SpecialType.System_Int16,
                SpecialType.System_Int32, SpecialType.System_Int64, SpecialType.System_Single, SpecialType.System_Double
            };
            for (var index = 0; index < names.Length; index++)
            {
                var method = Method(widget, names[index] + "Array");
                var result = Array(method.ReturnType, 1, method.Name + " result");
                var argument = Array(method.Parameters[0].Type, 1, method.Name + " argument");
                Check.Equal(types[index], result.SpecialType, method.Name + " result element type");
                Check.Equal(types[index], argument.SpecialType, method.Name + " argument element type");
                var javaName = char.ToLowerInvariant(names[index][0]) + names[index].Substring(1) + "Array";
                AssertForwarding(method, "Call", javaName, "([" + descriptors[index] + ")[" + descriptors[index]);
            }
            AssertReferenceArray(widget, "StringArray", "stringArray", "Ljava/lang/String;", "String", 1);
            AssertReferenceArray(widget, "TypedArray", "typedArray", "Lfixtures/Widget;", "WidgetWrapper", 1);
            AssertReferenceArray(widget, "UnknownArray", "unknownArray", "Lfixtures/Unknown;", "AndroidJavaObject", 1);
            AssertReferenceArray(widget, "Jagged", "jagged", "Lfixtures/Widget;", "WidgetWrapper", 2);
            var primitives = Method(widget, "JaggedPrimitives");
            Check.Equal(SpecialType.System_Int32, Array(primitives.ReturnType, 2, "jagged primitive result").SpecialType, "jagged primitive leaf");
            Array(primitives.Parameters[0].Type, 2, "jagged primitive argument");
            AssertForwarding(primitives, "Call", "jaggedPrimitives", "([[I)[[I");
            var varargs = Method(widget, "Varargs");
            Check.Equal(SpecialType.System_SByte, Array(varargs.Parameters[0].Type, 1, "varargs").SpecialType, "varargs signed-byte leaf");
            AssertForwarding(varargs, "Call", "varargs", "([B)V");
        }

        /// <summary>
        /// Verifies compile-time constants, including non-finite floats and XML-sensitive strings.
        /// </summary>
        /// <param name="widget">The generated widget wrapper.</param>
        private static void AssertConstants(INamedTypeSymbol widget)
        {
            AssertConstant(widget, "Revision", 1, SpecialType.System_Int32);
            AssertConstant(widget, "ByteMin", (sbyte)-128, SpecialType.System_SByte);
            AssertConstant(widget, "Letter", '\u03A9', SpecialType.System_Char);
            AssertConstant(widget, "Enabled", true, SpecialType.System_Boolean);
            AssertConstant(widget, "ShortMin", (short)-32768, SpecialType.System_Int16);
            AssertConstant(widget, "LongMax", long.MaxValue, SpecialType.System_Int64);
            AssertConstant(widget, "Fraction", 1.25f, SpecialType.System_Single);
            AssertConstant(widget, "FloatNan", float.NaN, SpecialType.System_Single);
            AssertConstant(widget, "FloatInfinity", float.PositiveInfinity, SpecialType.System_Single);
            AssertConstant(widget, "DoubleNegativeInfinity", double.NegativeInfinity, SpecialType.System_Double);
            AssertConstant(widget, "DoubleNan", double.NaN, SpecialType.System_Double);
            AssertConstant(widget, "Text", "Line\n\"\\\t\u4F60\u597D\0", SpecialType.System_String);
            AssertConstant(widget, "Surrogates", "\uD800X\uDC00", SpecialType.System_String);
        }

        /// <summary>
        /// Verifies nearest covariant overrides, field hiding, and inherited public members.
        /// </summary>
        /// <param name="widget">The generated widget wrapper.</param>
        /// <param name="includeInherited">Whether inherited members were requested.</param>
        private static void AssertInheritance(INamedTypeSymbol widget, bool includeInherited)
        {
            var covariant = Method(widget, "Covariant");
            Check.Equal("WidgetWrapper", covariant.ReturnType.Name, "covariant override result");
            AssertForwarding(covariant, "Call", "covariant", "()Lfixtures/Widget;");
            Check.Equal(includeInherited ? 1 : 0, widget.GetMembers("InheritedValue").Length, "inherited instance-method inclusion");
            Check.Equal(includeInherited ? 1 : 0, widget.GetMembers("InheritedStatic").Length, "inherited static-method inclusion");
            Check.Equal(includeInherited ? 1 : 0, widget.GetMembers("InheritedCount").Length, "inherited field inclusion");
            if (includeInherited)
            {
                AssertForwarding(Method(widget, "InheritedValue"), "Call", "inheritedValue", "(I)I");
                var inheritedStatic = Method(widget, "InheritedStatic");
                Check.True(inheritedStatic.IsStatic, "Inherited static methods must stay static.");
                AssertForwarding(inheritedStatic, "Call", "inheritedStatic", "(I)I");
                AssertField(widget, "InheritedCount", "inheritedCount", "J", "Int64");
                AssertForwarding(Method(widget, "DefaultValue"), "Call", "defaultValue", "(I)I");
            }
        }

        /// <summary>
        /// Requires interface methods and constants without any Java instantiation constructor.
        /// </summary>
        /// <param name="contract">The interface's generated C# reference wrapper.</param>
        private static void AssertInterface(INamedTypeSymbol contract)
        {
            AssertReferenceOnlyConstruction(contract);
            AssertConstant(contract, "Answer", 42, SpecialType.System_Int32);
            AssertForwarding(Method(contract, "Describe"), "Call", "describe", "()Ljava/lang/String;");
            AssertForwarding(Method(contract, "DefaultValue"), "Call", "defaultValue", "(I)I");
            var staticMethod = Method(contract, "StaticValue");
            Check.True(staticMethod.IsStatic, "Java interface static method must stay static.");
            AssertForwarding(staticMethod, "Call", "staticValue", "(J)J");
        }

        /// <summary>
        /// Requires compiler-generated enum methods and typed nullable enum constants.
        /// </summary>
        /// <param name="mode">The enum's generated reference wrapper.</param>
        private static void AssertEnum(INamedTypeSymbol mode)
        {
            AssertReferenceOnlyConstruction(mode);
            var values = Method(mode, "Values");
            Check.True(values.IsStatic, "Enum values must be static.");
            Check.Equal("ModeWrapper", Array(values.ReturnType, 1, "enum values").Name, "enum values element type");
            AssertForwarding(values, "Call", "values", "()[Lfixtures/Mode;");
            var valueOf = Method(mode, "ValueOf", "String");
            Check.True(valueOf.IsStatic, "Enum valueOf must be static.");
            Check.Equal("ModeWrapper", valueOf.ReturnType.Name, "enum valueOf result");
            AssertNullable(valueOf.ReturnType, "enum valueOf reference");
            AssertForwarding(valueOf, "Call", "valueOf", "(Ljava/lang/String;)Lfixtures/Mode;");
            AssertField(mode, "First", "FIRST", "Lfixtures/Mode;", "ModeWrapper");
            AssertField(mode, "Second", "SECOND", "Lfixtures/Mode;", "ModeWrapper");
        }

        /// <summary>
        /// Requires binary dollar-sign descriptors for a public nested Java class.
        /// </summary>
        /// <param name="nested">The nested Java class's top-level C# wrapper.</param>
        private static void AssertNested(INamedTypeSymbol nested)
        {
            AssertField(nested, "Marker", "marker", "C", "Char");
            var constructor = nested.InstanceConstructors.Single(item => item.Parameters.Length == 1 && item.Parameters[0].Type.SpecialType == SpecialType.System_Char);
            AssertConstructorDescriptor(constructor, "(C)V");
            AssertReferenceMethod(nested, "RoundTrip", "roundTrip", "(Lfixtures/Widget$Nested;)Lfixtures/Widget$Nested;", "NestedWrapper");
        }

        /// <summary>
        /// Requires the JVM's hidden leading enclosing-instance parameter in every member-class constructor.
        /// </summary>
        /// <param name="inner">The non-static member class's generated top-level wrapper.</param>
        private static void AssertInner(INamedTypeSymbol inner)
        {
            AssertField(inner, "Count", "count", "I", "Int32");
            var constructors = inner.InstanceConstructors.Where(constructor => constructor.Parameters.Length > 0 && constructor.Parameters[0].Type.Name == "WidgetWrapper").ToArray();
            Check.Equal(3, constructors.Length, "Non-static member class Java constructor count");
            foreach (var constructor in constructors)
            {
                Check.Equal("enclosingInstance", constructor.Parameters[0].Name, "Synthetic outer reference C# parameter name");
                Check.Equal("WidgetWrapper", constructor.Parameters[0].Type.Name, "Synthetic outer reference typed wrapper");
            }
            var empty = constructors.Single(constructor => constructor.Parameters.Length == 1);
            AssertConstructorDescriptor(empty, "(Lfixtures/Widget;)V");
            var primitive = constructors.Single(constructor => constructor.Parameters.Length == 2 && constructor.Parameters[1].Type.SpecialType == SpecialType.System_Int32);
            Check.Equal("count", primitive.Parameters[1].Name, "Inner constructor visible Java parameter name");
            AssertConstructorDescriptor(primitive, "(Lfixtures/Widget;I)V");
            var contract = constructors.Single(constructor => constructor.Parameters.Length == 2 && constructor.Parameters[1].Type.Name == "ContractWrapper");
            AssertConstructorDescriptor(contract, "(Lfixtures/Widget;Lfixtures/Contract;)V");
            var outer = Method(inner, "Outer");
            Check.Equal("WidgetWrapper", outer.ReturnType.Name, "Inner enclosing-widget result type");
            AssertForwarding(outer, "Call", "outer", "()Lfixtures/Widget;");
        }

        /// <summary>
        /// Requires valid XML documentation and the original Java parameter name and text.
        /// </summary>
        /// <param name="widget">The documented wrapper.</param>
        private static void AssertDocumentation(INamedTypeSymbol widget)
        {
            var typeXml = XDocument.Parse(Check.NotNull(widget.GetDocumentationCommentXml(), "widget documentation XML"));
            Check.True(typeXml.Descendants("summary").Any(element => element.Value.Contains("documented widget", StringComparison.OrdinalIgnoreCase)),
                "Java type summary must be present in generated XML documentation.");
            var echo = Method(widget, "Echo");
            Check.Equal("text", echo.Parameters[0].Name, "documented Java parameter name");
            var methodXml = XDocument.Parse(Check.NotNull(echo.GetDocumentationCommentXml(), "echo documentation XML"));
            Check.True(methodXml.Descendants("summary").Any(element => element.Value.Contains("A < B & B > C", StringComparison.Ordinal)),
                "XML-sensitive summary must be escaped without losing its text.");
            Check.True(methodXml.Descendants("param").Any(element => (string?)element.Attribute("name") == "text"
                    && element.Value.Contains("<tag> & \"quotes\"", StringComparison.Ordinal)),
                "Java @param documentation must be valid XML and retain its source text.");
            Check.True(methodXml.Descendants("returns").Any(element => element.Value.Contains("nullable text", StringComparison.Ordinal)),
                "Java return documentation must be emitted.");
        }

        /// <summary>
        /// Finds one generated property by its PascalCase name.
        /// </summary>
        /// <param name="type">The declaring wrapper.</param>
        /// <param name="name">The expected property name.</param>
        /// <returns>The getter property symbol.</returns>
        private static IPropertySymbol Property(INamedTypeSymbol type, string name)
        {
            return Check.NotNull(type.GetMembers(name).OfType<IPropertySymbol>().SingleOrDefault(), type.Name + "." + name + " getter property");
        }

        /// <summary>
        /// Requires a getter-only field property and exact JNI GetField forwarding.
        /// </summary>
        /// <param name="type">The declaring wrapper.</param>
        /// <param name="name">The expected PascalCase property name.</param>
        /// <param name="javaName">The original case-sensitive Java field name.</param>
        /// <param name="descriptor">The exact JVM field descriptor.</param>
        /// <param name="typeName">The optional expected C# property's short type name.</param>
        /// <returns>The checked getter-only property.</returns>
        private static IPropertySymbol AssertField(INamedTypeSymbol type, string name, string javaName, string descriptor, string? typeName)
        {
            var property = Property(type, name);
            Check.True(property.GetMethod is not null && property.SetMethod is null, type.Name + "." + name + " must be getter-only even for mutable Java fields.");
            if (typeName is not null)
            {
                Check.Equal(typeName, property.Type.Name, name + " property type");
            }
            AssertForwarding(property, "GetField", javaName, descriptor);
            return property;
        }

        /// <summary>
        /// Requires a correctly typed nullable reference method and exact Call forwarding.
        /// </summary>
        /// <param name="type">The declaring wrapper.</param>
        /// <param name="name">The expected PascalCase method name.</param>
        /// <param name="javaName">The original Java method name.</param>
        /// <param name="descriptor">The erased exact JVM method descriptor.</param>
        /// <param name="typeName">The expected argument and return short type name.</param>
        private static void AssertReferenceMethod(INamedTypeSymbol type, string name, string javaName, string descriptor, string typeName)
        {
            var method = Method(type, name);
            Check.Equal(0, method.TypeParameters.Length, name + " Java generics must be erased");
            Check.Equal(typeName, method.ReturnType.Name, name + " result type");
            Check.Equal(typeName, method.Parameters[0].Type.Name, name + " argument type");
            AssertNullable(method.ReturnType, name + " nullable result");
            AssertNullable(method.Parameters[0].Type, name + " nullable argument");
            AssertForwarding(method, "Call", javaName, descriptor);
        }

        /// <summary>
        /// Requires nullable reference array dimensions and nullable leaf references.
        /// </summary>
        /// <param name="widget">The array fixture wrapper.</param>
        /// <param name="name">The expected PascalCase method name.</param>
        /// <param name="javaName">The original Java method name.</param>
        /// <param name="elementDescriptor">The exact JVM leaf descriptor.</param>
        /// <param name="typeName">The expected reference leaf type.</param>
        /// <param name="dimensions">The required jagged array dimension count.</param>
        private static void AssertReferenceArray(INamedTypeSymbol widget, string name, string javaName, string elementDescriptor, string typeName, int dimensions)
        {
            var method = Method(widget, name);
            var result = Array(method.ReturnType, dimensions, name + " result");
            var argument = Array(method.Parameters[0].Type, dimensions, name + " argument");
            Check.Equal(typeName, result.Name, name + " result leaf");
            Check.Equal(typeName, argument.Name, name + " argument leaf");
            AssertNullable(result, name + " nullable result element");
            AssertNullable(argument, name + " nullable argument element");
            var arrayDescriptor = new string('[', dimensions) + elementDescriptor;
            AssertForwarding(method, "Call", javaName, "(" + arrayDescriptor + ")" + arrayDescriptor);
        }

        /// <summary>
        /// Walks a jagged array shape and requires every reference dimension to allow null.
        /// </summary>
        /// <param name="type">The method's result or parameter type.</param>
        /// <param name="dimensions">The expected number of array dimensions.</param>
        /// <param name="context">The method and direction under validation.</param>
        /// <returns>The non-array leaf element type.</returns>
        private static ITypeSymbol Array(ITypeSymbol type, int dimensions, string context)
        {
            for (var index = 0; index < dimensions; index++)
            {
                AssertNullable(type, context + " nullable array dimension " + index);
                var array = Check.NotNull(type as IArrayTypeSymbol, context + " array dimension " + index);
                Check.Equal(1, array.Rank, context + " must be jagged, not multidimensional");
                type = array.ElementType;
            }
            Check.True(type is not IArrayTypeSymbol, context + " has too many array dimensions.");
            return type;
        }

        /// <summary>
        /// Requires a reference type's nullable annotation to be explicit.
        /// </summary>
        /// <param name="type">The reference symbol to check.</param>
        /// <param name="context">The API position under validation.</param>
        private static void AssertNullable(ITypeSymbol type, string context)
        {
            Check.Equal(NullableAnnotation.Annotated, type.NullableAnnotation, context);
        }

        /// <summary>
        /// Requires a Java constant to be emitted as a true C# const of the exact primitive type.
        /// </summary>
        /// <param name="type">The declaring wrapper.</param>
        /// <param name="name">The expected PascalCase constant name.</param>
        /// <param name="expected">The expected boxed value, including NaN or embedded NUL.</param>
        /// <param name="specialType">The exact primitive or string type.</param>
        public static void AssertConstant(INamedTypeSymbol type, string name, object expected, SpecialType specialType)
        {
            var field = Check.NotNull(type.GetMembers(name).OfType<IFieldSymbol>().SingleOrDefault(), type.Name + "." + name + " constant");
            Check.True(field.IsConst && field.IsStatic && field.HasConstantValue, name + " must be a true public static compile-time const.");
            Check.Equal(Accessibility.Public, field.DeclaredAccessibility, name + " constant accessibility");
            Check.Equal(specialType, field.Type.SpecialType, name + " constant type");
            Check.Equal(expected, field.ConstantValue, name + " constant value");
        }

        /// <summary>
        /// Requires a generated JNI invocation to contain the original name and exact descriptor together.
        /// </summary>
        /// <param name="symbol">The generated method or field property.</param>
        /// <param name="operation">The required AndroidJni method name.</param>
        /// <param name="javaName">The original Java member name.</param>
        /// <param name="descriptor">The exact erased JVM descriptor.</param>
        private static void AssertForwarding(ISymbol symbol, string operation, string javaName, string descriptor)
        {
            var found = symbol.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax())
                .SelectMany(syntax => syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
                .Any(invocation => invocation.Expression.ToString().Contains("AndroidJni." + operation, StringComparison.Ordinal)
                    && invocation.ArgumentList.Arguments.Any(argument => argument.Expression is LiteralExpressionSyntax literal && literal.Token.ValueText == javaName)
                    && invocation.ArgumentList.Arguments.Any(argument => argument.Expression is LiteralExpressionSyntax literal && literal.Token.ValueText == descriptor));
            Check.True(found, symbol.ContainingType.Name + "." + symbol.Name + " must forward " + javaName + " " + descriptor + " to AndroidJni." + operation + ".");
        }

        /// <summary>
        /// Requires the exact Java class, JVM descriptor, and ordered parameter forwarding in the JavaObject base initializer.
        /// </summary>
        /// <param name="constructor">The generated Java instantiation constructor.</param>
        /// <param name="descriptor">The exact descriptor, including its void return.</param>
        /// <exception cref="InvalidOperationException">The constructor forwards an incorrect class, descriptor, or parameter array.</exception>
        private static void AssertConstructorDescriptor(IMethodSymbol constructor, string descriptor)
        {
            var context = constructor.ContainingType.Name + " constructor " + descriptor;
            var mapping = constructor.ContainingType.GetAttributes().Single(attribute =>
                attribute.AttributeClass?.ToDisplayString() == "MajdataPlay.Platform.Android.JavaClassAttribute");
            var expectedClassName = Check.NotNull(mapping.ConstructorArguments[0].Value as string, context + " Java class mapping");
            var syntax = constructor.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax())
                .OfType<ConstructorDeclarationSyntax>().Single();
            var initializer = Check.NotNull(syntax.Initializer, context + " base initializer");
            Check.True(initializer.IsKind(SyntaxKind.BaseConstructorInitializer), context + " must invoke JavaObject through base.");
            Check.Equal(3, initializer.ArgumentList.Arguments.Count, context + " base argument count");
            var className = Check.NotNull(initializer.ArgumentList.Arguments[0].Expression as LiteralExpressionSyntax,
                context + " forwarded Java class literal");
            Check.Equal(expectedClassName, className.Token.ValueText, context + " forwarded Java class");
            var signature = Check.NotNull(initializer.ArgumentList.Arguments[1].Expression as LiteralExpressionSyntax,
                context + " forwarded descriptor literal");
            Check.Equal(descriptor, signature.Token.ValueText, context + " forwarded JVM descriptor");
            var array = Check.NotNull(initializer.ArgumentList.Arguments[2].Expression as ArrayCreationExpressionSyntax,
                context + " forwarded parameter array");
            var values = Check.NotNull(array.Initializer, context + " parameter array initializer").Expressions;
            Check.Equal(constructor.Parameters.Length, values.Count, context + " forwarded parameter count");
            for (var index = 0; index < values.Count; index++)
            {
                var argument = Check.NotNull(values[index] as IdentifierNameSyntax, context + " parameter at position " + index);
                Check.Equal(constructor.Parameters[index].Name, argument.Identifier.ValueText,
                    context + " forwarded parameter at position " + index);
            }
        }
    }
}
