using UnityEngine;
#nullable enable
namespace MajdataPlay.Numerics
{
    public static class Vector3Extensions
    {
        /// <summary>
        /// 获取一个在原点与当前坐标连线上并与当前坐标相隔<paramref name="distance"/>的坐标
        /// </summary>
        /// <param name="source"></param>
        /// <param name="distance"></param>
        /// <returns></returns>
        public static Vector3 GetPoint(this Vector3 source, float distance)
        {
            var d = source.magnitude;
            var ratio = (d + distance) / d;
            return source * ratio;
        }
        /// <summary>
        /// 传入一个坐标作为原点，获取一个在原点与当前坐标连线上并与当前坐标相隔<paramref name="distance"/>的坐标
        /// </summary>
        /// <param name="source"></param>
        /// <param name="distance"></param>
        /// <returns></returns>
        public static Vector3 GetPoint(this Vector3 source, Vector3 origin, float distance)
        {
            var v = source - origin;
            var d = v.magnitude;
            var ratio = (d + distance) / d;
            return origin + (v * ratio);
        }
    }
}