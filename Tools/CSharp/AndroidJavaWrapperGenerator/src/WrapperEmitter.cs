using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Emits independent C# 9 wrappers for the effective public Java API.</summary>
    internal static partial class WrapperEmitter
    {
        /// <summary>Generates deterministic namespace-relative source files and their dependency closure.</summary>
        /// <param name="api">The extracted JVM declarations, including nonpublic hierarchy metadata.</param>
        /// <param name="options">The root namespace, optional Java package filters, and worker count.</param>
        /// <param name="ct">The cancellation token for selection, planning, and file emission.</param>
        /// <returns>The relative output paths mapped to nullable, CRLF C# source.</returns>
        /// <exception cref="ArgumentNullException">An input is null.</exception>
        /// <exception cref="FormatException">A public member has a malformed JVM descriptor.</exception>
        /// <exception cref="InvalidOperationException">The input contains a cyclic inheritance hierarchy.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The configured worker count is not positive.</exception>
        /// <exception cref="OperationCanceledException">Cancellation is requested.</exception>
        internal static IReadOnlyDictionary<string, string> Generate(JavaApi api, GeneratorOptions options, CancellationToken ct = default)
        {
            if (api is null)
            {
                throw new ArgumentNullException(nameof(api));
            }
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            return new Emitter(api, options, ct).Generate();
        }

        /// <summary>Finds the real metadata declarations exported by the public selection and signature closure.</summary>
        /// <param name="api">The extracted JVM declarations, including hierarchy metadata.</param>
        /// <param name="options">The optional Java package filters used for wrapper generation.</param>
        /// <returns>The selected input declarations ordered by Java binary name, excluding synthetic fallbacks.</returns>
        /// <exception cref="ArgumentNullException">An input is null.</exception>
        /// <exception cref="FormatException">A public member has a malformed JVM descriptor.</exception>
        internal static IReadOnlyList<JavaType> GetSelectedTypes(JavaApi api, GeneratorOptions options)
        {
            if (api is null)
            {
                throw new ArgumentNullException(nameof(api));
            }
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            return new Emitter(api, options, CancellationToken.None).GetSelectedTypes();
        }

        /// <summary>Stores one generation's names, hierarchy plans, and descriptor caches.</summary>
        private sealed partial class Emitter
        {
            /// <summary>Identifies the JVM root, including in SDK-free metadata sets.</summary>
            private const string ObjectName = "java.lang.Object";

            /// <summary>Identifies the JVM reflection class used by every Class property.</summary>
            private const string ClassName = "java.lang.Class";

            /// <summary>Identifies the JVM exception root that must derive from a CLR exception.</summary>
            private const string ThrowableName = "java.lang.Throwable";

            /// <summary>Identifies Unity's owned JNI object handle.</summary>
            private const string UnityObject = "global::UnityEngine.AndroidJavaObject";

            /// <summary>Lists runtime, CLR, and generated members requiring Java aliases.</summary>
            private static readonly HashSet<string> s_reservedNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "Class", "ClassJavaProperty", "JavaReference", "Dispose", "Equals", "GetHashCode", "GetType",
                "ToString", "Finalize", "MemberwiseClone", "ReferenceEquals"
            };

            /// <summary>Lists CLR exception members reserved only in the Java throwable hierarchy.</summary>
            private static readonly HashSet<string> s_exceptionReservedNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "JavaClassName", "JavaStackTrace", "Message", "StackTrace", "InnerException", "Data", "HelpLink", "HResult", "Source", "TargetSite"
            };

            /// <summary>Receives metadata diagnostics without modifying extracted declarations.</summary>
            private readonly JavaApi _api;

            /// <summary>Stores the configured namespace and package selection.</summary>
            private readonly GeneratorOptions _options;

            /// <summary>Observes cancellation during serial planning and independent file emission.</summary>
            private readonly CancellationToken _ct;

            /// <summary>Buffers selection diagnostics until generation completes successfully.</summary>
            private readonly List<string> _warnings = new List<string>();

            /// <summary>Prevents reference-cache writes after all emission plans have been completed.</summary>
            private bool _referencesFrozen;

            /// <summary>Contains input declarations and the guaranteed Object fallback.</summary>
            private readonly Dictionary<string, JavaType> _types;

            /// <summary>Contains the public types selected by filters and dependency closure.</summary>
            private readonly HashSet<string> _selected = new HashSet<string>(StringComparer.Ordinal);

            /// <summary>Stores each public type's package namespace.</summary>
            private readonly Dictionary<string, string> _namespaces = new Dictionary<string, string>(StringComparer.Ordinal);

            /// <summary>Stores simple wrapper names after namespace and type collision resolution.</summary>
            private readonly Dictionary<string, string> _wrapperNames = new Dictionary<string, string>(StringComparer.Ordinal);

            /// <summary>Stores simple public interface names after wrapper collision resolution.</summary>
            private readonly Dictionary<string, string> _interfaceNames = new Dictionary<string, string>(StringComparer.Ordinal);

            /// <summary>Caches completed class and interface emission plans.</summary>
            private readonly Dictionary<string, IReadOnlyList<string>> _nestedNames = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

            /// <summary>Caches completed class and interface emission plans.</summary>
            private readonly Dictionary<string, TypePlan> _plans = new Dictionary<string, TypePlan>(StringComparer.Ordinal);

            /// <summary>Detects cycles while recursively planning base classes and interfaces.</summary>
            private readonly HashSet<string> _planning = new HashSet<string>(StringComparer.Ordinal);

            /// <summary>Caches parsed method descriptors shared across the SDK.</summary>
            private readonly Dictionary<string, JavaMethodDescriptor> _methodDescriptors = new Dictionary<string, JavaMethodDescriptor>(StringComparer.Ordinal);

            /// <summary>Caches parsed field descriptors shared across the SDK.</summary>
            private readonly Dictionary<string, JavaTypeDescriptor> _fieldDescriptors = new Dictionary<string, JavaTypeDescriptor>(StringComparer.Ordinal);

            /// <summary>Caches visible substitutes for inaccessible or unavailable signature types.</summary>
            private readonly Dictionary<string, string> _referenceNames = new Dictionary<string, string>(StringComparer.Ordinal);

            /// <summary>Suppresses repeated diagnostics for unavailable reference metadata.</summary>
            private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

            /// <summary>Identifies the independent runtime invocation helper.</summary>
            private readonly string _jni;

            /// <summary>Identifies the namespace containing generated Java package bindings.</summary>
            private readonly string _bindingsNamespace;

            /// <summary>Initializes generation and guarantees a real Object wrapper declaration.</summary>
            /// <param name="api">The extracted JVM metadata and diagnostic collection.</param>
            /// <param name="options">The namespace, package filters, and worker count.</param>
            /// <param name="ct">The cancellation token observed throughout generation.</param>
            internal Emitter(JavaApi api, GeneratorOptions options, CancellationToken ct)
            {
                _api = api;
                _options = options;
                _ct = ct;
                _types = new Dictionary<string, JavaType>(api.Types, StringComparer.Ordinal);
                _jni = "global::" + options.RootNamespace + ".AndroidJni";
                _bindingsNamespace = JavaNames.Namespace(options.RootNamespace, string.Empty);
                EnsureRuntimeType(ClassName, ObjectName, "<T:Ljava/lang/Object;>Ljava/lang/Object;");
                EnsureRuntimeType(ThrowableName, ObjectName, null);
                if (!_types.ContainsKey(ObjectName))
                {
                    _types.Add(ObjectName, new JavaType
                    {
                        Name = ObjectName,
                        AccessFlags = 0x0001,
                        IsPublic = true
                    });
                }
            }

            /// <summary>Plans the closed API and emits one source file per public Java type.</summary>
            /// <returns>A deterministically ordered dictionary of relative source paths.</returns>
            /// <exception cref="FormatException">A selected public descriptor is malformed.</exception>
            /// <exception cref="InvalidOperationException">An inheritance cycle is discovered.</exception>
            /// <exception cref="ArgumentOutOfRangeException">The configured worker count is not positive.</exception>
            /// <exception cref="OperationCanceledException">Cancellation is requested.</exception>
            internal IReadOnlyDictionary<string, string> Generate()
            {
                _ct.ThrowIfCancellationRequested();
                if (_options.Jobs < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(_options), "The worker count must be positive.");
                }
                SelectTypes();
                AllocateTypeNames();
                var plans = new TypePlan[_selected.Count];
                var paths = new string[plans.Length];
                var planIndex = 0;
                foreach (var name in _selected.OrderBy(value => value, StringComparer.Ordinal))
                {
                    _ct.ThrowIfCancellationRequested();
                    var plan = BuildPlan(name);
                    var relativeNamespace = plan.Namespace.Substring(_options.RootNamespace.Length).TrimStart('.');
                    var directory = relativeNamespace.Replace('.', '/');
                    plans[planIndex] = plan;
                    paths[planIndex] = (directory.Length == 0 ? string.Empty : directory + "/") + WrapperPath(name) + ".g.cs";
                    planIndex++;
                }
                foreach (var plan in plans)
                {
                    _ct.ThrowIfCancellationRequested();
                    plan.Constructors = BuildConstructors(plan);
                }
                _referencesFrozen = true;
                var files = new string[plans.Length];
                var diagnostics = new IReadOnlyList<string>[plans.Length];
                ParallelWork.For(plans.Length, _options.Jobs, index =>
                {
                    var warnings = new List<string>();
                    files[index] = Emit(plans[index], warnings);
                    diagnostics[index] = warnings;
                }, _ct);
                var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
                for (var index = 0; index < paths.Length; index++)
                {
                    _ct.ThrowIfCancellationRequested();
                    result.Add(paths[index], files[index]);
                }
                result.Add("JavaWrapperConverter.Exceptions.cs", EmitExceptionRegistry());
                result.Add("JavaObjectExtensions.cs", EmitObjectExtensions());
                var committedWarnings = new HashSet<string>(_api.Warnings, StringComparer.Ordinal);
                foreach (var warning in _warnings.Concat(diagnostics.SelectMany(value => value))
                    .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
                {
                    if (committedWarnings.Add(warning))
                    {
                        _api.Warnings.Add(warning);
                    }
                }
                return result;
            }

            /// <summary>Expands the same public dependency closure without emitting files or committing diagnostics.</summary>
            /// <returns>The deterministically ordered selected declarations present in the input metadata.</returns>
            /// <exception cref="FormatException">A public member descriptor is malformed.</exception>
            internal IReadOnlyList<JavaType> GetSelectedTypes()
            {
                SelectTypes();
                return _selected.Where(name => _api.Types.ContainsKey(name)).OrderBy(name => name, StringComparer.Ordinal)
                    .Select(name => _api.Types[name]).ToArray();
            }

            /// <summary>Expands selected packages through public hierarchies and signature references.</summary>
            /// <exception cref="FormatException">A public member descriptor is malformed.</exception>
            /// <exception cref="OperationCanceledException">Cancellation is requested.</exception>
            private void SelectTypes()
            {
                _ct.ThrowIfCancellationRequested();
                var pending = new Queue<JavaType>();
                foreach (var type in _types.Values.OrderBy(value => value.Name, StringComparer.Ordinal))
                {
                    _ct.ThrowIfCancellationRequested();
                    if (IsExportable(type) && IsIncluded(JavaNames.Package(type.Name)))
                    {
                        AddSelected(type.Name, pending);
                    }
                }
                AddSelected(ObjectName, pending);
                AddSelected(ClassName, pending);
                AddSelected(ThrowableName, pending);
                while (pending.Count != 0)
                {
                    _ct.ThrowIfCancellationRequested();
                    var selected = pending.Dequeue();
                    if (selected.EnclosingType is not null)
                    {
                        AddSelected(selected.EnclosingType, pending);
                    }
                    SelectGenericReferences(selected, pending);
                    foreach (var type in Hierarchy(selected, true))
                    {
                        if (IsExportable(type))
                        {
                            AddSelected(type.Name, pending);
                        }
                        foreach (var field in type.Fields.Where(IsVisibleField))
                        {
                            SelectReference(ParseField(field.Descriptor), pending);
                        }
                        foreach (var method in type.Methods.Where(IsVisibleMethod))
                        {
                            if (method.Name == "<clinit>" || method.Name == "<init>" && type != selected)
                            {
                                continue;
                            }
                            var signature = ParseMethod(method.Descriptor);
                            SelectReference(signature.ReturnType, pending);
                            foreach (var parameter in signature.Parameters)
                            {
                                SelectReference(parameter, pending);
                            }
                        }
                    }
                }
            }

            /// <summary>Checks a package against exact prefixes and their child packages.</summary>
            /// <param name="package">The Java package being considered.</param>
            /// <returns>Whether the package is an initial selection.</returns>
            private bool IsIncluded(string package)
            {
                return _options.IncludePackages.Count == 0 || _options.IncludePackages.Any(prefix =>
                    package == prefix.TrimEnd('.') || package.StartsWith(prefix.TrimEnd('.') + ".", StringComparison.Ordinal));
            }

            /// <summary>Enqueues a public type only once.</summary>
            /// <param name="name">The candidate JVM binary name.</param>
            /// <param name="pending">The dependency expansion queue.</param>
            private void AddSelected(string name, Queue<JavaType> pending)
            {
                if (_types.TryGetValue(name, out var type) && IsExportable(type) && _selected.Add(name))
                {
                    pending.Enqueue(type);
                }
            }

            /// <summary>Selects a signature reference and the visible type used to represent it.</summary>
            /// <param name="descriptor">The field, parameter, or return descriptor.</param>
            /// <param name="pending">The dependency expansion queue.</param>
            private void SelectReference(JavaTypeDescriptor descriptor, Queue<JavaType> pending)
            {
                if (descriptor.Element is not null)
                {
                    SelectReference(descriptor.Element, pending);
                }
                else if (descriptor.Code == 'L')
                {
                    AddSelected(descriptor.BinaryName, pending);
                    AddSelected(ReferenceName(descriptor.BinaryName), pending);
                }
            }

            /// <summary>Checks effective public visibility, including enclosing declarations.</summary>
            /// <param name="type">The class or interface to inspect.</param>
            /// <returns>Whether a public nonsynthetic wrapper can be emitted.</returns>
            private bool IsExportable(JavaType type)
            {
                if (!type.IsPublic || type.IsSynthetic)
                {
                    return false;
                }
                var enclosing = type.EnclosingType;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                while (enclosing is not null && seen.Add(enclosing) && _types.TryGetValue(enclosing, out var owner))
                {
                    if (!owner.IsPublic || owner.IsSynthetic)
                    {
                        return false;
                    }
                    enclosing = owner.EnclosingType;
                }
                return true;
            }

            /// <summary>Enumerates known class and interface ancestors without requiring public visibility.</summary>
            /// <param name="type">The most derived declaration.</param>
            /// <param name="includeInterfaces">Whether implemented interfaces are traversed.</param>
            /// <returns>The declaration followed by its known ancestors, with each type once.</returns>
            private IEnumerable<JavaType> Hierarchy(JavaType type, bool includeInterfaces)
            {
                var pending = new Queue<JavaType>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                pending.Enqueue(type);
                while (pending.Count != 0)
                {
                    var current = pending.Dequeue();
                    if (!seen.Add(current.Name))
                    {
                        continue;
                    }
                    yield return current;
                    if (current.SuperName is not null && _types.TryGetValue(current.SuperName, out var parent))
                    {
                        pending.Enqueue(parent);
                    }
                    if (includeInterfaces)
                    {
                        foreach (var name in current.Interfaces.OrderBy(value => value, StringComparer.Ordinal))
                        {
                            if (_types.TryGetValue(name, out var implemented))
                            {
                                pending.Enqueue(implemented);
                            }
                        }
                    }
                }
            }

            /// <summary>Finds a public signature substitute for nonpublic or missing declarations.</summary>
            /// <param name="name">The declared reference binary name.</param>
            /// <returns>An available public binary name, falling back to java.lang.Object.</returns>
            /// <exception cref="InvalidOperationException">Emission requests a reference absent from the completed plans.</exception>
            private string ReferenceName(string name)
            {
                if (_referenceNames.TryGetValue(name, out var cached))
                {
                    return cached;
                }
                if (_referencesFrozen)
                {
                    throw new InvalidOperationException("Unplanned Java reference '" + name + "' encountered during wrapper emission.");
                }
                var result = ObjectName;
                if (_types.TryGetValue(name, out var type))
                {
                    if (IsExportable(type))
                    {
                        result = name;
                    }
                    else
                    {
                        result = Hierarchy(type, type.IsInterface)
                            .FirstOrDefault(candidate => IsExportable(candidate) && candidate.Name != ObjectName)?.Name ?? ObjectName;
                    }
                }
                else if (name != "java.lang.String" && _warned.Add(name))
                {
                    _warnings.Add("No metadata for public signature type '" + name + "'; using the generated JavaObject wrapper while preserving its JNI descriptor.");
                }
                _referenceNames.Add(name, result);
                return result;
            }

            /// <summary>Allocates names against all available public types so filters do not rename wrappers.</summary>
            /// <exception cref="OperationCanceledException">Cancellation is requested.</exception>
            private void AllocateTypeNames()
            {
                var declarations = _types.Values.Where(IsExportable).OrderBy(type => type.Name, StringComparer.Ordinal).ToArray();
                var occupied = new HashSet<string>(StringComparer.Ordinal);
                var namespaceNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var type in declarations)
                {
                    _ct.ThrowIfCancellationRequested();
                    var namespaceName = JavaNames.Namespace(_options.RootNamespace, JavaNames.Package(type.Name));
                    _namespaces.Add(type.Name, namespaceName);
                    while (namespaceName.Length > _options.RootNamespace.Length)
                    {
                        namespaceNames.Add(namespaceName);
                        namespaceName = namespaceName.Substring(0, namespaceName.LastIndexOf('.'));
                    }
                }
                occupied.UnionWith(namespaceNames);
                occupied.Add(_options.RootNamespace + ".AndroidJni");
                occupied.Add(_options.RootNamespace + ".IJavaObject");
                occupied.Add(_options.RootNamespace + ".JavaObjectReference");
                occupied.Add(_options.RootNamespace + ".JavaInvocationException");
                occupied.Add(_options.RootNamespace + ".JavaWrapperConverter");
                occupied.Add(_options.RootNamespace + ".AndroidJavaObjectExtensions");
                occupied.Add(_options.RootNamespace + ".JavaObjectExtensions");
                // Preserve aliases allocated against the previous runtime namespace when bindings move to their new directory.
                occupied.Add(_bindingsNamespace + ".Runtime.AndroidJni");
                occupied.Add(_bindingsNamespace + ".Runtime.IJavaObject");
                occupied.Add(_bindingsNamespace + ".Runtime.JavaObjectReference");
                occupied.Add(_bindingsNamespace + ".Runtime.JavaInvocationException");
                foreach (var type in declarations.OrderBy(type => type.Name == ObjectName ? 0 : 1))
                {
                    _ct.ThrowIfCancellationRequested();
                    var scope = TypeScope(type.Name);
                    var proposed = type.EnclosingType is null ? JavaNames.TypeName(type.Name)
                        : JavaNames.PascalCase(type.Name.Substring(type.EnclosingType.Length + 1));
                    if (s_reservedNames.Contains(proposed) && type.EnclosingType is not null
                        || type.EnclosingType is not null && proposed == _wrapperNames[type.EnclosingType])
                    {
                        proposed += "JavaType";
                    }
                    var name = AllocateTypeName(scope, proposed, type.Name, occupied);
                    _wrapperNames.Add(type.Name, name);
                }
                foreach (var type in declarations.Where(type => type.IsInterface))
                {
                    _ct.ThrowIfCancellationRequested();
                    _interfaceNames.Add(type.Name, AllocateTypeName(TypeScope(type.Name), "I" + _wrapperNames[type.Name], "interface:" + type.Name, occupied));
                }
                foreach (var group in declarations.Where(type => type.EnclosingType is not null).GroupBy(type => type.EnclosingType!))
                {
                    _nestedNames.Add(group.Key, group.SelectMany(type => type.IsInterface
                        ? new[] { _wrapperNames[type.Name], _interfaceNames[type.Name] } : new[] { _wrapperNames[type.Name] }).ToArray());
                }
            }

            /// <summary>Resolves a namespace-scoped type name with a stable binary-name suffix.</summary>
            /// <param name="namespaceName">The destination namespace.</param>
            /// <param name="proposed">The preferred simple C# name.</param>
            /// <param name="identity">The exact Java type or interface identity.</param>
            /// <param name="occupied">The already occupied qualified type and namespace names.</param>
            /// <returns>A unique simple type name.</returns>
            private static string AllocateTypeName(string namespaceName, string proposed, string identity, HashSet<string> occupied)
            {
                var name = proposed;
                var suffix = "_" + JavaNames.StableSuffix(identity);
                while (!occupied.Add(namespaceName + "." + name))
                {
                    name += suffix;
                }
                return name;
            }

            /// <summary>Creates a type plan after its actual C# base and public interface parents.</summary>
            /// <param name="name">The selected Java binary name.</param>
            /// <returns>The completed wrapper and optional interface plan.</returns>
            /// <exception cref="InvalidOperationException">The hierarchy contains a cycle.</exception>
            /// <exception cref="OperationCanceledException">Cancellation is requested.</exception>
            private TypePlan BuildPlan(string name)
            {
                _ct.ThrowIfCancellationRequested();
                if (_plans.TryGetValue(name, out var existing))
                {
                    return existing;
                }
                if (!_planning.Add(name))
                {
                    throw new InvalidOperationException("Cyclic Java inheritance at '" + name + "'.");
                }
                var type = _types[name];
                var plan = new TypePlan(type, _namespaces[name], _wrapperNames[name],
                    type.IsInterface ? _interfaceNames[name] : null, TypeScope(name));
                if (name != ObjectName && name != ThrowableName)
                {
                    var parent = type.IsInterface ? ObjectName : NearestPublicSuperclass(type);
                    plan.Base = BuildPlan(parent);
                }
                if (type.IsInterface)
                {
                    foreach (var parent in PublicInterfaceParents(type).OrderBy(value => value.Name, StringComparer.Ordinal))
                    {
                        plan.InterfaceParents.Add(BuildPlan(parent.Name));
                    }
                    BuildInterfaceMethods(plan);
                }
                foreach (var implemented in Hierarchy(type, true).Where(value => value.IsInterface && IsExportable(value)))
                {
                    if (implemented.Name != name)
                    {
                        plan.Interfaces.Add(BuildPlan(implemented.Name));
                    }
                }
                BuildClassMethods(plan);
                BuildFields(plan);
                _plans.Add(name, plan);
                _planning.Remove(name);
                return plan;
            }

            /// <summary>Finds the nearest public class while skipping inaccessible intermediary classes.</summary>
            /// <param name="type">The most derived Java class.</param>
            /// <returns>The binary name of the C# superclass wrapper.</returns>
            private string NearestPublicSuperclass(JavaType type)
            {
                return Hierarchy(type, false).Skip(1).FirstOrDefault(IsExportable)?.Name ?? ObjectName;
            }

            /// <summary>Finds public interface parents through inaccessible interface declarations.</summary>
            /// <param name="type">The Java interface whose direct parents are being mapped.</param>
            /// <returns>The nearest available public interface on each inheritance branch.</returns>
            private IEnumerable<JavaType> PublicInterfaceParents(JavaType type)
            {
                var pending = new Queue<string>(type.Interfaces);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                while (pending.Count != 0)
                {
                    var name = pending.Dequeue();
                    if (!seen.Add(name) || !_types.TryGetValue(name, out var parent))
                    {
                        continue;
                    }
                    if (parent.IsInterface && IsExportable(parent))
                    {
                        yield return parent;
                    }
                    else
                    {
                        foreach (var ancestor in parent.Interfaces)
                        {
                            pending.Enqueue(ancestor);
                        }
                    }
                }
            }

            /// <summary>Parses and caches a complete method descriptor.</summary>
            /// <param name="descriptor">The exact JVM descriptor.</param>
            /// <returns>The immutable parsed signature.</returns>
            /// <exception cref="FormatException">The descriptor is malformed.</exception>
            private JavaMethodDescriptor ParseMethod(string descriptor)
            {
                if (!_methodDescriptors.TryGetValue(descriptor, out var parsed))
                {
                    parsed = JavaDescriptors.ParseMethod(descriptor);
                    _methodDescriptors.Add(descriptor, parsed);
                }
                return parsed;
            }

            /// <summary>Parses and caches a complete field descriptor.</summary>
            /// <param name="descriptor">The exact JVM descriptor.</param>
            /// <returns>The immutable parsed field type.</returns>
            /// <exception cref="FormatException">The descriptor is malformed.</exception>
            private JavaTypeDescriptor ParseField(string descriptor)
            {
                if (!_fieldDescriptors.TryGetValue(descriptor, out var parsed))
                {
                    parsed = JavaDescriptors.ParseField(descriptor);
                    _fieldDescriptors.Add(descriptor, parsed);
                }
                return parsed;
            }

            /// <summary>Checks whether a field belongs to the exported Java surface.</summary>
            /// <param name="field">The extracted field.</param>
            /// <returns>Whether the public field is nonsynthetic.</returns>
            private static bool IsVisibleField(JavaField field)
            {
                return field.IsPublic && (field.AccessFlags & 0x1000) == 0;
            }

            /// <summary>Checks public methods while retaining bridges needed for erased interface contracts.</summary>
            /// <param name="method">The extracted method.</param>
            /// <returns>Whether the method can contribute to the public surface.</returns>
            private static bool IsVisibleMethod(JavaMethod method)
            {
                return method.IsPublic && (!method.IsSynthetic || method.IsBridge);
            }

            /// <summary>Plans interface declarations while preserving each covariant parent contract.</summary>
            /// <param name="plan">The interface wrapper plan to populate.</param>
            private void BuildInterfaceMethods(TypePlan plan)
            {
                var inherited = plan.InterfaceParents.SelectMany(parent => parent.InterfaceSurface).ToList();
                plan.InterfaceSurface.AddRange(inherited);
                var pending = new Queue<JavaType>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                pending.Enqueue(plan.Type);
                while (pending.Count != 0)
                {
                    var type = pending.Dequeue();
                    if (!seen.Add(type.Name))
                    {
                        continue;
                    }
                    foreach (var candidate in DeclaredMethods(type).Where(method => !method.Method.IsStatic))
                    {
                        AddMethod(plan, candidate, inherited, plan.InterfaceMethods, plan.InterfaceSurface, true);
                    }
                    foreach (var parentName in type.Interfaces.OrderBy(value => value, StringComparer.Ordinal))
                    {
                        if (_types.TryGetValue(parentName, out var parent) && !IsExportable(parent))
                        {
                            pending.Enqueue(parent);
                        }
                    }
                }
            }

            /// <summary>Plans only methods not already supplied by an equivalent inherited wrapper.</summary>
            /// <param name="plan">The wrapper class plan to populate.</param>
            private void BuildClassMethods(TypePlan plan)
            {
                var inherited = plan.Base?.PublicMethods ?? new List<MethodMember>();
                plan.PublicMethods.AddRange(inherited);
                foreach (var candidate in EffectiveMethods(plan.Type))
                {
                    AddMethod(plan, candidate, inherited, plan.Methods, plan.PublicMethods, false);
                }
            }

            /// <summary>Names one method, deduplicates inherited signatures, and resolves CLR overload collisions.</summary>
            /// <param name="plan">The declaring wrapper or interface plan.</param>
            /// <param name="candidate">The Java method and its true declaring type.</param>
            /// <param name="inherited">The C# base members available without another declaration.</param>
            /// <param name="declared">The methods already declared in this C# type.</param>
            /// <param name="surface">The effective public method surface to update.</param>
            /// <param name="isInterface">Whether an interface declaration is being planned.</param>
            private void AddMethod(TypePlan plan, MethodMember candidate, List<MethodMember> inherited,
                List<MethodMember> declared, List<MethodMember> surface, bool isInterface)
            {
                var sameJavaMethod = inherited.FirstOrDefault(method => method.JavaSignature == candidate.JavaSignature);
                if (sameJavaMethod is not null && sameJavaMethod.ReturnType == candidate.ReturnType
                    && sameJavaMethod.ParameterSignature == candidate.ParameterSignature)
                {
                    return;
                }
                var sameFamily = declared.Concat(inherited).FirstOrDefault(method => method.Method.Name == candidate.Method.Name);
                var proposed = sameJavaMethod?.Name ?? sameFamily?.Name ?? JavaNames.PascalCase(candidate.Method.Name);
                var nonMethodNames = isInterface ? Enumerable.Empty<string>() : plan.Base?.PublicFields.Select(field => field.Name) ?? Enumerable.Empty<string>();
                if (IsReservedMember(plan, proposed) || NestedMemberNames(plan.Type).Contains(proposed) || proposed == plan.Name || proposed == plan.InterfaceSimpleName
                    || nonMethodNames.Contains(proposed, StringComparer.Ordinal))
                {
                    proposed += "JavaMethod";
                }
                while (IsReservedMember(plan, proposed) || NestedMemberNames(plan.Type).Contains(proposed) || proposed == plan.Name || proposed == plan.InterfaceSimpleName
                    || nonMethodNames.Contains(proposed, StringComparer.Ordinal))
                {
                    proposed += "JavaMethod";
                }
                candidate.Name = proposed;
                var conflict = declared.FirstOrDefault(method => method.CSharpSignature == candidate.CSharpSignature);
                var inheritedConflict = inherited.FirstOrDefault(method => method.CSharpSignature == candidate.CSharpSignature);
                if (conflict is not null && conflict.JavaSignature == candidate.JavaSignature && conflict.ReturnType == candidate.ReturnType)
                {
                    return;
                }
                if (conflict is not null || inheritedConflict is not null && inheritedConflict.JavaSignature != candidate.JavaSignature)
                {
                    candidate.Name += "_" + JavaNames.StableSuffix(candidate.Owner.Name + "." + candidate.Method.Name + candidate.Method.Descriptor);
                    while (declared.Concat(inherited).Any(method => method.CSharpSignature == candidate.CSharpSignature))
                    {
                        candidate.Name += "JavaMethod";
                    }
                }
                candidate.HidesInherited = inherited.Any(method => method.CSharpSignature == candidate.CSharpSignature);
                declared.Add(candidate);
                if (!isInterface)
                {
                    surface.RemoveAll(method => method.CSharpSignature == candidate.CSharpSignature);
                }
                surface.Add(candidate);
            }

            /// <summary>Collects declared public methods, dropping only redundant same-parameter bridges.</summary>
            /// <param name="type">The exact declaring Java type.</param>
            /// <returns>Distinct ordinary methods, retaining erased bridges with different parameters.</returns>
            private IEnumerable<MethodMember> DeclaredMethods(JavaType type)
            {
                var groups = type.Methods.Where(method => IsVisibleMethod(method) && method.Name != "<init>" && method.Name != "<clinit>")
                    .GroupBy(method => (method.IsStatic ? "S:" : "I:") + JavaDescriptors.ParameterSignature(method.Name, method.Descriptor))
                    .OrderBy(group => group.Key, StringComparer.Ordinal);
                foreach (var group in groups)
                {
                    var ordinary = group.Where(method => !method.IsBridge).ToArray();
                    var methods = ordinary.Length == 0 ? group : ordinary.AsEnumerable();
                    foreach (var method in methods.OrderBy(value => value.Descriptor, StringComparer.Ordinal)
                        .GroupBy(value => value.Descriptor, StringComparer.Ordinal).Select(value => value.First()))
                    {
                        yield return CreateMethod(type, method);
                    }
                }
            }

            /// <summary>Collects class methods and inherited Java interface default or abstract methods.</summary>
            /// <param name="type">The wrapper's most derived Java declaration.</param>
            /// <returns>The effective Java methods, with class declarations taking precedence.</returns>
            private IEnumerable<MethodMember> EffectiveMethods(JavaType type)
            {
                var classSignatures = new HashSet<string>(StringComparer.Ordinal);
                if (!type.IsInterface)
                {
                    foreach (var owner in Hierarchy(type, false))
                    {
                        var declared = DeclaredMethods(owner).ToArray();
                        foreach (var method in declared)
                        {
                            if (!classSignatures.Contains(method.JavaSignature))
                            {
                                yield return method;
                            }
                        }
                        classSignatures.UnionWith(declared.Select(method => method.JavaSignature));
                    }
                }
                else
                {
                    foreach (var method in DeclaredMethods(type).Where(value => value.Method.IsStatic))
                    {
                        classSignatures.Add(method.JavaSignature);
                        yield return method;
                    }
                }
                var defaults = new Dictionary<string, MethodMember>(StringComparer.Ordinal);
                foreach (var owner in Hierarchy(type, true).Where(value => value.IsInterface))
                {
                    foreach (var method in DeclaredMethods(owner).Where(value => !value.Method.IsStatic))
                    {
                        if (classSignatures.Contains(method.JavaSignature))
                        {
                            continue;
                        }
                        if (!defaults.TryGetValue(method.JavaSignature, out var prior)
                            || IsMoreSpecific(method.Signature.ReturnType, prior.Signature.ReturnType))
                        {
                            defaults[method.JavaSignature] = method;
                        }
                    }
                }
                foreach (var method in defaults.Values.OrderBy(value => value.JavaSignature, StringComparer.Ordinal))
                {
                    yield return method;
                }
            }

            /// <summary>Chooses a narrower erased reference return when interface declarations compete.</summary>
            /// <param name="candidate">The candidate return descriptor.</param>
            /// <param name="prior">The currently selected return descriptor.</param>
            /// <returns>Whether the candidate is a strict known subtype.</returns>
            private bool IsMoreSpecific(JavaTypeDescriptor candidate, JavaTypeDescriptor prior)
            {
                if (candidate.Code == '[' && prior.Code == '[' && candidate.Element is not null && prior.Element is not null)
                {
                    return IsMoreSpecific(candidate.Element, prior.Element);
                }
                if (prior.Code == 'L' && prior.BinaryName == ObjectName)
                {
                    return candidate.Code == '[' || candidate.Code == 'L' && candidate.BinaryName != ObjectName;
                }
                if (candidate.Code != 'L' || prior.Code != 'L' || candidate.BinaryName == prior.BinaryName)
                {
                    return false;
                }
                return _types.TryGetValue(candidate.BinaryName, out var type)
                    && Hierarchy(type, true).Skip(1).Any(parent => parent.Name == prior.BinaryName);
            }

            /// <summary>Creates a method plan with mapped types and its original JNI descriptor.</summary>
            /// <param name="owner">The exact declaring Java type.</param>
            /// <param name="method">The extracted method metadata.</param>
            /// <returns>The named-later method emission record.</returns>
            /// <exception cref="FormatException">The method descriptor is malformed.</exception>
            private MethodMember CreateMethod(JavaType owner, JavaMethod method)
            {
                var signature = ParseMethod(method.Descriptor);
                return new MethodMember(owner, method, signature, TypeName(signature.ReturnType),
                    signature.Parameters.Select(parameter => TypeName(parameter)).ToArray());
            }

            /// <summary>Plans getter-only fields, including fields hidden behind inaccessible ancestors.</summary>
            /// <param name="plan">The wrapper class plan to populate.</param>
            private void BuildFields(TypePlan plan)
            {
                if (plan.Base is not null)
                {
                    plan.PublicFields.AddRange(plan.Base.PublicFields);
                }
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var allocated = new HashSet<string>(s_reservedNames, StringComparer.Ordinal);
                if (IsThrowable(plan.Type))
                {
                    allocated.UnionWith(s_exceptionReservedNames);
                }
                allocated.UnionWith(NestedMemberNames(plan.Type));
                allocated.Add(plan.Name);
                allocated.UnionWith(plan.PublicMethods.Select(method => method.Name));
                foreach (var owner in Hierarchy(plan.Type, true))
                {
                    foreach (var field in owner.Fields.Where(IsVisibleField).OrderBy(value => value.Name, StringComparer.Ordinal))
                    {
                        if (!seen.Add(field.Name))
                        {
                            continue;
                        }
                        var inherited = plan.Base?.PublicFields.FirstOrDefault(value => value.Field.Name == field.Name);
                        if (inherited is not null && inherited.Owner.Name == owner.Name && inherited.Field.Descriptor == field.Descriptor)
                        {
                            continue;
                        }
                        var name = inherited?.Name ?? JavaNames.PascalCase(field.Name);
                        if (allocated.Contains(name))
                        {
                            name += "JavaField";
                        }
                        while (allocated.Contains(name) || plan.Fields.Any(value => value.Name == name))
                        {
                            name += "_" + JavaNames.StableSuffix(owner.Name + "." + field.Name + field.Descriptor);
                        }
                        allocated.Add(name);
                        var member = new FieldMember(owner, field, ParseField(field.Descriptor), name)
                        {
                            HidesInherited = plan.Base?.PublicFields.Any(value => value.Name == name) == true
                        };
                        plan.Fields.Add(member);
                        plan.PublicFields.RemoveAll(value => value.Name == name);
                        plan.PublicFields.Add(member);
                    }
                }
            }

            /// <summary>Maps erased descriptors to nullable typed signatures or raw JNI result types.</summary>
            /// <param name="descriptor">The parsed JVM descriptor.</param>
            /// <param name="raw">Whether reference leaves should remain Unity JNI handles.</param>
            /// <returns>A C# 9 type name with nullability at every reference and array level.</returns>
            private string TypeName(JavaTypeDescriptor descriptor, bool raw = false)
            {
                switch (descriptor.Code)
                {
                    case 'V':
                        return "void";
                    case 'Z':
                        return "bool";
                    case 'B':
                        return "sbyte";
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
                        if (descriptor.BinaryName == "java.lang.String")
                        {
                            return "string?";
                        }
                        if (raw)
                        {
                            return UnityObject + "?";
                        }
                        var reference = ReferenceName(descriptor.BinaryName);
                        var simpleName = _types[reference].IsInterface ? _interfaceNames[reference] : _wrapperNames[reference];
                        return "global::" + TypeScope(reference) + "." + simpleName + "?";
                    case '[':
                        return TypeName(descriptor.Element!, raw) + "[]?";
                    default:
                        throw new FormatException("Unsupported JVM descriptor code '" + descriptor.Code + "'.");
                }
            }

            /// <summary>Wraps object results and recursively maps nullable object-array leaves.</summary>
            /// <param name="descriptor">The declared Java result descriptor.</param>
            /// <param name="expression">The raw JNI expression or array element.</param>
            /// <param name="depth">The array depth used to name collision-free lambda parameters.</param>
            /// <returns>The converted typed result expression.</returns>
            private string WrapResult(JavaTypeDescriptor descriptor, string expression, int depth = 0)
            {
                if (descriptor.Code == 'L' && descriptor.BinaryName != "java.lang.String")
                {
                    var reference = ReferenceName(descriptor.BinaryName);
                    var wrapper = QualifiedWrapper(reference);
                    return ConverterName + ".ToWrapper<" + wrapper + ">(" + expression + ", ownsReference: true)";
                }
                if (descriptor.Code == '[' && ContainsWrapper(descriptor.Element!))
                {
                    var parameter = "__element" + depth.ToString(CultureInfo.InvariantCulture);
                    return _jni + ".MapArray<" + TypeName(descriptor.Element!, true) + ", " + TypeName(descriptor.Element!) + ">(" + expression
                        + ", " + parameter + " => " + WrapResult(descriptor.Element!, parameter, depth + 1) + ")";
                }
                return expression;
            }

            /// <summary>Checks whether an erased result needs typed wrapper conversion.</summary>
            /// <param name="descriptor">The result or array-element descriptor.</param>
            /// <returns>Whether any leaf is a reference other than java.lang.String.</returns>
            private static bool ContainsWrapper(JavaTypeDescriptor descriptor)
            {
                return descriptor.Code == 'L' && descriptor.BinaryName != "java.lang.String"
                    || descriptor.Element is not null && ContainsWrapper(descriptor.Element);
            }

            /// <summary>Emits the public interface, optional Java constructors, and handle wrapper.</summary>
            /// <param name="plan">The completed type and hierarchy plan.</param>
            /// <param name="warnings">The diagnostics owned exclusively by this source file.</param>
            /// <returns>The complete C# source using explicit CRLF line endings.</returns>
            /// <exception cref="OperationCanceledException">Cancellation is requested.</exception>
            private string Emit(TypePlan plan, List<string> warnings)
            {
                _ct.ThrowIfCancellationRequested();
                var source = new SourceWriter();
                source.Line("// <auto-generated />");
                source.Line("#nullable enable");
                source.Line();
                source.Open("namespace " + plan.Namespace);
                EmitOwnerScopes(source, plan, warnings);
                source.Close();
                return source.ToString();
            }

            /// <summary>Emits one erased wrapper declaration inside its current containing scope.</summary>
            /// <param name="source">The indented output writer.</param>
            /// <param name="plan">The completed declaration plan.</param>
            /// <param name="warnings">The output diagnostics.</param>
            private void EmitDeclaration(SourceWriter source, TypePlan plan, List<string> warnings)
            {
                if (plan.Type.IsInterface)
                {
                    EmitInterface(source, plan);
                    source.Line();
                }
                WrapperDocumentation.WriteSummary(source.Line, plan.Type.Documentation,
                    "Wraps the Java " + (plan.Type.IsInterface ? "interface" : "class") + " <c>"
                    + JavaNames.Xml(plan.Type.Name) + "</c> without changing its JNI descriptors.");
                WrapperDocumentation.WriteDetails(source.Line, plan.Type.Documentation);
                var bases = new List<string>
                {
                    plan.Type.Name == ThrowableName ? "global::" + _options.RootNamespace + ".JavaInvocationException"
                        : GenericSuperclass(plan) ?? plan.Base?.QualifiedName ?? "global::" + _options.RootNamespace + ".JavaObjectReference"
                };
                if (plan.InterfaceQualifiedName is not null)
                {
                    bases.Add(plan.InterfaceSimpleName!);
                }
                else
                {
                    bases.AddRange(plan.Interfaces.Select(value => value.InterfaceQualifiedName!).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal));
                }
                AddManagedInterfaces(plan, bases);
                bases.AddRange(ConcreteGenericInterfaces(plan));
                source.Open("public partial class " + plan.Name + " : " + string.Join(", ", bases));
                EmitRegistration(source, plan.Name, plan.Name, plan.Type.Name);
                EmitReferenceConstructor(source, plan);
                EmitJavaConstructors(source, plan, warnings);
                source.Line();
                EmitClassProperty(source, plan);
                foreach (var field in plan.Fields)
                {
                    _ct.ThrowIfCancellationRequested();
                    source.Line();
                    EmitField(source, field);
                }
                foreach (var method in plan.Methods)
                {
                    _ct.ThrowIfCancellationRequested();
                    source.Line();
                    EmitMethod(source, method, null);
                }
                var contracts = plan.Type.IsInterface ? new[] { plan }.Concat(plan.Interfaces) : plan.Interfaces.AsEnumerable();
                foreach (var contract in contracts.OrderBy(value => value.Type.Name, StringComparer.Ordinal))
                {
                    foreach (var method in contract.InterfaceMethods)
                    {
                        _ct.ThrowIfCancellationRequested();
                        if (plan.PublicMethods.Any(candidate => !candidate.Method.IsStatic && candidate.CSharpSignature == method.CSharpSignature
                            && candidate.ReturnType == method.ReturnType))
                        {
                            continue;
                        }
                        source.Line();
                        EmitMethod(source, method, ReferenceEquals(contract, plan) ? plan.InterfaceSimpleName : contract.InterfaceQualifiedName);
                    }
                }
                EmitManagedContracts(source, plan);
                EmitConcreteGenericContracts(source, plan);
                source.Close();
            }

            /// <summary>Emits the Java interface contract and its accessible parent interfaces.</summary>
            /// <param name="source">The indented output writer.</param>
            /// <param name="plan">The interface declaration and its method plans.</param>
            /// <exception cref="OperationCanceledException">Cancellation is requested.</exception>
            private void EmitInterface(SourceWriter source, TypePlan plan)
            {
                WrapperDocumentation.WriteSummary(source.Line, plan.Type.Documentation,
                    "Represents the Java interface <c>" + JavaNames.Xml(plan.Type.Name) + "</c> and its JNI reference.");
                WrapperDocumentation.WriteDetails(source.Line, plan.Type.Documentation);
                var bases = plan.InterfaceParents.Select(parent => parent.InterfaceQualifiedName!)
                    .Concat(new[] { "global::" + _options.RootNamespace + ".IJavaObject" });
                source.Open("public interface " + plan.InterfaceSimpleName + " : " + string.Join(", ", bases));
                for (var index = 0; index < plan.InterfaceMethods.Count; index++)
                {
                    _ct.ThrowIfCancellationRequested();
                    if (index != 0)
                    {
                        source.Line();
                    }
                    var method = plan.InterfaceMethods[index];
                    var parameters = ParameterNames(method.Method, method.Signature);
                    DocumentMethod(source, method, parameters);
                    source.Line((method.HidesInherited ? "new " : string.Empty) + method.ReturnType + " " + method.Name
                        + "(" + ParameterDeclarations(method.ParameterTypes, parameters) + ");");
                }
                source.Close();
            }

            /// <summary>Emits the mandatory constructor for wrapping an existing JNI object.</summary>
            /// <param name="source">The indented output writer.</param>
            /// <param name="plan">The wrapper receiving the constructor.</param>
            private static void EmitReferenceConstructor(SourceWriter source, TypePlan plan)
            {
                source.Line("/// <summary>Wraps an existing Java object reference.</summary>");
                source.Line("/// <param name=\"javaReference\">The non-null Unity JNI reference to wrap.</param>");
                source.Line("/// <param name=\"ownsReference\">Whether disposing this wrapper also disposes the JNI reference.</param>");
                source.Line("/// <exception cref=\"global::System.ArgumentNullException\">The Java reference is null.</exception>");
                source.Line("internal " + plan.Name + "(" + UnityObject + " javaReference, bool ownsReference = true)");
                source.Open("    : base(javaReference, ownsReference)");
                source.Close();
            }

            /// <summary>Plans Java constructors and stable named factories before any files are emitted.</summary>
            /// <param name="plan">The completed concrete wrapper plan and its public member names.</param>
            /// <returns>The descriptor-ordered constructors with resolved signatures and optional factory names.</returns>
            /// <exception cref="FormatException">A constructor descriptor is malformed.</exception>
            /// <exception cref="OperationCanceledException">Cancellation is requested.</exception>
            private IReadOnlyList<ConstructorMember> BuildConstructors(TypePlan plan)
            {
                if (plan.Type.IsAbstract || plan.Type.IsInterface)
                {
                    return Array.Empty<ConstructorMember>();
                }
                var constructors = new List<ConstructorMember>();
                var signatures = new HashSet<string>(StringComparer.Ordinal);
                var occupied = new HashSet<string>(plan.PublicMethods.Select(value => value.Name)
                    .Concat(plan.PublicFields.Select(value => value.Name)), StringComparer.Ordinal)
                {
                    plan.Name
                };
                foreach (var method in plan.Type.Methods.Where(value => value.IsPublic && !value.IsSynthetic && value.Name == "<init>")
                    .OrderBy(value => value.Descriptor, StringComparer.Ordinal).GroupBy(value => value.Descriptor, StringComparer.Ordinal).Select(value => value.First()))
                {
                    _ct.ThrowIfCancellationRequested();
                    var signature = ParseMethod(method.Descriptor);
                    var parameterTypes = signature.Parameters.Select(parameter => TypeName(parameter)).ToArray();
                    var parameters = ParameterNames(method, signature);
                    string? factory = null;
                    if (!signatures.Add(string.Join(",", parameterTypes.Select(JavaNames.SignatureType))))
                    {
                        factory = "CreateJavaInstance_" + JavaNames.StableSuffix(method.Descriptor);
                        while (!occupied.Add(factory))
                        {
                            factory += "JavaConstructor";
                        }
                    }
                    constructors.Add(new ConstructorMember(method, signature, parameterTypes, parameters, factory));
                }
                return constructors.ToArray();
            }

            /// <summary>Emits planned Java constructors or stable factories for mapped C# signature collisions.</summary>
            /// <param name="source">The indented output writer.</param>
            /// <param name="plan">The concrete Java class wrapper and its completed constructor plans.</param>
            /// <param name="warnings">The diagnostics owned exclusively by this source file.</param>
            /// <exception cref="OperationCanceledException">Cancellation is requested.</exception>
            private void EmitJavaConstructors(SourceWriter source, TypePlan plan, List<string> warnings)
            {
                foreach (var constructor in plan.Constructors)
                {
                    _ct.ThrowIfCancellationRequested();
                    var method = constructor.Method;
                    source.Line();
                    WrapperDocumentation.WriteSummary(source.Line, method.Documentation,
                        "Constructs Java <c>" + JavaNames.Xml(plan.Type.Name) + "</c> using descriptor <c>"
                        + JavaNames.Xml(method.Descriptor) + "</c>.");
                    DocumentParameters(source, constructor.ParameterNames, method, constructor.Signature);
                    if (constructor.FactoryName is not null)
                    {
                        source.Line("/// <returns>The newly constructed, independently owned Java wrapper.</returns>");
                    }
                    WrapperDocumentation.WriteDetails(source.Line, method.Documentation);
                    DocumentJniExceptions(source, false);
                    var construction = _jni + ".Construct(" + JavaNames.StringLiteral(plan.Type.Name) + ", "
                        + JavaNames.StringLiteral(method.Descriptor) + ", " + Arguments(constructor.ParameterNames) + ")";
                    if (constructor.FactoryName is not null)
                    {
                        source.Open("public static " + plan.Name + " " + constructor.FactoryName + "("
                            + ParameterDeclarations(constructor.ParameterTypes, constructor.ParameterNames) + ")");
                        source.Line("return new " + plan.Name + "(" + construction + ", ownsReference: true);");
                        source.Close();
                        warnings.Add("Java constructor '" + plan.Type.Name + method.Descriptor
                            + "' shares a mapped C# signature; emitted factory '" + constructor.FactoryName + "' instead of a duplicate constructor.");
                    }
                    else
                    {
                        source.Line("public " + plan.Name + "(" + ParameterDeclarations(constructor.ParameterTypes, constructor.ParameterNames) + ")");
                        source.Open("    : this(" + construction + ", ownsReference: true)");
                        source.Close();
                    }
                }
            }

            /// <summary>Emits a fresh owned Java Class reference without constructing the Java type.</summary>
            /// <param name="source">The indented output writer.</param>
            /// <param name="plan">The Java declaration represented by the class object.</param>
            private void EmitClassProperty(SourceWriter source, TypePlan plan)
            {
                source.Line("/// <summary>Gets a fresh owned Java Class reference for <c>" + JavaNames.Xml(plan.Type.Name) + "</c>.</summary>");
                source.Line("/// <returns>The Java Class reference; dispose it when it is no longer needed.</returns>");
                DocumentJniExceptions(source, false);
                var hides = plan.Base is not null && plan.Base.ClassPropertyName == plan.ClassPropertyName;
                var classWrapper = QualifiedWrapper(ClassName) + (IsThrowable(plan.Type) ? string.Empty : "<" + plan.Name + ">");
                source.Open("public static " + (hides ? "new " : string.Empty) + classWrapper + " " + plan.ClassPropertyName);
                source.Open("get");
                source.Line("return " + ConverterName + ".ToWrapper<" + classWrapper + ">(" + _jni + ".GetClass(" + JavaNames.StringLiteral(plan.Type.Name) + "), ownsReference: true)!;");
                source.Close();
                source.Close();
            }

            /// <summary>Emits one public Java field as a getter-only property.</summary>
            /// <param name="source">The indented output writer.</param>
            /// <param name="field">The field's mapped name and exact declaring Java type.</param>
            private void EmitField(SourceWriter source, FieldMember field)
            {
                WrapperDocumentation.WriteSummary(source.Line, field.Field.Documentation,
                    "Gets Java field <c>" + JavaNames.Xml(field.Owner.Name + "." + field.Field.Name) + "</c>.");
                WrapperDocumentation.WriteReturn(source.Line, field.Field.Documentation, field.Descriptor);
                WrapperDocumentation.WriteDetails(source.Line, field.Field.Documentation);
                DocumentJniExceptions(source, !field.Field.IsStatic);
                var modifiers = "public " + (field.Field.IsStatic ? "static " : string.Empty) + (field.HidesInherited ? "new " : string.Empty);
                source.Open(modifiers + TypeName(field.Descriptor) + " " + field.Name);
                source.Open("get");
                var expression = _jni + ".GetField<" + TypeName(field.Descriptor, true) + ">(" + (field.Field.IsStatic ? "null" : "JavaReference")
                    + ", " + JavaNames.StringLiteral(field.Owner.Name) + ", " + JavaNames.StringLiteral(field.Field.Name)
                    + ", " + JavaNames.StringLiteral(field.Field.Descriptor) + ")";
                source.Line("return " + WrapResult(field.Descriptor, expression) + ";");
                source.Close();
                source.Close();
            }

            /// <summary>Emits an ordinary Java call or an exact explicit interface implementation.</summary>
            /// <param name="source">The indented output writer.</param>
            /// <param name="method">The Java method, descriptor, and mapped CLR signature.</param>
            /// <param name="explicitInterface">The interface to explicitly implement, or null for a public method.</param>
            private void EmitMethod(SourceWriter source, MethodMember method, string? explicitInterface)
            {
                var parameters = ParameterNames(method.Method, method.Signature);
                DocumentMethod(source, method, parameters);
                var modifiers = explicitInterface is not null ? string.Empty : "public " + (method.Method.IsStatic ? "static " : string.Empty)
                    + (method.HidesInherited ? "new " : string.Empty) + (method.Method.IsStatic ? string.Empty : "virtual ");
                var name = explicitInterface is null ? method.Name : explicitInterface + "." + method.Name;
                source.Open(modifiers + method.ReturnType + " " + name + "(" + ParameterDeclarations(method.ParameterTypes, parameters) + ")");
                var isVoid = method.Signature.ReturnType.Code == 'V';
                var invocation = _jni + ".Call" + (isVoid ? string.Empty : "<" + TypeName(method.Signature.ReturnType, true) + ">")
                    + "(" + (method.Method.IsStatic ? "null" : "JavaReference") + ", " + JavaNames.StringLiteral(method.Owner.Name)
                    + ", " + JavaNames.StringLiteral(method.Method.Name) + ", " + JavaNames.StringLiteral(method.Method.Descriptor)
                    + ", " + Arguments(parameters) + ")";
                source.Line(isVoid ? invocation + ";" : "return " + WrapResult(method.Signature.ReturnType, invocation) + ";");
                source.Close();
            }

            /// <summary>Documents a Java method with its exact descriptor and mapped parameters.</summary>
            /// <param name="source">The indented output writer.</param>
            /// <param name="method">The method being documented.</param>
            /// <param name="parameters">The final C# parameter identifiers.</param>
            private void DocumentMethod(SourceWriter source, MethodMember method, IReadOnlyList<string> parameters)
            {
                WrapperDocumentation.WriteSummary(source.Line, method.Method.Documentation,
                    "Calls Java <c>" + JavaNames.Xml(method.Owner.Name + "." + method.Method.Name)
                    + "</c> using descriptor <c>" + JavaNames.Xml(method.Method.Descriptor) + "</c>.");
                DocumentParameters(source, parameters, method.Method, method.Signature);
                if (method.Signature.ReturnType.Code != 'V')
                {
                    WrapperDocumentation.WriteReturn(source.Line, method.Method.Documentation, method.Signature.ReturnType);
                }
                WrapperDocumentation.WriteDetails(source.Line, method.Method.Documentation);
                DocumentJniExceptions(source, !method.Method.IsStatic);
            }

            /// <summary>Documents every argument in JVM descriptor order.</summary>
            /// <param name="source">The indented output writer.</param>
            /// <param name="parameters">The C# parameter identifiers.</param>
            /// <param name="method">The Java declaration containing original parameter descriptions.</param>
            /// <param name="signature">The parsed Java method descriptor.</param>
            private static void DocumentParameters(SourceWriter source, IReadOnlyList<string> parameters, JavaMethod method, JavaMethodDescriptor signature)
            {
                for (var index = 0; index < parameters.Count; index++)
                {
                    WrapperDocumentation.WriteParameter(source.Line, parameters[index], method, signature, index);
                }
            }

            /// <summary>Documents the platform, Java exception, and instance lifecycle boundaries.</summary>
            /// <param name="source">The indented output writer.</param>
            /// <param name="instance">Whether the operation dereferences the wrapper's owned reference.</param>
            private void DocumentJniExceptions(SourceWriter source, bool instance)
            {
                source.Line("/// <exception cref=\"global::System.PlatformNotSupportedException\">The JNI runtime is unavailable on this platform.</exception>");
                source.Line("/// <exception cref=\"global::UnityEngine.AndroidJavaException\">Java reports an exception during the JNI operation.</exception>");
                source.Line("/// <exception cref=\"global::" + _options.RootNamespace + ".JavaInvocationException\">Java raises an exception during the JNI operation.</exception>");
                source.Line("/// <exception cref=\"global::System.InvalidOperationException\">JNI cannot allocate a frame or resolve a required reference.</exception>");
                if (instance)
                {
                    source.Line("/// <exception cref=\"global::System.ObjectDisposedException\">This wrapper has been disposed.</exception>");
                }
            }

            /// <summary>Allocates unique safe parameter names without colliding with conversion lambdas.</summary>
            /// <param name="method">The method's optional Java parameter names.</param>
            /// <param name="signature">The descriptor supplying the authoritative parameter count.</param>
            /// <returns>The final C# identifiers in JVM descriptor order.</returns>
            private static IReadOnlyList<string> ParameterNames(JavaMethod method, JavaMethodDescriptor signature)
            {
                var result = new List<string>();
                var allocated = new HashSet<string>(StringComparer.Ordinal);
                for (var index = 0; index < signature.Parameters.Count; index++)
                {
                    var name = JavaNames.ParameterName(WrapperDocumentation.OriginalParameterName(method, signature.Parameters.Count, index), index);
                    while (!allocated.Add(name.TrimStart('@')))
                    {
                        name += "_" + index.ToString(CultureInfo.InvariantCulture);
                    }
                    result.Add(name);
                }
                return result;
            }

            /// <summary>Joins mapped parameter types and their allocated identifiers.</summary>
            /// <param name="types">The mapped C# signature types.</param>
            /// <param name="names">The unique C# parameter identifiers.</param>
            /// <returns>The C# parameter declaration list.</returns>
            private static string ParameterDeclarations(IReadOnlyList<string> types, IReadOnlyList<string> names)
            {
                return string.Join(", ", types.Select((type, index) => type + " " + names[index]));
            }

            /// <summary>Packs arguments explicitly so a Java array is never expanded as a CLR params array.</summary>
            /// <param name="names">The ordered C# argument identifiers.</param>
            /// <returns>The explicit nullable object array passed to the descriptor runtime.</returns>
            private static string Arguments(IReadOnlyList<string> names)
            {
                return names.Count == 0 ? "global::System.Array.Empty<object?>()" : "new object?[] { " + string.Join(", ", names) + " }";
            }
        }

        /// <summary>Stores a public wrapper's resolved hierarchy, members, and optional interface contract.</summary>
        private sealed class TypePlan
        {
            /// <summary>Gets the exact Java declaration.</summary>
            internal JavaType Type { get; }

            /// <summary>Gets the C# package namespace.</summary>
            internal string Namespace { get; }

            /// <summary>Gets the simple wrapper class name.</summary>
            internal string Name { get; }

            /// <summary>Gets the globally qualified wrapper class name.</summary>
            internal string QualifiedName => "global::" + Scope + "." + Name;

            /// <summary>Gets the namespace and containing wrapper path.</summary>
            internal string Scope { get; }

            /// <summary>Gets the simple interface name, or null for a Java class.</summary>
            internal string? InterfaceSimpleName { get; }

            /// <summary>Gets the globally qualified interface name, or null for a Java class.</summary>
            internal string? InterfaceQualifiedName => InterfaceSimpleName is null ? null : "global::" + Scope + "." + InterfaceSimpleName;

            /// <summary>Gets the static Class property name, including the Class wrapper exception.</summary>
            internal string ClassPropertyName => Name == "Class" ? "ClassJavaProperty" : "Class";

            /// <summary>Gets or sets the actual public C# superclass wrapper.</summary>
            internal TypePlan? Base { get; set; }

            /// <summary>Gets the nearest public parents of a Java interface.</summary>
            internal List<TypePlan> InterfaceParents { get; } = new List<TypePlan>();

            /// <summary>Gets all reachable public Java interface plans.</summary>
            internal List<TypePlan> Interfaces { get; } = new List<TypePlan>();

            /// <summary>Gets methods declared by this C# interface, excluding equivalent inherited contracts.</summary>
            internal List<MethodMember> InterfaceMethods { get; } = new List<MethodMember>();

            /// <summary>Gets the complete inherited C# interface method surface.</summary>
            internal List<MethodMember> InterfaceSurface { get; } = new List<MethodMember>();

            /// <summary>Gets or sets the immutable constructor plans completed before parallel emission.</summary>
            internal IReadOnlyList<ConstructorMember> Constructors { get; set; } = Array.Empty<ConstructorMember>();

            /// <summary>Gets ordinary methods emitted on this wrapper.</summary>
            internal List<MethodMember> Methods { get; } = new List<MethodMember>();

            /// <summary>Gets the effective public wrapper methods, including inherited implementations.</summary>
            internal List<MethodMember> PublicMethods { get; } = new List<MethodMember>();

            /// <summary>Gets getter-only fields emitted on this wrapper.</summary>
            internal List<FieldMember> Fields { get; } = new List<FieldMember>();

            /// <summary>Gets the effective field properties, including inherited properties.</summary>
            internal List<FieldMember> PublicFields { get; } = new List<FieldMember>();

            /// <summary>Creates a resolved public type identity.</summary>
            /// <param name="type">The exact Java declaration.</param>
            /// <param name="namespaceName">The C# package namespace.</param>
            /// <param name="name">The simple wrapper name.</param>
            /// <param name="interfaceName">The simple interface name, if any.</param>
            /// <param name="scope">The namespace and containing wrapper path.</param>
            internal TypePlan(JavaType type, string namespaceName, string name, string? interfaceName, string scope)
            {
                Type = type;
                Namespace = namespaceName;
                Scope = scope;
                Name = name;
                InterfaceSimpleName = interfaceName;
            }
        }

        /// <summary>Stores a Java constructor's resolved signature and stable optional factory name.</summary>
        private sealed class ConstructorMember
        {
            /// <summary>Gets the actual Java constructor declaration and its official documentation.</summary>
            internal JavaMethod Method { get; }

            /// <summary>Gets the exact parsed constructor descriptor.</summary>
            internal JavaMethodDescriptor Signature { get; }

            /// <summary>Gets the mapped C# parameter types in JVM descriptor order.</summary>
            internal IReadOnlyList<string> ParameterTypes { get; }

            /// <summary>Gets the unique final C# parameter identifiers in JVM descriptor order.</summary>
            internal IReadOnlyList<string> ParameterNames { get; }

            /// <summary>Gets the stable factory name, or null when a C# constructor is available.</summary>
            internal string? FactoryName { get; }

            /// <summary>Creates a constructor emission record that is read-only during parallel generation.</summary>
            /// <param name="method">The actual Java constructor declaration.</param>
            /// <param name="signature">The parsed descriptor, including implicit enclosing-instance parameters.</param>
            /// <param name="parameterTypes">The nullable mapped C# parameter types.</param>
            /// <param name="parameterNames">The allocated C# parameter names.</param>
            /// <param name="factoryName">The stable factory name for a collision, or null for a constructor.</param>
            internal ConstructorMember(JavaMethod method, JavaMethodDescriptor signature, IReadOnlyList<string> parameterTypes,
                IReadOnlyList<string> parameterNames, string? factoryName)
            {
                Method = method;
                Signature = signature;
                ParameterTypes = parameterTypes;
                ParameterNames = parameterNames;
                FactoryName = factoryName;
            }
        }

        /// <summary>Stores a method's declaring Java type, descriptor, and collision-free CLR signature.</summary>
        private sealed class MethodMember
        {
            /// <summary>Gets the Java declaration used for JNI lookup.</summary>
            internal JavaType Owner { get; }

            /// <summary>Gets the exact Java method metadata.</summary>
            internal JavaMethod Method { get; }

            /// <summary>Gets the parsed method descriptor.</summary>
            internal JavaMethodDescriptor Signature { get; }

            /// <summary>Gets the mapped C# result type.</summary>
            internal string ReturnType { get; }

            /// <summary>Gets mapped parameter types in JVM order.</summary>
            internal IReadOnlyList<string> ParameterTypes { get; }

            /// <summary>Gets or sets the final generated method name.</summary>
            internal string Name { get; set; } = string.Empty;

            /// <summary>Gets or sets whether an identical inherited CLR signature needs explicit hiding.</summary>
            internal bool HidesInherited { get; set; }

            /// <summary>Gets the Java override identity, keeping static and instance methods separate.</summary>
            internal string JavaSignature => (Method.IsStatic ? "S:" : "I:") + JavaDescriptors.ParameterSignature(Method.Name, Method.Descriptor);

            /// <summary>Gets the CLR parameter identity without nullable annotations.</summary>
            internal string ParameterSignature => "(" + string.Join(",", ParameterTypes.Select(JavaNames.SignatureType)) + ")";

            /// <summary>Gets the CLR overload identity, excluding result type and staticness.</summary>
            internal string CSharpSignature => Name + ParameterSignature;

            /// <summary>Creates a method record whose final name is assigned during planning.</summary>
            /// <param name="owner">The exact Java declaring type.</param>
            /// <param name="method">The extracted JVM method metadata.</param>
            /// <param name="signature">The parsed method descriptor.</param>
            /// <param name="returnType">The nullable mapped C# result type.</param>
            /// <param name="parameterTypes">The mapped C# parameters in JVM order.</param>
            internal MethodMember(JavaType owner, JavaMethod method, JavaMethodDescriptor signature, string returnType, IReadOnlyList<string> parameterTypes)
            {
                Owner = owner;
                Method = method;
                Signature = signature;
                ReturnType = returnType;
                ParameterTypes = parameterTypes;
            }
        }

        /// <summary>Stores a Java field and the getter-only property used to expose it.</summary>
        private sealed class FieldMember
        {
            /// <summary>Gets the exact Java declaring type used for JNI lookup.</summary>
            internal JavaType Owner { get; }

            /// <summary>Gets the extracted field metadata and exact descriptor.</summary>
            internal JavaField Field { get; }

            /// <summary>Gets the parsed field descriptor.</summary>
            internal JavaTypeDescriptor Descriptor { get; }

            /// <summary>Gets the final C# property name.</summary>
            internal string Name { get; }

            /// <summary>Gets or sets whether this getter hides an inherited field property.</summary>
            internal bool HidesInherited { get; set; }

            /// <summary>Creates a getter-only field emission record.</summary>
            /// <param name="owner">The exact declaring Java type.</param>
            /// <param name="field">The field metadata and exact JNI descriptor.</param>
            /// <param name="descriptor">The parsed field descriptor.</param>
            /// <param name="name">The collision-free property name.</param>
            internal FieldMember(JavaType owner, JavaField field, JavaTypeDescriptor descriptor, string name)
            {
                Owner = owner;
                Field = field;
                Descriptor = descriptor;
                Name = name;
            }
        }

        /// <summary>Writes block-style C# source with four spaces and explicit CRLF line endings.</summary>
        private sealed class SourceWriter
        {
            /// <summary>Accumulates generated source text.</summary>
            private readonly StringBuilder _text = new StringBuilder();

            /// <summary>Tracks the current brace indentation depth.</summary>
            private int _indent;

            /// <summary>Writes one source line at the current indentation.</summary>
            /// <param name="text">The source or documentation line; empty emits a blank line.</param>
            internal void Line(string text = "")
            {
                if (text.Length != 0)
                {
                    _text.Append(' ', _indent * 4);
                    _text.Append(text);
                }
                _text.Append("\r\n");
            }

            /// <summary>Writes a declaration and opens its brace block.</summary>
            /// <param name="declaration">The declaration preceding the opening brace.</param>
            internal void Open(string declaration)
            {
                Line(declaration);
                Line("{");
                _indent++;
            }

            /// <summary>Closes the current brace block.</summary>
            internal void Close()
            {
                _indent--;
                Line("}");
            }

            /// <summary>Returns the accumulated C# source.</summary>
            /// <returns>The generated source, including its final CRLF newline.</returns>
            public override string ToString()
            {
                return _text.ToString();
            }
        }
    }
}
