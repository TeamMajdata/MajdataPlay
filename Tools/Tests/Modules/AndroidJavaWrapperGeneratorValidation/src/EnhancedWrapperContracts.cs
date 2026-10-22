using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

#nullable enable

namespace MajdataPlay.Tests.AndroidJavaWrapperGeneratorValidation
{
    /// <summary>Checks Java structure and CLR contracts restored by generated wrappers.</summary>
    internal static partial class Program
    {
        /// <summary>Checks nested names, reference-constructor visibility, object overrides, and generic constraints.</summary>
        /// <param name="sources">The fixture and dependency wrapper sources.</param>
        /// <exception cref="InvalidOperationException">A generated declaration no longer preserves its Java contract.</exception>
        private static void VerifyEnhancedDeclarations(IReadOnlyDictionary<string, string> sources)
        {
            var classes = sources.Where(source => source.Key.EndsWith(".g.cs", StringComparison.Ordinal))
                .SelectMany(source => CSharpSyntaxTree.ParseText(source.Value).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
                .ToArray();
            var widget = classes.Single(type => type.Identifier.ValueText == "Widget"
                && type.Members.OfType<ConstructorDeclarationSyntax>().Any());
            var widgetParts = classes.Where(type => type.Identifier.ValueText == "Widget").ToArray();
            Require(widgetParts.SelectMany(type => type.Members.OfType<ClassDeclarationSyntax>())
                .Where(type => type.Identifier.ValueText == "Nested")
                .Any(type => type.Members.OfType<ClassDeclarationSyntax>().Any(child => child.Identifier.ValueText == "Deep")),
                "Multilevel Java nested types retain their lexical wrapper parents");
            Require(widgetParts.Any(type => type.Members.OfType<ClassDeclarationSyntax>().Any(child => child.Identifier.ValueText == "Inner")),
                "Nonstatic nested Java types remain inside their parent wrapper");
            Require(classes.Any(type => type.Identifier.ValueText == "WidgetNested" && type.Parent is NamespaceDeclarationSyntax),
                "A top-level underscore name remains distinct from the corresponding nested Java type");

            var referenceConstructors = classes.SelectMany(type => type.Members.OfType<ConstructorDeclarationSyntax>())
                .Where(constructor => constructor.ParameterList.Parameters.Count == 2
                    && constructor.ParameterList.Parameters[0].Type!.ToString().EndsWith("AndroidJavaObject", StringComparison.Ordinal))
                .ToArray();
            Require(referenceConstructors.Length > 0 && referenceConstructors.All(constructor =>
                constructor.Modifiers.Any(token => token.IsKind(SyntaxKind.InternalKeyword))),
                "Every generated Java-reference constructor is internal");
            var classProperty = widget.Members.OfType<PropertyDeclarationSyntax>().Single(property => property.Identifier.ValueText == "Class");
            Require(classProperty.Type.DescendantNodesAndSelf().OfType<GenericNameSyntax>().Any(name =>
                name.Identifier.ValueText == "Class" && name.TypeArgumentList.Arguments.Count == 1),
                "Static Class properties return the typed java.lang.Class wrapper");
            var extensions = CSharpSyntaxTree.ParseText(sources["JavaObjectExtensions.cs"]).GetRoot()
                .DescendantNodes().OfType<ClassDeclarationSyntax>().Single(type => type.Identifier.ValueText == "JavaObjectExtensions");
            var asMethod = extensions.Members.OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "As");
            var asParameter = asMethod.TypeParameterList!.Parameters.Single().Identifier.ValueText;
            Require(asMethod.ParameterList.Parameters.Count == 1
                && asMethod.ParameterList.Parameters[0].Modifiers.Any(token => token.IsKind(SyntaxKind.ThisKeyword))
                && asMethod.ParameterList.Parameters[0].Type!.ToString().EndsWith(".JavaObject", StringComparison.Ordinal)
                && asMethod.ReturnType.ToString() == asParameter
                && asMethod.ConstraintClauses.Single().Constraints.OfType<TypeConstraintSyntax>()
                    .Any(constraint => constraint.Type.ToString().EndsWith(".JavaObject", StringComparison.Ordinal)),
                "JavaObject has a nonnullable As extension constrained to JavaObject wrappers");

            foreach (var name in new[] { "Equals", "GetHashCode", "ToString" })
            {
                Require(widget.Members.OfType<MethodDeclarationSyntax>().Any(method =>
                    method.Identifier.ValueText == name && method.Modifiers.Any(token => token.IsKind(SyntaxKind.OverrideKeyword))),
                    "Java object overrides produce CLR overrides: " + name);
            }
            Require(widget.BaseList!.Types.Any(type => type.ToString().Contains("IEquatable<", StringComparison.Ordinal)),
                "Java equals overrides also implement the typed CLR equality contract");

            var generic = classes.Single(type => type.Identifier.ValueText == "GenericBox" && type.TypeParameterList?.Parameters.Count == 1
                && type.Members.OfType<PropertyDeclarationSyntax>().Any(property => property.Identifier.ValueText == "Value"));
            var parameter = generic.TypeParameterList!.Parameters[0].Identifier.ValueText;
            Require(parameter.StartsWith("T", StringComparison.Ordinal), "Generated type parameters follow the repository naming convention");
            var constraints = generic.ConstraintClauses.Single(clause => clause.Name.Identifier.ValueText == parameter).ToString();
            Require(constraints.Contains("Number", StringComparison.Ordinal) && constraints.Contains("Comparable", StringComparison.Ordinal),
                "Java intersection and self-referential interface bounds survive in CLR generic constraints");
            Require(generic.Members.OfType<PropertyDeclarationSyntax>().Any(property =>
                property.Identifier.ValueText == "Value" && property.Type.ToString().TrimEnd('?') == parameter),
                "Generic wrapper members preserve the declared type parameter");
            var genericParts = classes.Where(type => type.Identifier.ValueText == "GenericBox" && type.TypeParameterList?.Parameters.Count == 1).ToArray();
            Require(genericParts.Any(type => type.Members.OfType<ClassDeclarationSyntax>().Any(child =>
                child.Identifier.ValueText == "Inner" && child.TypeParameterList?.Parameters.Count == 1)),
                "Generic inner wrappers remain inside their generic parent");
            var inner = genericParts.SelectMany(type => type.Members.OfType<ClassDeclarationSyntax>()).Single(type =>
                type.Identifier.ValueText == "Inner" && type.TypeParameterList?.Parameters.Count == 1);
            Require(inner.ConstraintClauses.Any(clause => clause.Constraints.OfType<TypeConstraintSyntax>().Any(constraint =>
                constraint.Type.ToString() == parameter)), "Inner generic bounds preserve references to the enclosing type parameter");
            Require(classes.Where(type => type.Identifier.ValueText == "Nested" && type.TypeParameterList?.Parameters.Count == 1)
                .Any(type => type.ConstraintClauses.Any(clause => clause.ToString().Contains("JavaObject", StringComparison.Ordinal))),
                "Unbounded Java generic parameters explicitly require the JavaObject wrapper root");
            var nonGenericInner = genericParts.SelectMany(type => type.Members.OfType<ClassDeclarationSyntax>())
                .Single(type => type.Identifier.ValueText == "NonGenericInner"
                    && type.Members.OfType<PropertyDeclarationSyntax>().Any(property => property.Identifier.ValueText == "Value"));
            Require(nonGenericInner.TypeParameterList is null
                && nonGenericInner.Members.OfType<PropertyDeclarationSyntax>().Any(property =>
                    property.Identifier.ValueText == "Value" && property.Type.ToString().TrimEnd('?') == parameter)
                && nonGenericInner.Members.OfType<MethodDeclarationSyntax>().Any(method =>
                    method.Identifier.ValueText == "Echo" && method.ReturnType.ToString().TrimEnd('?') == parameter),
                "Nongeneric inner wrappers retain typed members from their generic enclosing class");
            var throwBox = classes.Single(type => type.Identifier.ValueText == "ThrowBox" && type.TypeParameterList?.Parameters.Count == 1
                && type.Members.OfType<PropertyDeclarationSyntax>().Any(property => property.Identifier.ValueText == "Value"));
            Require(throwBox.ConstraintClauses.Single().Constraints.OfType<TypeConstraintSyntax>()
                .Any(constraint => constraint.Type.ToString().EndsWith(".Throwable", StringComparison.Ordinal))
                && !throwBox.Members.OfType<PropertyDeclarationSyntax>().Single(property => property.Identifier.ValueText == "Type")
                    .Type.DescendantNodesAndSelf().OfType<GenericNameSyntax>().Any(),
                "Throwable bounds remain typed while incompatible Class arguments use the raw reflection wrapper");
        }

        /// <summary>Checks equivalent CLR interfaces and catchable Java exception hierarchy in compiled generated metadata.</summary>
        /// <param name="assembly">The fixture assembly compiled against the real Unity modules.</param>
        /// <exception cref="InvalidOperationException">A wrapper fails to expose its corresponding CLR contract.</exception>
        private static void VerifyEnhancedRuntimeMetadata(Assembly assembly)
        {
            const string FixtureRoot = "Validation.CustomRoot.Bindings.Fixtures.";
            var widget = assembly.GetType(FixtureRoot + "Widget", true)!;
            Require(typeof(IEquatable<>).MakeGenericType(widget).IsAssignableFrom(widget),
                "Compiled equals overrides implement IEquatable of their wrapper");
            var classDefinition = assembly.GetType("Validation.CustomRoot.Bindings.Java.Lang.Class`1", true)!;
            Require(widget.GetProperty("Class", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!.PropertyType
                == classDefinition.MakeGenericType(widget), "The compiled Class property retains its represented wrapper type");
            var integer = assembly.GetType("Validation.CustomRoot.Bindings.Java.Lang.Integer", true)!;
            var genericBox = assembly.GetType(FixtureRoot + "GenericBox`1", true)!.MakeGenericType(integer);
            Require(genericBox.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.PropertyType == integer,
                "A concrete Java class satisfying intersection bounds closes the typed wrapper and member signature");
            var javaString = assembly.GetType("Validation.CustomRoot.Bindings.Java.Lang.String", true)!;
            var stringBox = assembly.GetType(FixtureRoot + "StringBox`1", true)!.MakeGenericType(javaString);
            Require(stringBox.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.PropertyType == javaString
                && stringBox.GetProperty("Values", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.PropertyType == javaString.MakeArrayType()
                && stringBox.GetMethod("Echo", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.ReturnType == javaString,
                "String-bounded generic fields, arrays, and methods retain Java String wrappers rather than CLR strings");
            var throwable = assembly.GetType("Validation.CustomRoot.Bindings.Java.Lang.Throwable", true)!;
            var throwBox = assembly.GetType(FixtureRoot + "ThrowBox`1", true)!.MakeGenericType(throwable);
            var rawClass = assembly.GetType("Validation.CustomRoot.Bindings.Java.Lang.Class", true)!;
            Require(throwBox.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.PropertyType == throwable
                && throwBox.GetProperty("Type", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.PropertyType == rawClass
                && throwBox.GetMethod("TypeOf", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.ReturnType == rawClass
                && throwBox.GetMethod("Subtype", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.ReturnType == rawClass
                && throwBox.GetMethod("Self", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.ReturnType == throwBox,
                "Throwable-bound fields and transitive method bounds compile while compatible self parameterizations remain typed");
            var javaObject = assembly.GetType("Validation.CustomRoot.Bindings.Java.Lang.JavaObject", true)!;
            var extensionType = assembly.GetType("Validation.CustomRoot.JavaObjectExtensions", true)!;
            var asMethod = extensionType.GetMethod("As", BindingFlags.Public | BindingFlags.Static)!;
            var asParameter = asMethod.GetGenericArguments().Single();
            Require(asMethod.IsGenericMethodDefinition && asMethod.GetParameters().Length == 1
                && asMethod.GetParameters()[0].ParameterType == javaObject
                && asMethod.ReturnType == asParameter
                && asMethod.IsDefined(typeof(ExtensionAttribute), false)
                && asParameter.GetGenericParameterConstraints().SequenceEqual(new[] { javaObject }),
                "Compiled JavaObject.As extension returns its generic wrapper type with the exact JavaObject constraint");
            Require(javaObject.GetMethod("As", BindingFlags.Public | BindingFlags.Instance) is null,
                "JavaObject conversion is provided as an extension rather than an instance member");
            Expect<ArgumentNullException>(() => Invoke(asMethod.MakeGenericMethod(javaObject), new object?[] { null }),
                "JavaObject.As rejects a null wrapper before JNI access");
            var natural = assembly.GetType(FixtureRoot + "ManagedContracts+Natural", true)!;
            Require(typeof(IComparable).IsAssignableFrom(natural)
                && typeof(IComparable<>).MakeGenericType(natural).IsAssignableFrom(natural),
                "Java Comparable maps to both CLR comparison contracts");
            var comparisonProbe = RuntimeHelpers.GetUninitializedObject(natural);
            Require(((IComparable)comparisonProbe).CompareTo(null) == 1,
                "CLR non-generic comparison orders a wrapper above null without JNI");
            var typedComparison = typeof(IComparable<>).MakeGenericType(natural).GetMethod("CompareTo")!;
            Require((int)typedComparison.Invoke(comparisonProbe, new object?[] { null })! == 1,
                "CLR generic comparison orders a wrapper above null without JNI");
            var order = assembly.GetType(FixtureRoot + "ManagedContracts+Order", true)!;
            Require(typeof(IComparer<>).MakeGenericType(natural).IsAssignableFrom(order),
                "Java Comparator maps to the CLR generic comparer contract");
            var items = assembly.GetType(FixtureRoot + "ManagedContracts+Items", true)!;
            Require(typeof(System.Collections.IEnumerable).IsAssignableFrom(items)
                && typeof(IEnumerable<>).MakeGenericType(natural).IsAssignableFrom(items),
                "Java Iterable maps to both CLR enumeration contracts");
            var failure = assembly.GetType(FixtureRoot + "ManagedContracts+Failure", true)!;
            var argumentFailure = assembly.GetType("Validation.CustomRoot.Bindings.Java.Lang.IllegalArgumentException", true)!;
            Require(typeof(Exception).IsAssignableFrom(failure) && argumentFailure.IsAssignableFrom(failure),
                "Java exception wrappers are catchable CLR exceptions and preserve Java exception inheritance");
        }
    }
}
