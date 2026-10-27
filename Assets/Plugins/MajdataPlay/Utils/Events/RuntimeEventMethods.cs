using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Utils.Events
{
    /// <summary>
    /// Finds the methods that a <see cref="RuntimeEvent{TDelegate}"/> binding can call and creates delegates for them.
    /// The Inspector and the runtime share these rules, so a binding that the Inspector offers resolves in a Player and a
    /// binding that the Inspector reports as missing does not.
    /// </summary>
    /// <remarks>
    /// A method is bindable when it is a non-abstract, non-generic instance method marked with
    /// <see cref="BindableMethodAttribute"/> whose return type and parameters, including <c>in</c>, <c>ref</c> and
    /// <c>out</c> modifiers, match the delegate's <c>Invoke</c> method exactly. Public and non-public methods declared by
    /// base classes are found too. A method with the same name and signature in a more derived class hides the base
    /// declaration, so an unmarked override makes the method unbindable.
    /// </remarks>
    public static class RuntimeEventMethods
    {
        /// <summary>The binding flags that list the instance methods declared by one type.</summary>
        private const BindingFlags DeclaredInstanceMethods =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>The C# keywords used for built-in types in formatted signatures.</summary>
        private static readonly Dictionary<Type, string> s_typeKeywords = new()
        {
            [typeof(void)] = "void",
            [typeof(bool)] = "bool",
            [typeof(byte)] = "byte",
            [typeof(sbyte)] = "sbyte",
            [typeof(char)] = "char",
            [typeof(short)] = "short",
            [typeof(ushort)] = "ushort",
            [typeof(int)] = "int",
            [typeof(uint)] = "uint",
            [typeof(long)] = "long",
            [typeof(ulong)] = "ulong",
            [typeof(float)] = "float",
            [typeof(double)] = "double",
            [typeof(decimal)] = "decimal",
            [typeof(string)] = "string",
            [typeof(object)] = "object",
        };

        /// <summary>The resolved methods, including failed lookups, keyed by target type, delegate type and method name.</summary>
        private static readonly Dictionary<(Type TargetType, Type DelegateType, string MethodName), MethodInfo?> s_methodCache = new();

        /// <summary>Guards <see cref="s_methodCache"/>.</summary>
        private static readonly object s_methodCacheLock = new();

        /// <summary>Checks whether a type is a concrete delegate type that bindings can be resolved for.</summary>
        /// <param name="delegateType">The type to check.</param>
        /// <returns>
        /// True for closed delegate types; false for <see cref="Delegate"/>, <see cref="MulticastDelegate"/>, open generic
        /// delegate types and non-delegate types.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="delegateType"/> is null.</exception>
        public static bool IsSupportedDelegateType(Type delegateType)
        {
            if (delegateType is null)
            {
                throw new ArgumentNullException(nameof(delegateType));
            }
            return GetInvokeMethod(delegateType) is not null;
        }

        /// <summary>Finds the bindable method that a binding with the given method name resolves to.</summary>
        /// <param name="targetType">The runtime type of the bound object.</param>
        /// <param name="methodName">The serialized method name.</param>
        /// <param name="delegateType">The delegate type that the method must match.</param>
        /// <returns>
        /// The bindable method, or null when the name is empty, the delegate type is not supported, or no bindable method
        /// with that name and a matching signature exists.
        /// </returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static MethodInfo? FindBindableMethod(Type targetType, string methodName, Type delegateType)
        {
            if (targetType is null)
            {
                throw new ArgumentNullException(nameof(targetType));
            }
            if (methodName is null)
            {
                throw new ArgumentNullException(nameof(methodName));
            }
            if (delegateType is null)
            {
                throw new ArgumentNullException(nameof(delegateType));
            }
            if (methodName.Length == 0)
            {
                return null;
            }

            var key = (targetType, delegateType, methodName);
            lock (s_methodCacheLock)
            {
                if (s_methodCache.TryGetValue(key, out var cachedMethod))
                {
                    return cachedMethod;
                }
            }

            var invokeMethod = GetInvokeMethod(delegateType);
            var method = invokeMethod is null ? null : SearchBindableMethod(targetType, methodName, invokeMethod);
            lock (s_methodCacheLock)
            {
                s_methodCache[key] = method;
            }
            return method;
        }

        /// <summary>Lists the bindable methods of a type for a delegate type, as the Inspector offers them.</summary>
        /// <param name="targetType">The runtime type of the object to bind.</param>
        /// <param name="delegateType">The delegate type that the methods must match.</param>
        /// <returns>
        /// The bindable methods ordered by name, one per name; empty when the delegate type is not supported.
        /// </returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static IReadOnlyList<MethodInfo> GetBindableMethods(Type targetType, Type delegateType)
        {
            if (targetType is null)
            {
                throw new ArgumentNullException(nameof(targetType));
            }
            if (delegateType is null)
            {
                throw new ArgumentNullException(nameof(delegateType));
            }

            var methods = new List<MethodInfo>();
            var invokeMethod = GetInvokeMethod(delegateType);
            if (invokeMethod is null)
            {
                return methods;
            }

            var visitedNames = new HashSet<string>(StringComparer.Ordinal);
            for (var type = targetType; type is not null; type = type.BaseType)
            {
                foreach (var method in type.GetMethods(DeclaredInstanceMethods))
                {
                    if (!HasMatchingSignature(method, invokeMethod) || !visitedNames.Add(method.Name))
                    {
                        continue;
                    }
                    if (IsBindableDeclaration(method))
                    {
                        methods.Add(method);
                    }
                }
            }
            methods.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
            return methods;
        }

        /// <summary>Formats the parameter list and return type of a delegate type for display and log messages.</summary>
        /// <param name="delegateType">The delegate type to format.</param>
        /// <returns>
        /// The signature in C# notation, such as <c>(int, in Payload)</c>, followed by <c> -&gt; T</c> when the delegate
        /// returns a value of type <c>T</c>.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="delegateType"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="delegateType"/> is not a supported delegate type.</exception>
        public static string FormatSignature(Type delegateType)
        {
            if (delegateType is null)
            {
                throw new ArgumentNullException(nameof(delegateType));
            }
            var invokeMethod = GetInvokeMethod(delegateType);
            if (invokeMethod is null)
            {
                throw new ArgumentException($"{delegateType} is not a concrete delegate type.", nameof(delegateType));
            }

            var builder = new StringBuilder();
            builder.Append('(');
            var parameters = invokeMethod.GetParameters();
            for (var i = 0; i < parameters.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }
                var parameter = parameters[i];
                if (parameter.ParameterType.IsByRef)
                {
                    builder.Append(GetByRefModifier(parameter));
                    builder.Append(' ');
                }
                AppendTypeName(builder, parameter.ParameterType);
            }
            builder.Append(')');
            if (invokeMethod.ReturnType != typeof(void))
            {
                builder.Append(" -> ");
                if (invokeMethod.ReturnType.IsByRef)
                {
                    builder.Append("ref ");
                }
                AppendTypeName(builder, invokeMethod.ReturnType);
            }
            return builder.ToString();
        }

        /// <summary>Creates a delegate that calls the bindable method a binding resolves to on its target.</summary>
        /// <param name="target">The bound object.</param>
        /// <param name="methodName">The serialized method name.</param>
        /// <param name="delegateType">The delegate type to create.</param>
        /// <param name="handler">The created delegate, or null when no bindable method was found.</param>
        /// <returns>True when the delegate was created.</returns>
        internal static bool TryCreateDelegate(
            Object target,
            string methodName,
            Type delegateType,
            [NotNullWhen(true)] out Delegate? handler)
        {
            var method = FindBindableMethod(target.GetType(), methodName, delegateType);
            handler = method is null ? null : Delegate.CreateDelegate(delegateType, target, method, false);
            return handler is not null;
        }

        /// <summary>Gets the <c>Invoke</c> method of a concrete delegate type.</summary>
        /// <param name="delegateType">The delegate type.</param>
        /// <returns>The <c>Invoke</c> method, or null when the type is not a concrete delegate type.</returns>
        private static MethodInfo? GetInvokeMethod(Type delegateType)
        {
            if (!delegateType.IsSubclassOf(typeof(MulticastDelegate)) || delegateType.ContainsGenericParameters)
            {
                return null;
            }
            return delegateType.GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public);
        }

        /// <summary>
        /// Walks a type and its base types for the most derived method with the given name and the delegate's signature.
        /// </summary>
        /// <param name="targetType">The runtime type of the bound object.</param>
        /// <param name="methodName">The method name.</param>
        /// <param name="invokeMethod">The delegate's <c>Invoke</c> method.</param>
        /// <returns>The method when its most derived declaration is bindable; otherwise null.</returns>
        private static MethodInfo? SearchBindableMethod(Type targetType, string methodName, MethodInfo invokeMethod)
        {
            for (var type = targetType; type is not null; type = type.BaseType)
            {
                foreach (var method in type.GetMethods(DeclaredInstanceMethods))
                {
                    if (method.Name != methodName || !HasMatchingSignature(method, invokeMethod))
                    {
                        continue;
                    }
                    return IsBindableDeclaration(method) ? method : null;
                }
            }
            return null;
        }

        /// <summary>Checks whether a method declaration with a matching signature may be bound.</summary>
        /// <param name="method">The method declaration.</param>
        /// <returns>True when the method is marked, has a body and is not a generic method definition.</returns>
        private static bool IsBindableDeclaration(MethodInfo method)
        {
            return !method.IsAbstract
                && !method.IsGenericMethodDefinition
                && method.IsDefined(typeof(BindableMethodAttribute), false);
        }

        /// <summary>Checks whether a method has exactly the return type and parameters of a delegate's <c>Invoke</c> method.</summary>
        /// <param name="method">The candidate method.</param>
        /// <param name="invokeMethod">The delegate's <c>Invoke</c> method.</param>
        /// <returns>True when the return type, parameter types and by-reference modifiers are identical.</returns>
        private static bool HasMatchingSignature(MethodInfo method, MethodInfo invokeMethod)
        {
            if (method.ReturnType != invokeMethod.ReturnType)
            {
                return false;
            }

            var parameters = method.GetParameters();
            var expectedParameters = invokeMethod.GetParameters();
            if (parameters.Length != expectedParameters.Length)
            {
                return false;
            }
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                var expectedParameter = expectedParameters[i];
                if (parameter.ParameterType != expectedParameter.ParameterType)
                {
                    return false;
                }
                if (parameter.ParameterType.IsByRef && GetByRefModifier(parameter) != GetByRefModifier(expectedParameter))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Gets the C# modifier of a by-reference parameter.</summary>
        /// <param name="parameter">A by-reference parameter.</param>
        /// <returns><c>out</c>, <c>in</c> or <c>ref</c>.</returns>
        private static string GetByRefModifier(ParameterInfo parameter)
        {
            if (parameter.IsOut)
            {
                return "out";
            }
            return parameter.IsIn ? "in" : "ref";
        }

        /// <summary>Appends the C# name of a type, using keywords for built-in types and angle brackets for generics.</summary>
        /// <param name="builder">The builder to append to.</param>
        /// <param name="type">The type to name; by-reference types are named by their element type.</param>
        private static void AppendTypeName(StringBuilder builder, Type type)
        {
            if (type.IsByRef)
            {
                AppendTypeName(builder, type.GetElementType()!);
                return;
            }
            if (type.IsArray)
            {
                AppendTypeName(builder, type.GetElementType()!);
                builder.Append('[');
                builder.Append(',', type.GetArrayRank() - 1);
                builder.Append(']');
                return;
            }
            if (s_typeKeywords.TryGetValue(type, out var keyword))
            {
                builder.Append(keyword);
                return;
            }
            var underlyingType = Nullable.GetUnderlyingType(type);
            if (underlyingType is not null)
            {
                AppendTypeName(builder, underlyingType);
                builder.Append('?');
                return;
            }
            if (!type.IsGenericType)
            {
                builder.Append(type.Name);
                return;
            }

            var name = type.Name;
            var arityIndex = name.IndexOf('`');
            builder.Append(arityIndex < 0 ? name : name.Substring(0, arityIndex));
            builder.Append('<');
            var arguments = type.GetGenericArguments();
            for (var i = 0; i < arguments.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }
                AppendTypeName(builder, arguments[i]);
            }
            builder.Append('>');
        }
    }
}
