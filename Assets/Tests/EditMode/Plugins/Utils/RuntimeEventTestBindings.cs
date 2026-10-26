using NUnit.Framework;
using UnityEditor;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Utils
{
    /// <summary>
    /// Writes <see cref="MajdataPlay.Utils.Events.RuntimeEvent{TDelegate}"/> bindings through a
    /// <see cref="SerializedObject"/>, the way the Inspector does, so tests exercise Unity's serialization.
    /// </summary>
    internal static class RuntimeEventTestBindings
    {
        /// <summary>Replaces the bindings of a serialized event.</summary>
        /// <param name="owner">The object that owns the event field.</param>
        /// <param name="eventPath">The property path of the event field.</param>
        /// <param name="bindings">The new bindings in invocation order.</param>
        public static void Set(Object owner, string eventPath, params (Object? Target, string MethodName)[] bindings)
        {
            using var serializedObject = new SerializedObject(owner);
            var list = serializedObject.FindProperty($"{eventPath}._bindings");
            Assert.That(list, Is.Not.Null, $"{eventPath} has no serialized binding list.");
            list.arraySize = bindings.Length;
            for (var i = 0; i < bindings.Length; i++)
            {
                var binding = list.GetArrayElementAtIndex(i);
                binding.FindPropertyRelative("_target").objectReferenceValue = bindings[i].Target;
                binding.FindPropertyRelative("_methodName").stringValue = bindings[i].MethodName;
            }
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
