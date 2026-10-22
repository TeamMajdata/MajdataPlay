using System.Collections.Generic;
using System.IO;
using ShimSkiaSharp;

namespace Svg.Model {

    public class TypefaceSpan
    {
        public string Text { get; }
        public float Advance { get; }
        public SKTypeface Typeface { get; }

        public TypefaceSpan(string text, float advance, SKTypeface typeface)
        {
            Text = text;
            Advance = advance;
            Typeface = typeface; // Consider how you want to handle nulls here, given that .NET Framework 4.6 doesn't have nullable reference type support.
        }
    }

    public interface IAssetLoader
    {
        SKImage LoadImage(Stream stream);
        List<TypefaceSpan> FindTypefaces(string text, SKPaint paintPreferredTypeface); // Nullable reference types are not supported, so you need to handle null checks manually inside the method.
    }

}
