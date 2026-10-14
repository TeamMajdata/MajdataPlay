using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Resolves source declarations against a complete source and dependency type index.</summary>
    internal sealed class JavaSourceResolver
    {
        /// <summary>Contains known implicit java.lang names usable even without dependency metadata.</summary>
        private static readonly HashSet<string> s_javaLangNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Object", "String", "StringBuilder", "StringBuffer", "CharSequence", "Appendable", "Comparable",
            "Iterable", "Cloneable", "AutoCloseable", "Readable", "Runnable", "Thread", "ThreadGroup", "ThreadLocal",
            "InheritableThreadLocal", "Throwable", "Exception", "RuntimeException", "Error", "Number", "Boolean",
            "Byte", "Character", "Short", "Integer", "Long", "Float", "Double", "Void", "Class", "Enum", "Record",
            "Math", "StrictMath", "System", "Process", "ProcessBuilder", "Runtime", "Package", "ClassLoader",
            "StackTraceElement", "SecurityManager", "Compiler", "Override", "Deprecated", "SuppressWarnings",
            "SafeVarargs", "FunctionalInterface", "ArithmeticException", "ArrayIndexOutOfBoundsException",
            "ArrayStoreException", "ClassCastException", "ClassNotFoundException", "CloneNotSupportedException",
            "EnumConstantNotPresentException", "IllegalAccessException", "IllegalArgumentException",
            "IllegalMonitorStateException", "IllegalStateException", "IllegalThreadStateException",
            "IndexOutOfBoundsException", "InstantiationException", "InterruptedException", "NegativeArraySizeException",
            "NoSuchFieldException", "NoSuchMethodException", "NullPointerException", "NumberFormatException",
            "ReflectiveOperationException", "SecurityException", "StringIndexOutOfBoundsException",
            "TypeNotPresentException", "UnsupportedOperationException", "AbstractMethodError", "AssertionError",
            "BootstrapMethodError", "ClassCircularityError", "ClassFormatError", "ExceptionInInitializerError",
            "IllegalAccessError", "IncompatibleClassChangeError", "InstantiationError", "InternalError", "LinkageError",
            "NoClassDefFoundError", "NoSuchFieldError", "NoSuchMethodError", "OutOfMemoryError", "StackOverflowError",
            "ThreadDeath", "UnknownError", "UnsatisfiedLinkError", "UnsupportedClassVersionError", "VerifyError",
            "VirtualMachineError", "Module", "ModuleLayer", "StackWalker"
        };

        /// <summary>Stores the output graph, which is changed only after successful resolution.</summary>
        private readonly JavaApi _api;

        /// <summary>Stores every dependency and newly declared source type by binary name.</summary>
        private readonly Dictionary<string, JavaType> _types;

        /// <summary>Stores source declarations by binary name.</summary>
        private readonly Dictionary<string, JavaSourceType> _sources = new Dictionary<string, JavaSourceType>(StringComparer.Ordinal);

        /// <summary>Stores source-qualified aliases of named nested declarations.</summary>
        private readonly Dictionary<string, HashSet<string>> _aliases = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        /// <summary>Tracks source hierarchies currently being resolved or already resolved.</summary>
        private readonly Dictionary<string, int> _hierarchyStates = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Collects warnings until extraction succeeds.</summary>
        private readonly List<string> _warnings = new List<string>();

        /// <summary>Stores the extraction cancellation token.</summary>
        private readonly CancellationToken _cancellationToken;

        /// <summary>Creates the first-pass type index, rejecting duplicate source declarations.</summary>
        /// <param name="api">The existing dependency metadata graph.</param>
        /// <param name="units">The source compilation units.</param>
        /// <param name="cancellationToken">The extraction cancellation token.</param>
        /// <exception cref="InvalidDataException">Two source declarations have the same binary name.</exception>
        private JavaSourceResolver(JavaApi api, IReadOnlyList<JavaSourceUnit> units, CancellationToken cancellationToken)
        {
            _api = api;
            _cancellationToken = cancellationToken;
            _types = new Dictionary<string, JavaType>(api.Types, StringComparer.Ordinal);
            foreach (var unit in units)
            {
                foreach (var type in unit.Types)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_sources.TryGetValue(type.Metadata.Name, out var previous))
                    {
                        throw Invalid(type, type.Token, $"duplicate source declaration '{type.Metadata.Name}', also declared in '{previous.Unit.Path}'");
                    }
                    _sources.Add(type.Metadata.Name, type);
                    if (_types.ContainsKey(type.Metadata.Name))
                    {
                        _warnings.Add($"Java source '{unit.Path}' replaces existing metadata for '{type.Metadata.Name}'; the scanner checks whether this override is permitted.");
                    }
                    _types[type.Metadata.Name] = type.Metadata;
                }
            }
            foreach (var type in _types.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddAlias(CanonicalName(type, new HashSet<string>(StringComparer.Ordinal)), type.Name);
            }
        }

        /// <summary>Adds source declarations after all dependency and source names are known.</summary>
        /// <param name="api">The dependency and output metadata graph.</param>
        /// <param name="units">The units to analyze together.</param>
        /// <param name="cancellationToken">The extraction cancellation token.</param>
        /// <exception cref="InvalidDataException">A name is unresolved, ambiguous, duplicate, or unsupported.</exception>
        /// <exception cref="OperationCanceledException">Extraction is canceled.</exception>
        internal static void AddDeclarations(JavaApi api, IReadOnlyList<JavaSourceUnit> units, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (units.Count == 0)
            {
                return;
            }
            new JavaSourceResolver(api, units, cancellationToken).ResolveAll();
        }

        /// <summary>Resolves hierarchies, erased bounds, and member descriptors before committing metadata.</summary>
        /// <exception cref="InvalidDataException">A source declaration cannot be resolved.</exception>
        private void ResolveAll()
        {
            foreach (var type in _sources.Values)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                ResolveHierarchy(type);
            }
            foreach (var type in _sources.Values)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                ValidateBounds(type, null, type.TypeParameters);
                type.Metadata.Signature = BuildClassSignature(type);
                var fieldNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var field in type.Fields)
                {
                    if (!fieldNames.Add(field.Token.Text))
                    {
                        throw Invalid(type, field.Token, $"duplicate field '{field.Token.Text}' in '{type.Metadata.Name}'");
                    }
                    type.Metadata.Fields.Add(new JavaField
                    {
                        Name = field.Token.Text,
                        Descriptor = Descriptor(field.Type, type, null, false),
                        Signature = HasGenericReference(field.Type, type, null) ? GenericTypeSignature(field.Type, type, null) : null,
                        AccessFlags = field.AccessFlags
                    });
                }
                var methodKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var method in type.Methods)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    ValidateBounds(type, method, method.TypeParameters);
                    var metadata = BuildMethod(type, method);
                    if (!methodKeys.Add(metadata.Name + metadata.Descriptor))
                    {
                        throw Invalid(type, method.Token, $"duplicate method '{metadata.Name}{metadata.Descriptor}'");
                    }
                    type.Metadata.Methods.Add(metadata);
                }
                AddImplicitMembers(type);
            }
            foreach (var type in _sources.Values)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                _api.Types[type.Metadata.Name] = type.Metadata;
            }
            _api.Warnings.AddRange(_warnings);
        }

        /// <summary>Resolves direct superclass and interface names, allowing lexical hierarchy lookup.</summary>
        /// <param name="type">The source declaration to resolve.</param>
        /// <exception cref="InvalidDataException">A hierarchy reference cannot be resolved.</exception>
        private void ResolveHierarchy(JavaSourceType type)
        {
            if (_hierarchyStates.TryGetValue(type.Metadata.Name, out var state))
            {
                if (state == 2 || state == 1)
                {
                    return;
                }
            }
            _hierarchyStates[type.Metadata.Name] = 1;
            if (type.SuperType is not null)
            {
                type.Metadata.SuperName = ReferenceName(type.SuperType, type, null);
            }
            else if (type.Metadata.Name == "java.lang.Object")
            {
                type.Metadata.SuperName = null;
            }
            else
            {
                type.Metadata.SuperName = type.Kind == "enum" ? "java.lang.Enum" : type.Kind == "record" ? "java.lang.Record" : "java.lang.Object";
            }
            if (type.Kind == "annotation")
            {
                type.Metadata.Interfaces.Add("java.lang.annotation.Annotation");
            }
            foreach (var implemented in type.Interfaces)
            {
                type.Metadata.Interfaces.Add(ReferenceName(implemented, type, null));
            }
            _hierarchyStates[type.Metadata.Name] = 2;
        }

        /// <summary>Checks every generic bound while preserving first-bound erasure.</summary>
        /// <param name="owner">The declaring lexical type.</param>
        /// <param name="method">The declaring method, if any.</param>
        /// <param name="parameters">The generic variables to validate.</param>
        /// <exception cref="InvalidDataException">A bound is unresolved, cyclic, or not a reference type.</exception>
        private void ValidateBounds(JavaSourceType owner, JavaSourceMethod? method, IReadOnlyList<JavaSourceTypeParameter> parameters)
        {
            foreach (var parameter in parameters)
            {
                Descriptor(new JavaSourceReference(parameter.Token, parameter.Token.Text), owner, method, false);
                foreach (var bound in parameter.Bounds)
                {
                    ReferenceName(bound, owner, method);
                }
            }
        }

        /// <summary>Builds a method descriptor, including mandated enclosing or enum constructor parameters.</summary>
        /// <param name="owner">The declaring type.</param>
        /// <param name="method">The source method or constructor.</param>
        /// <returns>The exact erased JVM method metadata.</returns>
        /// <exception cref="InvalidDataException">A parameter or return reference cannot be resolved.</exception>
        private JavaMethod BuildMethod(JavaSourceType owner, JavaSourceMethod method)
        {
            var result = new JavaMethod
            {
                Name = method.IsConstructor ? "<init>" : method.Token.Text,
                AccessFlags = method.AccessFlags
            };
            var descriptor = new StringBuilder("(");
            if (method.IsConstructor)
            {
                AddConstructorPrefix(owner, descriptor, result.ParameterNames);
            }
            foreach (var parameter in method.Parameters)
            {
                descriptor.Append(Descriptor(parameter.Type, owner, method, false));
                result.ParameterNames.Add(parameter.Token.Text);
            }
            descriptor.Append(')');
            if (method.IsConstructor)
            {
                descriptor.Append('V');
            }
            else
            {
                if (method.ReturnType is null)
                {
                    throw Invalid(owner, method.Token, "method return type is missing");
                }
                descriptor.Append(Descriptor(method.ReturnType, owner, method, true));
            }
            result.Descriptor = descriptor.ToString();
            result.Signature = BuildMethodSignature(owner, method);
            return result;
        }

        /// <summary>Builds the optional generic class Signature attribute independently from erased names.</summary>
        /// <param name="type">The resolved source declaration.</param>
        /// <returns>The JVM generic signature, or null when all types are raw.</returns>
        /// <exception cref="InvalidDataException">A generic reference cannot be resolved.</exception>
        private string? BuildClassSignature(JavaSourceType type)
        {
            var signature = new StringBuilder();
            AppendTypeParameters(signature, type.TypeParameters, type, null);
            var generic = type.TypeParameters.Count != 0 || type.Kind == "enum";
            if (type.SuperType is not null)
            {
                signature.Append(GenericTypeSignature(type.SuperType, type, null));
                generic |= HasGenericReference(type.SuperType, type, null);
            }
            else if (type.Kind == "enum")
            {
                signature.Append("Ljava/lang/Enum<").Append(ObjectDescriptor(type.Metadata.Name)).Append(">;");
            }
            else
            {
                signature.Append(ObjectDescriptor(type.Metadata.SuperName ?? "java.lang.Object"));
            }
            if (type.Kind == "annotation")
            {
                signature.Append("Ljava/lang/annotation/Annotation;");
            }
            foreach (var implemented in type.Interfaces)
            {
                signature.Append(GenericTypeSignature(implemented, type, null));
                generic |= HasGenericReference(implemented, type, null);
            }
            return generic ? signature.ToString() : null;
        }

        /// <summary>Builds a generic method Signature attribute with source-ordered constructor parameters.</summary>
        /// <param name="owner">The lexical declaring class.</param>
        /// <param name="method">The source method or constructor.</param>
        /// <returns>The optional generic method signature.</returns>
        /// <exception cref="InvalidDataException">A generic reference cannot be resolved.</exception>
        private string? BuildMethodSignature(JavaSourceType owner, JavaSourceMethod method)
        {
            var signature = new StringBuilder();
            AppendTypeParameters(signature, method.TypeParameters, owner, method);
            signature.Append('(');
            var generic = method.TypeParameters.Count != 0;
            foreach (var parameter in method.Parameters)
            {
                signature.Append(GenericTypeSignature(parameter.Type, owner, method));
                generic |= HasGenericReference(parameter.Type, owner, method);
            }
            signature.Append(')');
            if (method.IsConstructor)
            {
                signature.Append('V');
            }
            else if (method.ReturnType is not null)
            {
                signature.Append(GenericTypeSignature(method.ReturnType, owner, method));
                generic |= HasGenericReference(method.ReturnType, owner, method);
            }
            var genericThrows = false;
            foreach (var thrown in method.ThrowsTypes)
            {
                genericThrows |= HasGenericReference(thrown, owner, method);
            }
            if (genericThrows)
            {
                foreach (var thrown in method.ThrowsTypes)
                {
                    signature.Append('^').Append(GenericTypeSignature(thrown, owner, method));
                }
            }
            return generic || genericThrows ? signature.ToString() : null;
        }

        /// <summary>Appends formal generic variables and their class/interface intersection bounds.</summary>
        /// <param name="signature">The signature builder to update.</param>
        /// <param name="parameters">The variables in declaration order.</param>
        /// <param name="owner">The lexical declaring class.</param>
        /// <param name="method">The lexical declaring method, if any.</param>
        /// <exception cref="InvalidDataException">A generic bound cannot be resolved.</exception>
        private void AppendTypeParameters(StringBuilder signature, IReadOnlyList<JavaSourceTypeParameter> parameters, JavaSourceType owner, JavaSourceMethod? method)
        {
            if (parameters.Count == 0)
            {
                return;
            }
            signature.Append('<');
            foreach (var parameter in parameters)
            {
                signature.Append(parameter.Token.Text).Append(':');
                if (parameter.Bounds.Count == 0)
                {
                    signature.Append("Ljava/lang/Object;");
                    continue;
                }
                var first = parameter.Bounds[0];
                if (!TryFindVariable(first.Name, owner, method, out _, out _, out _))
                {
                    var name = ReferenceName(first, owner, method);
                    if ((_types.TryGetValue(name, out var boundType) && boundType.IsInterface)
                        || name == "java.lang.Comparable" || name == "java.lang.Runnable" || name == "java.lang.CharSequence"
                        || name == "java.lang.Cloneable" || name == "java.lang.AutoCloseable" || name == "java.lang.Iterable"
                        || name == "java.lang.Appendable" || name == "java.lang.Readable")
                    {
                        signature.Append(':');
                    }
                }
                for (var index = 0; index < parameter.Bounds.Count; index++)
                {
                    if (index != 0)
                    {
                        signature.Append(':');
                    }
                    signature.Append(GenericTypeSignature(parameter.Bounds[index], owner, method));
                }
            }
            signature.Append('>');
        }

        /// <summary>Determines whether a reference requires a generic Signature attribute.</summary>
        /// <param name="reference">The source type reference.</param>
        /// <param name="owner">The lexical declaring class.</param>
        /// <param name="method">The lexical declaring method, if any.</param>
        /// <returns>Whether the reference retains information beyond its erased descriptor.</returns>
        private bool HasGenericReference(JavaSourceReference reference, JavaSourceType owner, JavaSourceMethod? method)
        {
            if (reference.Wildcard is not null || reference.TypeArguments.Count != 0 || reference.DeclaringType is not null
                || TryFindVariable(reference.Name, owner, method, out _, out _, out _))
            {
                return true;
            }
            return PrimitiveDescriptor(reference.Name) is null && ImplicitDeclaringReference(ResolveName(reference.Name, owner, reference.Token), owner, reference.Token) is not null;
        }

        /// <summary>Restores generic enclosing arguments omitted by a lexical member-type reference.</summary>
        /// <param name="name">The resolved binary name of the referenced member type.</param>
        /// <param name="owner">The lexical declaring class.</param>
        /// <param name="token">The original diagnostic token.</param>
        /// <returns>The lexical parameterized enclosing reference, or null for a raw/static owner.</returns>
        private JavaSourceReference? ImplicitDeclaringReference(string name, JavaSourceType owner, JavaSourceToken token)
        {
            if (!_types.TryGetValue(name, out var type) || type.EnclosingType is null || (type.AccessFlags & 0x0008) != 0)
            {
                return null;
            }
            for (JavaSourceType? scope = owner; scope is not null; scope = scope.Enclosing)
            {
                if (scope.Metadata.Name == type.EnclosingType)
                {
                    var arguments = new List<JavaSourceReference>();
                    foreach (var parameter in scope.TypeParameters)
                    {
                        arguments.Add(new JavaSourceReference(parameter.Token, parameter.Token.Text));
                    }
                    var declaringType = ImplicitDeclaringReference(scope.Metadata.Name, scope, token);
                    if (arguments.Count == 0 && declaringType is null)
                    {
                        return null;
                    }
                    return new JavaSourceReference(token, scope.Metadata.Name, typeArguments: arguments, declaringType: declaringType);
                }
                if ((scope.Metadata.AccessFlags & 0x0008) != 0)
                {
                    break;
                }
            }
            return null;
        }

        /// <summary>Resolves a retained source reference to its exact JVM generic type signature.</summary>
        /// <param name="reference">The source reference, including arrays and wildcard bounds.</param>
        /// <param name="owner">The lexical declaring class.</param>
        /// <param name="method">The lexical declaring method, if any.</param>
        /// <returns>The complete generic type signature.</returns>
        /// <exception cref="InvalidDataException">A generic reference cannot be resolved.</exception>
        private string GenericTypeSignature(JavaSourceReference reference, JavaSourceType owner, JavaSourceMethod? method)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (reference.Wildcard is char variance)
            {
                if (variance == '*')
                {
                    return "*";
                }
                if (reference.WildcardBound is null)
                {
                    throw Invalid(owner, reference.Token, "a bounded wildcard has no bound");
                }
                return variance + GenericTypeSignature(reference.WildcardBound, owner, method);
            }
            var signature = new StringBuilder(new string('[', reference.Dimensions));
            var primitive = PrimitiveDescriptor(reference.Name);
            if (primitive is not null)
            {
                return signature.Append(primitive).ToString();
            }
            if (TryFindVariable(reference.Name, owner, method, out _, out _, out _))
            {
                return signature.Append('T').Append(reference.Name).Append(';').ToString();
            }
            var name = ResolveName(reference.Name, owner, reference.Token);
            var declaringReference = reference.DeclaringType ?? ImplicitDeclaringReference(name, owner, reference.Token);
            if (declaringReference is not null)
            {
                var declaringName = ResolveName(declaringReference.Name, owner, reference.Token);
                var declaringSignature = GenericTypeSignature(declaringReference, owner, method);
                signature.Append(declaringSignature, 0, declaringSignature.Length - 1).Append('.').Append(name.Substring(declaringName.Length + 1));
            }
            else
            {
                signature.Append('L').Append(name.Replace('.', '/'));
            }
            if (reference.TypeArguments.Count != 0)
            {
                signature.Append('<');
                foreach (var argument in reference.TypeArguments)
                {
                    signature.Append(GenericTypeSignature(argument, owner, method));
                }
                signature.Append('>');
            }
            return signature.Append(';').ToString();
        }

        /// <summary>Prepends JVM-mandated constructor parameters absent from the source parameter list.</summary>
        /// <param name="owner">The constructor's declaring type.</param>
        /// <param name="descriptor">The descriptor being built.</param>
        /// <param name="names">The descriptor-ordered parameter names.</param>
        private static void AddConstructorPrefix(JavaSourceType owner, StringBuilder descriptor, List<string> names)
        {
            if (owner.Kind == "enum")
            {
                descriptor.Append("Ljava/lang/String;I");
                names.Add("$enum$name");
                names.Add("$enum$ordinal");
            }
            else if (owner.Metadata.EnclosingType is not null && (owner.Metadata.AccessFlags & 0x0008) == 0)
            {
                descriptor.Append(ObjectDescriptor(owner.Metadata.EnclosingType));
                names.Add("this$0");
            }
        }

        /// <summary>Adds default constructors and the nonsynthetic API mandated for enums and records.</summary>
        /// <param name="type">The source declaration to complete.</param>
        /// <exception cref="InvalidDataException">A record component cannot be resolved.</exception>
        private void AddImplicitMembers(JavaSourceType type)
        {
            if (type.Kind == "enum")
            {
                var self = ObjectDescriptor(type.Metadata.Name);
                AddGeneratedMethod(type, new JavaMethod { Name = "values", Descriptor = "()[" + self, AccessFlags = 0x0009 });
                var valueOf = new JavaMethod { Name = "valueOf", Descriptor = "(Ljava/lang/String;)" + self, AccessFlags = 0x0009 };
                valueOf.ParameterNames.Add("name");
                AddGeneratedMethod(type, valueOf);
            }
            if (type.Kind == "record")
            {
                AddRecordMembers(type);
                return;
            }
            if (type.Metadata.IsInterface)
            {
                return;
            }
            foreach (var method in type.Metadata.Methods)
            {
                if (method.Name == "<init>")
                {
                    return;
                }
            }
            var constructor = new JavaMethod
            {
                Name = "<init>",
                AccessFlags = type.Kind == "enum" ? (ushort)0x0002 : (ushort)(type.Metadata.AccessFlags & 0x0007)
            };
            var descriptor = new StringBuilder("(");
            AddConstructorPrefix(type, descriptor, constructor.ParameterNames);
            constructor.Descriptor = descriptor.Append(")V").ToString();
            type.Metadata.Methods.Add(constructor);
        }

        /// <summary>Adds canonical constructors, fields, accessors, and record object methods.</summary>
        /// <param name="type">The record declaration.</param>
        /// <exception cref="InvalidDataException">A component has an unresolved or duplicate declaration.</exception>
        private void AddRecordMembers(JavaSourceType type)
        {
            var canonical = new JavaMethod { Name = "<init>", AccessFlags = (ushort)(type.Metadata.AccessFlags & 0x0007) };
            var descriptor = new StringBuilder("(");
            var signature = new StringBuilder("(");
            var generic = false;
            var componentNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var component in type.Components)
            {
                if (!componentNames.Add(component.Token.Text))
                {
                    throw Invalid(type, component.Token, $"duplicate record component '{component.Token.Text}'");
                }
                var componentDescriptor = Descriptor(component.Type, type, null, false);
                var componentSignature = GenericTypeSignature(component.Type, type, null);
                var genericComponent = HasGenericReference(component.Type, type, null);
                descriptor.Append(componentDescriptor);
                signature.Append(componentSignature);
                generic |= genericComponent;
                canonical.ParameterNames.Add(component.Token.Text);
                if (component.IsVarargs)
                {
                    canonical.AccessFlags |= 0x0080;
                }
                foreach (var field in type.Metadata.Fields)
                {
                    if (field.Name == component.Token.Text)
                    {
                        throw Invalid(type, component.Token, $"record field conflicts with component '{component.Token.Text}'");
                    }
                }
                type.Metadata.Fields.Add(new JavaField { Name = component.Token.Text, Descriptor = componentDescriptor, Signature = genericComponent ? componentSignature : null, AccessFlags = 0x0012 });
                AddGeneratedMethod(type, new JavaMethod { Name = component.Token.Text, Descriptor = "()" + componentDescriptor, Signature = genericComponent ? "()" + componentSignature : null, AccessFlags = 0x0001 });
            }
            canonical.Descriptor = descriptor.Append(")V").ToString();
            canonical.Signature = generic ? signature.Append(")V").ToString() : null;
            AddGeneratedMethod(type, canonical);
            var equals = new JavaMethod { Name = "equals", Descriptor = "(Ljava/lang/Object;)Z", AccessFlags = 0x0011 };
            equals.ParameterNames.Add("o");
            AddGeneratedMethod(type, equals);
            AddGeneratedMethod(type, new JavaMethod { Name = "hashCode", Descriptor = "()I", AccessFlags = 0x0011 });
            AddGeneratedMethod(type, new JavaMethod { Name = "toString", Descriptor = "()Ljava/lang/String;", AccessFlags = 0x0011 });
        }

        /// <summary>Adds a mandated method only when an explicit declaration supplies no exact signature.</summary>
        /// <param name="type">The declaring source type.</param>
        /// <param name="method">The method to add.</param>
        private static void AddGeneratedMethod(JavaSourceType type, JavaMethod method)
        {
            foreach (var existing in type.Metadata.Methods)
            {
                if (existing.Name == method.Name && existing.Descriptor == method.Descriptor)
                {
                    return;
                }
            }
            type.Metadata.Methods.Add(method);
        }

        /// <summary>Resolves a source reference to a complete JVM descriptor.</summary>
        /// <param name="reference">The source type reference.</param>
        /// <param name="owner">The lexical declaring type.</param>
        /// <param name="method">The lexical declaring method, if any.</param>
        /// <param name="allowVoid">Whether a scalar void return is permitted.</param>
        /// <returns>The exact erased descriptor.</returns>
        /// <exception cref="InvalidDataException">The reference is unresolved, ambiguous, cyclic, or invalid.</exception>
        private string Descriptor(JavaSourceReference reference, JavaSourceType owner, JavaSourceMethod? method, bool allowVoid)
        {
            return DescriptorCore(reference, owner, method, allowVoid, new HashSet<JavaSourceTypeParameter>());
        }

        /// <summary>Resolves references recursively through first-bound generic variable erasure.</summary>
        /// <param name="reference">The source type reference.</param>
        /// <param name="owner">The lexical declaring type.</param>
        /// <param name="method">The lexical declaring method.</param>
        /// <param name="allowVoid">Whether scalar void is permitted.</param>
        /// <param name="active">The generic variables currently being erased.</param>
        /// <returns>The exact erased descriptor.</returns>
        /// <exception cref="InvalidDataException">A reference is unresolved or generic erasure is cyclic.</exception>
        private string DescriptorCore(JavaSourceReference reference, JavaSourceType owner, JavaSourceMethod? method, bool allowVoid, HashSet<JavaSourceTypeParameter> active)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (reference.Dimensions > 255)
            {
                throw Invalid(owner, reference.Token, "array rank exceeds the JVM limit of 255");
            }
            var primitive = PrimitiveDescriptor(reference.Name);
            string erased;
            if (primitive is not null)
            {
                if (primitive == "V" && (!allowVoid || reference.Dimensions != 0))
                {
                    throw Invalid(owner, reference.Token, "void is only permitted as a scalar method return type");
                }
                erased = primitive;
            }
            else if (TryFindVariable(reference.Name, owner, method, out var parameter, out var declaringType, out var declaringMethod))
            {
                if (parameter is null || declaringType is null)
                {
                    throw Invalid(owner, reference.Token, "generic variable scope is missing");
                }
                if (!active.Add(parameter) || active.Count > 256)
                {
                    throw Invalid(owner, reference.Token, $"cyclic or excessively deep erasure of type variable '{reference.Name}'");
                }
                erased = parameter.Bounds.Count == 0
                    ? "Ljava/lang/Object;"
                    : DescriptorCore(parameter.Bounds[0], declaringType, declaringMethod, false, active);
                active.Remove(parameter);
                if (erased[0] != 'L')
                {
                    throw Invalid(owner, reference.Token, $"type variable '{reference.Name}' has an unsupported non-reference bound");
                }
            }
            else
            {
                erased = ObjectDescriptor(ResolveName(reference.Name, owner, reference.Token));
            }
            return new string('[', reference.Dimensions) + erased;
        }

        /// <summary>Resolves a nonarray hierarchy or intersection-bound reference.</summary>
        /// <param name="reference">The source reference.</param>
        /// <param name="owner">The declaring lexical type.</param>
        /// <param name="method">The declaring lexical method.</param>
        /// <returns>The dot-separated binary reference name.</returns>
        /// <exception cref="InvalidDataException">The reference does not resolve to a scalar object type.</exception>
        private string ReferenceName(JavaSourceReference reference, JavaSourceType owner, JavaSourceMethod? method)
        {
            var descriptor = Descriptor(reference, owner, method, false);
            if (descriptor[0] != 'L')
            {
                throw Invalid(owner, reference.Token, $"'{reference.Name}' must denote a scalar reference type");
            }
            return descriptor.Substring(1, descriptor.Length - 2).Replace('/', '.');
        }

        /// <summary>Finds generic variables with method shadowing and static nested-type scope boundaries.</summary>
        /// <param name="name">The source variable name.</param>
        /// <param name="owner">The declaring lexical type.</param>
        /// <param name="method">The declaring lexical method.</param>
        /// <param name="parameter">The found variable, or null.</param>
        /// <param name="declaringType">The variable's lexical type scope.</param>
        /// <param name="declaringMethod">The variable's method scope, or null for type variables.</param>
        /// <returns>Whether the name denotes an in-scope type variable.</returns>
        private static bool TryFindVariable(string name, JavaSourceType owner, JavaSourceMethod? method, out JavaSourceTypeParameter? parameter, out JavaSourceType? declaringType, out JavaSourceMethod? declaringMethod)
        {
            parameter = null;
            declaringType = null;
            declaringMethod = null;
            if (name.IndexOf('.') >= 0)
            {
                return false;
            }
            if (method is not null)
            {
                foreach (var candidate in method.TypeParameters)
                {
                    if (candidate.Token.Text == name)
                    {
                        parameter = candidate;
                        declaringType = owner;
                        declaringMethod = method;
                        return true;
                    }
                }
            }
            for (JavaSourceType? scope = owner; scope is not null; scope = scope.Enclosing)
            {
                foreach (var candidate in scope.TypeParameters)
                {
                    if (candidate.Token.Text == name)
                    {
                        parameter = candidate;
                        declaringType = scope;
                        return true;
                    }
                }
                if ((scope.Metadata.AccessFlags & 0x0008) != 0)
                {
                    break;
                }
            }
            return false;
        }

        /// <summary>Resolves lexical, imported, package, and fully qualified source names.</summary>
        /// <param name="name">The name without generic arguments.</param>
        /// <param name="owner">The lexical declaring type.</param>
        /// <param name="token">The diagnostic token.</param>
        /// <returns>The actual JVM binary name.</returns>
        /// <exception cref="InvalidDataException">A name is unresolved or any lookup is ambiguous.</exception>
        private string ResolveName(string name, JavaSourceType owner, JavaSourceToken token)
        {
            var separator = name.IndexOf('.');
            var first = separator < 0 ? name : name.Substring(0, separator);
            var scoped = TrySimpleName(first, owner, token);
            if (scoped is not null)
            {
                return separator < 0 ? scoped : ResolveNestedSuffix(scoped, name.Substring(separator + 1), owner, token);
            }
            if (separator >= 0)
            {
                var qualified = TryQualifiedName(name, owner, token);
                if (qualified is not null)
                {
                    return qualified;
                }
            }
            throw Invalid(owner, token, $"unresolved type '{name}' in '{owner.Metadata.Name}'; supply the corresponding source, SDK, class, or JAR metadata");
        }

        /// <summary>Looks up one simple name in Java's lexical and import precedence order.</summary>
        /// <param name="name">The simple source name.</param>
        /// <param name="owner">The lexical declaring type.</param>
        /// <param name="token">The diagnostic token.</param>
        /// <returns>The binary name, or null if no scope supplies it.</returns>
        /// <exception cref="InvalidDataException">More than one declaration has the same lookup precedence.</exception>
        private string? TrySimpleName(string name, JavaSourceType owner, JavaSourceToken token)
        {
            for (JavaSourceType? scope = owner; scope is not null; scope = scope.Enclosing)
            {
                if (scope.Token.Text == name)
                {
                    return scope.Metadata.Name;
                }
                var nested = FindNested(scope.Metadata.Name, name, owner, token);
                if (nested is not null)
                {
                    return nested;
                }
            }
            foreach (var declared in owner.Unit.Types)
            {
                if (declared.Enclosing is null && declared.Token.Text == name)
                {
                    return declared.Metadata.Name;
                }
            }
            var candidates = new HashSet<string>(StringComparer.Ordinal);
            foreach (var import in owner.Unit.Imports)
            {
                if (!import.IsWildcard && LastSegment(import.Name) == name)
                {
                    var imported = TryQualifiedName(import.Name, owner, token);
                    if (imported is not null)
                    {
                        candidates.Add(imported);
                    }
                }
            }
            if (candidates.Count != 0)
            {
                return Unique(candidates, name, owner, token);
            }
            var packageName = owner.Unit.PackageName.Length == 0 ? name : owner.Unit.PackageName + "." + name;
            var inPackage = TryKnownName(packageName, owner, token);
            if (inPackage is not null)
            {
                return inPackage;
            }
            foreach (var import in owner.Unit.Imports)
            {
                if (!import.IsWildcard)
                {
                    continue;
                }
                var importedOwner = TryQualifiedName(import.Name, owner, token);
                if (importedOwner is not null)
                {
                    var nested = FindNested(importedOwner, name, owner, token);
                    if (nested is not null && (!import.IsStatic || (_types[nested].AccessFlags & 0x0008) != 0))
                    {
                        candidates.Add(nested);
                    }
                }
                else if (!import.IsStatic)
                {
                    var imported = TryKnownName(import.Name + "." + name, owner, token);
                    if (imported is not null)
                    {
                        candidates.Add(imported);
                    }
                }
            }
            var implicitName = TryKnownName("java.lang." + name, owner, token);
            if (implicitName is not null)
            {
                candidates.Add(implicitName);
            }
            else if (s_javaLangNames.Contains(name))
            {
                candidates.Add("java.lang." + name);
            }
            return candidates.Count == 0 ? null : Unique(candidates, name, owner, token);
        }

        /// <summary>Resolves a qualified import or type name, including inherited member types.</summary>
        /// <param name="name">The source-qualified name.</param>
        /// <param name="owner">The lexical declaring type for diagnostics.</param>
        /// <param name="token">The diagnostic token.</param>
        /// <returns>The binary name, or null when dependency metadata does not identify it.</returns>
        /// <exception cref="InvalidDataException">A known type prefix has no such member or lookup is ambiguous.</exception>
        private string? TryQualifiedName(string name, JavaSourceType owner, JavaSourceToken token)
        {
            var known = TryKnownName(name, owner, token);
            if (known is not null)
            {
                return known;
            }
            for (var separator = name.LastIndexOf('.'); separator > 0; separator = name.LastIndexOf('.', separator - 1))
            {
                var prefix = TryKnownName(name.Substring(0, separator), owner, token);
                if (prefix is not null)
                {
                    return ResolveNestedSuffix(prefix, name.Substring(separator + 1), owner, token);
                }
            }
            return null;
        }

        /// <summary>Looks up exact binary names or known canonical nested aliases.</summary>
        /// <param name="name">The binary or source-qualified name.</param>
        /// <param name="owner">The diagnostic scope.</param>
        /// <param name="token">The diagnostic token.</param>
        /// <returns>The binary name, or null when metadata does not identify it.</returns>
        /// <exception cref="InvalidDataException">Canonical lookup is ambiguous.</exception>
        private string? TryKnownName(string name, JavaSourceType owner, JavaSourceToken token)
        {
            if (_types.ContainsKey(name))
            {
                return name;
            }
            if (_aliases.TryGetValue(name, out var candidates))
            {
                return Unique(candidates, name, owner, token);
            }
            if (name.StartsWith("java.lang.", StringComparison.Ordinal) && s_javaLangNames.Contains(name.Substring(10)))
            {
                return name;
            }
            return null;
        }

        /// <summary>Resolves each member-type segment using actual declaration metadata.</summary>
        /// <param name="binaryName">The already resolved outer type.</param>
        /// <param name="suffix">The source-dot member path.</param>
        /// <param name="owner">The diagnostic scope.</param>
        /// <param name="token">The diagnostic token.</param>
        /// <returns>The actual final binary name.</returns>
        /// <exception cref="InvalidDataException">A nested member is unresolved or ambiguous.</exception>
        private string ResolveNestedSuffix(string binaryName, string suffix, JavaSourceType owner, JavaSourceToken token)
        {
            var current = binaryName;
            foreach (var segment in suffix.Split('.'))
            {
                var nested = FindNested(current, segment, owner, token);
                if (nested is null)
                {
                    throw Invalid(owner, token, $"unresolved nested type '{segment}' in '{current}' while resolving '{binaryName}.{suffix}'");
                }
                current = nested;
            }
            return current;
        }

        /// <summary>Finds a direct or inherited member type, respecting hiding within each hierarchy branch.</summary>
        /// <param name="binaryName">The outer type to search.</param>
        /// <param name="simpleName">The member type's simple name.</param>
        /// <param name="owner">The diagnostic lexical scope.</param>
        /// <param name="token">The diagnostic token.</param>
        /// <returns>The found binary name, or null.</returns>
        /// <exception cref="InvalidDataException">Inherited member lookup is ambiguous or excessively deep.</exception>
        private string? FindNested(string binaryName, string simpleName, JavaSourceType owner, JavaSourceToken token)
        {
            var candidates = new HashSet<string>(StringComparer.Ordinal);
            CollectNested(binaryName, simpleName, candidates, new HashSet<string>(StringComparer.Ordinal), owner, token, 0);
            return candidates.Count == 0 ? null : Unique(candidates, simpleName, owner, token);
        }

        /// <summary>Collects inherited member declarations, stopping a branch at its nearest declaration.</summary>
        /// <param name="binaryName">The current hierarchy type.</param>
        /// <param name="simpleName">The desired nested name.</param>
        /// <param name="candidates">The declarations found.</param>
        /// <param name="visited">The hierarchy nodes already visited.</param>
        /// <param name="owner">The diagnostic scope.</param>
        /// <param name="token">The diagnostic token.</param>
        /// <param name="depth">The hierarchy recursion depth.</param>
        /// <exception cref="InvalidDataException">The hierarchy is excessively deep.</exception>
        private void CollectNested(string binaryName, string simpleName, HashSet<string> candidates, HashSet<string> visited, JavaSourceType owner, JavaSourceToken token, int depth)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (depth > 256)
            {
                throw Invalid(owner, token, "nested member lookup exceeds 256 hierarchy levels");
            }
            if (!visited.Add(binaryName))
            {
                return;
            }
            var direct = binaryName + "$" + simpleName;
            if (_types.TryGetValue(direct, out var member) && member.EnclosingType == binaryName)
            {
                candidates.Add(direct);
                return;
            }
            if (!_types.TryGetValue(binaryName, out var type))
            {
                return;
            }
            if (_sources.TryGetValue(binaryName, out var source))
            {
                if (!_hierarchyStates.TryGetValue(binaryName, out var state) || state != 1)
                {
                    ResolveHierarchy(source);
                }
            }
            if (type.SuperName is not null)
            {
                CollectNested(type.SuperName, simpleName, candidates, visited, owner, token, depth + 1);
            }
            foreach (var implemented in type.Interfaces)
            {
                CollectNested(implemented, simpleName, candidates, visited, owner, token, depth + 1);
            }
        }

        /// <summary>Chooses a unique lookup result and reports competing binary declarations.</summary>
        /// <param name="candidates">The lookup results at one precedence level.</param>
        /// <param name="name">The unresolved source spelling.</param>
        /// <param name="owner">The diagnostic lexical type.</param>
        /// <param name="token">The diagnostic token.</param>
        /// <returns>The unique binary name.</returns>
        /// <exception cref="InvalidDataException">The lookup has multiple results.</exception>
        private static string Unique(HashSet<string> candidates, string name, JavaSourceType owner, JavaSourceToken token)
        {
            if (candidates.Count > 1)
            {
                var names = new List<string>(candidates);
                names.Sort(StringComparer.Ordinal);
                throw Invalid(owner, token, $"ambiguous type '{name}' in '{owner.Metadata.Name}': {string.Join(", ", names)}");
            }
            foreach (var candidate in candidates)
            {
                return candidate;
            }
            throw Invalid(owner, token, $"unresolved type '{name}'");
        }

        /// <summary>Builds canonical source names using recorded enclosing relationships rather than guessing dollar signs.</summary>
        /// <param name="type">The declaration whose canonical name is needed.</param>
        /// <param name="active">The enclosing chain being visited.</param>
        /// <returns>The source-dot name for named nested types.</returns>
        private string CanonicalName(JavaType type, HashSet<string> active)
        {
            if (!active.Add(type.Name) || type.EnclosingType is null || !type.Name.StartsWith(type.EnclosingType + "$", StringComparison.Ordinal))
            {
                return type.Name;
            }
            var prefix = _types.TryGetValue(type.EnclosingType, out var enclosing)
                ? CanonicalName(enclosing, active)
                : type.EnclosingType.Replace('$', '.');
            return prefix + "." + type.Name.Substring(type.EnclosingType.Length + 1);
        }

        /// <summary>Adds a canonical name to the first-pass alias index.</summary>
        /// <param name="canonical">The source-qualified spelling.</param>
        /// <param name="binary">The actual JVM binary name.</param>
        private void AddAlias(string canonical, string binary)
        {
            if (!_aliases.TryGetValue(canonical, out var names))
            {
                names = new HashSet<string>(StringComparer.Ordinal);
                _aliases.Add(canonical, names);
            }
            names.Add(binary);
        }

        /// <summary>Gets the final segment of a qualified import.</summary>
        /// <param name="name">The qualified import name.</param>
        /// <returns>The imported simple name.</returns>
        private static string LastSegment(string name)
        {
            return name.Substring(name.LastIndexOf('.') + 1);
        }

        /// <summary>Maps primitive Java spellings to JVM descriptor codes.</summary>
        /// <param name="name">The source type spelling.</param>
        /// <returns>The descriptor code, or null for references.</returns>
        private static string? PrimitiveDescriptor(string name)
        {
            switch (name)
            {
                case "boolean":
                    return "Z";
                case "byte":
                    return "B";
                case "char":
                    return "C";
                case "short":
                    return "S";
                case "int":
                    return "I";
                case "long":
                    return "J";
                case "float":
                    return "F";
                case "double":
                    return "D";
                case "void":
                    return "V";
                default:
                    return null;
            }
        }

        /// <summary>Converts an actual binary class name into an object descriptor.</summary>
        /// <param name="binaryName">The dot-separated name preserving nested dollar signs.</param>
        /// <returns>The JVM object descriptor.</returns>
        private static string ObjectDescriptor(string binaryName)
        {
            return "L" + binaryName.Replace('.', '/') + ";";
        }

        /// <summary>Creates a source-located type-resolution error.</summary>
        /// <param name="owner">The lexical source declaration.</param>
        /// <param name="token">The diagnostic token.</param>
        /// <param name="message">The diagnostic detail.</param>
        /// <returns>The extraction error.</returns>
        private static InvalidDataException Invalid(JavaSourceType owner, JavaSourceToken token, string message)
        {
            return new InvalidDataException($"Java source '{owner.Unit.Path}' ({token.Line},{token.Column}): {message}.");
        }
    }
}
