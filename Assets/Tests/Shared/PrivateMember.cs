using System;
using System.Linq;
using System.Reflection;

#nullable enable
namespace MajdataPlay.Tests
{
    /// <summary>
    /// Reads and writes non-public members through reflection so tests can arrange and observe state that production
    /// code keeps private, such as serialized fields of components.
    /// </summary>
    public static class PrivateMember
    {
        /// <summary>The binding flags used to find instance members declared on a type.</summary>
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        /// <summary>The binding flags used to find static members declared on a type.</summary>
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>Gets the value of an instance field declared on the target type or one of its base types.</summary>
        /// <typeparam name="T">The expected field type.</typeparam>
        /// <param name="target">The object that owns the field.</param>
        /// <param name="name">The field name.</param>
        /// <returns>The current field value.</returns>
        /// <exception cref="MissingFieldException">No field with that name exists.</exception>
        public static T GetField<T>(object target, string name)
        {
            return (T)FindField(target.GetType(), name, InstanceFlags).GetValue(target)!;
        }

        /// <summary>Sets an instance field declared on the target type or one of its base types.</summary>
        /// <param name="target">The object that owns the field.</param>
        /// <param name="name">The field name.</param>
        /// <param name="value">The value to store.</param>
        /// <exception cref="MissingFieldException">No field with that name exists.</exception>
        public static void SetField(object target, string name, object? value)
        {
            FindField(target.GetType(), name, InstanceFlags).SetValue(target, value);
        }

        /// <summary>Gets the value of a static field declared on a type or one of its base types.</summary>
        /// <typeparam name="T">The expected field type.</typeparam>
        /// <param name="type">The type that declares the field.</param>
        /// <param name="name">The field name.</param>
        /// <returns>The current field value.</returns>
        /// <exception cref="MissingFieldException">No field with that name exists.</exception>
        public static T GetStaticField<T>(Type type, string name)
        {
            return (T)FindField(type, name, StaticFlags).GetValue(null)!;
        }

        /// <summary>Sets a static field declared on a type or one of its base types.</summary>
        /// <param name="type">The type that declares the field.</param>
        /// <param name="name">The field name.</param>
        /// <param name="value">The value to store.</param>
        /// <exception cref="MissingFieldException">No field with that name exists.</exception>
        public static void SetStaticField(Type type, string name, object? value)
        {
            FindField(type, name, StaticFlags).SetValue(null, value);
        }

        /// <summary>Gets an instance property value, including properties with non-public getters.</summary>
        /// <typeparam name="T">The expected property type.</typeparam>
        /// <param name="target">The object that owns the property.</param>
        /// <param name="name">The property name.</param>
        /// <returns>The current property value.</returns>
        /// <exception cref="MissingMemberException">No readable property with that name exists.</exception>
        public static T GetProperty<T>(object target, string name)
        {
            var property = FindProperty(target.GetType(), name, InstanceFlags);
            var getter = property.GetGetMethod(true) ?? throw new MissingMemberException(target.GetType().FullName, name);
            return (T)getter.Invoke(target, null)!;
        }

        /// <summary>
        /// Sets an instance property, using a non-public setter when present and the compiler generated backing field
        /// of a get-only auto property otherwise.
        /// </summary>
        /// <param name="target">The object that owns the property.</param>
        /// <param name="name">The property name.</param>
        /// <param name="value">The value to store.</param>
        /// <exception cref="MissingMemberException">The property has neither a setter nor a backing field.</exception>
        public static void SetProperty(object target, string name, object? value)
        {
            var property = FindProperty(target.GetType(), name, InstanceFlags);
            var setter = property.GetSetMethod(true);
            if (setter is not null)
            {
                setter.Invoke(target, new[] { value });
                return;
            }
            FindField(property.DeclaringType!, BackingFieldName(name), InstanceFlags).SetValue(target, value);
        }

        /// <summary>Gets a static property value, including properties with non-public getters.</summary>
        /// <typeparam name="T">The expected property type.</typeparam>
        /// <param name="type">The type that declares the property.</param>
        /// <param name="name">The property name.</param>
        /// <returns>The current property value.</returns>
        /// <exception cref="MissingMemberException">No readable property with that name exists.</exception>
        public static T GetStaticProperty<T>(Type type, string name)
        {
            var property = FindProperty(type, name, StaticFlags);
            var getter = property.GetGetMethod(true) ?? throw new MissingMemberException(type.FullName, name);
            return (T)getter.Invoke(null, null)!;
        }

        /// <summary>
        /// Sets a static property, using a non-public setter when present and the compiler generated backing field
        /// of a get-only auto property otherwise.
        /// </summary>
        /// <param name="type">The type that declares the property.</param>
        /// <param name="name">The property name.</param>
        /// <param name="value">The value to store.</param>
        /// <exception cref="MissingMemberException">The property has neither a setter nor a backing field.</exception>
        public static void SetStaticProperty(Type type, string name, object? value)
        {
            var property = FindProperty(type, name, StaticFlags);
            var setter = property.GetSetMethod(true);
            if (setter is not null)
            {
                setter.Invoke(null, new[] { value });
                return;
            }
            FindField(property.DeclaringType!, BackingFieldName(name), StaticFlags).SetValue(null, value);
        }

        /// <summary>Invokes an instance method, including non-public and inherited methods.</summary>
        /// <param name="target">The object that owns the method.</param>
        /// <param name="name">The method name.</param>
        /// <param name="arguments">The arguments; overloads are chosen by count and argument compatibility.</param>
        /// <returns>The method's return value, or null for void methods.</returns>
        /// <exception cref="MissingMethodException">No compatible method exists.</exception>
        /// <exception cref="TargetInvocationException">The invoked method threw; the original exception is the inner exception.</exception>
        public static object? Invoke(object target, string name, params object?[] arguments)
        {
            return FindMethod(target.GetType(), name, InstanceFlags, arguments).Invoke(target, arguments);
        }

        /// <summary>Invokes an instance method and casts its return value.</summary>
        /// <typeparam name="T">The expected return type.</typeparam>
        /// <param name="target">The object that owns the method.</param>
        /// <param name="name">The method name.</param>
        /// <param name="arguments">The arguments; overloads are chosen by count and argument compatibility.</param>
        /// <returns>The method's return value.</returns>
        /// <exception cref="MissingMethodException">No compatible method exists.</exception>
        /// <exception cref="TargetInvocationException">The invoked method threw; the original exception is the inner exception.</exception>
        public static T Invoke<T>(object target, string name, params object?[] arguments)
        {
            return (T)Invoke(target, name, arguments)!;
        }

        /// <summary>Invokes a static method, including non-public methods.</summary>
        /// <param name="type">The type that declares the method.</param>
        /// <param name="name">The method name.</param>
        /// <param name="arguments">The arguments; overloads are chosen by count and argument compatibility.</param>
        /// <returns>The method's return value, or null for void methods.</returns>
        /// <exception cref="MissingMethodException">No compatible method exists.</exception>
        /// <exception cref="TargetInvocationException">The invoked method threw; the original exception is the inner exception.</exception>
        public static object? InvokeStatic(Type type, string name, params object?[] arguments)
        {
            return FindMethod(type, name, StaticFlags, arguments).Invoke(null, arguments);
        }

        /// <summary>Invokes a static method and casts its return value.</summary>
        /// <typeparam name="T">The expected return type.</typeparam>
        /// <param name="type">The type that declares the method.</param>
        /// <param name="name">The method name.</param>
        /// <param name="arguments">The arguments; overloads are chosen by count and argument compatibility.</param>
        /// <returns>The method's return value.</returns>
        /// <exception cref="MissingMethodException">No compatible method exists.</exception>
        /// <exception cref="TargetInvocationException">The invoked method threw; the original exception is the inner exception.</exception>
        public static T InvokeStatic<T>(Type type, string name, params object?[] arguments)
        {
            return (T)InvokeStatic(type, name, arguments)!;
        }

        /// <summary>Finds a field on a type or its base types.</summary>
        /// <param name="type">The type to search first.</param>
        /// <param name="name">The field name.</param>
        /// <param name="flags">The binding flags applied to each type in the hierarchy.</param>
        /// <returns>The field.</returns>
        /// <exception cref="MissingFieldException">No field with that name exists.</exception>
        private static FieldInfo FindField(Type type, string name, BindingFlags flags)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                var field = current.GetField(name, flags);
                if (field is not null)
                {
                    return field;
                }
            }
            throw new MissingFieldException(type.FullName, name);
        }

        /// <summary>Finds a property on a type or its base types.</summary>
        /// <param name="type">The type to search first.</param>
        /// <param name="name">The property name.</param>
        /// <param name="flags">The binding flags applied to each type in the hierarchy.</param>
        /// <returns>The property.</returns>
        /// <exception cref="MissingMemberException">No property with that name exists.</exception>
        private static PropertyInfo FindProperty(Type type, string name, BindingFlags flags)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                var property = current.GetProperty(name, flags);
                if (property is not null)
                {
                    return property;
                }
            }
            throw new MissingMemberException(type.FullName, name);
        }

        /// <summary>Finds the first method on a type or its base types whose parameters accept the arguments.</summary>
        /// <param name="type">The type to search first.</param>
        /// <param name="name">The method name.</param>
        /// <param name="flags">The binding flags applied to each type in the hierarchy.</param>
        /// <param name="arguments">The arguments the method must accept.</param>
        /// <returns>The method.</returns>
        /// <exception cref="MissingMethodException">No compatible method exists.</exception>
        private static MethodInfo FindMethod(Type type, string name, BindingFlags flags, object?[] arguments)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                var candidates = current.GetMethods(flags).Where(x => x.Name == name && !x.IsGenericMethodDefinition);
                foreach (var candidate in candidates)
                {
                    if (Accepts(candidate.GetParameters(), arguments))
                    {
                        return candidate;
                    }
                }
            }
            throw new MissingMethodException(type.FullName, name);
        }

        /// <summary>Checks whether a parameter list accepts the given arguments.</summary>
        /// <param name="parameters">The parameters of a candidate method.</param>
        /// <param name="arguments">The arguments to pass.</param>
        /// <returns>True when the counts match and every argument is assignable to its parameter.</returns>
        private static bool Accepts(ParameterInfo[] parameters, object?[] arguments)
        {
            if (parameters.Length != arguments.Length)
            {
                return false;
            }
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameterType = parameters[i].ParameterType;
                if (parameterType.IsByRef)
                {
                    parameterType = parameterType.GetElementType()!;
                }
                var argument = arguments[i];
                if (argument is null)
                {
                    if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) is null)
                    {
                        return false;
                    }
                    continue;
                }
                if (!parameterType.IsInstanceOfType(argument))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Gets the name the C# compiler gives the backing field of an auto property.</summary>
        /// <param name="propertyName">The property name.</param>
        /// <returns>The backing field name.</returns>
        private static string BackingFieldName(string propertyName)
        {
            return $"<{propertyName}>k__BackingField";
        }
    }
}
