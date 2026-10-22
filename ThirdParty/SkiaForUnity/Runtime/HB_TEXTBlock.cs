using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using Topten.RichTextKit;
using TextAlignment = Topten.RichTextKit.TextAlignment;

namespace SkiaSharp.Unity.HB {
	public enum HBColorFormat {
		alpha8,
		rgb32
	}
	
	public class HB_TEXTBlock : MonoBehaviour, ILayoutElement {
		[SerializeField]
		[TextArea]
		protected string Text;
		[SerializeField]
		public TextAsset font;
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
		protected bool italic, bold, autoFitVertical = true, autoFitHorizontal, renderLinks, enableGradiant, enableEllipsis = true;
		[SerializeField]
		protected UnderlineStyle underlineStyle;
		[SerializeField]
		protected StrikeThroughStyle strikeThroughStyle;
		[SerializeField]
		protected float lineHeight = 1.0f, maxWidth = 264, maxHeight = -1 , gradiantAngle = 90;
		[SerializeField]
		protected HBColorFormat colorType = HBColorFormat.rgb32; 
		[SerializeField] 
		protected TextAlignment textAlignment = TextAlignment.Center;
		[SerializeField]
		protected Color[] gradiantColors;
		[SerializeField]
		protected float[] gradiantPositions;

        
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
		protected RectTransform rectTransform;
		protected float currentWidth, currentHeight, currentPreferdWidth = 0, currentPreferdHeight;
		protected TextGradient blockGradient;
		protected bool widthPreferred, heightPreferred;

		public virtual TextBlock Info => rs;

		protected enum VerticalAlignment { Top, Middle, Bottom }
		[SerializeField]
		protected VerticalAlignment verticalAlignment = VerticalAlignment.Top; // Default alignment

		
		public virtual float MaxWidth {
			get => maxWidth;
			set {
				maxWidth = value;
				if (rawImage == null) {
					rawImage = GetComponent<RawImage>();
					rectTransform = transform as RectTransform;
				}
				if (rawImage) {
					urls.Clear();
					RenderText();
				}
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
				if (rawImage) {
					urls.Clear();
					RenderText();
				}
			}
		}
		
		public virtual string text {
			get => Text;
			set {
				Text = value;
				ReUpdate();
			}
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
				if (rawImage == null) rawImage = GetComponent<RawImage>();
				return rawImage != null ? rawImage.color.a : 1f;
			}
			set {
				if (rawImage == null) rawImage = GetComponent<RawImage>();
				if (rawImage != null) {
					Color c = rawImage.color;
					c.a = value;
					rawImage.color = c;
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
		
		public virtual TextAsset Font {
			get {
				return font;
			}
			set {
				font = value;
				ReUpdate();
			}
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
				return LineHeight;
			}
			set {
				LineHeight = value;
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
			rawImage = GetComponent<RawImage>();
			if (rawImage) {
				rawImage.enabled = false;
			}
			rectTransform = transform as RectTransform;
			
			styleBoldItalic.FontSize = fontSize;
			styleBoldItalic.TextColor = new SKColor(ColorToUint(fontColor));
			styleBoldItalic.HaloWidth = outlineWidth;
			styleBoldItalic.ShadowWidth = shadowWidth;
			styleBoldItalic.HaloColor = outlineWidth > 0 ? outlineColor : new Gradient();
			styleBoldItalic.ShadowGradientColor = shadowGradientColor != null && shadowGradientColor.colorKeys.Length > 0
				? shadowGradientColor
				: new Gradient();
			styleBoldItalic.ShadowOffsetX = shadowOffsetX;
			styleBoldItalic.ShadowOffsetY = shadowOffsetY;
			styleBoldItalic.InnerGlowWidth = innerGlowWidth;
			styleBoldItalic.InnerGlowColor = innerGlowWidth > 0 ? new SKColor(ColorToUint(innerGlowColor)) : SKColor.Empty;
			
			styleBoldItalic.FontItalic = italic;
			styleBoldItalic.FontWeight = bold ? 700 : 400;
			styleBoldItalic.LetterSpacing = letterSpacing;
			styleBoldItalic.TextDirection = TextDirection.Auto;
			styleBoldItalic.HaloBlur = outlineBlur;
			styleBoldItalic.BackgroundColor = backgroundColor.a > 0 ? new SKColor(ColorToUint(backgroundColor)) : SKColors.Empty;
			styleBoldItalic.Underline = underlineStyle;
			styleBoldItalic.LineHeight = lineHeight;
			styleBoldItalic.StrikeThrough = strikeThroughStyle;
		}

		protected virtual void OnEnable() {
			if (String.IsNullOrEmpty(Text)){
				return;
			}
			
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
			Dispose();
			#if !UNITY_EDITOR
				DestroyImmediate(rawImage.texture);
			#else
			DestroyImmediate(rawImage.texture);
			#endif
			
			if (texture != null) {
				#if !UNITY_EDITOR
				DestroyImmediate(texture);
				#else
				DestroyImmediate(texture);
				#endif
			}
			
			if (rs == null) {
				rs = new TextBlock();
			}
			rs.Clear();
			rs.MaxHeight = null;
			rs.MaxWidth = null;
			rs.MaxLines = maxLines == 0 ? null : maxLines;
			if (colorType == HBColorFormat.alpha8) {
				rawImage.color = fontColor;
			} else {
				rawImage.color = Color.white;
			}
			rs.Alignment = textAlignment;
			rs.EllipsisEnabled = enableEllipsis;            
			rs.AddText(Text, styleBoldItalic);
			
			if (renderLinks) {
				RenderLinksCall();
			}

			if (font != null) {
				if (skTypeface == null) {
					var bytes = font.bytes;
					SKData copy = SKData.CreateCopy(bytes);
					skTypeface = SKTypeface.FromData(copy);
					copy.Dispose();
				}
				rs.FontMapper = new FontMapper(skTypeface);
			}

			if ( (!autoFitHorizontal && rectTransform.rect.width == 0)) {
				return;
			}
			
			currentPreferdWidth = autoFitHorizontal ? rs.MeasuredWidth > maxWidth ? maxWidth : rs.MeasuredWidth : rectTransform.rect.width;
			rs.MaxWidth = currentPreferdWidth;
		//	rs.MaxWidth = currentPreferdWidth = rs.MeasuredWidth;
			currentPreferdHeight = autoFitVertical ? maxHeight > -1  && rs.MeasuredHeight > maxHeight ? maxHeight : rs.MeasuredHeight : rectTransform.rect.height;

			rs.MaxHeight = currentPreferdHeight;
			//rs.MaxHeight = currentPreferdHeight = rs.MeasuredHeight;
			
			if (autoFitVertical) {
				var size = autoFitHorizontal
					? new Vector2(currentPreferdWidth, heightPreferred ? rectTransform.sizeDelta.y : rs.MeasuredHeight)
					: new Vector2(rectTransform.rect.width, heightPreferred ? rectTransform.sizeDelta.y : rs.MeasuredHeight);

				if (size.x == 0) return;

				// Only set sizeDelta if the RectTransform is not in stretch mode for that axis
				if (rectTransform.anchorMin.x == rectTransform.anchorMax.x) {
					rectTransform.sizeDelta = new Vector2(size.x, rectTransform.sizeDelta.y); // Modify width if not stretching horizontally
				}
				if (rectTransform.anchorMin.y == rectTransform.anchorMax.y) {
					rectTransform.sizeDelta = new Vector2(rectTransform.sizeDelta.x, size.y); // Modify height if not stretching vertically
				}
			} else {
				var size = autoFitHorizontal
					? new Vector2(currentPreferdWidth, rectTransform.rect.height)
					: new Vector2(rectTransform.rect.width, rectTransform.rect.height);

				if (rectTransform.anchorMin.x == rectTransform.anchorMax.x) {
					rectTransform.sizeDelta = new Vector2(size.x, rectTransform.sizeDelta.y); // Modify width if not stretching horizontally
				}
				if (rectTransform.anchorMin.y == rectTransform.anchorMax.y) {
					rectTransform.sizeDelta = new Vector2(rectTransform.sizeDelta.x, size.y); // Modify height if not stretching vertically
				}
			}

			
			currentWidth = rectTransform.rect.width;
			currentHeight = rectTransform.rect.height;
			LayoutRebuilder.MarkLayoutForRebuild(rectTransform);

			if (currentWidth == 0 || currentHeight == 0) {
				return;
			}

			int roundedWidth = Mathf.CeilToInt(rectTransform.rect.width / 4) * 4;
			int roundedHeight = Mathf.CeilToInt(rectTransform.rect.height / 4) * 4;
			
			if (info.IsEmpty) {
				info = new SKImageInfo(roundedWidth, roundedHeight);
			} else {
				info.Width = roundedWidth;
				info.Height = roundedHeight;
			}

			info.ColorType = colorType == HBColorFormat.alpha8 ? SKColorType.Alpha8 : info.ColorType ;

			surface = SKSurface.Create(info);
			canvas = surface.Canvas;
			canvas.Scale(1, -1);
   //canvas.Translate(0, -info.Height);
   
// Calculate vertical offset based on alignment
   float verticalOffset = 0;
   if (verticalAlignment == VerticalAlignment.Middle) {
	   verticalOffset = (-info.Height + (currentHeight - rs.MeasuredHeight) / 2);
   } else if (verticalAlignment == VerticalAlignment.Bottom) {
	   verticalOffset = -info.Height + (currentHeight - rs.MeasuredHeight);
   } else {
	   verticalOffset = -info.Height;
   }
   canvas.Translate(0, verticalOffset); // Apply vertical alignment offset within bounds

   
			TextureFormat format = (info.ColorType == SKColorType.Rgba8888) ? TextureFormat.RGBA32 : info.ColorType == SKColorType.Alpha8 ? TextureFormat.Alpha8 : TextureFormat.RGBA32;
			if (texture == null) {
				texture = new Texture2D(roundedWidth, roundedHeight, format, false);
			} else {
				texture.Reinitialize(roundedWidth, roundedHeight, format, false);
			}
			
			if (enableGradiant && gradiantColors != null) {
				SKColor[] colors = new SKColor[gradiantColors.Length];
				for (int i = 0; i < gradiantColors.Length; i++) {
					colors[i] = new SKColor(ColorToUint(gradiantColors[i]));
				}
				blockGradient = TextGradient.Linear(colors, gradiantPositions, gradiantAngle);
				var options = new TextPaintOptions() {
					TextGradient = blockGradient
				};
				rs.Paint(canvas,options);
			} else {
				rs.Paint(canvas);
			}
			
			texture.name = "HB_Text";
			texture.wrapMode = TextureWrapMode.Repeat;
			pixmap = surface.PeekPixels();
			texture.LoadRawTextureData(pixmap.GetPixels(), pixmap.RowBytes * pixmap.Height);
			texture.Compress(false);
			texture.Apply();
			rawImage.texture = texture;
			if (!rawImage.enabled) {
				rawImage.enabled = true;
			}
			Dispose();
			textRendered = true;
		}

		protected virtual void RenderLinksCall() {
			Style styleLink = new Style() {
			FontSize = fontSize,
			TextColor = new SKColor(ColorToUint(linkColor)),
			Underline = UnderlineStyle.Solid,
			HaloWidth = outlineWidth,
			ShadowWidth = shadowWidth,
			HaloColor = outlineWidth > 0 ? outlineColor : new Gradient(),
			ShadowGradientColor = shadowGradientColor != null && shadowGradientColor.colorKeys.Length > 0
				? shadowGradientColor
				: new Gradient(),
			InnerGlowColor = innerGlowWidth > 0 ? new SKColor(ColorToUint(innerGlowColor)) : SKColor.Empty,
			InnerGlowWidth = innerGlowWidth,
			FontItalic = italic,
			FontWeight = bold ? 700 : 400,
			LetterSpacing = letterSpacing,
			TextDirection = TextDirection.Auto,
			HaloBlur = outlineBlur,
			BackgroundColor = backgroundColor.a > 0 ? new SKColor(ColorToUint(backgroundColor)) : SKColors.Empty,
			LineHeight = lineHeight,
			StrikeThrough = strikeThroughStyle,
		};
			
			string pattern = @"(https?://\S+|www\.\S+)";
			if (regex == null) {
				regex = new Regex(pattern);
			}
			
			MatchCollection matches = regex.Matches(Text);
				foreach (Match match in matches) {
					var length = match.Index + match.Length;
					int differnce = 0;
					if (length > rs.Length) {
						differnce = length - rs.Length;
						length = length - differnce;
					}

					rs.ApplyStyle(Mathf.Clamp(match.Index,0,rs.Length), Mathf.Clamp(match.Length - differnce,0,match.Length),styleLink);
					urls.Add(match.Index,new HBLinks() {
						IndexStart = match.Index,
						IndexEnd = length,
						Length = match.Length,
						link = match.Value
					});
				}
		}
        
		protected virtual void FixedUpdate() {
			if (currentWidth != rectTransform.rect.width || currentHeight != currentPreferdHeight) {
				urls.Clear();
				RenderText();
			}
		}

		public virtual void ReUpdate() {
			if (rawImage == null) {
				rawImage = GetComponent<RawImage>();
				rectTransform = transform as RectTransform;
			}
			urls.Clear();
			RenderText();
		}
		
		#if UNITY_EDITOR
		public virtual void ReUpdateEditMode() {
			if (rawImage == null) {
				rawImage = GetComponent<RawImage>();
				rectTransform = transform as RectTransform;
			}

			styleBoldItalic.FontSize = fontSize;
			styleBoldItalic.TextColor = new SKColor(ColorToUint(fontColor));
			styleBoldItalic.HaloWidth = outlineWidth;
			styleBoldItalic.ShadowWidth = shadowWidth;
			styleBoldItalic.ShadowOffsetX = shadowOffsetX;
			styleBoldItalic.ShadowOffsetY = shadowOffsetY;
			styleBoldItalic.HaloColor = outlineWidth > 0 ? outlineColor : new Gradient();
			styleBoldItalic.ShadowGradientColor = shadowGradientColor != null && shadowGradientColor.colorKeys.Length > 0
				? shadowGradientColor
				: new Gradient();
			styleBoldItalic.InnerGlowColor = innerGlowWidth > 0 ? new SKColor(ColorToUint(innerGlowColor)) : SKColor.Empty;
			styleBoldItalic.InnerGlowWidth = innerGlowWidth;
			styleBoldItalic.FontItalic = italic;
			styleBoldItalic.FontWeight = bold ? 700 : 400;
			styleBoldItalic.LetterSpacing = letterSpacing;
			styleBoldItalic.TextDirection = TextDirection.Auto;
			styleBoldItalic.HaloBlur = outlineBlur;
			styleBoldItalic.BackgroundColor = backgroundColor.a > 0 ? new SKColor(ColorToUint(backgroundColor)) : SKColors.Empty;
			styleBoldItalic.Underline = underlineStyle;
			styleBoldItalic.LineHeight = lineHeight;
			styleBoldItalic.StrikeThrough = strikeThroughStyle;
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
							return url.Value.link;
						}
					}
				}

				return "";
		}

		protected virtual void OnDestroy() {
			Dispose();
			if (texture != null) {
				#if !UNITY_EDITOR
				DestroyImmediate(texture);
				#else
				DestroyImmediate(texture);
				#endif
				texture = null;
			}


			if (skTypeface != null) {
				skTypeface.Dispose();
			}
		}
		
		protected virtual void OnDisable() {
			Dispose();
			#if !UNITY_EDITOR
				DestroyImmediate(rawImage.texture);
			#else
			DestroyImmediate(rawImage.texture);
			#endif
			
			if (texture != null) {
				#if !UNITY_EDITOR
				DestroyImmediate(texture);
				#else
				DestroyImmediate(texture);
				#endif
			}
		}

		protected virtual void Dispose() {
			if (pixmap != null) {
				pixmap.Dispose();
				pixmap = null;
			}
			
			if (surface != null) {
				surface.Dispose();
			}
			
			if (canvas != null) {
				canvas.Dispose();
				canvas = null;
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
				var bytes = font.bytes;
				SKData copy = SKData.CreateCopy(bytes);
				skTypeface = SKTypeface.FromData(copy);
				copy.Dispose();
				if (skTypeface != null) {
					rs.FontMapper = new FontMapper(skTypeface);
				}
			}
		}
	}
}