using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Topten.RichTextKit;
using TextAlignment = Topten.RichTextKit.TextAlignment;

namespace SkiaSharp.Unity.HB {
	public enum HBColorFormat {
		alpha8,
		rgb32
	}

	[AddComponentMenu("Skia UI (Canvas)/HB TextBlock")]
	public class HB_TEXTBlock : MonoBehaviour, ILayoutElement {
		[SerializeField]
		[TextArea]
		protected string Text;
		[SerializeField]
		public UnityEngine.Object font;
		[SerializeField]
		protected int fontSize = 20, letterSpacing, maxLines;
		[SerializeField]
		[Range (0,100)]
		protected int outlineWidth, outlineBlur;
		[SerializeField]
		[Range (0,10)]
		protected int shadowWidth;
		[SerializeField]
		[Range (0,1)]
		protected int innerGlowWidth = 0;
		[SerializeField]
		[Range (-100,100)]
		protected float shadowOffsetX, shadowOffsetY = 1;
		[SerializeField]
		protected Color fontColor = Color.black, innerGlowColor = Color.white, backgroundColor = Color.clear,linkColor = Color.blue;
		[SerializeField]
		protected Gradient shadowGradientColor = new(), outlineColor = new ();
		[SerializeField]
		protected bool italic, bold, autoFitVertical = true, autoFitHorizontal = true, renderLinks, enableGradiant, enableEllipsis = true, richText;
		[SerializeField]
		protected UnderlineStyle underlineStyle;
		[SerializeField]
		protected StrikeThroughStyle strikeThroughStyle;
		[SerializeField]
		protected float lineHeight = 1.0f, maxWidth = -1, maxHeight = -1 , gradiantAngle = 90;
		[SerializeField]
		protected HBColorFormat colorType = HBColorFormat.rgb32;
		[SerializeField]
		protected TextAlignment textAlignment = TextAlignment.Center;
		[SerializeField]
		protected Color[] gradiantColors;
		[SerializeField]
		protected float[] gradiantPositions;
		[SerializeField]
		protected TextDirection textDirection = TextDirection.Auto;

		public virtual TextDirection TextDir {
			get => textDirection;
			set {
				if (textDirection != value) {
					textDirection = value;
					_renderDirty = true;
				}
			}
		}
		[SerializeField]
		[Range(100, 900)]
		protected int fontWeight = 400;
		[SerializeField]
		protected FontVariant fontVariant = FontVariant.Normal;
		[SerializeField]
		protected Vector4 padding; // left, top, right, bottom
		[SerializeField]
		protected HBFontData[] fallbackFonts;
		[SerializeField]
		[Range(0, 50)]
		protected float paragraphSpacing;

		[Header("Background Shape")]
		[SerializeField] protected bool enableBgFill;
		[SerializeField] protected SkiaFillType bgFillType = SkiaFillType.Solid;
		[SerializeField] protected Color bgFillColor = Color.white;
		[SerializeField] protected Gradient bgGradient = new Gradient();
		[SerializeField, Range(0f, 360f)] protected float bgGradientAngle = 0f;
		[SerializeField] protected Vector4 bgCornerRadii = new Vector4(16, 16, 16, 16);
		[SerializeField] protected Vector4 bgPadding = new Vector4(8, 8, 8, 8); // left, top, right, bottom

		[SerializeField] protected bool enableBgStroke;
		[SerializeField] protected Color bgStrokeColor = Color.black;
		[SerializeField, Range(0.5f, 20f)] protected float bgStrokeWidth = 2f;

		[SerializeField] protected bool enableBgShadow;
		[SerializeField] protected Color bgShadowColor = new Color(0, 0, 0, 0.3f);
		[SerializeField] protected Vector2 bgShadowOffset = new Vector2(0, 4);
		[SerializeField, Range(0f, 64f)] protected float bgShadowBlur = 8f;

		[SerializeField] protected bool enableBgInnerShadow;
		[SerializeField] protected Color bgInnerShadowColor = new Color(0, 0, 0, 0.4f);
		[SerializeField] protected Vector2 bgInnerShadowOffset = new Vector2(0, 2);
		[SerializeField, Range(0f, 32f)] protected float bgInnerShadowBlur = 4f;

		[Header("Bake")]
		[SerializeField] protected Sprite bakedSprite;

		public UnityEvent onTextChanged = new();
		public UnityEvent<string> onLinkClicked = new();

		protected SKCanvas canvas;
		protected SKImageInfo info = new();
		protected SKSurface surface;
		protected SKPixmap pixmap;
		protected RawImage rawImage;
		protected Texture2D texture;
		protected TextBlock rs;
		protected Dictionary<int, HBLinks> urls = new();
		protected Style styleBoldItalic = new();
		protected string pattern = @"(https?://\S+|www\.\S+)";
		protected Regex regex;
		protected SKTypeface skTypeface;
		private static readonly Dictionary<EntityId, SKTypeface> _typefaceCache = new();
		private static readonly Dictionary<EntityId, int> _typefaceRefCounts = new();
		private EntityId _typefaceCacheKey;
		private bool _typefaceCacheRegistered;
		protected RectTransform rectTransform;
		protected float currentWidth, currentHeight, currentPreferdWidth = 0, currentPreferdHeight;
		protected TextGradient blockGradient;
		protected bool widthPreferred, heightPreferred;

		// Cached objects to avoid per-render allocations
		private FontMapper _cachedFontMapper;
		private EntityId _cachedFontMapperKey;
		private Style _linkStyle;
		private Style _cachedSpacerStyle;
		private float _cachedSpacerFontSize;
		private static readonly Gradient _emptyGradient = new();

		// Cached gradient to avoid per-render allocation
		private SKColor[] _cachedGradColors;
		private int _cachedGradColorHash;
		private TextPaintOptions _cachedPaintOptions;

		// Background shape rendering
		private SKPaint _bgPaint;
		private readonly SKPoint[] _bgCachedRadii = new SKPoint[4];
		private SKColor[] _bgCachedGradColors;
		private float[] _bgCachedGradPositions;

		// Layout rebuild tracking
		private float _lastLayoutWidth, _lastLayoutHeight;

		// Dirty flag — coalesces multiple property changes into a single render per frame
		private bool _renderDirty;

		public bool IsBaked => bakedSprite != null;

		private MaskableGraphic ActiveGraphic {
			get {
				if (bakedSprite != null) {
					var img = GetComponent<Image>();
					if (img != null) return img;
				}
				if (rawImage == null) rawImage = GetComponent<RawImage>();
				return rawImage;
			}
		}

		public virtual TextBlock Info => rs;

		public enum VerticalAlignment { Top, Middle, Bottom }
		[SerializeField]
		protected VerticalAlignment verticalAlignment = VerticalAlignment.Top; // Default alignment

		public virtual VerticalAlignment VAlign {
			get => verticalAlignment;
			set {
				if (verticalAlignment != value) {
					verticalAlignment = value;
					_renderDirty = true;
				}
			}
		}


		public virtual float MaxWidth {
			get => maxWidth;
			set {
				maxWidth = value;
				if (rawImage == null) {
					rawImage = GetComponent<RawImage>();
					rectTransform = transform as RectTransform;
				}
				_renderDirty = true;
			}
		}

		public virtual float MaxHeight {
			get => maxHeight;
			set {
				maxHeight = value;
				if (rawImage == null) {
					rawImage = GetComponent<RawImage>();
					rectTransform = transform as RectTransform;
				}
				_renderDirty = true;
			}
		}

		public virtual string text {
			get => Text;
			set {
				// Unchanged text must NOT re-shape/re-rasterize. Game timers assign
				// the same string every frame (60x/s) — without this guard each
				// assignment forced a full Skia render = constant main-thread burn
				// on device. Mirrors the != guards on every other property here.
				if (Text == value) return;
				Text = value;
				ReUpdate();
				onTextChanged?.Invoke();
			}
		}

		public virtual void SetRichText(string value) {
			richText = true;
			Text = value;
			ReUpdate();
			onTextChanged?.Invoke();
		}

		public virtual Color FontColor {
			get {
				return fontColor;
			}
			set {
				if (value != fontColor) {
					fontColor = value;
					styleBoldItalic.TextColor = new SKColor(ColorToUint(fontColor));
					ReUpdate();
				}
			}
		}

		public float alpha {
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

		public virtual Gradient HaloColor {
			get {
				return outlineColor;
			}
			set {
				outlineColor = value;
				ReUpdate();
			}
		}

		public virtual Gradient ShadowGradientColor {
			get => shadowGradientColor;
			set {
				shadowGradientColor = value;
				ReUpdate();
			}
		}

		public virtual Color InnerGlowColor {
			get {
				return innerGlowColor;
			}
			set {
				innerGlowColor = value;
				ReUpdate();
			}
		}

		public virtual Color BackgroundColor {
			get {
				return backgroundColor;
			}
			set {
				backgroundColor = value;
				ReUpdate();
			}
		}

		public virtual HBColorFormat ColorType {
			get {
				return colorType;
			}
			set {
				colorType = value;
				ReUpdate();
			}
		}

		public virtual bool AutoFitVertical {
			get {
				return autoFitVertical;
			}
			set {
				autoFitVertical = value;
				ReUpdate();
			}
		}

		public virtual bool AutoFitHorizontal {
			get {
				return autoFitHorizontal;
			}
			set {
				autoFitHorizontal = value;
				ReUpdate();
			}
		}

		public virtual bool IsGradiantEnabled {
			get {
				return enableGradiant;
			}
			set {
				enableGradiant = value;
				ReUpdate();
			}
		}
		public virtual bool RenderLinks {
			get {
				return renderLinks;
			}
			set {
				renderLinks = value;
				ReUpdate();
			}
		}

		public virtual UnityEngine.Object Font {
			get {
				return font;
			}
			set {
				font = value;
				ReUpdate();
			}
		}

		protected byte[] GetFontBytes() {
			if (font is HBFontData hb) return hb.fontBytes;
			if (font is TextAsset ta) return ta.bytes;
			return null;
		}

		public virtual bool Bold {
			get {
				return bold;
			}
			set {
				bold = value;
				ReUpdate();
			}
		}

		public virtual bool Italic {
			get {
				return italic;
			}
			set {
				italic = value;
				ReUpdate();
			}
		}

		public virtual int FontSize {
			get {
				return fontSize;
			}
			set {
				fontSize = value;
				ReUpdate();
			}
		}

		public virtual int HaloWidth {
			get {
				return outlineWidth;
			}
			set {
				outlineWidth = value;
				ReUpdate();
			}
		}

		public virtual int ShadowWidth {
			get {
				return shadowWidth;
			}
			set {
				shadowWidth = value;
				ReUpdate();
			}
		}

		public virtual int InnerGlowWidth {
			get {
				return innerGlowWidth;
			}
			set {
				innerGlowWidth = value;
				ReUpdate();
			}
		}

		public virtual int LetterSpacing {
			get {
				return letterSpacing;
			}
			set {
				letterSpacing = value;
				ReUpdate();
			}
		}

		public virtual int HaloBlur {
			get {
				return outlineBlur;
			}
			set {
				outlineBlur = value;
				ReUpdate();
			}
		}

		public virtual UnderlineStyle UnderLineStyle {
			get {
				return underlineStyle;
			}
			set {
				underlineStyle = value;
				ReUpdate();
			}
		}

		public virtual StrikeThroughStyle  StrikeThroughStyle{
			get {
				return strikeThroughStyle;
			}
			set {
				strikeThroughStyle = value;
				ReUpdate();
			}
		}

		public virtual float LineHeight{
			get {
				return lineHeight;
			}
			set {
				lineHeight = value;
				ReUpdate();
			}
		}

		public virtual TextAlignment TextAlignment{
			get {
				return textAlignment;
			}
			set {
				if (textAlignment != value) {
					textAlignment = value;
					ReUpdate();
				}
			}
		}

		protected virtual void Awake() {
			rectTransform = transform as RectTransform;

			if (bakedSprite != null) {
				// Baked mode — no Skia rendering needed
				rawImage = GetComponent<RawImage>();
				if (rawImage != null) rawImage.enabled = false;
				return;
			}

			rawImage = GetComponent<RawImage>();
			if (rawImage == null) {
				var existingGraphic = GetComponent<Graphic>();
				if (existingGraphic != null)
					DestroyImmediate(existingGraphic);
				rawImage = gameObject.AddComponent<RawImage>();
			}
			if (rawImage) {
				rawImage.enabled = false;
				#if UNITY_EDITOR
				rawImage.hideFlags |= HideFlags.HideInInspector;
				#endif
			}

			styleBoldItalic.FontSize = fontSize;
			styleBoldItalic.TextColor = new SKColor(ColorToUint(fontColor));
			styleBoldItalic.HaloWidth = outlineWidth;
			styleBoldItalic.ShadowWidth = shadowWidth;
			styleBoldItalic.HaloColor = outlineWidth > 0 ? outlineColor : _emptyGradient;
			styleBoldItalic.ShadowGradientColor = shadowGradientColor != null && shadowGradientColor.colorKeys.Length > 0
				? shadowGradientColor
				: _emptyGradient;
			styleBoldItalic.ShadowOffsetX = shadowOffsetX;
			styleBoldItalic.ShadowOffsetY = shadowOffsetY;
			styleBoldItalic.InnerGlowWidth = innerGlowWidth;
			styleBoldItalic.InnerGlowColor = innerGlowWidth > 0 ? new SKColor(ColorToUint(innerGlowColor)) : SKColor.Empty;

			styleBoldItalic.FontItalic = italic;
			styleBoldItalic.FontWeight = bold ? 700 : fontWeight;
			styleBoldItalic.LetterSpacing = letterSpacing;
			styleBoldItalic.TextDirection = textDirection;
			styleBoldItalic.HaloBlur = outlineBlur;
			styleBoldItalic.BackgroundColor = backgroundColor.a > 0 ? new SKColor(ColorToUint(backgroundColor)) : SKColors.Empty;
			styleBoldItalic.Underline = underlineStyle;
			styleBoldItalic.LineHeight = lineHeight;
			styleBoldItalic.StrikeThrough = strikeThroughStyle;
			styleBoldItalic.FontVariant = fontVariant;
		}

		protected virtual void OnEnable() {
			if (bakedSprite != null) {
				if (rawImage != null) rawImage.enabled = false;
				var img = GetComponent<Image>();
				if (img != null) {
					img.sprite = bakedSprite;
					img.color = Color.white;
				}
				return;
			}

			if (String.IsNullOrEmpty(Text)){
				return;
			}

			if (rawImage == null)
				rawImage = GetComponent<RawImage>();
			if (rawImage) {
				urls.Clear();
				RenderText();
			}
		}

		protected bool textRendered;


		// Convert a Color to a uint
		public virtual uint ColorToUint(Color color){
			uint alpha = (uint)(color.a * 255);
			uint red = (uint)(color.r * 255);
			uint green = (uint)(color.g * 255);
			uint blue = (uint)(color.b * 255);
			return (alpha << 24) | (red << 16) | (green << 8) | blue;
		}

		protected virtual void RenderText() {
			if (rawImage && String.IsNullOrEmpty(text)) {
				rawImage.enabled = false;
			}

			// Dispose only the pixmap from previous render; keep surface for reuse
			if (pixmap != null) {
				pixmap.Dispose();
				pixmap = null;
			}

			if (rs == null) {
				rs = new TextBlock();
			}
			rs.Clear();
			rs.MaxHeight = null;
			rs.MaxWidth = null;
			rs.MaxLines = maxLines == 0 ? null : maxLines;
			if (rawImage != null) {
				if (colorType == HBColorFormat.alpha8) {
					rawImage.color = fontColor;
				} else {
					rawImage.color = Color.white;
				}
			}
			rs.Alignment = textAlignment;
			rs.BaseDirection = textDirection;
			rs.EllipsisEnabled = enableEllipsis;
			if (paragraphSpacing > 0 && Text.Contains("\n")) {
				string[] paragraphs = Text.Split('\n');
				if (_cachedSpacerStyle == null || _cachedSpacerFontSize != paragraphSpacing) {
					_cachedSpacerStyle = styleBoldItalic.Modify(fontSize: paragraphSpacing, lineHeight: 1f);
					_cachedSpacerFontSize = paragraphSpacing;
				}
				var spacerStyle = _cachedSpacerStyle;
				for (int i = 0; i < paragraphs.Length; i++) {
					if (i > 0) {
						rs.AddText("\n", styleBoldItalic);
						rs.AddText("\n", spacerStyle);
					}
					if (string.IsNullOrEmpty(paragraphs[i])) continue;
					if (richText) {
						HBRichTextParser.Parse(rs, paragraphs[i], styleBoldItalic);
					} else {
						rs.AddText(paragraphs[i], styleBoldItalic);
					}
				}
			} else if (richText) {
				HBRichTextParser.Parse(rs, Text, styleBoldItalic);
			} else {
				rs.AddText(Text, styleBoldItalic);
			}

			if (renderLinks) {
				RenderLinksCall();
			}

			if (font != null) {
				var fontBytes = GetFontBytes();
				if (fontBytes == null) return;
				if (skTypeface == null) {
					EntityId key = font.GetEntityId();
					if (!_typefaceCache.ContainsKey(key)) {
						SKData copy = SKData.CreateCopy(fontBytes);
						_typefaceCache[key] = SKTypeface.FromData(copy);
						copy.Dispose();
						_typefaceRefCounts[key] = 0;
					}
					skTypeface = _typefaceCache[key];
					_typefaceCacheKey = key;
					_typefaceRefCounts[key]++;
					_typefaceCacheRegistered = true;
				}
				// Reuse FontMapper if typeface hasn't changed
				EntityId typefaceKey = font.GetEntityId();
				if (_cachedFontMapper == null || _cachedFontMapperKey != typefaceKey) {
					_cachedFontMapper = new FontMapper(skTypeface);
					_cachedFontMapperKey = typefaceKey;
				}
				rs.FontMapper = _cachedFontMapper;
			} else {
				// Reset to default FontMapper so system fonts respect weight/italic
				rs.FontMapper = null;
			}

			if ( (!autoFitHorizontal && rectTransform.rect.width == 0)) {
				return;
			}

			// Calculate extra padding needed for effects (outline, blur, shadow) + user padding
			float effectPadX = 0f;
			float effectPadY = 0f;
			if (outlineWidth > 0) {
				float outlineExtra = outlineWidth + outlineBlur;
				effectPadX = Mathf.Max(effectPadX, outlineExtra);
				effectPadY = Mathf.Max(effectPadY, outlineExtra);
			}
			if (shadowWidth > 0) {
				effectPadX = Mathf.Max(effectPadX, shadowWidth + Mathf.Abs(shadowOffsetX));
				effectPadY = Mathf.Max(effectPadY, shadowWidth + Mathf.Abs(shadowOffsetY));
			}
			// Add background shape shadow padding
			if (enableBgFill && enableBgShadow) {
				effectPadX = Mathf.Max(effectPadX, bgShadowBlur + Mathf.Abs(bgShadowOffset.x));
				effectPadY = Mathf.Max(effectPadY, bgShadowBlur + Mathf.Abs(bgShadowOffset.y));
			}
			// Add user-defined padding + background padding (left, top, right, bottom)
			float userPadL = padding.x + (enableBgFill ? bgPadding.x : 0);
			float userPadT = padding.y + (enableBgFill ? bgPadding.y : 0);
			float userPadR = padding.z + (enableBgFill ? bgPadding.z : 0);
			float userPadB = padding.w + (enableBgFill ? bgPadding.w : 0);
			effectPadX += (userPadL + userPadR) / 2f;
			effectPadY += (userPadT + userPadB) / 2f;
			float padW = effectPadX * 2f;
			float padH = effectPadY * 2f;

			// --- Width ---
			if (autoFitHorizontal) {
				// Measure unconstrained (MaxWidth is null from line 483)
				float naturalW = rs.MeasuredWidth;
				float neededW = naturalW + padW;
				if (maxWidth > -1 && neededW > maxWidth) {
					// Text exceeds user's max limit — constrain
					currentPreferdWidth = maxWidth;
					rs.MaxWidth = Mathf.Max(0, maxWidth - padW);
				} else {
					// Text fits — size rect to content, add small buffer to MaxWidth
					// to prevent floating-point precision issues from truncating
					// single-word text (no break points) to 0 lines.
					currentPreferdWidth = neededW;
					rs.MaxWidth = Mathf.Ceil(naturalW) + 2;
				}
			} else {
				currentPreferdWidth = rectTransform.rect.width;
				rs.MaxWidth = Mathf.Max(0, currentPreferdWidth - padW);
			}

			// --- Height ---
			// Capture measured height BEFORE setting MaxHeight (which can trigger truncation)
			float measuredH = rs.MeasuredHeight;
			if (autoFitVertical) {
				float neededH = measuredH + padH;
				if (maxHeight > -1 && neededH > maxHeight) {
					currentPreferdHeight = maxHeight;
				} else {
					currentPreferdHeight = neededH;
				}
			} else {
				currentPreferdHeight = rectTransform.rect.height;
			}

			if (autoFitVertical && maxHeight <= -1) {
				rs.MaxHeight = null;
			} else if (autoFitVertical) {
				if (maxHeight > -1 && measuredH + padH > maxHeight) {
					rs.MaxHeight = Mathf.Max(0, maxHeight - padH);
				} else {
					// Add buffer to prevent exact-match truncation
					rs.MaxHeight = Mathf.Ceil(measuredH) + 2;
				}
			} else {
				rs.MaxHeight = Mathf.Max(0, currentPreferdHeight - padH);
			}

			if (autoFitVertical) {
				float fitHeight = measuredH + padH;
				var size = autoFitHorizontal
					? new Vector2(currentPreferdWidth, fitHeight)
					: new Vector2(rectTransform.rect.width, fitHeight);

				if (size.x == 0) return;

				// Only set sizeDelta if the RectTransform is not in stretch mode for that axis
				if (rectTransform.anchorMin.x == rectTransform.anchorMax.x) {
					rectTransform.sizeDelta = new Vector2(size.x, rectTransform.sizeDelta.y);
				}
				if (rectTransform.anchorMin.y == rectTransform.anchorMax.y) {
					rectTransform.sizeDelta = new Vector2(rectTransform.sizeDelta.x, size.y);
				}
			} else {
				var size = autoFitHorizontal
					? new Vector2(currentPreferdWidth, rectTransform.rect.height)
					: new Vector2(rectTransform.rect.width, rectTransform.rect.height);

				if (rectTransform.anchorMin.x == rectTransform.anchorMax.x) {
					rectTransform.sizeDelta = new Vector2(size.x, rectTransform.sizeDelta.y);
				}
				if (rectTransform.anchorMin.y == rectTransform.anchorMax.y) {
					rectTransform.sizeDelta = new Vector2(rectTransform.sizeDelta.x, size.y);
				}
			}


			currentWidth = rectTransform.rect.width;
			currentHeight = rectTransform.rect.height;
			// Only trigger layout rebuild when size actually changed
			if (currentWidth != _lastLayoutWidth || currentHeight != _lastLayoutHeight) {
				_lastLayoutWidth = currentWidth;
				_lastLayoutHeight = currentHeight;
				LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
			}

			if (currentWidth == 0 || currentHeight == 0) {
				return;
			}

			int roundedWidth = Mathf.CeilToInt(rectTransform.rect.width / 4) * 4;
			int roundedHeight = Mathf.CeilToInt(rectTransform.rect.height / 4) * 4;

			SKColorType targetColorType = (colorType == HBColorFormat.alpha8 && !enableBgFill) ? SKColorType.Alpha8 : SKColorType.Rgba8888;

			// Reuse surface if size and format match, otherwise recreate
			if (surface != null && (info.Width != roundedWidth || info.Height != roundedHeight || info.ColorType != targetColorType)) {
				surface.Dispose();
				surface = null;
				canvas = null;
			}

			if (info.IsEmpty) {
				info = new SKImageInfo(roundedWidth, roundedHeight);
			} else {
				info.Width = roundedWidth;
				info.Height = roundedHeight;
			}
			info.ColorType = targetColorType;

			if (surface == null) {
				surface = SKSurface.Create(info);
			}
			if (surface == null) return;
			canvas = surface.Canvas;
			canvas.Clear();
			canvas.ResetMatrix();
			canvas.Scale(1, -1);

			// Draw background shape before text
			if (enableBgFill) {
				float bgPadX = enableBgShadow ? (bgShadowBlur + Mathf.Abs(bgShadowOffset.x)) : 0;
				float bgPadY = enableBgShadow ? (bgShadowBlur + Mathf.Abs(bgShadowOffset.y)) : 0;
				DrawBgBackground(canvas, roundedWidth, roundedHeight, bgPadX, bgPadY);
			}

			// Calculate vertical offset based on alignment
			float baseEffectPadY = effectPadY - (userPadT + userPadB) / 2f;
			float baseEffectPadX = effectPadX - (userPadL + userPadR) / 2f;
			float translateX = baseEffectPadX + userPadL;
			float verticalOffset;
			if (verticalAlignment == VerticalAlignment.Middle) {
				verticalOffset = (-info.Height + (currentHeight - rs.MeasuredHeight) / 2);
			} else if (verticalAlignment == VerticalAlignment.Bottom) {
				verticalOffset = -info.Height + (currentHeight - rs.MeasuredHeight) - userPadB;
			} else {
				verticalOffset = -info.Height + baseEffectPadY + userPadT;
			}
			canvas.Translate(translateX, verticalOffset);


			TextureFormat format = (info.ColorType == SKColorType.Rgba8888) ? TextureFormat.RGBA32 : info.ColorType == SKColorType.Alpha8 ? TextureFormat.Alpha8 : TextureFormat.RGBA32;
			if (texture == null) {
				texture = new Texture2D(roundedWidth, roundedHeight, format, false);
				texture.name = "HB_Text";
				texture.hideFlags = HideFlags.DontSave;
				texture.wrapMode = TextureWrapMode.Repeat;
			} else {
				texture.Reinitialize(roundedWidth, roundedHeight, format, false);
			}

			if (enableGradiant && gradiantColors != null && gradiantColors.Length > 0) {
				// Reuse gradient color array — only reallocate if color count changed
				if (_cachedGradColors == null || _cachedGradColors.Length != gradiantColors.Length)
					_cachedGradColors = new SKColor[gradiantColors.Length];
				int hash = gradiantColors.Length;
				for (int i = 0; i < gradiantColors.Length; i++) {
					uint c = ColorToUint(gradiantColors[i]);
					_cachedGradColors[i] = new SKColor(c);
					hash = hash * 31 + (int)c;
				}
				hash = hash * 31 + gradiantAngle.GetHashCode();

				// Only recreate gradient/options when colors or angle changed
				if (blockGradient == null || hash != _cachedGradColorHash) {
					blockGradient = TextGradient.Linear(_cachedGradColors, gradiantPositions, gradiantAngle);
					_cachedGradColorHash = hash;
					if (_cachedPaintOptions == null)
						_cachedPaintOptions = new TextPaintOptions();
					_cachedPaintOptions.TextGradient = blockGradient;
				}
				rs.Paint(canvas, _cachedPaintOptions);
			} else {
				rs.Paint(canvas);
			}

			pixmap = surface.PeekPixels();
			texture.LoadRawTextureData(pixmap.GetPixels(), pixmap.RowBytes * pixmap.Height);
			texture.Apply();
			if (rawImage != null) {
				rawImage.texture = texture;
				if (!rawImage.enabled) {
					rawImage.enabled = true;
				}
			}

			// Dispose pixmap (it just references the surface's memory)
			pixmap.Dispose();
			pixmap = null;

			textRendered = true;
		}

		// ── Background Shape Drawing ──────────────────────────────────────

		void DrawBgBackground(SKCanvas c, int surfaceW, int surfaceH, float bgPadX, float bgPadY) {
			if (_bgPaint == null) _bgPaint = new SKPaint { IsAntialias = true };

			c.Save();
			c.Translate(0, -surfaceH);
			// Now in normal top-left origin coords

			SKRect bgRect = new SKRect(bgPadX, bgPadY, surfaceW - bgPadX, surfaceH - bgPadY);

			_bgCachedRadii[0] = new SKPoint(bgCornerRadii.x, bgCornerRadii.x);
			_bgCachedRadii[1] = new SKPoint(bgCornerRadii.y, bgCornerRadii.y);
			_bgCachedRadii[2] = new SKPoint(bgCornerRadii.z, bgCornerRadii.z);
			_bgCachedRadii[3] = new SKPoint(bgCornerRadii.w, bgCornerRadii.w);

			if (enableBgShadow) DrawBgShadow(c, bgRect);
			DrawBgFill(c, bgRect);
			if (enableBgInnerShadow) DrawBgInnerShadow(c, bgRect);
			if (enableBgStroke) DrawBgStroke(c, bgRect);

			c.Restore();
		}

		void DrawBgShadow(SKCanvas c, SKRect bgRect) {
			SKRect shadowRect = bgRect;
			shadowRect.Offset(bgShadowOffset.x, bgShadowOffset.y);

			ResetBgPaint();
			_bgPaint.Style = SKPaintStyle.Fill;
			_bgPaint.Color = new SKColor(ColorToUint(bgShadowColor));
			if (bgShadowBlur > 0)
				_bgPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, bgShadowBlur * 0.5f);

			DrawBgShape(c, shadowRect);
			ClearBgPaintEffects();
		}

		void DrawBgFill(SKCanvas c, SKRect rect) {
			ResetBgPaint();
			_bgPaint.Style = SKPaintStyle.Fill;

			switch (bgFillType) {
				case SkiaFillType.Solid:
					_bgPaint.Color = new SKColor(ColorToUint(bgFillColor));
					break;
				case SkiaFillType.LinearGradient:
					_bgPaint.IsDither = true;
					_bgPaint.Shader = CreateBgLinearGradient(rect);
					break;
				case SkiaFillType.RadialGradient:
					_bgPaint.IsDither = true;
					_bgPaint.Shader = CreateBgRadialGradient(rect);
					break;
				case SkiaFillType.SweepGradient:
					_bgPaint.IsDither = true;
					_bgPaint.Shader = CreateBgSweepGradient(rect);
					break;
				default:
					_bgPaint.Color = new SKColor(ColorToUint(bgFillColor));
					break;
			}

			DrawBgShape(c, rect);
			ClearBgPaintEffects();
		}

		void DrawBgInnerShadow(SKCanvas c, SKRect bgRect) {
			c.Save();

			using (var clipPath = CreateBgShapePath(bgRect)) {
				c.ClipPath(clipPath);
			}

			ResetBgPaint();
			_bgPaint.Style = SKPaintStyle.Fill;
			_bgPaint.Color = new SKColor(ColorToUint(bgInnerShadowColor));
			if (bgInnerShadowBlur > 0)
				_bgPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, bgInnerShadowBlur * 0.5f);

			float expand = bgInnerShadowBlur * 2 + 50;
			SKRect outerRect = new SKRect(
				bgRect.Left - expand, bgRect.Top - expand,
				bgRect.Right + expand, bgRect.Bottom + expand
			);

			using (var shadowPath = new SKPath())
			using (var shapePath = CreateBgShapePath(bgRect)) {
				shadowPath.AddRect(outerRect);
				shadowPath.AddPath(shapePath);
				shadowPath.FillType = SKPathFillType.EvenOdd;

				c.Translate(bgInnerShadowOffset.x, bgInnerShadowOffset.y);
				c.DrawPath(shadowPath, _bgPaint);
			}

			ClearBgPaintEffects();
			c.Restore();
		}

		void DrawBgStroke(SKCanvas c, SKRect rect) {
			ResetBgPaint();
			_bgPaint.Style = SKPaintStyle.Stroke;
			_bgPaint.StrokeWidth = bgStrokeWidth;
			_bgPaint.Color = new SKColor(ColorToUint(bgStrokeColor));

			DrawBgShape(c, rect);
			ClearBgPaintEffects();
		}

		void DrawBgShape(SKCanvas c, SKRect rect) {
			bool hasRadius = bgCornerRadii.x > 0 || bgCornerRadii.y > 0 || bgCornerRadii.z > 0 || bgCornerRadii.w > 0;
			if (hasRadius) {
				using (var rrect = new SKRoundRect()) {
					rrect.SetRectRadii(rect, _bgCachedRadii);
					c.DrawRoundRect(rrect, _bgPaint);
				}
			} else {
				c.DrawRect(rect, _bgPaint);
			}
		}

		SKPath CreateBgShapePath(SKRect rect) {
			var path = new SKPath();
			bool hasRadius = bgCornerRadii.x > 0 || bgCornerRadii.y > 0 || bgCornerRadii.z > 0 || bgCornerRadii.w > 0;
			if (hasRadius) {
				using (var rrect = new SKRoundRect()) {
					rrect.SetRectRadii(rect, _bgCachedRadii);
					path.AddRoundRect(rrect);
				}
			} else {
				path.AddRect(rect);
			}
			return path;
		}

		void ResetBgPaint() {
			_bgPaint.Reset();
			_bgPaint.IsAntialias = true;
		}

		void ClearBgPaintEffects() {
			_bgPaint.Shader?.Dispose();
			_bgPaint.Shader = null;
			_bgPaint.PathEffect?.Dispose();
			_bgPaint.PathEffect = null;
			_bgPaint.MaskFilter?.Dispose();
			_bgPaint.MaskFilter = null;
		}

		SKShader CreateBgLinearGradient(SKRect rect) {
			float rad = bgGradientAngle * Mathf.Deg2Rad;
			float cx = rect.MidX, cy = rect.MidY;
			float halfDiag = Mathf.Sqrt(rect.Width * rect.Width + rect.Height * rect.Height) * 0.5f;
			float dx = Mathf.Cos(rad) * halfDiag;
			float dy = Mathf.Sin(rad) * halfDiag;

			var start = new SKPoint(cx - dx, cy - dy);
			var end = new SKPoint(cx + dx, cy + dy);

			GetBgGradientData(out SKColor[] colors, out float[] positions);
			return SKShader.CreateLinearGradient(start, end, colors, positions, SKShaderTileMode.Clamp);
		}

		SKShader CreateBgRadialGradient(SKRect rect) {
			float radius = Mathf.Max(rect.Width, rect.Height) * 0.5f;
			var center = new SKPoint(rect.MidX, rect.MidY);

			GetBgGradientData(out SKColor[] colors, out float[] positions);
			return SKShader.CreateRadialGradient(center, radius, colors, positions, SKShaderTileMode.Clamp);
		}

		SKShader CreateBgSweepGradient(SKRect rect) {
			var center = new SKPoint(rect.MidX, rect.MidY);

			GetBgGradientData(out SKColor[] colors, out float[] positions);
			return SKShader.CreateSweepGradient(center, colors, positions);
		}

		void GetBgGradientData(out SKColor[] colors, out float[] positions) {
			if (bgGradient == null || bgGradient.colorKeys.Length < 2) {
				colors = new[] { SKColors.White, SKColors.Black };
				positions = new[] { 0f, 1f };
				return;
			}

			int count = bgGradient.colorKeys.Length;

			if (_bgCachedGradColors == null || _bgCachedGradColors.Length != count)
				_bgCachedGradColors = new SKColor[count];
			if (_bgCachedGradPositions == null || _bgCachedGradPositions.Length != count)
				_bgCachedGradPositions = new float[count];

			for (int i = 0; i < count; i++) {
				var key = bgGradient.colorKeys[i];
				float alpha = bgGradient.Evaluate(key.time).a;
				_bgCachedGradColors[i] = new SKColor(
					(byte)(key.color.r * 255),
					(byte)(key.color.g * 255),
					(byte)(key.color.b * 255),
					(byte)(alpha * 255)
				);
				_bgCachedGradPositions[i] = key.time;
			}

			colors = _bgCachedGradColors;
			positions = _bgCachedGradPositions;
		}

		// ── End Background Shape ──────────────────────────────────────────

		protected virtual void RenderLinksCall() {
			if (_linkStyle == null) {
				_linkStyle = new Style();
			}
			_linkStyle.FontSize = fontSize;
			_linkStyle.TextColor = new SKColor(ColorToUint(linkColor));
			_linkStyle.Underline = UnderlineStyle.Solid;
			_linkStyle.HaloWidth = outlineWidth;
			_linkStyle.ShadowWidth = shadowWidth;
			_linkStyle.HaloColor = outlineWidth > 0 ? outlineColor : _emptyGradient;
			_linkStyle.ShadowGradientColor = shadowGradientColor != null && shadowGradientColor.colorKeys.Length > 0
				? shadowGradientColor
				: _emptyGradient;
			_linkStyle.InnerGlowColor = innerGlowWidth > 0 ? new SKColor(ColorToUint(innerGlowColor)) : SKColor.Empty;
			_linkStyle.InnerGlowWidth = innerGlowWidth;
			_linkStyle.FontItalic = italic;
			_linkStyle.FontWeight = bold ? 700 : 400;
			_linkStyle.LetterSpacing = letterSpacing;
			_linkStyle.TextDirection = TextDirection.Auto;
			_linkStyle.HaloBlur = outlineBlur;
			_linkStyle.BackgroundColor = backgroundColor.a > 0 ? new SKColor(ColorToUint(backgroundColor)) : SKColors.Empty;
			_linkStyle.LineHeight = lineHeight;
			_linkStyle.StrikeThrough = strikeThroughStyle;

			if (regex == null) {
				regex = new Regex(pattern, RegexOptions.Compiled);
			}

			MatchCollection matches = regex.Matches(Text);
				foreach (Match match in matches) {
					var length = match.Index + match.Length;
					int differnce = 0;
					if (length > rs.Length) {
						differnce = length - rs.Length;
						length = length - differnce;
					}

					rs.ApplyStyle(Mathf.Clamp(match.Index,0,rs.Length), Mathf.Clamp(match.Length - differnce,0,match.Length),_linkStyle);
					urls.Add(match.Index,new HBLinks() {
						IndexStart = match.Index,
						IndexEnd = length,
						Length = match.Length,
						link = match.Value
					});
				}
		}

		protected virtual void FixedUpdate() {
			if (bakedSprite != null) return;
			float w = rectTransform.rect.width;
			float h = rectTransform.rect.height;
			if (Mathf.Abs(currentWidth - w) > 0.5f || Mathf.Abs(currentHeight - h) > 0.5f) {
				_renderDirty = true;
			}
		}

		protected virtual void LateUpdate() {
			if (bakedSprite != null) return;
			if (_renderDirty) {
				_renderDirty = false;
				urls.Clear();
				RenderText();
			}
		}

		public virtual void ReUpdate() {
			if (bakedSprite != null) return;
			if (rawImage == null) {
				rawImage = GetComponent<RawImage>();
				rectTransform = transform as RectTransform;
			}
			SyncStyleFromFields();
			_renderDirty = true;
		}

		/// <summary>
		/// Rebuilds the TextBlock layout immediately so caret/hit-test queries
		/// return results for the current text. Does NOT re-render to texture.
		/// </summary>
		public virtual void FlushLayout() {
			SyncStyleFromFields();
			if (rs == null) rs = new Topten.RichTextKit.TextBlock();
			rs.Clear();
			rs.MaxHeight = null;
			rs.MaxWidth = maxWidth > 0 ? (float?)maxWidth : null;
			rs.MaxLines = maxLines == 0 ? null : (int?)maxLines;
			rs.Alignment = textAlignment;
			rs.BaseDirection = textDirection;
			if (richText)
				HBRichTextParser.Parse(rs, Text, styleBoldItalic);
			else
				rs.AddText(Text, styleBoldItalic);
		}

		public virtual float GradientAngle {
			get => gradiantAngle;
			set {
				if (gradiantAngle != value) {
					gradiantAngle = value;
					ReUpdate();
				}
			}
		}

		// Computes the same canvas transform used in RenderText.
		// Canvas does Scale(1,-1) then Translate(translateX, verticalOffset).
		// TextBlock point (tx,ty) → surface pixel (tx+translateX, -(ty+verticalOffset)).
		// Surface pixel (0,0) maps to bottom of Unity RawImage.
		private void GetRenderTransform(out float translateX, out float verticalOffset, out int surfW, out int surfH) {
			float epX = 0f, epY = 0f;
			if (outlineWidth > 0) {
				float oe = outlineWidth + outlineBlur;
				epX = Mathf.Max(epX, oe);
				epY = Mathf.Max(epY, oe);
			}
			if (shadowWidth > 0) {
				epX = Mathf.Max(epX, shadowWidth + Mathf.Abs(shadowOffsetX));
				epY = Mathf.Max(epY, shadowWidth + Mathf.Abs(shadowOffsetY));
			}
			float uPadL = padding.x, uPadT = padding.y, uPadR = padding.z, uPadB = padding.w;
			epX += (uPadL + uPadR) / 2f;
			epY += (uPadT + uPadB) / 2f;
			float baseEpX = epX - (uPadL + uPadR) / 2f;
			float baseEpY = epY - (uPadT + uPadB) / 2f;

			translateX = baseEpX + uPadL;

			float rectW = rectTransform.rect.width;
			float rectH = rectTransform.rect.height;
			surfW = Mathf.CeilToInt(rectW / 4) * 4;
			surfH = Mathf.CeilToInt(rectH / 4) * 4;

			if (verticalAlignment == VerticalAlignment.Middle)
				verticalOffset = -surfH + (rectH - rs.MeasuredHeight) / 2f;
			else if (verticalAlignment == VerticalAlignment.Bottom)
				verticalOffset = -surfH + (rectH - rs.MeasuredHeight) - uPadB;
			else
				verticalOffset = -surfH + baseEpY + uPadT;
		}

		/// <summary>
		/// Returns the caret rectangle in RectTransform local coordinates.
		/// Used by HBInputField for cursor positioning.
		/// </summary>
		public bool GetCaretLocalRect(int codePointIndex, out Rect localRect) {
			localRect = default;
			if (rs == null) return false;

			int clampedIndex = Mathf.Clamp(codePointIndex, 0, rs.MeasuredLength);
			var caretInfo = rs.GetCaretInfo(new Topten.RichTextKit.CaretPosition(clampedIndex));
			if (caretInfo.IsNone) return false;

			GetRenderTransform(out float translateX, out float verticalOffset, out int surfW, out int surfH);

			// TextBlock (tx,ty) → surface (tx+translateX, -(ty+verticalOffset))
			float surfX = caretInfo.CaretRectangle.Left + translateX;
			float surfYTop = -(caretInfo.CaretRectangle.Top + verticalOffset);
			float surfYBot = -(caretInfo.CaretRectangle.Bottom + verticalOffset);

			// Surface → RectTransform local
			// Surface (0,0) = bottom of RawImage, (surfW,surfH) = top of RawImage
			Rect rect = rectTransform.rect;
			float rectW = rect.width;
			float rectH = rect.height;

			float localX = (surfX / surfW) * rectW + rect.xMin;
			float localYTop = (surfYTop / surfH) * rectH + rect.yMin;
			float localYBot = (surfYBot / surfH) * rectH + rect.yMin;

			localRect = new Rect(localX, localYBot, 2f, localYTop - localYBot);
			return true;
		}

		/// <summary>
		/// Hit-test a local point and return the closest code point index.
		/// Used by HBInputField for click-to-caret.
		/// </summary>
		public int HitTestLocal(Vector2 localPoint) {
			if (rs == null) return 0;

			GetRenderTransform(out float translateX, out float verticalOffset, out int surfW, out int surfH);

			// Local → surface
			Rect rect = rectTransform.rect;
			float rectW = rect.width;
			float rectH = rect.height;

			float surfX = (localPoint.x - rect.xMin) / rectW * surfW;
			float surfY = (localPoint.y - rect.yMin) / rectH * surfH;

			// Surface → TextBlock: tx = surfX - translateX, ty = -surfY - verticalOffset
			float tx = surfX - translateX;
			float ty = -surfY - verticalOffset;

			var result = rs.HitTest(tx, ty);
			return result.ClosestCodePointIndex;
		}

		/// <summary>
		/// Set multiple properties at once and render a single frame.
		/// Used by HBTextAnimator to avoid multiple re-renders per frame.
		/// </summary>
		public virtual void ApplyAnimatedValues(
			string displayText = null,
			Color? fontColor = null,
			int? haloWidth = null,
			float? shadowOffX = null,
			float? shadowOffY = null,
			int? fontSize = null,
			int? letterSpacing = null,
			float? gradientAngle = null,
			float? maxWidth = null,
			float? maxHeight = null) {
			if (bakedSprite != null) return;
			if (displayText != null) Text = displayText;
			if (fontColor.HasValue) this.fontColor = fontColor.Value;
			if (haloWidth.HasValue) outlineWidth = haloWidth.Value;
			if (shadowOffX.HasValue) shadowOffsetX = shadowOffX.Value;
			if (shadowOffY.HasValue) shadowOffsetY = shadowOffY.Value;
			if (fontSize.HasValue) this.fontSize = fontSize.Value;
			if (letterSpacing.HasValue) this.letterSpacing = letterSpacing.Value;
			if (gradientAngle.HasValue) gradiantAngle = gradientAngle.Value;
			if (maxWidth.HasValue) this.maxWidth = maxWidth.Value;
			if (maxHeight.HasValue) this.maxHeight = maxHeight.Value;

			if (rawImage == null) {
				rawImage = GetComponent<RawImage>();
				rectTransform = transform as RectTransform;
			}
			SyncStyleFromFields();
			_renderDirty = false; // Rendering immediately — clear deferred flag
			urls.Clear();
			RenderText();
		}

		protected void SyncStyleFromFields() {
			styleBoldItalic.FontSize = fontSize;
			styleBoldItalic.TextColor = new SKColor(ColorToUint(fontColor));
			styleBoldItalic.HaloWidth = outlineWidth;
			styleBoldItalic.ShadowWidth = shadowWidth;
			styleBoldItalic.ShadowOffsetX = shadowOffsetX;
			styleBoldItalic.ShadowOffsetY = shadowOffsetY;
			styleBoldItalic.HaloColor = outlineWidth > 0 ? outlineColor : _emptyGradient;
			styleBoldItalic.ShadowGradientColor = shadowGradientColor != null && shadowGradientColor.colorKeys.Length > 0
				? shadowGradientColor
				: _emptyGradient;
			styleBoldItalic.InnerGlowColor = innerGlowWidth > 0 ? new SKColor(ColorToUint(innerGlowColor)) : SKColor.Empty;
			styleBoldItalic.InnerGlowWidth = innerGlowWidth;
			styleBoldItalic.FontItalic = italic;
			styleBoldItalic.FontWeight = bold ? 700 : fontWeight;
			styleBoldItalic.LetterSpacing = letterSpacing;
			styleBoldItalic.TextDirection = textDirection;
			styleBoldItalic.HaloBlur = outlineBlur;
			styleBoldItalic.BackgroundColor = backgroundColor.a > 0 ? new SKColor(ColorToUint(backgroundColor)) : SKColors.Empty;
			styleBoldItalic.Underline = underlineStyle;
			styleBoldItalic.LineHeight = lineHeight;
			styleBoldItalic.StrikeThrough = strikeThroughStyle;
			styleBoldItalic.FontVariant = fontVariant;
		}

		// --- Reset ---
		[ContextMenu("Reset to Defaults")]
		private void ResetToDefaults() {
			#if UNITY_EDITOR
			UnityEditor.Undo.RecordObject(this, "Reset HB_TEXTBlock");
			#endif
			Text = "";
			fontSize = 20;
			letterSpacing = 0;
			maxLines = 0;
			outlineWidth = 0;
			outlineBlur = 0;
			shadowWidth = 0;
			innerGlowWidth = 0;
			shadowOffsetX = 0;
			shadowOffsetY = 1;
			fontColor = Color.black;
			innerGlowColor = Color.white;
			backgroundColor = Color.clear;
			linkColor = Color.blue;
			italic = false;
			bold = false;
			autoFitVertical = true;
			autoFitHorizontal = false;
			renderLinks = false;
			enableGradiant = false;
			enableEllipsis = true;
			richText = false;
			underlineStyle = default;
			strikeThroughStyle = default;
			lineHeight = 1.0f;
			maxHeight = -1;
			gradiantAngle = 90;
			textAlignment = TextAlignment.Center;
			verticalAlignment = VerticalAlignment.Top;
			textDirection = TextDirection.Auto;
			fontWeight = 400;
			fontVariant = FontVariant.Normal;
			padding = Vector4.zero;
			paragraphSpacing = 0;
			fallbackFonts = null;
			gradiantColors = null;
			gradiantPositions = null;
			ReUpdate();
		}

		#if UNITY_EDITOR
		public byte[] BakeToTexture() {
			if (rawImage == null) {
				rawImage = GetComponent<RawImage>();
			}
			if (rectTransform == null) rectTransform = transform as RectTransform;

			// Force RGB32 for bake output (Alpha8 doesn't encode to PNG)
			var savedColorType = colorType;
			colorType = HBColorFormat.rgb32;
			SyncStyleFromFields();
			urls.Clear();
			RenderText();
			colorType = savedColorType;

			if (texture == null) return null;
			return texture.EncodeToPNG();
		}

		public virtual void ReUpdateEditMode() {
			if (bakedSprite != null) return;
			if (rawImage == null) {
				rawImage = GetComponent<RawImage>();
				rectTransform = transform as RectTransform;
			}
			SyncStyleFromFields();
			urls.Clear();
			RenderText();
		}
		#endif

		public virtual string LinkPressed() {
				RectTransform rawImageRect = GetComponent<RectTransform>();
				if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rawImageRect, Input.mousePosition, Camera.main, out var localMousePosition)) {
					float normalizedX = Mathf.InverseLerp(-rawImageRect.rect.width / 2, rawImageRect.rect.width / 2, localMousePosition.x);
					float normalizedY = Mathf.InverseLerp(-rawImageRect.rect.height / 2, rawImageRect.rect.height / 2, localMousePosition.y);
					var caretPos = rs.HitTest(rawImageRect.sizeDelta.x * normalizedX, rawImageRect.sizeDelta.y * normalizedY);
					foreach (var url in urls) {
						if (caretPos.ClosestCodePointIndex >= url.Value.IndexStart && caretPos.ClosestCodePointIndex <=  url.Value.IndexEnd) {
							onLinkClicked?.Invoke(url.Value.link);
							return url.Value.link;
						}
					}
				}

				return "";
		}

		protected virtual void OnDestroy() {
			Dispose();
			if (rawImage != null) {
				rawImage.texture = null;
				rawImage = null;
			}
			if (texture != null) {
				DestroyImmediate(texture);
				texture = null;
			}

			if (rs != null) {
				rs.Clear();
				rs = null;
			}

			if (_typefaceCacheRegistered && _typefaceRefCounts.ContainsKey(_typefaceCacheKey)) {
				_typefaceRefCounts[_typefaceCacheKey]--;
				if (_typefaceRefCounts[_typefaceCacheKey] <= 0) {
					if (_typefaceCache.TryGetValue(_typefaceCacheKey, out var cachedTypeface)) {
						cachedTypeface?.Dispose();
						_typefaceCache.Remove(_typefaceCacheKey);
					}
					_typefaceRefCounts.Remove(_typefaceCacheKey);
				}
			}
			skTypeface = null;
			_cachedFontMapper = null;
		}

		protected virtual void OnDisable() {
			Dispose();
			if (rawImage != null) {
				rawImage.texture = null;
			}

			if (texture != null) {
				DestroyImmediate(texture);
				texture = null;
			}
		}

		protected virtual void Dispose() {
			if (pixmap != null) {
				pixmap.Dispose();
				pixmap = null;
			}

			if (surface != null) {
				surface.Dispose();
				surface = null;
			}
			// canvas is owned by surface — already disposed above
			canvas = null;

			if (_bgPaint != null) {
				ClearBgPaintEffects();
				_bgPaint.Dispose();
				_bgPaint = null;
			}
		}

		public virtual void CalculateLayoutInputHorizontal() {}


		public virtual void CalculateLayoutInputVertical() {}


		public virtual float minWidth { get; }
		public virtual float preferredWidth {
			get {
				widthPreferred = true;
				if (rs != null) {
					return currentPreferdWidth;
				}
				return currentWidth;
			}
		}
		public virtual float flexibleWidth { get; }
		public virtual float minHeight { get; }
		protected TextBlock temp = new TextBlock();

		public virtual float preferredHeight {
			get {
				heightPreferred = true;
				if (rs != null && textRendered) {
					return currentPreferdHeight;
				}
				temp.Clear();
				styleBoldItalic.FontSize = fontSize;
				styleBoldItalic.FontWeight = bold ? 700 : 400;

				temp.AddText(text,styleBoldItalic);
				if (rectTransform == null) {
					rectTransform = transform as RectTransform;
				}
				var currentPreferdWidth2 = autoFitHorizontal ? temp.MeasuredWidth > maxWidth ? maxWidth : temp.MeasuredWidth + 20 : rectTransform.sizeDelta.x;
				temp.MaxWidth = currentPreferdWidth2;
				temp.MaxHeight = autoFitVertical ? temp.MeasuredHeight : rectTransform.rect.height;

				return temp.MeasuredHeight;
			}
		}
		public virtual float flexibleHeight { get; }
		public virtual int layoutPriority { get; }

		public virtual void RefreshFontFamily() {
			if (font != null) {
				EntityId key = font.GetEntityId();
				if (!_typefaceCache.ContainsKey(key)) {
					var fontBytes = GetFontBytes();
					if (fontBytes == null) return;
					SKData copy = SKData.CreateCopy(fontBytes);
					_typefaceCache[key] = SKTypeface.FromData(copy);
					copy.Dispose();
					_typefaceRefCounts[key] = 0;
				}
				skTypeface = _typefaceCache[key];
				_cachedFontMapper = new FontMapper(skTypeface);
				_cachedFontMapperKey = key;
				if (rs != null) {
					rs.FontMapper = _cachedFontMapper;
				}
			}
		}
	}
}
