#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MajdataPlay.Tests.AndroidJavaGeneratorValidation
{
    /// <summary>
    /// Validates SDK and fixture Object overrides without constructing Java references or executing JNI.
    /// </summary>
    internal static class ObjectOverrideCases
    {
        /// <summary>
        /// Executes one bounded Object-override regression using the production generator.
        /// </summary>
        /// <param name="name">The stable regression name.</param>
        /// <param name="workspace">The isolated fixtures and selected Android SDK.</param>
        /// <exception cref="ArgumentException">The case name is unknown.</exception>
        /// <exception cref="InvalidOperationException">A generated contract differs from Java metadata.</exception>
        public static void Run(string name, FixtureWorkspace workspace)
        {
            var runner = new GeneratorRunner(workspace);
            switch (name)
            {
                case "object-overrides-source":
                    AssertFixtures(runner, workspace, workspace.SourceFiles(), name, true);
                    break;
                case "object-overrides-class":
                    AssertFixtures(runner, workspace, workspace.ClassFiles(), name, true);
                    break;
                case "object-overrides-jar":
                    AssertFixtures(runner, workspace, new[] { workspace.JarPath }, name, true);
                    break;
                case "object-overrides-no-inherited":
                    AssertFixtures(runner, workspace, new[] { workspace.JarPath }, name, false);
                    break;
                case "object-overrides-sdk":
                    AssertSdk(runner, workspace, name);
                    break;
                case "object-overrides-user-conflicts":
                    AssertUserConflicts(runner, workspace, name);
                    break;
                default:
                    throw new ArgumentException("Unknown Object-override case " + name + ".", nameof(name));
            }
        }

        /// <summary>
        /// Checks genuine overrides, inherited implementations, overloads, and interface redeclarations.
        /// </summary>
        /// <param name="runner">The actual generator driver factory.</param>
        /// <param name="workspace">The prepared SDK and fixture configuration.</param>
        /// <param name="sources">The selected Java source, class-file, or archive inputs.</param>
        /// <param name="name">The isolated case name.</param>
        /// <param name="includeInherited">Whether ordinary inherited Java methods are requested.</param>
        /// <exception cref="InvalidOperationException">A wrapper contract or deterministic rerun differs.</exception>
        private static void AssertFixtures(GeneratorRunner runner, FixtureWorkspace workspace, string[] sources,
            string name, bool includeInherited)
        {
            var names = new[] { "All", "EqualsOnly", "HashOnly", "StringOnly", "Inherited", "Plain", "Overloads", "StaticLookalikes", "RedeclaredContract", "InterfaceImplementer" };
            var types = names.Select(item => new KeyValuePair<string, string>("fixtures.ObjectOverrides$" + item, item + "Wrapper"));
            var source = Declarations(workspace, sources, types, includeInherited);
            var input = runner.CreateCompilation(source, name);
            var run = runner.Run(input, name);
            run.AssertCompiles();
            AssertContracts(run, "AllWrapper", true, true, true);
            AssertContracts(run, "EqualsOnlyWrapper", true, true, false);
            AssertContracts(run, "HashOnlyWrapper", false, true, false);
            AssertContracts(run, "StringOnlyWrapper", false, false, true);
            AssertContracts(run, "InheritedWrapper", true, true, true);
            foreach (var item in new[] { "Plain", "Overloads", "StaticLookalikes", "RedeclaredContract", "InterfaceImplementer" })
            {
                AssertContracts(run, item + "Wrapper", false, false, false);
            }
            foreach (var item in names)
            {
                AssertNoObjectAliases(run, item + "Wrapper");
            }
            AssertJavaMethod(run, "OverloadsWrapper", "equals", "(Lfixtures/ObjectOverrides$Overloads;)Z", false);
            AssertJavaMethod(run, "OverloadsWrapper", "hashCode", "(I)I", false);
            AssertJavaMethod(run, "OverloadsWrapper", "toString", "(Ljava/lang/String;)Ljava/lang/String;", false);
            Check.True(ApiAssertions.Type(run, "OverloadsWrapper").GetMembers("ToString").OfType<IMethodSymbol>()
                    .Any(method => !method.IsStatic && method.Parameters.Length == 1),
                "A Java toString overload must keep the natural C# ToString name instead of a renamed alias.");
            Check.Equal(0, ApiAssertions.Type(run, "StaticLookalikesWrapper").GetMembers("ToStringJavaMethod").Length,
                "A static Java toString lookalike must not produce a renamed Object alias.");
            AssertJavaMethod(run, "OverloadsWrapper", "getHashCode", "()I", false);
            AssertJavaMethod(run, "StaticLookalikesWrapper", "equals", "(Ljava/lang/String;)Z", true);
            AssertJavaMethod(run, "StaticLookalikesWrapper", "hashCode", "(I)I", true);
            AssertJavaMethod(run, "StaticLookalikesWrapper", "toString", "(Ljava/lang/String;)Ljava/lang/String;", true);
            Check.True(!run.Output.GetDiagnostics().Any(diagnostic => diagnostic.Id == "CS0659" || diagnostic.Id == "CS0661"),
                "Equality wrappers must forward GetHashCode and avoid CLR equality/hash contract warnings.");
            if (name == "object-overrides-source")
            {
                AssertManagedGuards(run);
                var reused = runner.Run(input, name + "-reused", run.Driver);
                var fresh = runner.Run(input, name + "-fresh");
                reused.AssertCompiles();
                fresh.AssertCompiles();
                Check.True(run.GeneratedSources.SequenceEqual(reused.GeneratedSources), "Object overrides and IEquatable declarations must remain deterministic on a reused driver.");
                Check.True(run.GeneratedSources.SequenceEqual(fresh.GeneratedSources), "Object overrides and IEquatable declarations must remain deterministic on a fresh driver.");
            }
        }

        /// <summary>
        /// Determines whether a generated method maps a Java member instead of a managed Object override.
        /// </summary>
        /// <param name="method">The generated method symbol.</param>
        /// <returns>Whether the method body forwards to a Java member.</returns>
        private static bool IsJavaMapped(IMethodSymbol method)
        {
            return Declaration(method).DescendantNodes().OfType<InvocationExpressionSyntax>().Any(invocation =>
                invocation.ArgumentList.Arguments.Count >= 4 &&
                invocation.ArgumentList.Arguments[2].Expression is LiteralExpressionSyntax);
        }

        /// <summary>
        /// Checks effective overrides directly in installed SDK metadata with both inherited-member settings.
        /// </summary>
        /// <param name="runner">The actual generator driver factory.</param>
        /// <param name="workspace">The selected SDK configuration.</param>
        /// <param name="name">The isolated case name.</param>
        /// <exception cref="InvalidOperationException">A real SDK wrapper has an incorrect CLR contract.</exception>
        private static void AssertSdk(GeneratorRunner runner, FixtureWorkspace workspace, string name)
        {
            var types = new[]
            {
                new KeyValuePair<string, string>("java.util.ArrayList", "ArrayListWrapper"),
                new KeyValuePair<string, string>("java.lang.StringBuilder", "StringBuilderWrapper"),
                new KeyValuePair<string, string>("java.lang.Object", "ObjectWrapper"),
                new KeyValuePair<string, string>("java.util.List", "ListWrapper"),
                new KeyValuePair<string, string>("android.net.Uri", "UriWrapper")
            };
            foreach (var includeInherited in new[] { true, false })
            {
                var mode = name + (includeInherited ? "-included" : "-excluded");
                var source = Declarations(workspace, Array.Empty<string>(), types, includeInherited);
                var run = runner.Run(runner.CreateCompilation(source, mode), mode);
                run.AssertCompiles();
                AssertContracts(run, "ArrayListWrapper", true, true, true);
                AssertContracts(run, "StringBuilderWrapper", false, false, true);
                AssertContracts(run, "ObjectWrapper", false, false, false);
                AssertContracts(run, "ListWrapper", false, false, false);
                AssertContracts(run, "UriWrapper", true, true, true);
                foreach (var type in types)
                {
                    AssertNoObjectAliases(run, type.Value);
                }
            }
        }

        /// <summary>
        /// Verifies override signatures, nullable results, typed equality, and direct Java virtual dispatch.
        /// </summary>
        /// <param name="run">The completed production-generator execution.</param>
        /// <param name="name">The generated wrapper's short type name.</param>
        /// <param name="equals">Whether typed and Object equality must be declared.</param>
        /// <param name="hashCode">Whether GetHashCode must be declared, including equality's contract forwarding.</param>
        /// <param name="toString">Whether ToString must be declared.</param>
        /// <exception cref="InvalidOperationException">The wrapper's CLR object contract is incorrect.</exception>
        private static void AssertContracts(GeneratorRun run, string name, bool equals, bool hashCode, bool toString)
        {
            var type = ApiAssertions.Type(run, name);
            AssertOverride(run, type, "Equals", equals, SpecialType.System_Boolean, SpecialType.System_Object);
            AssertOverride(run, type, "GetHashCode", hashCode, SpecialType.System_Int32);
            AssertOverride(run, type, "ToString", toString, SpecialType.System_String);
            var typedMethods = type.GetMembers("Equals").OfType<IMethodSymbol>().Where(method => method.Parameters.Length == 1
                && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, type)
                && !IsJavaMapped(method)).ToArray();
            Check.Equal(equals ? 1 : 0, typedMethods.Length, name + " typed Equals count");
            var interfaces = type.AllInterfaces.Where(item => item.OriginalDefinition.MetadataName == "IEquatable`1"
                && item.ContainingNamespace.ToDisplayString() == "System").ToArray();
            Check.Equal(equals ? 1 : 0, interfaces.Length, name + " IEquatable count");
            if (equals)
            {
                var typed = typedMethods.Single();
                Check.True(!typed.IsOverride && !typed.IsStatic && typed.DeclaredAccessibility == Accessibility.Public,
                    name + " typed Equals must implement public instance IEquatable equality.");
                Check.Equal(SpecialType.System_Boolean, typed.ReturnType.SpecialType, name + " typed Equals result");
                Check.Equal(NullableAnnotation.Annotated, typed.Parameters[0].Type.NullableAnnotation, name + " typed Equals nullable argument");
                var contract = interfaces.Single();
                Check.True(SymbolEqualityComparer.Default.Equals(type, contract.TypeArguments.Single()), name + " IEquatable must target its own wrapper type.");
                Check.True(SymbolEqualityComparer.Default.Equals(typed, type.FindImplementationForInterfaceMember(contract.GetMembers("Equals").Single())),
                    name + " typed Equals must be the semantic implementation of IEquatable.Equals.");
                var declaration = Declaration(typed);
                Check.True(declaration.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(invocation => invocation.Expression is IdentifierNameSyntax identifier
                    && identifier.Identifier.ValueText == "Equals"
                    && invocation.ArgumentList.Arguments.Count == 1
                    && invocation.ArgumentList.Arguments[0].Expression is CastExpressionSyntax cast
                    && cast.Type.ToString() == "object?"),
                    name + " typed Equals must dispatch through nullable CLR Object equality without restricting cross-wrapper Java equality.");
            }
        }

        /// <summary>
        /// Requires an optional wrapper override to invoke its Java virtual method with an exact descriptor.
        /// </summary>
        /// <param name="run">The completed production-generator execution.</param>
        /// <param name="type">The generated wrapper type.</param>
        /// <param name="name">The CLR Object method name.</param>
        /// <param name="expected">Whether the override must be present.</param>
        /// <param name="returnType">The expected CLR result type.</param>
        /// <param name="parameterTypes">The expected CLR parameter types.</param>
        /// <exception cref="InvalidOperationException">The override is missing, unexpected, or incorrectly forwarded.</exception>
        private static void AssertOverride(GeneratorRun run, INamedTypeSymbol type, string name, bool expected, SpecialType returnType,
            params SpecialType[] parameterTypes)
        {
            var methods = type.GetMembers(name).OfType<IMethodSymbol>().Where(method => method.Parameters.Length == parameterTypes.Length
                && method.Parameters.Select(parameter => parameter.Type.SpecialType).SequenceEqual(parameterTypes)).ToArray();
            Check.Equal(expected ? 1 : 0, methods.Length, type.Name + "." + name + " override count");
            if (!expected)
            {
                return;
            }
            var method = methods.Single();
            Check.True(method.IsOverride && !method.IsStatic && method.DeclaredAccessibility == Accessibility.Public,
                type.Name + "." + name + " must be a public instance override.");
            Check.Equal("MajdataPlay.Platform.Android.Runtime.Java.Lang.JavaObject", method.OverriddenMethod?.ContainingType.ToDisplayString(),
                type.Name + "." + name + " JavaObject override target");
            Check.Equal(returnType, method.ReturnType.SpecialType, type.Name + "." + name + " result type");
            if (name == "Equals")
            {
                Check.Equal(NullableAnnotation.Annotated, method.Parameters.Single().Type.NullableAnnotation, type.Name + ".Equals nullable Object argument");
            }
            if (name == "ToString")
            {
                Check.Equal(NullableAnnotation.Annotated, method.ReturnType.NullableAnnotation, type.Name + ".ToString nullable Java string result");
            }
            var javaName = name == "Equals" ? "equals" : name == "GetHashCode" ? "hashCode" : "toString";
            var descriptor = name == "Equals" ? "(Ljava/lang/Object;)Z" : name == "GetHashCode" ? "()I" : "()Ljava/lang/String;";
            var invocation = DirectCall(run, method, javaName, descriptor);
            var declaration = Declaration(method);
            Check.True(!declaration.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call => call.Expression is MemberAccessExpressionSyntax access
                && access.Expression is BaseExpressionSyntax && access.Name.Identifier.ValueText == name),
                type.Name + "." + name + " must call the Java virtual method directly.");
            var semantic = run.Output.GetSemanticModel(declaration.SyntaxTree);
            var callSymbol = (IMethodSymbol)semantic.GetSymbolInfo(invocation).Symbol!;
            Check.Equal(returnType, callSymbol.TypeArguments.Single().SpecialType, type.Name + "." + name + " exact JNI result type");
            var javaClass = type.GetAttributes().Single(attribute => attribute.AttributeClass?.Name == "JavaClassAttribute").ConstructorArguments[0].Value;
            Check.Equal(javaClass, (object?)((LiteralExpressionSyntax)invocation.ArgumentList.Arguments[1].Expression).Token.ValueText,
                type.Name + "." + name + " JNI dispatch class");
            Check.Equal(name == "Equals" ? 5 : 4, invocation.ArgumentList.Arguments.Count, type.Name + "." + name + " direct JNI argument count");
            Check.Equal("UnityEngine.AndroidJavaObject", semantic.GetTypeInfo(invocation.ArgumentList.Arguments[0].Expression).Type?.ToDisplayString(),
                type.Name + "." + name + " direct JNI live receiver type");
            if (name == "Equals")
            {
                Check.Equal("UnityEngine.AndroidJavaObject", semantic.GetTypeInfo(invocation.ArgumentList.Arguments[4].Expression).Type?.ToDisplayString(),
                    type.Name + ".Equals direct JNI live comparison reference type");
            }
            var referenceReads = declaration.DescendantNodes().OfType<ExpressionSyntax>()
                .Where(expression => expression is MemberAccessExpressionSyntax || (expression is IdentifierNameSyntax
                    && !(expression.Parent is MemberAccessExpressionSyntax access && access.Name == expression)))
                .Count(expression =>
                semantic.GetSymbolInfo(expression).Symbol is IPropertySymbol property
                && property.Name == "JavaReference"
                && property.ContainingType.ToDisplayString() == "MajdataPlay.Platform.Android.Runtime.Java.Lang.JavaObject");
            Check.True(referenceReads >= (name == "Equals" ? 2 : 1),
                type.Name + "." + name + " must validate every participating Java reference before native dispatch.");
        }

        /// <summary>
        /// Finds the generated declaration for one method symbol.
        /// </summary>
        /// <param name="method">The generated method to inspect.</param>
        /// <returns>The unique generated method syntax.</returns>
        /// <exception cref="InvalidOperationException">There is no unique method declaration.</exception>
        private static MethodDeclarationSyntax Declaration(IMethodSymbol method)
        {
            var declarations = method.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax()).OfType<MethodDeclarationSyntax>().ToArray();
            Check.Equal(1, declarations.Length, method.ContainingType.Name + "." + method.Name + " generated declaration count");
            return declarations.Single();
        }

        /// <summary>
        /// Requires exact Java Object instance signatures to have no ordinary generated aliases.
        /// </summary>
        /// <param name="run">The completed production-generator execution.</param>
        /// <param name="name">The generated wrapper's short type name.</param>
        /// <exception cref="InvalidOperationException">An exact Object signature still has an ordinary generated alias.</exception>
        private static void AssertNoObjectAliases(GeneratorRun run, string name)
        {
            var type = ApiAssertions.Type(run, name);
            foreach (var method in type.GetMembers().OfType<IMethodSymbol>().Where(method => method.MethodKind == MethodKind.Ordinary && !method.IsOverride))
            {
                foreach (var invocation in Declaration(method).DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var arguments = invocation.ArgumentList.Arguments;
                    if (arguments.Count < 4 || arguments[2].Expression is not LiteralExpressionSyntax javaName
                        || arguments[3].Expression is not LiteralExpressionSyntax descriptor)
                    {
                        continue;
                    }
                    var isObjectSignature = (javaName.Token.ValueText == "equals" && descriptor.Token.ValueText == "(Ljava/lang/Object;)Z")
                        || (javaName.Token.ValueText == "hashCode" && descriptor.Token.ValueText == "()I")
                        || (javaName.Token.ValueText == "toString" && descriptor.Token.ValueText == "()Ljava/lang/String;");
                    Check.True(method.IsStatic || !isObjectSignature,
                        name + "." + method.Name + " must not expose a Java Object instance alias.");
                }
            }
        }

        /// <summary>
        /// Requires a genuine Java overload or static lookalike to retain its exact invocation contract.
        /// </summary>
        /// <param name="run">The completed production-generator execution.</param>
        /// <param name="name">The generated wrapper's short type name.</param>
        /// <param name="javaName">The original Java method name.</param>
        /// <param name="descriptor">The erased Java method descriptor.</param>
        /// <param name="isStatic">Whether the genuine method is static.</param>
        /// <exception cref="InvalidOperationException">The method is absent or has an incorrect invocation contract.</exception>
        private static void AssertJavaMethod(GeneratorRun run, string name, string javaName, string descriptor, bool isStatic)
        {
            var type = ApiAssertions.Type(run, name);
            var methods = type.GetMembers().OfType<IMethodSymbol>().Where(method => method.MethodKind == MethodKind.Ordinary && !method.IsOverride)
                .Where(method => Declaration(method).DescendantNodes().OfType<InvocationExpressionSyntax>().Any(invocation =>
                    invocation.ArgumentList.Arguments.Count >= 4
                    && invocation.ArgumentList.Arguments[2].Expression is LiteralExpressionSyntax methodName && methodName.Token.ValueText == javaName
                    && invocation.ArgumentList.Arguments[3].Expression is LiteralExpressionSyntax methodDescriptor && methodDescriptor.Token.ValueText == descriptor)).ToArray();
            Check.Equal(1, methods.Length, name + " preserved Java " + javaName + descriptor);
            var method = methods.Single();
            Check.Equal(isStatic, method.IsStatic, name + " Java " + javaName + descriptor + " static contract");
            DirectCall(run, method, javaName, descriptor);
        }

        /// <summary>
        /// Finds the unique semantic AndroidJni.Call invocation carrying an exact Java method signature.
        /// </summary>
        /// <param name="run">The completed production-generator execution.</param>
        /// <param name="method">The generated method under inspection.</param>
        /// <param name="javaName">The original Java method name.</param>
        /// <param name="descriptor">The exact erased JNI descriptor.</param>
        /// <returns>The verified direct AndroidJni.Call invocation.</returns>
        /// <exception cref="InvalidOperationException">The method does not contain one exact semantic JNI invocation.</exception>
        private static InvocationExpressionSyntax DirectCall(GeneratorRun run, IMethodSymbol method, string javaName, string descriptor)
        {
            var declaration = Declaration(method);
            var semantic = run.Output.GetSemanticModel(declaration.SyntaxTree);
            var invocations = declaration.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(invocation =>
                semantic.GetSymbolInfo(invocation).Symbol is IMethodSymbol called
                && called.Name == "Call" && called.ContainingType.ToDisplayString() == "MajdataPlay.Platform.Android.AndroidJni"
                && invocation.ArgumentList.Arguments.Count >= 4
                && invocation.ArgumentList.Arguments[2].Expression is LiteralExpressionSyntax methodName && methodName.Token.ValueText == javaName
                && invocation.ArgumentList.Arguments[3].Expression is LiteralExpressionSyntax methodDescriptor && methodDescriptor.Token.ValueText == descriptor).ToArray();
            Check.Equal(1, invocations.Length, method.ContainingType.Name + "." + method.Name + " direct Java " + javaName + descriptor);
            return invocations.Single();
        }

        /// <summary>
        /// Executes generated managed shortcuts and disposal guards using wrappers with no Java reference or JNI handle.
        /// </summary>
        /// <param name="run">The generated fixture compilation to emit in memory.</param>
        /// <exception cref="InvalidOperationException">A generated shortcut or disposal guard violates its managed contract.</exception>
        private static void AssertManagedGuards(GeneratorRun run)
        {
            using var stream = new MemoryStream();
            var emit = run.Output.Emit(stream);
            Check.True(emit.Success, "Generated Object-override guard assembly must emit successfully: " + string.Join("\r\n", emit.Diagnostics));
            var assembly = Assembly.Load(stream.ToArray());
            var type = Check.NotNull(assembly.GetType("Fixtures.Wrappers.AllWrapper"), "generated AllWrapper reflection type");
            var plainType = Check.NotNull(assembly.GetType("Fixtures.Wrappers.PlainWrapper"), "generated PlainWrapper reflection type");
            var first = RuntimeHelpers.GetUninitializedObject(type);
            var second = RuntimeHelpers.GetUninitializedObject(type);
            var crossWrapper = RuntimeHelpers.GetUninitializedObject(plainType);
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
            var equals = Check.NotNull(type.GetMethod("Equals", flags, binder: null, types: new[] { typeof(object) }, modifiers: null), "generated Equals(Object)");
            var typed = Check.NotNull(type.GetMethod("Equals", flags, binder: null, types: new[] { type }, modifiers: null), "generated typed Equals");
            Check.Equal((object)true, equals.Invoke(first, new[] { first }), "generated Equals managed identity");
            Check.Equal((object)false, equals.Invoke(first, new object?[] { null }), "generated Equals null shortcut");
            Check.Equal((object)false, equals.Invoke(first, new object?[] { "unrelated CLR value" }), "generated Equals unrelated CLR shortcut");
            Check.Equal((object)true, typed.Invoke(first, new[] { first }), "generated typed Equals managed identity");
            Check.Equal((object)false, typed.Invoke(first, new object?[] { null }), "generated typed Equals null shortcut");
            AssertDisposedInvocation(equals, first, new[] { second });
            AssertDisposedInvocation(equals, first, new[] { crossWrapper });
            AssertDisposedInvocation(typed, first, new[] { second });
            foreach (var name in new[] { "GetHashCode", "ToString" })
            {
                var method = Check.NotNull(type.GetMethod(name, flags, binder: null, types: Type.EmptyTypes, modifiers: null), "generated " + name);
                AssertDisposedInvocation(method, first, Array.Empty<object?>());
            }
        }

        /// <summary>
        /// Requires a generated method to reject a disposed-state wrapper before entering native dispatch.
        /// </summary>
        /// <param name="method">The generated CLR override or typed equality method.</param>
        /// <param name="target">The uninitialized wrapper containing no Java reference.</param>
        /// <param name="arguments">The managed comparison arguments, if any.</param>
        /// <exception cref="InvalidOperationException">Invocation returns normally or throws an unexpected exception.</exception>
        private static void AssertDisposedInvocation(MethodInfo method, object target, object?[] arguments)
        {
            try
            {
                method.Invoke(target, arguments);
            }
            catch (TargetInvocationException exception)
            {
                Check.True(exception.InnerException is ObjectDisposedException,
                    method.DeclaringType!.Name + "." + method.Name + " must reject disposed Java references before JNI; observed " + exception.InnerException?.GetType().Name + ".");
                return;
            }
            Check.True(false, method.DeclaringType!.Name + "." + method.Name + " must throw ObjectDisposedException for distinct disposed wrappers.");
        }

        /// <summary>
        /// Requires handwritten Object members and wrapper names that collide with required overrides to produce actionable diagnostics.
        /// </summary>
        /// <param name="runner">The actual generator driver factory.</param>
        /// <param name="workspace">The prepared Java fixture configuration.</param>
        /// <param name="name">The isolated case-name prefix.</param>
        /// <exception cref="InvalidOperationException">A conflict is missed or accepted as a generator crash.</exception>
        private static void AssertUserConflicts(GeneratorRunner runner, FixtureWorkspace workspace, string name)
        {
            var members = new[]
            {
                "public override bool Equals(object? obj)\r\n        {\r\n            return false;\r\n        }",
                "public bool Equals(AllWrapper? other)\r\n        {\r\n            return false;\r\n        }",
                "public override int GetHashCode()\r\n        {\r\n            return 0;\r\n        }",
                "public override string? ToString()\r\n        {\r\n            return null;\r\n        }",
                "public bool Equals(dynamic obj)\r\n        {\r\n            return false;\r\n        }",
                "public new bool Equals\r\n        {\r\n            get;\r\n        }",
                "public new int GetHashCode\r\n        {\r\n            get;\r\n        }",
                "public new string? ToString\r\n        {\r\n            get;\r\n        }"
            };
            for (var index = 0; index < members.Length; index++)
            {
                var mode = name + "-" + index;
                var types = new[] { new KeyValuePair<string, string>("fixtures.ObjectOverrides$All", "AllWrapper") };
                var source = Declarations(workspace, new[] { workspace.JarPath }, types, false);
                source = source.Replace("public partial class AllWrapper\r\n    {", "public partial class AllWrapper : global::MajdataPlay.Platform.Android.Runtime.Java.Lang.JavaObject\r\n    {\r\n        " + members[index]);
                var input = runner.CreateCompilation(source, mode);
                Check.True(!input.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                    "Handwritten override conflict requests must be legal C# before generation.");
                var run = runner.Run(input, mode);
                run.AssertDiagnostic("AJG008");
                Check.Equal(0, run.GeneratedSources.Count, "A conflicting Object-override wrapper must not emit incomplete source.");
            }
            var typeNameMode = name + "-type-name";
            var typeNameSource = Declarations(workspace, new[] { workspace.JarPath },
                new[] { new KeyValuePair<string, string>("fixtures.ObjectOverrides$All", "Equals") }, false);
            var typeNameInput = runner.CreateCompilation(typeNameSource, typeNameMode);
            Check.True(!typeNameInput.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                "An Equals wrapper declaration must remain legal C# before required override generation.");
            var typeNameRun = runner.Run(typeNameInput, typeNameMode);
            typeNameRun.AssertDiagnostic("AJG008");
            Check.Equal(0, typeNameRun.GeneratedSources.Count, "A wrapper type name that conflicts with required Equals overrides must not emit incomplete source.");
            var explicitMode = name + "-explicit-interface";
            var explicitSource = Declarations(workspace, new[] { workspace.JarPath },
                new[] { new KeyValuePair<string, string>("fixtures.ObjectOverrides$All", "AllWrapper") }, false);
            explicitSource = explicitSource.Replace("public partial class AllWrapper\r\n    {",
                "public partial class AllWrapper : global::System.IEquatable<AllWrapper>\r\n    {\r\n"
                + "        bool System.IEquatable<AllWrapper>.Equals(AllWrapper? other)\r\n        {\r\n            return false;\r\n        }");
            var explicitRun = runner.Run(runner.CreateCompilation(explicitSource, explicitMode), explicitMode);
            explicitRun.AssertDiagnostic("AJG008");
            Check.Equal(0, explicitRun.GeneratedSources.Count, "A handwritten explicit IEquatable implementation must not supersede generated Java equality.");
        }

        /// <summary>
        /// Creates wrapper declarations sharing an explicit source set and inherited-member setting.
        /// </summary>
        /// <param name="workspace">The selected Android API and fixture configuration.</param>
        /// <param name="sources">The custom Java inputs, or an empty array for SDK-only requests.</param>
        /// <param name="types">The Java binary names and C# wrapper names.</param>
        /// <param name="includeInherited">Whether ordinary inherited Java methods are included.</param>
        /// <returns>The nullable-enabled wrapper request source.</returns>
        private static string Declarations(FixtureWorkspace workspace, string[] sources,
            IEnumerable<KeyValuePair<string, string>> types, bool includeInherited)
        {
            var builder = new StringBuilder("#nullable enable\r\nusing MajdataPlay.Platform.Android;\r\n");
            builder.Append("[assembly: JavaApiConfiguration(Sources = ").Append(GeneratorRunner.StringArray(sources))
                .Append(", ApiLevel = ").Append(workspace.Options.ApiLevel)
                .Append(", IncludeInheritedMembers = ").Append(includeInherited ? "true" : "false").Append(")]\r\n")
                .Append("namespace Fixtures.Wrappers\r\n{\r\n");
            foreach (var type in types)
            {
                builder.Append("    [JavaClass(").Append(GeneratorRunner.Literal(type.Key)).Append(")]\r\n")
                    .Append("    public partial class ").Append(type.Value).Append("\r\n    {\r\n    }\r\n");
            }
            return builder.Append("}\r\n").ToString();
        }
    }
}
