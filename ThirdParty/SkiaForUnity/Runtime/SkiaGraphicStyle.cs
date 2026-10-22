using UnityEngine;

namespace SkiaSharp.Unity {

	[CreateAssetMenu(fileName = "SkiaGraphicStyle", menuName = "Skia UI/Graphic Style Preset")]
	public class SkiaGraphicStyle : ScriptableObject {
		// Shape
		public SkiaShapeType shape = SkiaShapeType.RoundedRect;
		public Vector4 cornerRadii = new Vector4(16, 16, 16, 16);

		// Fill
		public SkiaFillType fillType = SkiaFillType.Solid;
		public Color fillColor = Color.white;
		public Gradient gradient = new Gradient();
		public float gradientAngle;
		public Texture2D fillImage;
		public SkiaImageFit imageFit = SkiaImageFit.Stretch;

		// Stroke
		public bool enableStroke;
		public Color strokeColor = Color.black;
		public float strokeWidth = 2f;
		public bool enableDashedStroke;
		public float dashLength = 10f;
		public float dashGap = 5f;
		public bool enableGradientStroke;
		public Gradient strokeGradient = new Gradient();
		public float strokeGradientAngle;

		// Shadow
		public bool enableShadow;
		public Color shadowColor = new Color(0, 0, 0, 0.3f);
		public Vector2 shadowOffset = new Vector2(0, 4);
		public float shadowBlur = 8f;

		// Inner Shadow
		public bool enableInnerShadow;
		public Color innerShadowColor = new Color(0, 0, 0, 0.4f);
		public Vector2 innerShadowOffset = new Vector2(0, 2);
		public float innerShadowBlur = 4f;

		// Performance
		public float resolutionScale = 1f;

		// Bake
		public Sprite bakedSprite;
	}
}
