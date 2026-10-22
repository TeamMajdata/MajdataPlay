using UnityEngine;

namespace SkiaSharp.Unity.HB {
    [CreateAssetMenu(fileName = "New HBFont", menuName = "SkiaForUnity/HB Font Data")]
    [PreferBinarySerialization]
    public class HBFontData : ScriptableObject {
        [HideInInspector]
        public byte[] fontBytes;
    }
}
