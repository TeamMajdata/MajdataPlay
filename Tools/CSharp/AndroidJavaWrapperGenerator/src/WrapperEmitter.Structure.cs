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
            /// <summary>Gets the generated checked conversion helper.</summary>
            private string ConverterName => "global::" + _options.RootNamespace + ".JavaWrapperConverter";

            /// <summary>Supplies a minimal runtime dependency when SDK metadata is absent.</summary>
            /// <param name="name">The Java binary name.</param>
            /// <param name="superName">The Java superclass.</param>
            /// <param name="signature">The optional generic signature.</param>
            private void EnsureRuntimeType(string name, string superName, string? signature)
            {
                if (!_types.ContainsKey(name))
                {
                    _types.Add(name, new JavaType { Name = name, SuperName = superName, Signature = signature, IsPublic = true, AccessFlags = 1 });
                }
            }

            /// <summary>Gets the containing namespace or wrapper path.</summary>
            /// <param name="name">The Java binary name.</param>
            /// <returns>The C# scope without global qualification.</returns>
            private string TypeScope(string name)
            {
                var type = _types[name];
                return type.EnclosingType is not null && _wrapperNames.ContainsKey(type.EnclosingType)
                    ? TypeScope(type.EnclosingType) + "." + _wrapperNames[type.EnclosingType] : _namespaces[name];
            }

            /// <summary>Gets a globally qualified erased wrapper name.</summary>
            /// <param name="name">The selected Java binary name.</param>
            /// <returns>The C# reference wrapper type.</returns>
            private string QualifiedWrapper(string name)
            {
                return "global::" + TypeScope(name) + "." + _wrapperNames[name];
            }

            /// <summary>Gets a stable filename retaining the containing type structure.</summary>
            /// <param name="name">The selected Java binary name.</param>
            /// <returns>The namespace-relative wrapper path.</returns>
            private string WrapperPath(string name)
            {
                return TypeScope(name).Substring(_namespaces[name].Length).TrimStart('.')
                    + (TypeScope(name) == _namespaces[name] ? string.Empty : ".") + _wrapperNames[name];
            }

            /// <summary>Finds nested names that cannot also name Java methods or fields.</summary>
            /// <param name="type">The containing Java type.</param>
            /// <returns>The allocated immediate nested wrapper and interface names.</returns>
            private IEnumerable<string> NestedMemberNames(JavaType type)
            {
                return _nestedNames.TryGetValue(type.Name, out var names) ? names : Array.Empty<string>();
            }

            /// <summary>Checks runtime member collisions within the appropriate CLR inheritance hierarchy.</summary>
            /// <param name="plan">The wrapper declaring the Java member.</param>
            /// <param name="name">The proposed CLR member name.</param>
            /// <returns>Whether a runtime base member requires a Java alias.</returns>
            private bool IsReservedMember(TypePlan plan, string name)
            {
                return s_reservedNames.Contains(name) || IsThrowable(plan.Type) && s_exceptionReservedNames.Contains(name);
            }

            /// <summary>Checks the Java throwable hierarchy independently of CLR inheritance.</summary>
            /// <param name="type">The Java type.</param>
            /// <returns>Whether the type derives from java.lang.Throwable.</returns>
            private bool IsThrowable(JavaType type)
            {
                return Hierarchy(type, false).Any(value => value.Name == ThrowableName);
            }

            /// <summary>Emits containing partial classes and both erased and generic declarations.</summary>
            /// <param name="source">The output writer.</param>
            /// <param name="plan">The declaration being emitted.</param>
            /// <param name="warnings">The generation diagnostics.</param>
            private void EmitOwnerScopes(SourceWriter source, TypePlan plan, List<string> warnings)
            {
                var owners = new List<TypePlan>();
                var ownerName = plan.Type.EnclosingType;
                while (ownerName is not null && _plans.TryGetValue(ownerName, out var owner))
                {
                    owners.Insert(0, owner);
                    ownerName = owner.Type.EnclosingType;
                }
                EmitOwnerScope(source, plan, owners, 0, new Dictionary<string, string>(StringComparer.Ordinal), warnings);
            }

            /// <summary>Expands erased and parameterized containing wrapper scopes.</summary>
            /// <param name="source">The output writer.</param>
            /// <param name="plan">The nested declaration.</param>
            /// <param name="owners">The outermost-first containing declarations.</param>
            /// <param name="index">The next containing declaration.</param>
            /// <param name="variables">The type variables available in this scope.</param>
            /// <param name="warnings">The output diagnostics.</param>
            private void EmitOwnerScope(SourceWriter source, TypePlan plan, IReadOnlyList<TypePlan> owners, int index,
                Dictionary<string, string> variables, List<string> warnings)
            {
                if (index == owners.Count)
                {
                    if (variables.Count == 0 || GenericParameters(plan.Type).Count != 0)
                    {
                        EmitDeclaration(source, plan, warnings);
                    }
                    EmitGenericDeclaration(source, plan, variables);
                    return;
                }
                var owner = owners[index];
                source.Line("/// <summary>Contains the nested Java declarations.</summary>");
                source.Open("public partial class " + owner.Name);
                EmitOwnerScope(source, plan, owners, index + 1, variables, warnings);
                source.Close();
                var parameters = GenericParameters(owner.Type);
                if (parameters.Count != 0)
                {
                    var available = AddVariables(variables, parameters);
                    source.Line("/// <summary>Contains the parameterized nested Java declarations.</summary>");
                    source.Open("public partial class " + owner.Name + TypeParameterList(parameters, available)
                        + GenericConstraints(parameters, available));
                    EmitOwnerScope(source, plan, owners, index + 1, available, warnings);
                    source.Close();
                }
            }

            /// <summary>Registers a wrapper with an explicit, AOT-visible constructor factory.</summary>
            /// <param name="source">The output writer.</param>
            /// <param name="name">The simple constructor name.</param>
            /// <param name="qualifiedName">The closed wrapper type.</param>
            /// <param name="javaName">The Java binary class name.</param>
            private void EmitRegistration(SourceWriter source, string name, string qualifiedName, string javaName)
            {
                source.Line("/// <summary>Registers the checked Java reference factory.</summary>");
                source.Line("[global::UnityEngine.Scripting.Preserve]");
                source.Open("static " + name + "()");
                source.Line(ConverterName + ".Register<" + qualifiedName + ">(" + JavaNames.StringLiteral(javaName)
                    + ", (__reference, __owns) => new " + qualifiedName + "(__reference, __owns));");
                source.Close();
                source.Line();
            }

            /// <summary>Emits checked casts between existing JavaObject wrappers.</summary>
            /// <returns>The standalone JavaObject extension source.</returns>
            private string EmitObjectExtensions()
            {
                var source = new SourceWriter();
                source.Line("// <auto-generated />");
                source.Line("#nullable enable");
                source.Open("namespace " + _options.RootNamespace);
                source.Line("/// <summary>Provides checked conversions between Java object wrappers.</summary>");
                source.Open("public static class JavaObjectExtensions");
                source.Line("/// <summary>Creates a typed wrapper after checking the Java object's runtime type.</summary>");
                source.Line("/// <typeparam name=\"T\">The generated JavaObject-derived wrapper to create.</typeparam>");
                source.Line("/// <param name=\"javaObject\">The live source Java object wrapper.</param>");
                source.Line("/// <returns>An independently owned wrapper; dispose it after use.</returns>");
                source.Line("/// <exception cref=\"global::System.ArgumentNullException\">The source wrapper is null.</exception>");
                source.Line("/// <exception cref=\"global::System.InvalidCastException\">The Java object is not assignable to the requested Java type.</exception>");
                DocumentJniExceptions(source, true);
                source.Open("public static T As<T>(this " + QualifiedWrapper(ObjectName) + " javaObject) where T : " + QualifiedWrapper(ObjectName));
                source.Open("if (javaObject is null)");
                source.Line("throw new global::System.ArgumentNullException(nameof(javaObject));");
                source.Close();
                source.Line(_jni + ".EnsureAndroid();");
                source.Line("var __handle = javaObject.JavaReference.GetRawObject();");
                source.Open("if (__handle == global::System.IntPtr.Zero)");
                source.Line("throw new global::System.ObjectDisposedException(nameof(javaObject));");
                source.Close();
                source.Line("return " + ConverterName + ".FromHandle<T>(__handle)!;");
                source.Close();
                source.Close();
                source.Close();
                return source.ToString();
            }

            /// <summary>Emits direct references to all exported exception factories for IL2CPP.</summary>
            /// <returns>The standalone partial runtime source.</returns>
            private string EmitExceptionRegistry()
            {
                var source = new SourceWriter();
                source.Line("// <auto-generated />");
                source.Line("#nullable enable");
                source.Open("namespace " + _options.RootNamespace);
                source.Line("/// <summary>Registers the exported Java exception hierarchy.</summary>");
                source.Open("public static partial class JavaWrapperConverter");
                source.Line("/// <summary>Registers Java throwable factories without reflective construction.</summary>");
                source.Open("static partial void RegisterGeneratedExceptions()");
                foreach (var plan in _plans.Values.Where(value => IsThrowable(value.Type)).OrderBy(value => value.Type.Name, StringComparer.Ordinal))
                {
                    source.Line("Register<" + plan.QualifiedName + ">(" + JavaNames.StringLiteral(plan.Type.Name)
                        + ", (__reference, __owns) => new " + plan.QualifiedName + "(__reference, __owns));");
                }
                source.Close();
                source.Close();
                source.Close();
                return source.ToString();
            }
        }
    }
}
