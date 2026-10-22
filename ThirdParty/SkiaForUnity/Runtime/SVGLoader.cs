using System;
using UnityEngine;
using Svg.Skia;
using UnityEngine.UI;

namespace SkiaSharp.Unity {
  public class SVGLoader : MonoBehaviour, ILayoutElement {
    [SerializeField]
    public TextAsset svgFile;
        
    private SKBitmap bmp;
    private SKSurface surface;
    private Texture2D texture;
    private RawImage rawImage;
    private int currentWidth, currentHeight;
    
    void Start() {
      RenderSVG();
    }

    public void RenderSVG() {
      if (svgFile == null) {
        return;
      }
      if (rawImage == null) {
        rawImage = GetComponent<RawImage>();
      }

      if (texture != null) {
        #if !UNITY_EDITOR
				DestroyImmediate(texture);
        #else
        DestroyImmediate(texture);
        #endif
      }
      
      using (var svg = new SKSvg())
      {
        if (svg.FromSvg(svgFile.text) is { }) {
          if (rawImage.GetComponent<RectTransform>().rect.width == 0 ||
              rawImage.GetComponent<RectTransform>().rect.height == 0) {
            return;
          }
          int roundedWidth = Mathf.CeilToInt(rawImage.GetComponent<RectTransform>().rect.width / 4f) * 4;
          int roundedHeight = Mathf.CeilToInt(rawImage.GetComponent<RectTransform>().rect.height / 4f) * 4;
          currentWidth = roundedWidth;
          currentHeight = roundedHeight;
          bmp = svg.Picture.ToBitmap(SKColor.Empty, currentWidth/svg.Picture.CullRect.Width, currentHeight/ svg.Picture.CullRect.Height, SKColorType.Rgba8888, SKAlphaType.Opaque, null);
          surface = SKSurface.Create(bmp.Info); 
          TextureFormat format = (bmp.Info.ColorType == SKColorType.Rgba8888) ? TextureFormat.RGBA32 : TextureFormat.BGRA32;
          texture = new Texture2D(roundedWidth, roundedHeight, format, false);
          texture.wrapMode = TextureWrapMode.Repeat;
          var pixmap = surface.PeekPixels();
          texture.LoadRawTextureData(bmp.GetPixels(), pixmap.RowBytes * pixmap.Height);
          texture.name = "SVG";
          texture.Apply();
          rawImage.texture = texture;
        }
      }
    }

    private void Update() {
      int roundedWidthNew = Mathf.CeilToInt(rawImage.GetComponent<RectTransform>().rect.width / 4f) * 4;
      int roundedHeightNew = Mathf.CeilToInt(rawImage.GetComponent<RectTransform>().rect.height / 4f) * 4;
      if (currentWidth != roundedWidthNew ||
          currentHeight != roundedHeightNew) {
        RenderSVG();
      }
    }

    public void CalculateLayoutInputHorizontal() { }

    public void CalculateLayoutInputVertical() { }

    public float minWidth { get; }
    public float preferredWidth => rawImage.GetComponent<RectTransform>().rect.width;
    public float flexibleWidth { get; }
    public float minHeight { get; }
    public float preferredHeight => rawImage.GetComponent<RectTransform>().rect.height;
    public float flexibleHeight { get; }
    public int layoutPriority { get; }
  }
}