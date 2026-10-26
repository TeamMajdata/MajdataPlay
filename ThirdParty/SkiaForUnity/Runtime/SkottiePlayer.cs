using System;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Animation = SkiaSharp.Skottie.Animation;

namespace SkiaSharp.Unity {
  /// <summary>
  /// Provides functionality to play and control Lottie animations in Unity.
  /// </summary>
  ///
  /// <example>
  /// <code>
  /// // Example usage within the Start method:
  /// [SerializeField]
  /// SkottiePlayer skottiePlayer;
  /// 
  /// void Start() {
  ///     // Set the desired animation state
  ///     skottiePlayer.SetState("YourStateName");
  ///     
  ///     // Start playing the animation
  ///     skottiePlayer.PlayAnimation();
  /// }
  /// </code>
  /// </example>
  
  [Obsolete ("Recommended using SkottiePlayerV2")]
  public class SkottiePlayer : MonoBehaviour {
  [SerializeField]
  private TextAsset lottieFile;
  [SerializeField]
  int resWidth = 250, resHeight = 250;
  [SerializeField] 
  private string stateName;
  [SerializeField] 
  private bool resetAfterFinished = false;
  [SerializeField] 
  private bool autoPlay = false;
  [SerializeField] 
  private bool loop = false;

  public UnityAction<string> OnAnimationFinished;

  private Animation currentAnimation;
  private SKCanvas canvas;
  private SKRect rect;
  private double timer = 0, animationFps, animationStateDuration;
  private SKImageInfo info;
  private SKSurface surface;
  private RawImage rawImage;
  private SpriteRenderer spriteRenderer;
  private Texture2D texture;
  private bool playAniamtion = false;
  private SkottieMarkers states;
  private SkottieMarkers.state currentState;
  private TimeSpan animationDuration;

  
  private void Start() {
    if (lottieFile == null) {
      return;
    }

    playAniamtion = playAniamtion || autoPlay;
    LoadAnimation(lottieFile.text);
  }

  void LoadTexture() {
    if (texture != null) {
      DestroyImmediate(texture);
      texture = null;
    }
    
    states = JsonConvert.DeserializeObject<SkottieMarkers>(lottieFile.text);
    animationFps = currentAnimation.Fps;
    animationDuration = currentAnimation.Duration;
      
    if (stateName.Length > 0 && states != null && states.markers.Count > 0) {
      currentState = states.GetStateByName(stateName);
      if (currentState != null) {
        animationStateDuration = (currentState.tm + currentState.dr) / animationFps;
      }
    }
    rawImage = GetComponent<RawImage>();
    if (rawImage == null) {
      spriteRenderer = GetComponent<SpriteRenderer>();
    }
    info = new SKImageInfo(resWidth, resWidth);
    surface = SKSurface.Create(info);
    rect = SKRect.Create(resWidth, resHeight);
    canvas = surface.Canvas;
    currentAnimation.SeekFrame(currentState?.tm ?? 0);
    currentAnimation.Render(canvas,rect);

    TextureFormat format = (info.ColorType == SKColorType.Rgba8888) ? TextureFormat.RGBA32 : TextureFormat.BGRA32;
    texture = new Texture2D(info.Width, info.Height, format, false);
    texture.wrapMode = TextureWrapMode.Repeat;
    var pixmap = surface.PeekPixels();
    texture.LoadRawTextureData(pixmap.GetPixels(), pixmap.RowBytes * pixmap.Height);
    texture.Apply();
    if (rawImage) {
      rawImage.texture = texture;
    } else {
      spriteRenderer.sprite = Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),Vector2.one*0.5f,100f,0);
    }
  }

  /// <summary>
  /// Loads an animation from a JSON string and prepares it for playback.
  /// </summary>
  /// <param name="json">The JSON string containing the animation data.</param>
  public void LoadAnimation(string json) {
    if (!Animation.TryParse(json, out currentAnimation)) {
      Debug.LogError("[SkottiePlayer] - wrong json file");
      return;
    }
    LoadTexture();
  }

  /// <summary>
  /// Sets the current state of the Lottie animation.
  /// </summary>
  /// <param name="name">The name of the state to set.</param>
  public void SetState(string name) {
      if (states != null && currentAnimation != null) {
        playAniamtion = false;
        currentState = states.GetStateByName(name);
        if (currentState != null) {
          animationStateDuration = (currentState.tm + currentState.dr) / animationFps;
          timer = currentState.tm/animationFps; 
          canvas.Clear();
          currentAnimation.SeekFrameTime(timer); 
          currentAnimation.Render(canvas,rect); 
          var pixmap = surface.PeekPixels(); 
          texture.LoadRawTextureData(pixmap.GetPixels(), pixmap.RowBytes * pixmap.Height); 
          texture.Apply(); 
        } else {
          Debug.LogError($"[SkottiePlayer] - SetState({name}), state not found!");
        }
      }
  }

  /// <summary>
  /// Gets the name of the current animation state.
  /// </summary>
  /// <returns>The name of the current state if available; otherwise, an empty string.</returns>
    public string GetStateName() {
      if (currentState != null) {
        return currentState.cm;
      }
      return "";
    }

  /// <summary>
  /// Gets the frames per second (FPS) of the loaded animation.
  /// </summary>
  /// <returns>The FPS of the loaded animation.</returns>
    public double GetFps() {
      return animationFps;
    }
  
  /// <summary>
  /// Gets the total duration of the loaded animation in seconds.
  /// </summary>
  /// <returns>The total duration of the loaded animation in seconds.</returns>
    public double GetDurations() {
      return animationDuration.TotalSeconds;
    }

  /// <summary>
  /// Initiates playback of the loaded animation.
  /// </summary>
  /// <param name="reset">Whether to reset the animation to the beginning when finished.</param>
    public void PlayAnimation(bool? reset = null) {
      playAniamtion = true;
      resetAfterFinished = reset == null ? resetAfterFinished : reset.Value;
    }
  

    private void Update() {
        if (playAniamtion == false || currentAnimation == null || canvas == null) {
          return;
        }
        
        timer += Time.deltaTime;
        if (currentState != null && timer >= animationStateDuration) {
          timer = resetAfterFinished || loop
            ? currentState.tm / animationFps
            : animationStateDuration;
          playAniamtion = loop;
          OnAnimationFinished?.Invoke(currentState?.cm);
        } else if (timer >= animationDuration.TotalSeconds) {
          timer = resetAfterFinished || loop ? 0 : timer;
          playAniamtion = loop;
          OnAnimationFinished?.Invoke(currentState?.cm);
        }

        canvas.Clear();
        currentAnimation.SeekFrameTime(timer);
        currentAnimation.Render(canvas, rect);
        var pixmap = surface.PeekPixels();
        texture.LoadRawTextureData(pixmap.GetPixels(), pixmap.RowBytes * pixmap.Height);
        texture.Apply();
    }

    private void OnDestroy() {
      if (texture != null) {
        DestroyImmediate(texture);
        texture = null;
      }

      if (currentAnimation != null) {
        currentAnimation.Dispose();
        currentAnimation = null;
      }

      if (canvas != null) {
        canvas.Dispose();
        canvas = null;
      }

    }
  }
}