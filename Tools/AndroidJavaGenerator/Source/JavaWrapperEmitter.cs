#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace MajdataPlay.SourceGenerators.AndroidJava
{
    /// <summary>Emits nullable, descriptor-exact C# 9 wrappers without modeling Java inheritance in C#.</summary>
    internal sealed class JavaWrapperEmitter
    {
        /// <summary>Stores the annotated C# wrapper declaration.</summary>
        private readonly JavaWrapper _wrapper;

        /// <summary>Stores freshly extracted Java API metadata.</summary>
        private readonly JavaApiType _type;

        /// <summary>Stores the compilation's Java binary-name mappings.</summary>
        private readonly IReadOnlyDictionary<string, string> _mappings;

        /// <summary>Reports diagnostics at the wrapper's annotation.</summary>
        private readonly Action<DiagnosticDescriptor, string> _report;

        /// <summary>Tracks compilation cancellation during member emission.</summary>
        private readonly CancellationToken _cancellationToken;

        /// <summary>Builds indented CRLF generated source.</summary>
        private readonly GeneratedSource _source = new GeneratedSource();

        /// <summary>Tracks generated and runtime-reserved member names.</summary>
        private readonly HashSet<string> _allocatedNames = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Stores user-declared members grouped by their exact C# names.</summary>
        private readonly Dictionary<string, ISymbol[]> _userMembers;

        /// <summary>Indicates a user conflict that prevents safely augmenting the class.</summary>
        private bool _hasErrors;

        /// <summary>Creates a compilation-local emitter for one wrapper.</summary>
        /// <param name="wrapper">The annotated C# class.</param>
        /// <param name="type">The extracted Java type.</param>
        /// <param name="mappings">The annotated wrapper mappings.</param>
        /// <param name="report">The diagnostic callback.</param>
        /// <param name="cancellationToken">The compilation cancellation token.</param>
        private JavaWrapperEmitter(JavaWrapper wrapper, JavaApiType type, IReadOnlyDictionary<string, string> mappings,
            Action<DiagnosticDescriptor, string> report, CancellationToken cancellationToken)
        {
            _wrapper = wrapper;
            _type = type;
            _mappings = mappings;
            _report = report;
            _cancellationToken = cancellationToken;
            _userMembers = wrapper.Symbol.GetMembers().Where(member => !member.IsImplicitlyDeclared)
                .GroupBy(member => member.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            foreach (var name in new[] { wrapper.Symbol.Name, "JavaReference", "Dispose", "Equals", "GetHashCode", "GetType", "ToString", "ReferenceEquals", "MemberwiseClone", "Finalize" })
            {
                _allocatedNames.Add(name);
            }
            var baseType = wrapper.Symbol.BaseType;
            while (baseType != null)
            {
                foreach (var member in baseType.GetMembers())
                {
                    if (member.DeclaredAccessibility != Accessibility.Private && member.Kind != SymbolKind.NamedType && !(member is IMethodSymbol method && method.MethodKind == MethodKind.Constructor))
                    {
                        _allocatedNames.Add(member.Name);
                    }
                }
                baseType = baseType.BaseType;
            }
        }

        /// <summary>Emits a complete wrapper or reports why a handwritten member prevents emission.</summary>
        /// <param name="wrapper">The annotated C# declaration.</param>
        /// <param name="type">The extracted public Java API.</param>
        /// <param name="mappings">The Java binary-name to C# wrapper mappings.</param>
        /// <param name="report">The diagnostic callback.</param>
        /// <param name="cancellationToken">The compilation cancellation token.</param>
        /// <returns>The C# source, or null when a user-member conflict prevents safe generation.</returns>
        /// <exception cref="GeneratorException">The metadata cannot be represented accurately.</exception>
        /// <exception cref="OperationCanceledException">Compilation has been canceled.</exception>
        internal static string? Emit(JavaWrapper wrapper, JavaApiType type, IReadOnlyDictionary<string, string> mappings,
            Action<DiagnosticDescriptor, string> report, CancellationToken cancellationToken)
        {
            var emitter = new JavaWrapperEmitter(wrapper, type, mappings, report, cancellationToken);
            return emitter.EmitCore();
        }

        /// <summary>Emits the type, constants, fields, constructors, and flattened methods.</summary>
        /// <returns>The generated source, or null if a handwritten declaration conflicts.</returns>
        /// <exception cref="GeneratorException">The helper metadata is inconsistent.</exception>
        /// <exception cref="OperationCanceledException">Compilation has been canceled.</exception>
        private string? EmitCore()
        {
            _source.Line("// <auto-generated />");
            _source.Line("#nullable enable");
            _source.Line();
            var namespaceName = GetNamespaceName(_wrapper.Symbol.ContainingNamespace);
            if (namespaceName.Length != 0)
            {
                _source.Line("namespace " + namespaceName);
                _source.Open();
            }
            var kind = _type.IsInterface ? "interface" : _type.IsAbstract ? "abstract class" : "class";
            Documentation(_type, "Wraps the Java " + kind + " '" + _type.Name + "' using exact JVM member descriptors.", _type.Name);
            _source.Documentation("remarks", "Owns or borrows a Unity Java reference. Dispose owned references deterministically. " +
                "JNI invocation requires an Android player and the Unity main thread or a JVM-attached thread. " +
                "Java inheritance is flattened; this wrapper does not implement Java callback interfaces.");
            Deprecated(_type);
            _source.Line((_wrapper.Symbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal") +
                " partial class " + JavaNames.Escape(_wrapper.Symbol.Name) + " : global::MajdataPlay.Platform.Android.JavaObject");
            _source.Open();
            var classNameMember = AllocateName("JavaClassName", "JavaField", false);
            _source.Documentation("summary", "Gets the exact Java binary class name used by this wrapper.");
            _source.Line("public const string " + JavaNames.Escape(classNameMember) + " = " + JavaLiterals.String(_type.Name) + ";");
            _source.Line();
            EmitWrappingConstructor();
            var methods = PrepareMethods();
            foreach (var field in _type.Fields.OrderBy(field => field.Name, StringComparer.Ordinal)
                .ThenBy(field => field.DeclaringType, StringComparer.Ordinal).ThenBy(field => field.Descriptor, StringComparer.Ordinal))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                EmitField(field);
            }
            AllocateMethodNames(methods);
            foreach (var method in methods)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                EmitMethod(method);
            }
            _source.Close();
            if (namespaceName.Length != 0)
            {
                _source.Close();
            }
            return _hasErrors ? null : _source.ToString();
        }

        /// <summary>Emits the reference-wrapping constructor while validating handwritten constructors.</summary>
        private void EmitWrappingConstructor()
        {
            var parameters = new[] { JavaTypeMapping.JavaObjectType, "bool" };
            CheckUserConstructor(parameters, "the generated reference-wrapping constructor");
            _source.Documentation("summary", "Wraps an existing Unity Java reference without invoking a Java constructor.");
            _source.Documentation("param", "The non-null Java reference to wrap.", " name=\"javaObject\"");
            _source.Documentation("param", "Whether this wrapper adopts and disposes the reference instead of borrowing it.", " name=\"ownsReference\"");
            _source.Documentation("exception", "The Java reference is null.", " cref=\"global::System.ArgumentNullException\"");
            _source.Line("public " + JavaNames.Escape(_wrapper.Symbol.Name) + "(" + JavaTypeMapping.JavaObjectType + " javaObject, bool ownsReference = true)");
            _source.Line("    : base(javaObject, ownsReference)");
            _source.Open();
            _source.Close();
            _source.Line();
        }

        /// <summary>Prepares deterministic signatures, preserving every erased Java overload with aliases.</summary>
        /// <returns>The methods, constructors, and constructor factories to emit.</returns>
        /// <exception cref="GeneratorException">The helper exports an invalid inherited constructor or duplicate descriptor.</exception>
        private List<EmissionMethod> PrepareMethods()
        {
            var methods = new List<EmissionMethod>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var metadata in _type.Methods.OrderBy(method => method.Name, StringComparer.Ordinal)
                .ThenBy(method => method.Descriptor, StringComparer.Ordinal).ThenBy(method => method.DeclaringType, StringComparer.Ordinal))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var constructor = metadata.Name == "<init>";
                if (constructor && (_type.IsAbstract || _type.IsInterface))
                {
                    continue;
                }
                if (constructor && metadata.DeclaringType != _type.Name)
                {
                    throw new GeneratorException(GeneratorDiagnostics.InvalidMetadata,
                        "Java helper exported an inherited constructor for '" + metadata.DeclaringType + "' on '" + _type.Name + "'.");
                }
                if (!seen.Add(metadata.Name + "\0" + metadata.Descriptor))
                {
                    throw new GeneratorException(GeneratorDiagnostics.InvalidMetadata,
                        "Java helper exported duplicate member '" + _type.Name + "." + metadata.Name + metadata.Descriptor + "'.");
                }
                var signature = JavaDescriptors.ParseMethod(metadata.Descriptor);
                var types = signature.Parameters.Select(type => JavaTypeMapping.GetTypeName(type, _mappings)).ToArray();
                methods.Add(new EmissionMethod(metadata, signature, types, GetParameterNames(metadata, types.Length)));
            }
            foreach (var group in methods.GroupBy(method => method.Metadata.Name + "(" +
                string.Join(",", method.ParameterTypes.Select(JavaNames.SignatureType)) + ")", StringComparer.Ordinal))
            {
                var collisions = group.ToArray();
                var wrapperCollision = collisions[0].IsConstructor && IsWrappingSignature(collisions[0].ParameterTypes);
                if (collisions.Length <= 1 && !wrapperCollision)
                {
                    continue;
                }
                foreach (var method in collisions)
                {
                    var prefix = method.IsConstructor ? "Create" : JavaNames.PascalCase(method.Metadata.Name);
                    method.Name = prefix + DescriptorSuffix(method);
                    method.IsFactory = method.IsConstructor;
                    _report(GeneratorDiagnostics.SignatureCollision, "Java member '" + _type.Name + "." + method.Metadata.Name +
                        method.Metadata.Descriptor + "' " + (wrapperCollision ? "conflicts with reference wrapping" : "shares an erased C# signature with another Java overload") +
                        "; emitted as '" + method.Name + "' " + (method.IsFactory ? "constructor factory" : "method") + ". The exact JNI descriptor is preserved.");
                }
            }
            return methods;
        }

        /// <summary>Allocates safe method names after fields, sharing a name only among Java overloads.</summary>
        /// <param name="methods">The prepared method records.</param>
        private void AllocateMethodNames(List<EmissionMethod> methods)
        {
            var normalNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var method in methods)
            {
                if (method.IsConstructor && !method.IsFactory)
                {
                    CheckUserConstructor(method.ParameterTypes, "Java constructor '" + method.Metadata.Descriptor + "'");
                    continue;
                }
                if (method.Name.Length != 0)
                {
                    method.Name = AllocateName(method.Name, "JavaMethod", true);
                }
                else
                {
                    if (!normalNames.TryGetValue(method.Metadata.Name, out var name))
                    {
                        name = AllocateName(JavaNames.PascalCase(method.Metadata.Name), "JavaMethod", true);
                        normalNames.Add(method.Metadata.Name, name);
                    }
                    method.Name = name;
                }
                CheckUserMethod(method.Name, method.ParameterTypes, method.Metadata);
            }
        }

        /// <summary>Emits a constant or getter-only field with its original declaring-class lookup.</summary>
        /// <param name="field">The public Java field metadata.</param>
        /// <exception cref="GeneratorException">A constant cannot be represented accurately.</exception>
        private void EmitField(JavaApiField field)
        {
            var type = JavaDescriptors.ParseField(field.Descriptor);
            var name = AllocateName(JavaNames.PascalCase(field.Name), "JavaField", false);
            Documentation(field, "Gets the " + (field.IsStatic ? "static " : string.Empty) + "Java field '" + field.DeclaringType + "." + field.Name + "'.", field.DeclaringType);
            var constant = field.IsStatic && field.IsFinal && field.ConstantValue != null;
            if (!constant)
            {
                InvocationExceptions(!field.IsStatic);
            }
            Deprecated(field);
            if (constant)
            {
                var constantType = type.Code == 'L' ? "string" : JavaTypeMapping.GetTypeName(type, _mappings);
                _source.Line("public const " + constantType + " " + JavaNames.Escape(name) + " = " + JavaLiterals.Constant(field, type) + ";");
            }
            else
            {
                _source.Line("public " + (field.IsStatic ? "static " : string.Empty) + JavaTypeMapping.GetTypeName(type, _mappings) + " " + JavaNames.Escape(name));
                _source.Open();
                _source.Line("get");
                _source.Open();
                var invocation = JavaTypeMapping.JniType + ".GetField<" + JavaTypeMapping.GetTypeName(type, _mappings, true) + ">(" +
                    (field.IsStatic ? "null" : "JavaReference") + ", " + JavaLiterals.String(field.DeclaringType) + ", " +
                    JavaLiterals.String(field.Name) + ", " + JavaLiterals.String(field.Descriptor) + ")";
                _source.Line("return " + JavaTypeMapping.WrapResult(type, invocation, _mappings) + ";");
                _source.Close();
                _source.Close();
            }
            _source.Line();
        }

        /// <summary>Emits one constructor, constructor factory, or descriptor-exact Java method.</summary>
        /// <param name="method">The prepared and named method.</param>
        private void EmitMethod(EmissionMethod method)
        {
            var metadata = method.Metadata;
            var signature = method.Signature;
            var constructor = method.IsConstructor && !method.IsFactory;
            var fallback = method.IsConstructor
                ? "Creates an instance of Java class '" + _type.Name + "' using constructor descriptor '" + metadata.Descriptor + "'."
                : "Invokes the " + (metadata.IsStatic ? "static " : string.Empty) + "Java method '" + metadata.DeclaringType + "." + metadata.Name +
                    "' using descriptor '" + metadata.Descriptor + "'.";
            Documentation(metadata, fallback, metadata.DeclaringType);
            for (var index = 0; index < method.ParameterTypes.Length; index++)
            {
                var summary = index < metadata.Parameters.Count ? metadata.Parameters[index].Summary : string.Empty;
                if (string.IsNullOrWhiteSpace(summary))
                {
                    summary = "The " + DescribeJavaType(signature.Parameters[index]) + " argument at Java parameter position " +
                        (index + 1).ToString(CultureInfo.InvariantCulture) + ".";
                }
                _source.Documentation("param", summary, " name=\"" + GeneratedSource.EscapeXml(method.ParameterNames[index]) + "\"");
            }
            if (method.IsFactory)
            {
                _source.Documentation("returns", "A new owned wrapper for the Java instance created by constructor descriptor '" + metadata.Descriptor + "'.");
            }
            else if (!constructor && signature.ReturnType.Code != 'V')
            {
                var references = signature.ReturnType.Code == 'L' || signature.ReturnType.Code == '[';
                _source.Documentation("returns", string.IsNullOrWhiteSpace(metadata.Returns)
                    ? "The " + DescribeJavaType(signature.ReturnType) + " result returned by the Java method" + (references ? "; it may be null." : ".")
                    : metadata.Returns);
            }
            InvocationExceptions(!metadata.IsStatic && !method.IsConstructor);
            foreach (var exception in metadata.Exceptions)
            {
                var condition = string.IsNullOrWhiteSpace(exception.Summary) ? "the Java API reports this declared exception" : exception.Summary;
                _source.Documentation("exception", "Java exception '" + exception.Type + "' when " + condition +
                    ". Java invocation failures are surfaced by the runtime.", " cref=\"global::MajdataPlay.Platform.Android.JavaInvocationException\"");
            }
            Deprecated(metadata);
            var parameterList = string.Join(", ", method.ParameterTypes.Select((type, index) => type + " " + JavaNames.Escape(method.ParameterNames[index])));
            var arguments = "new object?[] { " + string.Join(", ", method.ParameterNames.Select(JavaNames.Escape)) + " }";
            if (constructor)
            {
                _source.Line("public " + JavaNames.Escape(_wrapper.Symbol.Name) + "(" + parameterList + ")");
                _source.Line("    : base(" + JavaLiterals.String(_type.Name) + ", " + JavaLiterals.String(metadata.Descriptor) + ", " + arguments + ")");
                _source.Open();
                _source.Close();
            }
            else if (method.IsFactory)
            {
                var wrapperType = _wrapper.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                _source.Line("public static " + wrapperType + " " + JavaNames.Escape(method.Name) + "(" + parameterList + ")");
                _source.Open();
                _source.Line("return new " + wrapperType + "(" + JavaTypeMapping.JniType + ".Construct(" +
                    JavaLiterals.String(_type.Name) + ", " + JavaLiterals.String(metadata.Descriptor) + ", " + arguments + "), ownsReference: true);");
                _source.Close();
            }
            else
            {
                _source.Line("public " + (metadata.IsStatic ? "static " : string.Empty) + JavaTypeMapping.GetTypeName(signature.ReturnType, _mappings) +
                    " " + JavaNames.Escape(method.Name) + "(" + parameterList + ")");
                _source.Open();
                var isVoid = signature.ReturnType.Code == 'V';
                var generic = isVoid ? string.Empty : "<" + JavaTypeMapping.GetTypeName(signature.ReturnType, _mappings, true) + ">";
                var invocation = JavaTypeMapping.JniType + ".Call" + generic + "(" + (metadata.IsStatic ? "null" : "JavaReference") + ", " +
                    JavaLiterals.String(metadata.DeclaringType) + ", " + JavaLiterals.String(metadata.Name) + ", " + JavaLiterals.String(metadata.Descriptor) + ", " + arguments + ")";
                _source.Line(isVoid ? invocation + ";" : "return " + JavaTypeMapping.WrapResult(signature.ReturnType, invocation, _mappings) + ";");
                _source.Close();
            }
            _source.Line();
        }

        /// <summary>Allocates a deterministic suffix when runtime, type, field, or method names conflict.</summary>
        /// <param name="preferred">The preferred PascalCase name.</param>
        /// <param name="suffix">The member-kind suffix used on conflict.</param>
        /// <param name="allowUserOverloads">Whether distinct handwritten methods may share the name.</param>
        /// <returns>The safe generated identifier.</returns>
        private string AllocateName(string preferred, string suffix, bool allowUserOverloads)
        {
            var name = preferred;
            if (_userMembers.TryGetValue(name, out var user) && (!allowUserOverloads || user.Any(member => !(member is IMethodSymbol))))
            {
                UserConflict("Generated Java member '" + name + "' conflicts with a handwritten member in '" + _wrapper.Symbol.ToDisplayString() + "'.");
            }
            var index = 1;
            while (_allocatedNames.Contains(name) || (_userMembers.TryGetValue(name, out user) && (!allowUserOverloads || user.Any(member => !(member is IMethodSymbol)))))
            {
                name = preferred + suffix + (index == 1 ? string.Empty : index.ToString(CultureInfo.InvariantCulture));
                index++;
            }
            _allocatedNames.Add(name);
            if (name != preferred)
            {
                _report(GeneratorDiagnostics.RenamedMember, "Generated member '" + preferred + "' on Java type '" + _type.Name +
                    "' was renamed to '" + name + "' to avoid a runtime, class, field, method, or handwritten-member name conflict.");
            }
            return name;
        }

        /// <summary>Checks the generated constructor signature against handwritten constructors.</summary>
        /// <param name="parameterTypes">The generated C# parameter types.</param>
        /// <param name="description">The constructor description for diagnostics.</param>
        private void CheckUserConstructor(IReadOnlyList<string> parameterTypes, string description)
        {
            foreach (var constructor in _wrapper.Symbol.InstanceConstructors.Where(item => !item.IsImplicitlyDeclared))
            {
                if (SameSignature(constructor, parameterTypes))
                {
                    UserConflict("Handwritten constructor in '" + _wrapper.Symbol.ToDisplayString() + "' conflicts with " + description + ".");
                }
            }
        }

        /// <summary>Checks a generated method signature against handwritten methods of the same name.</summary>
        /// <param name="name">The final generated method name.</param>
        /// <param name="parameterTypes">The generated C# parameter types.</param>
        /// <param name="metadata">The originating Java member.</param>
        private void CheckUserMethod(string name, IReadOnlyList<string> parameterTypes, JavaApiMethod metadata)
        {
            if (!_userMembers.TryGetValue(name, out var members))
            {
                return;
            }
            foreach (var method in members.OfType<IMethodSymbol>())
            {
                if (method.Arity == 0 && SameSignature(method, parameterTypes))
                {
                    UserConflict("Handwritten method '" + name + "' in '" + _wrapper.Symbol.ToDisplayString() + "' conflicts with Java member '" +
                        metadata.Name + metadata.Descriptor + "'. Static/instance modifiers and return types do not distinguish C# overloads.");
                }
            }
        }

        /// <summary>Compares parameter signatures without reference-nullability annotations.</summary>
        /// <param name="method">The handwritten method or constructor.</param>
        /// <param name="types">The generated parameter types.</param>
        /// <returns>Whether C# would treat the signatures as identical.</returns>
        private static bool SameSignature(IMethodSymbol method, IReadOnlyList<string> types)
        {
            if (method.Parameters.Length != types.Count)
            {
                return false;
            }
            for (var index = 0; index < types.Count; index++)
            {
                if (method.Parameters[index].RefKind != RefKind.None || JavaNames.SignatureType(method.Parameters[index].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)) !=
                    JavaNames.SignatureType(types[index]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Recognizes constructors that would collide with or capture reference wrapping.</summary>
        /// <param name="types">The generated parameter types.</param>
        /// <returns>Whether the Java constructor must be exposed as a factory.</returns>
        private static bool IsWrappingSignature(IReadOnlyList<string> types)
        {
            return types.Count > 0 && JavaNames.SignatureType(types[0]) == JavaNames.SignatureType(JavaTypeMapping.JavaObjectType) &&
                (types.Count == 1 || (types.Count == 2 && types[1] == "bool"));
        }

        /// <summary>Reports a handwritten-member conflict and prevents incomplete source emission.</summary>
        /// <param name="message">The conflict explanation.</param>
        private void UserConflict(string message)
        {
            _hasErrors = true;
            _report(GeneratorDiagnostics.UserMemberCollision, message);
        }

        /// <summary>Appends official API summaries or accurate descriptor-based fallback documentation.</summary>
        /// <param name="documentation">The extracted documentation.</param>
        /// <param name="fallback">The summary when Java documentation is unavailable.</param>
        /// <param name="declaringType">The API's declaring Java binary name.</param>
        private void Documentation(JavaDocumentation documentation, string fallback, string declaringType)
        {
            _source.Documentation("summary", string.IsNullOrWhiteSpace(documentation.Summary) ? fallback : documentation.Summary);
            var url = documentation.DocumentationUrl;
            if (string.IsNullOrWhiteSpace(url) && (declaringType.StartsWith("android.", StringComparison.Ordinal) ||
                declaringType.StartsWith("java.", StringComparison.Ordinal) || declaringType.StartsWith("javax.", StringComparison.Ordinal)))
            {
                url = "https://developer.android.com/reference/" + declaringType.Replace('.', '/').Replace('$', '.');
            }
            if (Uri.TryCreate(url, UriKind.Absolute, out var parsed) && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp))
            {
                _source.Line("/// <seealso href=\"" + GeneratedSource.EscapeXml(parsed.AbsoluteUri) + "\" />");
            }
        }

        /// <summary>Documents invocation, resolution, platform, argument, and lifetime failures.</summary>
        /// <param name="instance">Whether the member accesses a potentially disposed wrapper.</param>
        private void InvocationExceptions(bool instance)
        {
            _source.Documentation("exception", "The call is attempted outside an Android player.", " cref=\"global::System.PlatformNotSupportedException\"");
            _source.Documentation("exception", "Java invocation or field access throws a Java exception.", " cref=\"global::MajdataPlay.Platform.Android.JavaInvocationException\"");
            _source.Documentation("exception", "Unity's Java class or member resolution fails.", " cref=\"global::UnityEngine.AndroidJavaException\"");
            _source.Documentation("exception", "Arguments or references do not match the declared JVM descriptor.", " cref=\"global::System.ArgumentException\"");
            if (instance)
            {
                _source.Documentation("exception", "This wrapper has been disposed.", " cref=\"global::System.ObjectDisposedException\"");
            }
        }

        /// <summary>Emits a C# deprecation attribute for a deprecated Java declaration.</summary>
        /// <param name="documentation">The Java declaration metadata.</param>
        private void Deprecated(JavaDocumentation documentation)
        {
            if (documentation.Deprecated)
            {
                _source.Line("[global::System.Obsolete(\"The Java API is deprecated; consult its official Java documentation.\")]");
            }
        }

        /// <summary>Creates unique camelCase parameter names, retaining source names where practical.</summary>
        /// <param name="method">The parameter-name metadata.</param>
        /// <param name="count">The descriptor parameter count.</param>
        /// <returns>The unique, unescaped generated parameter names.</returns>
        private static string[] GetParameterNames(JavaApiMethod method, int count)
        {
            var result = new string[count];
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < count; index++)
            {
                var preferred = JavaNames.Parameter(index < method.Parameters.Count ? method.Parameters[index].Name : string.Empty, index);
                if (IsConversionParameterName(preferred))
                {
                    preferred += "Argument";
                }
                var name = preferred;
                var suffix = 2;
                while (!names.Add(name))
                {
                    name = preferred + suffix.ToString(CultureInfo.InvariantCulture);
                    suffix++;
                }
                result[index] = name;
            }
            return result;
        }

        /// <summary>Reserves recursive result-converter names so Java parameters cannot shadow generated lambdas.</summary>
        /// <param name="name">The preferred generated parameter name.</param>
        /// <returns>Whether the name is used by recursive reference conversion.</returns>
        private static bool IsConversionParameterName(string name)
        {
            var prefix = name.StartsWith("javaValue", StringComparison.Ordinal) ? "javaValue" :
                name.StartsWith("javaElement", StringComparison.Ordinal) ? "javaElement" : string.Empty;
            if (prefix.Length == 0 || name.Length == prefix.Length)
            {
                return false;
            }
            for (var index = prefix.Length; index < name.Length; index++)
            {
                if (name[index] < '0' || name[index] > '9')
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Creates a descriptive, descriptor-hashed suffix independent of extraction order.</summary>
        /// <param name="method">The erased overload or constructor.</param>
        /// <returns>The deterministic PascalCase method/factory suffix.</returns>
        private static string DescriptorSuffix(EmissionMethod method)
        {
            var description = method.Signature.Parameters.Count == 0 ? "NoArguments" :
                string.Join("And", method.Signature.Parameters.Select(DescriptorTypeName));
            if (description.Length > 120)
            {
                description = description.Substring(0, 120);
            }
            byte[] hash;
            using (var algorithm = SHA256.Create())
            {
                hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(method.Metadata.Descriptor));
            }
            var suffix = new StringBuilder();
            for (var index = 0; index < 8; index++)
            {
                suffix.Append(hash[index].ToString("X2", CultureInfo.InvariantCulture));
            }
            return "With" + description + "Signature" + suffix;
        }

        /// <summary>Describes a descriptor type for overload aliases.</summary>
        /// <param name="type">The parameter descriptor type.</param>
        /// <returns>A qualified, PascalCase type description.</returns>
        private static string DescriptorTypeName(JavaTypeReference type)
        {
            if (type.Code == '[' && type.Element != null)
            {
                return "ArrayOf" + DescriptorTypeName(type.Element);
            }
            if (type.Code == 'L')
            {
                return string.Concat(type.Name.Split('.').Select(JavaNames.PascalCase));
            }
            return JavaNames.PascalCase(DescribeJavaType(type));
        }

        /// <summary>Describes Java types in fallback parameter and result documentation.</summary>
        /// <param name="type">The parsed descriptor type.</param>
        /// <returns>The Java type name with array dimensions.</returns>
        private static string DescribeJavaType(JavaTypeReference type)
        {
            switch (type.Code)
            {
                case 'V':
                    return "void";
                case 'Z':
                    return "boolean";
                case 'B':
                    return "byte";
                case 'C':
                    return "char";
                case 'S':
                    return "short";
                case 'I':
                    return "int";
                case 'J':
                    return "long";
                case 'F':
                    return "float";
                case 'D':
                    return "double";
                case 'L':
                    return type.Name;
                case '[':
                    return type.Element == null ? "array" : DescribeJavaType(type.Element) + "[]";
                default:
                    return "Java value";
            }
        }

        /// <summary>Formats a keyword-safe block namespace.</summary>
        /// <param name="symbol">The namespace containing the wrapper.</param>
        /// <returns>The namespace declaration name, or empty for global scope.</returns>
        private static string GetNamespaceName(INamespaceSymbol symbol)
        {
            var parts = new Stack<string>();
            var current = symbol;
            while (!current.IsGlobalNamespace)
            {
                parts.Push(JavaNames.Escape(current.Name));
                current = current.ContainingNamespace;
            }
            return string.Join(".", parts);
        }
    }

    /// <summary>Stores one Java method's managed signature and final emission form.</summary>
    internal sealed class EmissionMethod
    {
        /// <summary>Gets the original Java method metadata.</summary>
        internal JavaApiMethod Metadata { get; }

        /// <summary>Gets the exact parsed descriptor.</summary>
        internal JavaMethodSignature Signature { get; }

        /// <summary>Gets the mapped C# parameter types.</summary>
        internal string[] ParameterTypes { get; }

        /// <summary>Gets the unique unescaped C# parameter names.</summary>
        internal string[] ParameterNames { get; }

        /// <summary>Gets or sets the final C# method or factory name.</summary>
        internal string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets whether an erased constructor is exposed as a static factory.</summary>
        internal bool IsFactory { get; set; }

        /// <summary>Gets whether this Java member is a constructor.</summary>
        internal bool IsConstructor
        {
            get
            {
                return Metadata.Name == "<init>";
            }
        }

        /// <summary>Creates a prepared method emission record.</summary>
        /// <param name="metadata">The original Java API metadata.</param>
        /// <param name="signature">The parsed erased descriptor.</param>
        /// <param name="parameterTypes">The mapped managed parameter types.</param>
        /// <param name="parameterNames">The unique managed parameter names.</param>
        internal EmissionMethod(JavaApiMethod metadata, JavaMethodSignature signature, string[] parameterTypes, string[] parameterNames)
        {
            Metadata = metadata;
            Signature = signature;
            ParameterTypes = parameterTypes;
            ParameterNames = parameterNames;
        }
    }
}
