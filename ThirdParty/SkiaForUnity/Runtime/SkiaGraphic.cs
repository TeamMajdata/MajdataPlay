using System;
using UnityEngine;
using UnityEngine.UI;

namespace SkiaSharp.Unity {

	public enum SkiaShapeType {
		Rectangle,
		RoundedRect,
		Circle,
		Ellipse
	}

	public enum SkiaFillType {
		None,
		Solid,
		LinearGradient,
		RadialGradient,
		SweepGradient,
		Image
	}

	public enum SkiaImageFit {
		Stretch,
		Fit,
		Fill,
		Tile
	}

	[AddComponentMenu("Skia UI (Canvas)/Skia Graphic")]
	[ExecuteAlways]
	public class SkiaGraphic : MonoBehaviour {

		[Header("Shape")]
		[SerializeField] protected SkiaShapeType shape = SkiaShapeType.RoundedRect;
		[SerializeField] protected Vector4 cornerRadii = new Vector4(16, 16, 16, 16);

		[Header("Fill")]
		[SerializeField] protected SkiaFillType fillType = SkiaFillType.Solid;
		[SerializeField] protected Color fillColor = Color.white;
		[SerializeField] protected Gradient gradient = new Gradient();
		[SerializeField, Range(0f, 360f)] protected float gradientAngle = 0f;
		[SerializeField] protected Texture2D fillImage;
		[SerializeField] protected SkiaImageFit imageFit = SkiaImageFit.Stretch;

		[Header("Stroke")]
		[SerializeField] protected bool enableStroke;
		[SerializeField] protected Color strokeColor = Color.black;
		[SerializeField, Range(0.5f, 20f)] protected float strokeWidth = 2f;
		[SerializeField] protected bool enableDashedStroke;
		[SerializeField, Range(1f, 50f)] protected float dashLength = 10f;
		[SerializeField, Range(1f, 50f)] protected float dashGap = 5f;
		[SerializeField] protected bool enableGradientStroke;
		[SerializeField] protected Gradient strokeGradient = new Gradient();
		[SerializeField, Range(0f, 360f)] protected float strokeGradientAngle = 0f;

		[Header("Shadow")]
		[SerializeField] protected bool enableShadow;
		[SerializeField] protected Color shadowColor = new Color(0, 0, 0, 0.3f);
		[SerializeField] protected Vector2 shadowOffset = new Vector2(0, 4);
		[SerializeField, Range(0f, 64f)] protected float shadowBlur = 8f;

		[Header("Inner Shadow")]
		[SerializeField] protected bool enableInnerShadow;
		[SerializeField] protected Color innerShadowColor = new Color(0, 0, 0, 0.4f);
		[SerializeField] protected Vector2 innerShadowOffset = new Vector2(0, 2);
		[SerializeField, Range(0f, 32f)] protected float innerShadowBlur = 4f;

		[Header("Performance")]
		[SerializeField, Range(0.25f, 1f)] protected float resolutionScale = 1f;

		[Header("Style")]
		[SerializeField] protected SkiaGraphicStyle stylePreset;

		[Header("Bake")]
		[SerializeField] protected Sprite bakedSprite;

		public bool IsBaked => bakedSprite != null;

		private RawImage rawImage;
		private Texture2D texture;
		private int lastWidth, lastHeight;
		private bool isDirty = true;

		// Child render target — extends beyond parent for shadow
		private GameObject renderChild;
		private RectTransform renderRT;
		private float lastPad;

		// Pooled paint — avoids GC alloc per draw call
		private SKPaint paint;

		// Image fill cache
		private SKBitmap cachedFillBitmap;
		private Texture2D cachedFillTexture;

		// Cached corner radii — avoid allocation per draw
		private readonly SKPoint[] cachedRadii = new SKPoint[4];

		// Reusable dash array
		private readonly float[] dashIntervals = new float[2];

		// Cached gradient arrays — avoid per-render allocation
		private SKColor[] cachedGradColors;
		private float[] cachedGradPositions;
		private SKColor[] cachedStrokeGradColors;
		private float[] cachedStrokeGradPositions;

		// Reusable pixel buffer for image fill
		private byte[] pixelBuffer;

		// --- Public API (UI proxy) ---

		private MaskableGraphic ActiveGraphic {
			get {
				if (bakedSprite != null) {
					var img = GetComponent<Image>();
					if (img != null) return img;
				}
				EnsureRenderChild();
				return rawImage;
			}
		}

		public float Alpha {
			get {
				var g = ActiveGraphic;
				return g != null ? g.color.a : 1f;
			}
			set {
				var g = ActiveGraphic;
				if (g != null) {
					Color c = g.color;
					c.a = value;
					g.color = c;
				}
			}
		}

		public bool RaycastTarget {
			get {
				var g = ActiveGraphic;
				return g != null && g.raycastTarget;
			}
			set {
				var g = ActiveGraphic;
				if (g != null) g.raycastTarget = value;
			}
		}

		public bool Maskable {
			get {
				var g = ActiveGraphic;
				return g != null && g.maskable;
			}
			set {
				var g = ActiveGraphic;
				if (g != null) g.maskable = value;
			}
		}

		// --- Public API ---

		public SkiaShapeType Shape {
			get => shape;
			set { shape = value; SetDirty(); }
		}

		public Vector4 CornerRadii {
			get => cornerRadii;
			set { cornerRadii = value; SetDirty(); }
		}

		public SkiaFillType FillType {
			get => fillType;
			set { fillType = value; SetDirty(); }
		}

		public Color FillColor {
			get => fillColor;
			set { fillColor = value; SetDirty(); }
		}

		public Texture2D FillImage {
			get => fillImage;
			set { fillImage = value; InvalidateFillBitmap(); SetDirty(); }
		}

		public SkiaImageFit ImageFit {
			get => imageFit;
			set { imageFit = value; SetDirty(); }
		}

		public bool EnableStroke {
			get => enableStroke;
			set { enableStroke = value; SetDirty(); }
		}

		public Color StrokeColor {
			get => strokeColor;
			set { strokeColor = value; SetDirty(); }
		}

		public float StrokeWidth {
			get => strokeWidth;
			set { strokeWidth = value; SetDirty(); }
		}

		public bool EnableDashedStroke {
			get => enableDashedStroke;
			set { enableDashedStroke = value; SetDirty(); }
		}

		public float DashLength {
			get => dashLength;
			set { dashLength = value; SetDirty(); }
		}

		public float DashGap {
			get => dashGap;
			set { dashGap = value; SetDirty(); }
		}

		public bool EnableGradientStroke {
			get => enableGradientStroke;
			set { enableGradientStroke = value; SetDirty(); }
		}

		public float StrokeGradientAngle {
			get => strokeGradientAngle;
			set { strokeGradientAngle = value; SetDirty(); }
		}

		public bool EnableShadow {
			get => enableShadow;
			set { enableShadow = value; SetDirty(); }
		}

		public Color ShadowColor {
			get => shadowColor;
			set { shadowColor = value; SetDirty(); }
		}

		public Vector2 ShadowOffset {
			get => shadowOffset;
			set { shadowOffset = value; SetDirty(); }
		}

		public float ShadowBlur {
			get => shadowBlur;
			set { shadowBlur = value; SetDirty(); }
		}

		public bool EnableInnerShadow {
			get => enableInnerShadow;
			set { enableInnerShadow = value; SetDirty(); }
		}

		public Color InnerShadowColor {
			get => innerShadowColor;
			set { innerShadowColor = value; SetDirty(); }
		}

		public Vector2 InnerShadowOffset {
			get => innerShadowOffset;
			set { innerShadowOffset = value; SetDirty(); }
		}

		public float InnerShadowBlur {
			get => innerShadowBlur;
			set { innerShadowBlur = value; SetDirty(); }
		}

		public float GradientAngle {
			get => gradientAngle;
			set { gradientAngle = value; SetDirty(); }
		}

		public float ResolutionScale {
			get => resolutionScale;
			set { resolutionScale = Mathf.Clamp(value, 0.25f, 1f); SetDirty(); }
		}

		public void SetDirty() {
			isDirty = true;
		}

		// --- Render Child Management ---

		private const string RenderChildName = "_SkiaRender";

		void EnsureRenderChild() {
			if (renderChild != null && rawImage != null) return;

			// Look for existing render child, clean up duplicates
			bool found = false;
			for (int i = transform.childCount - 1; i >= 0; i--) {
				var child = transform.GetChild(i);
				if (child.name == RenderChildName) {
					if (!found) {
						renderChild = child.gameObject;
						renderRT = child as RectTransform;
						rawImage = child.GetComponent<RawImage>();
						if (rawImage != null) found = true;
					} else {
						// Destroy duplicate
						if (Application.isPlaying)
							Destroy(child.gameObject);
						else
							DestroyImmediate(child.gameObject);
					}
				}
			}
			if (found) return;

			// Create new render child
			renderChild = new GameObject(RenderChildName, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
			renderChild.transform.SetParent(transform, false);
			renderChild.transform.SetAsFirstSibling();

			renderRT = renderChild.GetComponent<RectTransform>();
			renderRT.anchorMin = Vector2.zero;
			renderRT.anchorMax = Vector2.one;
			renderRT.offsetMin = Vector2.zero;
			renderRT.offsetMax = Vector2.zero;

			rawImage = renderChild.GetComponent<RawImage>();
			rawImage.raycastTarget = true;
		}

		void MigrateOldRawImage() {
			// If there's a RawImage on self (old approach), remove it
			var selfRI = GetComponent<RawImage>();
			if (selfRI != null) {
				if (Application.isPlaying)
					Destroy(selfRI);
				else
					DestroyImmediate(selfRI);
			}
		}

		void UpdateRenderChildPadding(float pad) {
			if (renderRT == null) return;
			if (Mathf.Approximately(pad, lastPad)) return;
			lastPad = pad;
			renderRT.offsetMin = new Vector2(-pad, -pad);
			renderRT.offsetMax = new Vector2(pad, pad);
		}

		float CalculateShadowPadding() {
			if (!enableShadow) return 0f;
			return shadowBlur + Mathf.Max(Mathf.Abs(shadowOffset.x), Mathf.Abs(shadowOffset.y));
		}

		// --- Lifecycle ---

		void OnEnable() {
			if (bakedSprite != null) {
				// Baked mode: use Image on self
				var selfRI = GetComponent<RawImage>();
				if (selfRI != null) selfRI.enabled = false;
				var img = GetComponent<Image>();
				if (img != null) {
					img.sprite = bakedSprite;
					img.color = Color.white;
				}
				if (renderChild != null) renderChild.SetActive(false);
				return;
			}

			MigrateOldRawImage();
			// Reset references so EnsureRenderChild re-scans (handles play/stop transitions)
			renderChild = null;
			renderRT = null;
			rawImage = null;
			lastPad = -1;
			EnsureRenderChild();

			if (renderChild != null) renderChild.SetActive(true);

#if UNITY_EDITOR
			if (rawImage != null)
				rawImage.hideFlags |= HideFlags.HideInInspector;
#endif

			paint = new SKPaint();
			SetDirty();
			Render();
		}

		void OnDisable() {
			Cleanup();
		}

		void OnDestroy() {
			Cleanup();
			// Destroy render child
			if (renderChild != null) {
				if (Application.isPlaying)
					Destroy(renderChild);
				else
					DestroyImmediate(renderChild);
				renderChild = null;
			}
		}

		private RectTransform _cachedRT;

		void Update() {
			if (rawImage == null) return;
			if (bakedSprite != null) return;

			// Cached: GetComponent per frame on every instance is waste.
			RectTransform rt = _cachedRT != null ? _cachedRT : (_cachedRT = GetComponent<RectTransform>());
			if (rt == null) return;

			int w = ScaledRoundTo4(rt.rect.width);
			int h = ScaledRoundTo4(rt.rect.height);

			if (isDirty || w != lastWidth || h != lastHeight) {
				Render();
			}
		}

		void OnValidate() {
			InvalidateFillBitmap();
			SetDirty();
		}

		// --- Rendering ---

		void Render() {
			RectTransform rt = _cachedRT != null ? _cachedRT : (_cachedRT = GetComponent<RectTransform>());
			if (rt == null) return;

			float shapeW = rt.rect.width;
			float shapeH = rt.rect.height;
			if (shapeW < 4 || shapeH < 4) return;

			// Calculate shadow padding — extends the surface beyond the shape
			float pad = CalculateShadowPadding();

			// Total surface includes padding on all sides
			int totalW = ScaledRoundTo4(shapeW + pad * 2);
			int totalH = ScaledRoundTo4(shapeH + pad * 2);
			if (totalW < 4 || totalH < 4) return;

			// Update render child to extend beyond parent by padding
			EnsureRenderChild();
			UpdateRenderChildPadding(pad);

			// Scale padding to match surface scaling
			float scaledPad = pad * resolutionScale;
			int scaledShapeW = ScaledRoundTo4(shapeW);
			int scaledShapeH = ScaledRoundTo4(shapeH);

			var info = new SKImageInfo(totalW, totalH, SKColorType.Rgba8888, SKAlphaType.Premul);
			using (var surface = SKSurface.Create(info)) {
				if (surface == null) return;

				var skCanvas = surface.Canvas;
				skCanvas.Clear(SKColors.Transparent);
				skCanvas.Scale(1, -1);
				skCanvas.Translate(0, -totalH);

				// Shape rect is at the center of the surface, full logical size
				float padX = (totalW - scaledShapeW) * 0.5f;
				float padY = (totalH - scaledShapeH) * 0.5f;
				SKRect shapeRect = new SKRect(padX, padY, padX + scaledShapeW, padY + scaledShapeH);

				// Half stroke goes outside the shape
				if (enableStroke) {
					float half = strokeWidth * 0.5f;
					shapeRect.Left += half;
					shapeRect.Top += half;
					shapeRect.Right -= half;
					shapeRect.Bottom -= half;
				}

				if (shapeRect.Width <= 0 || shapeRect.Height <= 0) return;

				// Update cached corner radii
				cachedRadii[0] = new SKPoint(cornerRadii.x, cornerRadii.x);
				cachedRadii[1] = new SKPoint(cornerRadii.y, cornerRadii.y);
				cachedRadii[2] = new SKPoint(cornerRadii.z, cornerRadii.z);
				cachedRadii[3] = new SKPoint(cornerRadii.w, cornerRadii.w);

				if (enableShadow) DrawShadow(skCanvas, shapeRect);
				if (fillType != SkiaFillType.None) DrawFill(skCanvas, shapeRect);
				if (enableInnerShadow) DrawInnerShadow(skCanvas, shapeRect);
				if (enableStroke) DrawStroke(skCanvas, shapeRect);

				// Extract pixels
				using (var pixmap = surface.PeekPixels()) {
					EnsureTexture(totalW, totalH);
					texture.LoadRawTextureData(pixmap.GetPixels(), pixmap.RowBytes * pixmap.Height);
					texture.Apply();
				}
			}

			if (rawImage != null) {
				rawImage.texture = texture;
				rawImage.color = Color.white;
			}

			lastWidth = ScaledRoundTo4(shapeW);
			lastHeight = ScaledRoundTo4(shapeH);
			isDirty = false;
		}

		void DrawShadow(SKCanvas canvas, SKRect shapeRect) {
			SKRect shadowRect = shapeRect;
			shadowRect.Offset(shadowOffset.x, shadowOffset.y);

			ResetPaint();
			paint.Style = SKPaintStyle.Fill;
			paint.Color = ToSKColor(shadowColor);
			if (shadowBlur > 0)
				paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, shadowBlur * 0.5f);

			DrawShape(canvas, shadowRect, paint);
			ClearPaintEffects();
		}

		void DrawInnerShadow(SKCanvas canvas, SKRect shapeRect) {
			canvas.Save();

			using (var clipPath = CreateShapePath(shapeRect)) {
				canvas.ClipPath(clipPath);
			}

			ResetPaint();
			paint.Style = SKPaintStyle.Fill;
			paint.Color = ToSKColor(innerShadowColor);
			if (innerShadowBlur > 0)
				paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, innerShadowBlur * 0.5f);

			float expand = innerShadowBlur * 2 + 50;
			SKRect outerRect = new SKRect(
				shapeRect.Left - expand,
				shapeRect.Top - expand,
				shapeRect.Right + expand,
				shapeRect.Bottom + expand
			);

			using (var shadowPath = new SKPath())
			using (var shapePath = CreateShapePath(shapeRect)) {
				shadowPath.AddRect(outerRect);
				shadowPath.AddPath(shapePath);
				shadowPath.FillType = SKPathFillType.EvenOdd;

				canvas.Translate(innerShadowOffset.x, innerShadowOffset.y);
				canvas.DrawPath(shadowPath, paint);
			}

			ClearPaintEffects();
			canvas.Restore();
		}

		void DrawFill(SKCanvas canvas, SKRect rect) {
			ResetPaint();
			paint.Style = SKPaintStyle.Fill;

			switch (fillType) {
				case SkiaFillType.Solid:
					paint.Color = ToSKColor(fillColor);
					break;
				case SkiaFillType.LinearGradient:
					paint.IsDither = true;
					paint.Shader = CreateLinearGradient(rect);
					break;
				case SkiaFillType.RadialGradient:
					paint.IsDither = true;
					paint.Shader = CreateRadialGradient(rect);
					break;
				case SkiaFillType.SweepGradient:
					paint.IsDither = true;
					paint.Shader = CreateSweepGradient(rect);
					break;
				case SkiaFillType.Image:
					paint.Shader = CreateImageShader(rect);
					if (paint.Shader == null)
						paint.Color = ToSKColor(fillColor);
					break;
			}

			DrawShape(canvas, rect, paint);
			ClearPaintEffects();
		}

		void DrawStroke(SKCanvas canvas, SKRect rect) {
			ResetPaint();
			paint.Style = SKPaintStyle.Stroke;
			paint.StrokeWidth = strokeWidth;

			if (enableGradientStroke) {
				paint.IsDither = true;
				paint.Shader = CreateStrokeGradient(rect);
			} else {
				paint.Color = ToSKColor(strokeColor);
			}

			if (enableDashedStroke) {
				dashIntervals[0] = dashLength;
				dashIntervals[1] = dashGap;
				paint.PathEffect = SKPathEffect.CreateDash(dashIntervals, 0);
			}

			DrawShape(canvas, rect, paint);
			ClearPaintEffects();
		}

		// Reset paint to clean state (reuses same object)
		void ResetPaint() {
			paint.Reset();
			paint.IsAntialias = true;
		}

		// Dispose any shader/effect/filter set on paint
		void ClearPaintEffects() {
			paint.Shader?.Dispose();
			paint.Shader = null;
			paint.PathEffect?.Dispose();
			paint.PathEffect = null;
			paint.MaskFilter?.Dispose();
			paint.MaskFilter = null;
		}

		void DrawShape(SKCanvas canvas, SKRect rect, SKPaint p) {
			switch (shape) {
				case SkiaShapeType.Rectangle:
					canvas.DrawRect(rect, p);
					break;

				case SkiaShapeType.RoundedRect:
					using (var rrect = new SKRoundRect()) {
						rrect.SetRectRadii(rect, cachedRadii);
						canvas.DrawRoundRect(rrect, p);
					}
					break;

				case SkiaShapeType.Circle:
					float radius = Mathf.Min(rect.Width, rect.Height) * 0.5f;
					canvas.DrawCircle(rect.MidX, rect.MidY, radius, p);
					break;

				case SkiaShapeType.Ellipse:
					canvas.DrawOval(rect, p);
					break;
			}
		}

		SKPath CreateShapePath(SKRect rect) {
			var path = new SKPath();
			switch (shape) {
				case SkiaShapeType.Rectangle:
					path.AddRect(rect);
					break;
				case SkiaShapeType.RoundedRect:
					using (var rrect = new SKRoundRect()) {
						rrect.SetRectRadii(rect, cachedRadii);
						path.AddRoundRect(rrect);
					}
					break;
				case SkiaShapeType.Circle:
					float radius = Mathf.Min(rect.Width, rect.Height) * 0.5f;
					path.AddCircle(rect.MidX, rect.MidY, radius);
					break;
				case SkiaShapeType.Ellipse:
					path.AddOval(rect);
					break;
			}
			return path;
		}

		// --- Gradient & Shader Creation ---

		SKShader CreateLinearGradient(SKRect rect) {
			float rad = gradientAngle * Mathf.Deg2Rad;
			float cx = rect.MidX, cy = rect.MidY;
			float halfDiag = Mathf.Sqrt(rect.Width * rect.Width + rect.Height * rect.Height) * 0.5f;
			float dx = Mathf.Cos(rad) * halfDiag;
			float dy = Mathf.Sin(rad) * halfDiag;

			var start = new SKPoint(cx - dx, cy - dy);
			var end = new SKPoint(cx + dx, cy + dy);

			GetGradientData(gradient, out SKColor[] colors, out float[] positions);
			return SKShader.CreateLinearGradient(start, end, colors, positions, SKShaderTileMode.Clamp);
		}

		SKShader CreateRadialGradient(SKRect rect) {
			float radius = Mathf.Max(rect.Width, rect.Height) * 0.5f;
			var center = new SKPoint(rect.MidX, rect.MidY);

			GetGradientData(gradient, out SKColor[] colors, out float[] positions);
			return SKShader.CreateRadialGradient(center, radius, colors, positions, SKShaderTileMode.Clamp);
		}

		SKShader CreateSweepGradient(SKRect rect) {
			var center = new SKPoint(rect.MidX, rect.MidY);

			GetGradientData(gradient, out SKColor[] colors, out float[] positions);
			return SKShader.CreateSweepGradient(center, colors, positions);
		}

		SKShader CreateStrokeGradient(SKRect rect) {
			float rad = strokeGradientAngle * Mathf.Deg2Rad;
			float cx = rect.MidX, cy = rect.MidY;
			float halfDiag = Mathf.Sqrt(rect.Width * rect.Width + rect.Height * rect.Height) * 0.5f;
			float dx = Mathf.Cos(rad) * halfDiag;
			float dy = Mathf.Sin(rad) * halfDiag;

			var start = new SKPoint(cx - dx, cy - dy);
			var end = new SKPoint(cx + dx, cy + dy);

			GetGradientData(strokeGradient, out SKColor[] colors, out float[] positions, isStroke: true);
			return SKShader.CreateLinearGradient(start, end, colors, positions, SKShaderTileMode.Clamp);
		}

		SKShader CreateImageShader(SKRect rect) {
			var bitmap = GetFillBitmap();
			if (bitmap == null) return null;

			float imgW = bitmap.Width;
			float imgH = bitmap.Height;
			float rectW = rect.Width;
			float rectH = rect.Height;

			float scaleX, scaleY, tx, ty;

			switch (imageFit) {
				default:
				case SkiaImageFit.Stretch:
					scaleX = rectW / imgW;
					scaleY = rectH / imgH;
					tx = rect.Left;
					ty = rect.Top;
					break;
				case SkiaImageFit.Fit: {
					float s = Mathf.Min(rectW / imgW, rectH / imgH);
					scaleX = scaleY = s;
					tx = rect.Left + (rectW - imgW * s) * 0.5f;
					ty = rect.Top + (rectH - imgH * s) * 0.5f;
					break;
				}
				case SkiaImageFit.Fill: {
					float s = Mathf.Max(rectW / imgW, rectH / imgH);
					scaleX = scaleY = s;
					tx = rect.Left + (rectW - imgW * s) * 0.5f;
					ty = rect.Top + (rectH - imgH * s) * 0.5f;
					break;
				}
				case SkiaImageFit.Tile:
					scaleX = scaleY = 1f;
					tx = rect.Left;
					ty = rect.Top;
					break;
			}

			var matrix = SKMatrix.CreateScaleTranslation(scaleX, scaleY, tx, ty);

			var tileMode = imageFit == SkiaImageFit.Tile ? SKShaderTileMode.Repeat : SKShaderTileMode.Clamp;
			return SKShader.CreateBitmap(bitmap, tileMode, tileMode, matrix);
		}

		void GetGradientData(Gradient grad, out SKColor[] colors, out float[] positions, bool isStroke = false) {
			if (grad == null || grad.colorKeys.Length < 2) {
				colors = new[] { SKColors.White, SKColors.Black };
				positions = new[] { 0f, 1f };
				return;
			}

			int count = grad.colorKeys.Length;

			// Reuse cached arrays if size matches
			ref SKColor[] cachedC = ref (isStroke ? ref cachedStrokeGradColors : ref cachedGradColors);
			ref float[] cachedP = ref (isStroke ? ref cachedStrokeGradPositions : ref cachedGradPositions);

			if (cachedC == null || cachedC.Length != count)
				cachedC = new SKColor[count];
			if (cachedP == null || cachedP.Length != count)
				cachedP = new float[count];

			for (int i = 0; i < count; i++) {
				var key = grad.colorKeys[i];
				float alpha = grad.Evaluate(key.time).a;
				cachedC[i] = new SKColor(
					(byte)(key.color.r * 255),
					(byte)(key.color.g * 255),
					(byte)(key.color.b * 255),
					(byte)(alpha * 255)
				);
				cachedP[i] = key.time;
			}

			colors = cachedC;
			positions = cachedP;
		}

		// --- Image Fill Bitmap ---

		SKBitmap GetFillBitmap() {
			if (fillImage == null) {
				InvalidateFillBitmap();
				return null;
			}

			if (cachedFillBitmap != null && cachedFillTexture == fillImage)
				return cachedFillBitmap;

			InvalidateFillBitmap();
			cachedFillTexture = fillImage;

			try {
				int w = fillImage.width;
				int h = fillImage.height;

				Color32[] pixels = GetReadablePixels(fillImage);
				if (pixels == null) return null;

				cachedFillBitmap = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
				IntPtr ptr = cachedFillBitmap.GetPixels();

				// Flip vertically (Unity bottom-to-top → Skia top-to-bottom)
				int byteSize = w * h * 4;
				if (pixelBuffer == null || pixelBuffer.Length < byteSize)
					pixelBuffer = new byte[byteSize];
				for (int y = 0; y < h; y++) {
					int srcRow = h - 1 - y;
					int srcBase = srcRow * w;
					int dstBase = y * w;
					for (int x = 0; x < w; x++) {
						Color32 c = pixels[srcBase + x];
						int idx = (dstBase + x) * 4;
						pixelBuffer[idx] = c.r;
						pixelBuffer[idx + 1] = c.g;
						pixelBuffer[idx + 2] = c.b;
						pixelBuffer[idx + 3] = c.a;
					}
				}

				System.Runtime.InteropServices.Marshal.Copy(pixelBuffer, 0, ptr, byteSize);
			} catch (Exception e) {
				Debug.LogError($"SkiaGraphic: Failed to read fill image: {e.Message}");
				cachedFillBitmap?.Dispose();
				cachedFillBitmap = null;
				cachedFillTexture = null;
			}

			return cachedFillBitmap;
		}

		static Color32[] GetReadablePixels(Texture2D tex) {
			if (tex.isReadable)
				return tex.GetPixels32();

			RenderTexture prev = RenderTexture.active;
			RenderTexture tmp = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.Default);
			Graphics.Blit(tex, tmp);
			RenderTexture.active = tmp;

			Texture2D readable = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
			readable.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
			readable.Apply();

			RenderTexture.active = prev;
			RenderTexture.ReleaseTemporary(tmp);

			Color32[] pixels = readable.GetPixels32();

			if (Application.isPlaying)
				Destroy(readable);
			else
				DestroyImmediate(readable);

			return pixels;
		}

		void InvalidateFillBitmap() {
			if (cachedFillBitmap != null) {
				cachedFillBitmap.Dispose();
				cachedFillBitmap = null;
			}
			cachedFillTexture = null;
		}

		// --- Texture Management ---

		void EnsureTexture(int w, int h) {
			if (texture != null && (texture.width != w || texture.height != h)) {
				if (Application.isPlaying)
					Destroy(texture);
				else
					DestroyImmediate(texture);
				texture = null;
			}

			if (texture == null) {
				texture = new Texture2D(w, h, TextureFormat.RGBA32, false) {
					wrapMode = TextureWrapMode.Clamp,
					filterMode = FilterMode.Bilinear
				};
			}
		}

		void Cleanup() {
			InvalidateFillBitmap();
			pixelBuffer = null;
			cachedGradColors = null;
			cachedGradPositions = null;
			cachedStrokeGradColors = null;
			cachedStrokeGradPositions = null;

			if (paint != null) {
				ClearPaintEffects();
				paint.Dispose();
				paint = null;
			}

			if (texture != null) {
				if (Application.isPlaying)
					Destroy(texture);
				else
					DestroyImmediate(texture);
				texture = null;
			}

			if (rawImage != null)
				rawImage.texture = null;
		}

		// --- Helpers ---

		int ScaledRoundTo4(float v) {
			int scaled = Mathf.CeilToInt(v * resolutionScale);
			return Mathf.Max(4, (scaled + 3) & ~3);
		}

		static SKColor ToSKColor(Color c) =>
			new SKColor((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)(c.a * 255));

#if UNITY_EDITOR
		/// <summary>
		/// Renders at full resolution and returns PNG bytes. Editor-only.
		/// </summary>
		public byte[] BakeToTexture() {
			if (paint == null) paint = new SKPaint();

			float savedScale = resolutionScale;
			resolutionScale = 1f;
			isDirty = true;

			Render();

			byte[] png = texture != null ? texture.EncodeToPNG() : null;

			resolutionScale = savedScale;
			isDirty = true;

			return png;
		}

		/// <summary>
		/// Calculates nine-slice border (left, bottom, right, top) in pixels for the baked sprite.
		/// Returns zero for shapes/fills that nine-slice would distort.
		/// </summary>
		public Vector4 GetNineSliceBorder() {
			// Nine-slice distorts circles, ellipses, gradients, and image fills
			if (shape == SkiaShapeType.Circle || shape == SkiaShapeType.Ellipse)
				return Vector4.zero;
			if (fillType != SkiaFillType.Solid && fillType != SkiaFillType.None)
				return Vector4.zero;

			RectTransform rt = GetComponent<RectTransform>();
			if (rt == null) return Vector4.zero;

			float pad = CalculateShadowPadding();
			float halfStroke = enableStroke ? strokeWidth * 0.5f : 0;

			float cornerTL = shape == SkiaShapeType.RoundedRect ? cornerRadii.x : 0;
			float cornerTR = shape == SkiaShapeType.RoundedRect ? cornerRadii.y : 0;
			float cornerBR = shape == SkiaShapeType.RoundedRect ? cornerRadii.z : 0;
			float cornerBL = shape == SkiaShapeType.RoundedRect ? cornerRadii.w : 0;

			float left   = pad + halfStroke + Mathf.Max(cornerTL, cornerBL) + 2;
			float right  = pad + halfStroke + Mathf.Max(cornerTR, cornerBR) + 2;
			float top    = pad + halfStroke + Mathf.Max(cornerTL, cornerTR) + 2;
			float bottom = pad + halfStroke + Mathf.Max(cornerBL, cornerBR) + 2;

			// Clamp borders to not exceed half the texture dimensions
			float w = rt.rect.width;
			float h = rt.rect.height;
			float maxH = w * 0.45f;
			float maxV = h * 0.45f;
			left   = Mathf.Min(left, maxH);
			right  = Mathf.Min(right, maxH);
			top    = Mathf.Min(top, maxV);
			bottom = Mathf.Min(bottom, maxV);

			return new Vector4(left, bottom, right, top);
		}
#endif
	}
}
