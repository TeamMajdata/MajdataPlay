using System.Collections.Generic;
#nullable enable
namespace MajdataPlay.Collections
{
    public static class ListExtensions
    {
        public static bool IsEmpty<T>(this List<T> source) => source.Count == 0;
        public static bool TryGetElement<T>(this List<T> source, int index, out T? element)
        {
            element = default;
            if ((uint)index >= (uint)source.Count)
            {
                return false;
            }

            element = source[index];
            return true;
        }
    }
}