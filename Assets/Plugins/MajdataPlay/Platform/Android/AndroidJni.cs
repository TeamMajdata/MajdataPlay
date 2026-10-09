using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MajdataPlay.Platform.Android.Runtime.Java.Lang;
using UnityEngine;

#nullable enable

namespace MajdataPlay.Platform.Android
{
    /// <summary>
    /// Executes generated JNI bindings using JVM descriptors, without runtime signature inference.
    /// </summary>
    /// <remarks>
    /// Requires an Android player and the Unity main thread or a JVM-attached thread. Object results
    /// own global references and must be disposed. Declared array component types, null elements,
    /// and jagged arrays are preserved. Only managed descriptors, never VM-bound JNI IDs, are cached.
    /// </remarks>
    public static class AndroidJni
    {
        /// <summary>
        /// Reuses parsed descriptors without retaining JNI references across domain reloads.
        /// </summary>
        private static readonly ConcurrentDictionary<string, MethodSignature> s_signatures = new();

        /// <summary>
        /// Constructs a Java object with an exact constructor descriptor.
        /// </summary>
        /// <param name="className">The Java binary class name.</param>
        /// <param name="descriptor">The constructor's JVM descriptor, ending in V.</param>
        /// <param name="arguments">The arguments in declaration order.</param>
        /// <returns>A caller-owned Unity Java reference to the new object.</returns>
        /// <exception cref="PlatformNotSupportedException">Execution is not in an Android player.</exception>
        /// <exception cref="ArgumentException">The descriptor or arguments are invalid.</exception>
        /// <exception cref="AndroidJavaException">Java lookup or construction fails.</exception>
        /// <exception cref="JavaInvocationException">Java raises an exception during the JNI operation.</exception>
        /// <exception cref="InvalidOperationException">JNI returns null without reporting an exception.</exception>
        public static AndroidJavaObject Construct(string className, string descriptor, params object?[] arguments)
        {
            EnsureAndroid();
            var signature = GetSignature(descriptor);
            if (signature.ReturnType != "V")
            {
                throw new ArgumentException("A constructor must have a void JVM return descriptor.", nameof(descriptor));
            }

            using var frame = new LocalFrame(32);
            using var javaClass = new AndroidJavaClass(className);
            var classReference = javaClass.GetRawClass();
            var constructor = AndroidJNIHelper.GetMethodID(classReference, "<init>", descriptor, false);
            var jniArguments = WriteArguments(signature, arguments);
            var result = AndroidJNI.NewObject(classReference, constructor, jniArguments);
            CheckException();
            if (result == IntPtr.Zero)
            {
                throw new InvalidOperationException("JNI construction returned a null object without a Java exception.");
            }

            return new AndroidJavaObject(result);
        }

        /// <summary>
        /// Calls a Java method and converts its result using its exact JVM descriptor.
        /// </summary>
        /// <typeparam name="T">The generated primitive, string, Java-reference, or jagged-array result type.</typeparam>
        /// <param name="instance">The target reference, or null for a static method.</param>
        /// <param name="className">The binary class name used for static lookup.</param>
        /// <param name="name">The case-sensitive Java method name.</param>
        /// <param name="descriptor">The exact JVM method descriptor.</param>
        /// <param name="arguments">The arguments in declaration order.</param>
        /// <returns>The converted result, preserving null Java references.</returns>
        /// <exception cref="PlatformNotSupportedException">Execution is not in an Android player.</exception>
        /// <exception cref="ArgumentException">The descriptor or arguments are invalid.</exception>
        /// <exception cref="InvalidCastException">The result type does not match the descriptor.</exception>
        /// <exception cref="AndroidJavaException">Java lookup or invocation fails.</exception>
        /// <exception cref="JavaInvocationException">Java raises an exception during the JNI operation.</exception>
        public static T Call<T>(AndroidJavaObject? instance, string className, string name, string descriptor, params object?[] arguments)
        {
            EnsureAndroid();
            var signature = GetSignature(descriptor);
            if (signature.ReturnType == "V")
            {
                throw new ArgumentException("Use the non-generic overload for a void Java method.", nameof(descriptor));
            }

            using var frame = new LocalFrame(32);
            using var javaClass = instance is null ? new AndroidJavaClass(className) : null;
            var classReference = instance is null ? javaClass!.GetRawClass() : instance.GetRawClass();
            var isStatic = instance is null;
            var target = isStatic ? classReference : instance!.GetRawObject();
            var method = AndroidJNIHelper.GetMethodID(classReference, name, descriptor, isStatic);
            var jniArguments = WriteArguments(signature, arguments);
            var result = Invoke(target, method, signature.ReturnType[0], jniArguments, isStatic);
            CheckException();
            if (signature.ReturnType[0] == 'L' || signature.ReturnType[0] == '[')
            {
                return (T)ReadReference((IntPtr)result, signature.ReturnType, typeof(T))!;
            }

            return (T)result;
        }

        /// <summary>
        /// Calls a void Java method with an exact JVM descriptor.
        /// </summary>
        /// <param name="instance">The target reference, or null for a static method.</param>
        /// <param name="className">The binary class name used for static lookup.</param>
        /// <param name="name">The case-sensitive Java method name.</param>
        /// <param name="descriptor">The exact JVM method descriptor, ending in V.</param>
        /// <param name="arguments">The arguments in declaration order.</param>
        /// <exception cref="PlatformNotSupportedException">Execution is not in an Android player.</exception>
        /// <exception cref="ArgumentException">The descriptor or arguments are invalid.</exception>
        /// <exception cref="AndroidJavaException">Java lookup or invocation fails.</exception>
        /// <exception cref="JavaInvocationException">Java raises an exception during the JNI operation.</exception>
        public static void Call(AndroidJavaObject? instance, string className, string name, string descriptor, params object?[] arguments)
        {
            EnsureAndroid();
            var signature = GetSignature(descriptor);
            if (signature.ReturnType != "V")
            {
                throw new ArgumentException("The non-generic overload requires a void JVM return descriptor.", nameof(descriptor));
            }

            using var frame = new LocalFrame(32);
            using var javaClass = instance is null ? new AndroidJavaClass(className) : null;
            var classReference = instance is null ? javaClass!.GetRawClass() : instance.GetRawClass();
            var method = AndroidJNIHelper.GetMethodID(classReference, name, descriptor, instance is null);
            var jniArguments = WriteArguments(signature, arguments);
            if (instance is null)
            {
                AndroidJNI.CallStaticVoidMethod(classReference, method, jniArguments);
            }
            else
            {
                AndroidJNI.CallVoidMethod(instance.GetRawObject(), method, jniArguments);
            }

            CheckException();
        }

        /// <summary>
        /// Reads a public Java field with an exact JVM descriptor.
        /// </summary>
        /// <typeparam name="T">The generated primitive, string, Java-reference, or array result type.</typeparam>
        /// <param name="instance">The owning reference, or null for a static field.</param>
        /// <param name="className">The binary name of the Java class declaring the field.</param>
        /// <param name="name">The case-sensitive Java field name.</param>
        /// <param name="descriptor">The exact JVM field descriptor.</param>
        /// <returns>The current field value, preserving null references.</returns>
        /// <exception cref="PlatformNotSupportedException">Execution is not in an Android player.</exception>
        /// <exception cref="ArgumentException">The field descriptor is invalid.</exception>
        /// <exception cref="InvalidCastException">The result type does not match the descriptor.</exception>
        /// <exception cref="AndroidJavaException">Java lookup or field access fails.</exception>
        /// <exception cref="JavaInvocationException">Java raises an exception during the JNI operation.</exception>
        public static T GetField<T>(AndroidJavaObject? instance, string className, string name, string descriptor)
        {
            EnsureAndroid();
            var position = 0;
            ReadDescriptor(descriptor, ref position, false);
            if (position != descriptor.Length)
            {
                throw new ArgumentException("A field descriptor must contain exactly one Java type.", nameof(descriptor));
            }

            using var frame = new LocalFrame(32);
            // Declaring-class lookup honors hidden inherited fields rather than the runtime class's field.
            using var javaClass = new AndroidJavaClass(className);
            var classReference = javaClass.GetRawClass();
            var isStatic = instance is null;
            var target = isStatic ? classReference : instance!.GetRawObject();
            var field = AndroidJNIHelper.GetFieldID(classReference, name, descriptor, isStatic);
            var result = ReadField(target, field, descriptor[0], isStatic);
            CheckException();
            if (descriptor[0] == 'L' || descriptor[0] == '[')
            {
                return (T)ReadReference((IntPtr)result, descriptor, typeof(T))!;
            }

            return (T)result;
        }

        /// <summary>
        /// Transfers a returned Java reference into a generated wrapper while preserving null.
        /// </summary>
        /// <typeparam name="TWrapper">The generated wrapper type.</typeparam>
        /// <param name="value">The caller-owned Java result, or null.</param>
        /// <param name="factory">A generated constructor, avoiding reflective activation.</param>
        /// <returns>An owning wrapper, or null when Java returned null.</returns>
        /// <exception cref="ArgumentNullException">The factory is null.</exception>
        public static TWrapper? Wrap<TWrapper>(AndroidJavaObject? value, Func<AndroidJavaObject, TWrapper> factory)
            where TWrapper : JavaObject
        {
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            if (value is null)
            {
                return null;
            }

            try
            {
                return factory(value);
            }
            catch
            {
                value.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Maps one array dimension without changing null array or element semantics.
        /// </summary>
        /// <typeparam name="TSource">The source element type, including nullable references.</typeparam>
        /// <typeparam name="TResult">The wrapper or nested-array element type.</typeparam>
        /// <param name="values">The source array, or null.</param>
        /// <param name="converter">The generated element conversion function.</param>
        /// <returns>A converted array, or null when the source array is null.</returns>
        /// <exception cref="ArgumentNullException">The converter is null.</exception>
        public static TResult[]? MapArray<TSource, TResult>(TSource[]? values, Func<TSource, TResult> converter)
        {
            if (converter is null)
            {
                throw new ArgumentNullException(nameof(converter));
            }

            if (values is null)
            {
                return null;
            }

            var results = new TResult[values.Length];
            try
            {
                for (var i = 0; i < values.Length; i++)
                {
                    results[i] = converter(values[i]);
                }

                return results;
            }
            catch
            {
                DisposeReferences(results);
                DisposeReferences(values);
                throw;
            }
        }

        /// <summary>
        /// Rejects JNI access outside Android players before any Unity native call is attempted.
        /// </summary>
        /// <exception cref="PlatformNotSupportedException">Execution is not in an Android player.</exception>
        private static void EnsureAndroid()
        {
#if !UNITY_ANDROID || UNITY_EDITOR
            throw new PlatformNotSupportedException("Generated Java wrappers require an Android player; JNI is unavailable in the Unity Editor.");
#endif
        }

        /// <summary>
        /// Parses and caches the argument and result portions of a JVM method descriptor.
        /// </summary>
        /// <param name="descriptor">The complete JVM method descriptor.</param>
        /// <returns>The immutable parsed signature.</returns>
        /// <exception cref="ArgumentException">The descriptor is malformed.</exception>
        private static MethodSignature GetSignature(string descriptor)
        {
            if (string.IsNullOrEmpty(descriptor))
            {
                throw new ArgumentException("A JVM descriptor is required.", nameof(descriptor));
            }

            return s_signatures.GetOrAdd(descriptor, MethodSignature.Parse);
        }

        /// <summary>
        /// Reads one JVM type, rejecting void arguments and malformed references or arrays.
        /// </summary>
        /// <param name="descriptor">The containing JVM descriptor.</param>
        /// <param name="position">The cursor advanced past the type.</param>
        /// <param name="allowVoid">Whether a void return type is permitted.</param>
        /// <returns>The exact substring describing one JVM type.</returns>
        /// <exception cref="ArgumentException">The descriptor is malformed.</exception>
        private static string ReadDescriptor(string descriptor, ref int position, bool allowVoid)
        {
            if (string.IsNullOrEmpty(descriptor) || position >= descriptor.Length)
            {
                throw new ArgumentException("Incomplete JVM descriptor.", nameof(descriptor));
            }

            var start = position;
            while (position < descriptor.Length && descriptor[position] == '[')
            {
                position++;
            }

            if (position >= descriptor.Length)
            {
                throw new ArgumentException("Incomplete JVM array descriptor.", nameof(descriptor));
            }

            var kind = descriptor[position++];
            if (kind == 'L')
            {
                var end = descriptor.IndexOf(';', position);
                if (end <= position)
                {
                    throw new ArgumentException("Incomplete JVM object descriptor.", nameof(descriptor));
                }

                position = end + 1;
            }
            else if ("ZBCSIJFD".IndexOf(kind) < 0 && !(kind == 'V' && allowVoid && position == start + 1))
            {
                throw new ArgumentException("Invalid JVM type descriptor.", nameof(descriptor));
            }

            return descriptor.Substring(start, position - start);
        }

        /// <summary>
        /// Marshals boxed arguments using declared, rather than inferred, Java types.
        /// </summary>
        /// <param name="signature">The parsed method signature.</param>
        /// <param name="arguments">The boxed generated arguments.</param>
        /// <returns>The JNI arguments valid within the current local frame.</returns>
        /// <exception cref="ArgumentException">An argument count or primitive type is incorrect.</exception>
        private static jvalue[] WriteArguments(MethodSignature signature, object?[] arguments)
        {
            if (arguments is null || arguments.Length != signature.Parameters.Length)
            {
                throw new ArgumentException("Argument count does not match the JVM descriptor.", nameof(arguments));
            }

            var results = new jvalue[arguments.Length];
            for (var i = 0; i < arguments.Length; i++)
            {
                var value = arguments[i];
                try
                {
                    switch (signature.Parameters[i][0])
                    {
                        case 'Z':
                            results[i].z = (bool)value!;
                            break;
                        case 'B':
                            results[i].b = (sbyte)value!;
                            break;
                        case 'C':
                            results[i].c = (char)value!;
                            break;
                        case 'S':
                            results[i].s = (short)value!;
                            break;
                        case 'I':
                            results[i].i = (int)value!;
                            break;
                        case 'J':
                            results[i].j = (long)value!;
                            break;
                        case 'F':
                            results[i].f = (float)value!;
                            break;
                        case 'D':
                            results[i].d = (double)value!;
                            break;
                        default:
                            results[i].l = WriteReference(value, signature.Parameters[i], out _);
                            break;
                    }
                }
                catch (Exception exception) when (exception is InvalidCastException || exception is NullReferenceException)
                {
                    throw new ArgumentException($"Argument {i} does not match JVM type {signature.Parameters[i]}.", nameof(arguments), exception);
                }
            }

            CheckException();
            return results;
        }

        /// <summary>
        /// Marshals a nullable string, Java reference, proxy, or array into the current JNI frame.
        /// </summary>
        /// <param name="value">The managed argument.</param>
        /// <param name="descriptor">The declared object or array descriptor.</param>
        /// <param name="isLocal">Whether the reference belongs to the current local frame.</param>
        /// <returns>The JNI reference, or zero for null.</returns>
        /// <exception cref="ArgumentException">The argument is not a supported reference type.</exception>
        private static IntPtr WriteReference(object? value, string descriptor, out bool isLocal)
        {
            isLocal = false;
            if (value is null)
            {
                return IntPtr.Zero;
            }

            if (value is JavaObject wrapper)
            {
                return wrapper.JavaReference.GetRawObject();
            }

            if (value is AndroidJavaObject javaObject)
            {
                return javaObject.GetRawObject();
            }

            isLocal = true;
            if (value is string text)
            {
                // UTF-16 preserves embedded NULs and supplementary characters without Modified UTF-8 loss.
                var reference = AndroidJNI.NewString(text);
                CheckException();
                return reference;
            }

            if (value is Array array && descriptor[0] == '[')
            {
                return WriteArray(array, descriptor);
            }

            if (value is AndroidJavaProxy proxy)
            {
                var reference = AndroidJNIHelper.CreateJavaProxy(proxy);
                CheckException();
                return reference;
            }

            throw new ArgumentException($"Unsupported managed argument for JVM type {descriptor}.", nameof(value));
        }

        /// <summary>
        /// Creates a Java array with the exact component class, including nested arrays.
        /// </summary>
        /// <param name="values">The one-dimensional managed array for this dimension.</param>
        /// <param name="descriptor">The complete JVM array descriptor.</param>
        /// <returns>A local JNI array reference.</returns>
        /// <exception cref="ArgumentException">The array rank or element type is incorrect.</exception>
        private static IntPtr WriteArray(Array values, string descriptor)
        {
            if (values.Rank != 1)
            {
                throw new ArgumentException("Represent Java arrays as C# jagged arrays, not rectangular arrays.", nameof(values));
            }

            var component = descriptor.Substring(1);
            IntPtr result;
            switch (component)
            {
                case "Z":
                    result = AndroidJNI.ToBooleanArray((bool[])values);
                    break;
                case "B":
                    result = AndroidJNI.ToSByteArray((sbyte[])values);
                    break;
                case "C":
                    result = AndroidJNI.ToCharArray((char[])values);
                    break;
                case "S":
                    result = AndroidJNI.ToShortArray((short[])values);
                    break;
                case "I":
                    result = AndroidJNI.ToIntArray((int[])values);
                    break;
                case "J":
                    result = AndroidJNI.ToLongArray((long[])values);
                    break;
                case "F":
                    result = AndroidJNI.ToFloatArray((float[])values);
                    break;
                case "D":
                    result = AndroidJNI.ToDoubleArray((double[])values);
                    break;
                default:
                    var componentClass = ResolveArrayComponent(component);
                    result = AndroidJNI.NewObjectArray(values.Length, componentClass, IntPtr.Zero);
                    CheckException();
                    AndroidJNI.DeleteLocalRef(componentClass);
                    for (var i = 0; i < values.Length; i++)
                    {
                        var item = WriteReference(values.GetValue(i), component, out var isLocal);
                        try
                        {
                            AndroidJNI.SetObjectArrayElement(result, i, item);
                            CheckException();
                        }
                        finally
                        {
                            if (isLocal && item != IntPtr.Zero)
                            {
                                AndroidJNI.DeleteLocalRef(item);
                            }
                        }
                    }

                    break;
            }

            CheckException();
            return result;
        }

        /// <summary>
        /// Resolves an array component through the leaf type's loader; leaf lookup may initialize it.
        /// </summary>
        /// <param name="descriptor">An object descriptor or nested-array descriptor.</param>
        /// <returns>A local JNI class reference.</returns>
        /// <exception cref="AndroidJavaException">The component class cannot be resolved.</exception>
        private static IntPtr ResolveArrayComponent(string descriptor)
        {
            if (descriptor[0] == 'L')
            {
                using var javaClass = new AndroidJavaClass(descriptor.Substring(1, descriptor.Length - 2).Replace('/', '.'));
                var reference = AndroidJNI.NewLocalRef(javaClass.GetRawClass());
                CheckException();
                return reference;
            }

            var leaf = descriptor.TrimStart('[');
            using var classClass = new AndroidJavaClass("java.lang.Class");
            using var leafClass = leaf[0] == 'L'
                ? new AndroidJavaClass(leaf.Substring(1, leaf.Length - 2).Replace('/', '.'))
                : null;
            var classLoader = IntPtr.Zero;
            if (leafClass is not null)
            {
                var getLoader = AndroidJNIHelper.GetMethodID(classClass.GetRawClass(), "getClassLoader", "()Ljava/lang/ClassLoader;", false);
                classLoader = AndroidJNI.CallObjectMethod(leafClass.GetRawClass(), getLoader, Array.Empty<jvalue>());
                CheckException();
            }

            var name = AndroidJNI.NewString(descriptor.Replace('/', '.'));
            CheckException();
            var forName = AndroidJNIHelper.GetMethodID(classClass.GetRawClass(), "forName", "(Ljava/lang/String;ZLjava/lang/ClassLoader;)Ljava/lang/Class;", true);
            var arguments = new[] { new jvalue { l = name }, new jvalue { z = false }, new jvalue { l = classLoader } };
            var result = AndroidJNI.CallStaticObjectMethod(classClass.GetRawClass(), forName, arguments);
            CheckException();
            AndroidJNI.DeleteLocalRef(name);
            AndroidJNI.DeleteLocalRef(classLoader);
            return result;
        }

        /// <summary>
        /// Converts a local Java reference into a string, owned Unity reference, or managed array.
        /// </summary>
        /// <param name="reference">The local JNI reference.</param>
        /// <param name="descriptor">The exact Java reference descriptor.</param>
        /// <param name="resultType">The generated managed result type.</param>
        /// <returns>The managed value, or null for a null JNI reference.</returns>
        /// <exception cref="InvalidCastException">The result type is incompatible with the descriptor.</exception>
        private static object? ReadReference(IntPtr reference, string descriptor, Type resultType)
        {
            if (reference == IntPtr.Zero)
            {
                return null;
            }

            if (descriptor[0] == '[')
            {
                return ReadArray(reference, descriptor, resultType);
            }

            if (resultType == typeof(string))
            {
                var result = AndroidJNI.GetStringChars(reference);
                CheckException();
                return result;
            }

            if (resultType == typeof(AndroidJavaObject))
            {
                // Unity creates independent globals before the invocation's local frame is popped.
                return new AndroidJavaObject(reference);
            }

            throw new InvalidCastException($"JVM reference {descriptor} cannot be returned as {resultType.FullName}.");
        }

        /// <summary>
        /// Converts a Java array, releasing per-element locals and owned references on failure.
        /// </summary>
        /// <param name="reference">The local Java array reference.</param>
        /// <param name="descriptor">The exact JVM array descriptor.</param>
        /// <param name="resultType">The generated managed array type.</param>
        /// <returns>The primitive, reference, or nested array.</returns>
        /// <exception cref="InvalidCastException">The managed type is not an array.</exception>
        private static Array ReadArray(IntPtr reference, string descriptor, Type resultType)
        {
            var elementType = resultType.GetElementType()
                ?? throw new InvalidCastException("A JVM array requires a managed array return type.");
            var component = descriptor.Substring(1);
            Array result;
            switch (component)
            {
                case "Z":
                    result = AndroidJNI.FromBooleanArray(reference);
                    break;
                case "B":
                    result = AndroidJNI.FromSByteArray(reference);
                    break;
                case "C":
                    result = AndroidJNI.FromCharArray(reference);
                    break;
                case "S":
                    result = AndroidJNI.FromShortArray(reference);
                    break;
                case "I":
                    result = AndroidJNI.FromIntArray(reference);
                    break;
                case "J":
                    result = AndroidJNI.FromLongArray(reference);
                    break;
                case "F":
                    result = AndroidJNI.FromFloatArray(reference);
                    break;
                case "D":
                    result = AndroidJNI.FromDoubleArray(reference);
                    break;
                default:
                    var length = AndroidJNI.GetArrayLength(reference);
                    CheckException();
                    result = Array.CreateInstance(elementType, length);
                    try
                    {
                        for (var i = 0; i < length; i++)
                        {
                            var item = AndroidJNI.GetObjectArrayElement(reference, i);
                            try
                            {
                                CheckException();
                                result.SetValue(ReadReference(item, component, elementType), i);
                            }
                            finally
                            {
                                AndroidJNI.DeleteLocalRef(item);
                            }
                        }
                    }
                    catch
                    {
                        DisposeReferences(result);
                        throw;
                    }

                    break;
            }

            CheckException();
            return result;
        }

        /// <summary>
        /// Invokes the JNI entry point matching a method's exact return kind.
        /// </summary>
        /// <param name="target">The receiver object or static class reference.</param>
        /// <param name="method">The resolved method identifier.</param>
        /// <param name="kind">The first character of the return descriptor.</param>
        /// <param name="arguments">The marshaled JNI arguments.</param>
        /// <param name="isStatic">Whether this is a static invocation.</param>
        /// <returns>A boxed primitive result or a local object pointer.</returns>
        /// <exception cref="ArgumentException">The return kind is unsupported.</exception>
        private static object Invoke(IntPtr target, IntPtr method, char kind, jvalue[] arguments, bool isStatic)
        {
            switch (kind)
            {
                case 'Z':
                    return isStatic ? AndroidJNI.CallStaticBooleanMethod(target, method, arguments) : AndroidJNI.CallBooleanMethod(target, method, arguments);
                case 'B':
                    return isStatic ? AndroidJNI.CallStaticSByteMethod(target, method, arguments) : AndroidJNI.CallSByteMethod(target, method, arguments);
                case 'C':
                    return isStatic ? AndroidJNI.CallStaticCharMethod(target, method, arguments) : AndroidJNI.CallCharMethod(target, method, arguments);
                case 'S':
                    return isStatic ? AndroidJNI.CallStaticShortMethod(target, method, arguments) : AndroidJNI.CallShortMethod(target, method, arguments);
                case 'I':
                    return isStatic ? AndroidJNI.CallStaticIntMethod(target, method, arguments) : AndroidJNI.CallIntMethod(target, method, arguments);
                case 'J':
                    return isStatic ? AndroidJNI.CallStaticLongMethod(target, method, arguments) : AndroidJNI.CallLongMethod(target, method, arguments);
                case 'F':
                    return isStatic ? AndroidJNI.CallStaticFloatMethod(target, method, arguments) : AndroidJNI.CallFloatMethod(target, method, arguments);
                case 'D':
                    return isStatic ? AndroidJNI.CallStaticDoubleMethod(target, method, arguments) : AndroidJNI.CallDoubleMethod(target, method, arguments);
                case 'L':
                case '[':
                    return isStatic ? AndroidJNI.CallStaticObjectMethod(target, method, arguments) : AndroidJNI.CallObjectMethod(target, method, arguments);
                default:
                    throw new ArgumentException("Unsupported JVM method return kind.", nameof(kind));
            }
        }

        /// <summary>
        /// Reads a field using the JNI entry point matching its exact JVM kind.
        /// </summary>
        /// <param name="target">The owner object or static class reference.</param>
        /// <param name="field">The resolved field identifier.</param>
        /// <param name="kind">The first character of the field descriptor.</param>
        /// <param name="isStatic">Whether this is a static field.</param>
        /// <returns>A boxed primitive value or a local object pointer.</returns>
        /// <exception cref="ArgumentException">The field kind is unsupported.</exception>
        private static object ReadField(IntPtr target, IntPtr field, char kind, bool isStatic)
        {
            switch (kind)
            {
                case 'Z':
                    return isStatic ? AndroidJNI.GetStaticBooleanField(target, field) : AndroidJNI.GetBooleanField(target, field);
                case 'B':
                    return isStatic ? AndroidJNI.GetStaticSByteField(target, field) : AndroidJNI.GetSByteField(target, field);
                case 'C':
                    return isStatic ? AndroidJNI.GetStaticCharField(target, field) : AndroidJNI.GetCharField(target, field);
                case 'S':
                    return isStatic ? AndroidJNI.GetStaticShortField(target, field) : AndroidJNI.GetShortField(target, field);
                case 'I':
                    return isStatic ? AndroidJNI.GetStaticIntField(target, field) : AndroidJNI.GetIntField(target, field);
                case 'J':
                    return isStatic ? AndroidJNI.GetStaticLongField(target, field) : AndroidJNI.GetLongField(target, field);
                case 'F':
                    return isStatic ? AndroidJNI.GetStaticFloatField(target, field) : AndroidJNI.GetFloatField(target, field);
                case 'D':
                    return isStatic ? AndroidJNI.GetStaticDoubleField(target, field) : AndroidJNI.GetDoubleField(target, field);
                case 'L':
                case '[':
                    return isStatic ? AndroidJNI.GetStaticObjectField(target, field) : AndroidJNI.GetObjectField(target, field);
                default:
                    throw new ArgumentException("Unsupported JVM field kind.", nameof(kind));
            }
        }

        /// <summary>
        /// Clears a pending Java exception and reports its original message and stack trace.
        /// </summary>
        /// <exception cref="JavaInvocationException">JNI reports a pending Java exception.</exception>
        private static void CheckException()
        {
            var exception = AndroidJNI.ExceptionOccurred();
            if (exception == IntPtr.Zero)
            {
                return;
            }

            AndroidJNI.ExceptionClear();
            var message = "Java exception during generated JNI invocation.";
            var stack = string.Empty;
            var throwableClass = IntPtr.Zero;
            var logClass = IntPtr.Zero;
            try
            {
                // Do not use Unity's exception formatter here: an overridden Throwable.toString()
                // can throw, leaving secondary diagnostic JNI calls with a pending exception.
                throwableClass = AndroidJNI.GetObjectClass(exception);
                CheckDiagnosticException();
                var toString = AndroidJNI.GetMethodID(throwableClass, "toString", "()Ljava/lang/String;");
                CheckDiagnosticException();
                var description = AndroidJNI.CallObjectMethod(exception, toString, Array.Empty<jvalue>());
                try
                {
                    CheckDiagnosticException();
                    if (description != IntPtr.Zero)
                    {
                        message = AndroidJNI.GetStringChars(description);
                        CheckDiagnosticException();
                    }
                }
                finally
                {
                    AndroidJNI.DeleteLocalRef(description);
                }

                logClass = AndroidJNI.FindClass("android/util/Log");
                CheckDiagnosticException();
                var method = AndroidJNI.GetStaticMethodID(logClass, "getStackTraceString", "(Ljava/lang/Throwable;)Ljava/lang/String;");
                CheckDiagnosticException();
                var result = AndroidJNI.CallStaticObjectMethod(logClass, method, new[] { new jvalue { l = exception } });
                try
                {
                    CheckDiagnosticException();
                    if (result != IntPtr.Zero)
                    {
                        stack = AndroidJNI.GetStringChars(result);
                        CheckDiagnosticException();
                    }
                }
                finally
                {
                    AndroidJNI.DeleteLocalRef(result);
                }
            }
            catch (Exception diagnosticException)
            {
                message += " Exception details could not be read: " + diagnosticException.Message;
            }
            finally
            {
                // Diagnostic calls must never leave a second exception pending on this JVM thread.
                AndroidJNI.ExceptionClear();
                AndroidJNI.DeleteLocalRef(logClass);
                AndroidJNI.DeleteLocalRef(throwableClass);
                AndroidJNI.DeleteLocalRef(exception);
            }

            throw new JavaInvocationException(message, stack);
        }

        /// <summary>
        /// Clears secondary Java failures without recursively trying to format their throwables.
        /// </summary>
        /// <exception cref="InvalidOperationException">A JNI diagnostic operation raises a Java exception.</exception>
        private static void CheckDiagnosticException()
        {
            var exception = AndroidJNI.ExceptionOccurred();
            if (exception == IntPtr.Zero)
            {
                return;
            }

            AndroidJNI.ExceptionClear();
            AndroidJNI.DeleteLocalRef(exception);
            throw new InvalidOperationException("Java raised an exception while reading the original throwable's details.");
        }

        /// <summary>
        /// Disposes Java references or nested reference arrays during a failed conversion.
        /// </summary>
        /// <param name="values">The partially initialized reference array.</param>
        private static void DisposeReferences(Array values)
        {
            foreach (var value in values)
            {
                if (value is Array nested)
                {
                    DisposeReferences(nested);
                }
                else if (value is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        /// <summary>
        /// Holds immutable argument and result descriptors for one Java method.
        /// </summary>
        private sealed class MethodSignature
        {
            /// <summary>
            /// Gets the parameter descriptors in declaration order.
            /// </summary>
            public string[] Parameters { get; }

            /// <summary>
            /// Gets the method's result descriptor.
            /// </summary>
            public string ReturnType { get; }

            /// <summary>
            /// Initializes a parsed method signature.
            /// </summary>
            /// <param name="parameters">The argument descriptors.</param>
            /// <param name="returnType">The result descriptor.</param>
            private MethodSignature(string[] parameters, string returnType)
            {
                Parameters = parameters;
                ReturnType = returnType;
            }

            /// <summary>
            /// Parses a complete JVM method descriptor without inspecting runtime arguments.
            /// </summary>
            /// <param name="descriptor">The complete method descriptor.</param>
            /// <returns>The parsed immutable signature.</returns>
            /// <exception cref="ArgumentException">The descriptor is malformed.</exception>
            public static MethodSignature Parse(string descriptor)
            {
                if (descriptor[0] != '(')
                {
                    throw new ArgumentException("A JVM method descriptor must start with '('.", nameof(descriptor));
                }

                var parameters = new List<string>();
                var position = 1;
                while (position < descriptor.Length && descriptor[position] != ')')
                {
                    parameters.Add(ReadDescriptor(descriptor, ref position, false));
                }

                if (position >= descriptor.Length || descriptor[position++] != ')')
                {
                    throw new ArgumentException("Incomplete JVM method parameter list.", nameof(descriptor));
                }

                var result = ReadDescriptor(descriptor, ref position, true);
                if (position != descriptor.Length)
                {
                    throw new ArgumentException("Unexpected characters after the JVM result type.", nameof(descriptor));
                }

                return new MethodSignature(parameters.ToArray(), result);
            }
        }

        /// <summary>
        /// Bounds temporary JNI references for one invocation, including exceptional exits.
        /// </summary>
        private readonly struct LocalFrame : IDisposable
        {
            /// <summary>
            /// Creates a JNI local-reference frame with expandable capacity.
            /// </summary>
            /// <param name="capacity">The initial number of available local-reference slots.</param>
            /// <exception cref="AndroidJavaException">The JVM cannot allocate the frame.</exception>
            /// <exception cref="InvalidOperationException">JNI rejects the frame without an exception.</exception>
            public LocalFrame(int capacity)
            {
                if (AndroidJNI.PushLocalFrame(capacity) < 0)
                {
                    CheckException();
                    throw new InvalidOperationException("JNI could not allocate a local-reference frame.");
                }
            }

            /// <summary>
            /// Releases every temporary reference remaining in the invocation's local frame.
            /// </summary>
            public void Dispose()
            {
                AndroidJNI.PopLocalFrame(IntPtr.Zero);
            }
        }
    }
}
