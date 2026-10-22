@mainpage SkiaForUnity

# Getting Started

To get started with SkiaSharp for Unity, follow these steps:

1. Clone or download the SkiaSharp for Unity repository from [GitHub](https://github.com/ammariqais/SkiaForUnity), or use the Package Manager.

**Package Manager**
1. Open your Unity project and navigate to `Window` > `Package Manager`.
2. Click the `+` button in the top-left corner and select **Add package from git URL**.
3. Enter the following URL and click `Add`:

`https://github.com/ammariqais/SkiaForUnity.git?path=SkiaUnity/Assets/SkiaSharp`

**iOS Setup**

If building for iOS, add `__UNIFIED__` and `__IOS__` to your Scripting Define Symbols:
- Go to `Edit` > `Project Settings` > `Player` > iOS tab.
- Under `Other Settings`, add the symbols to the `Scripting Define Symbols` field.

# Components

## HB TextBlock

`HB_TEXTBlock` is the core text rendering component powered by HarfBuzz. It supports:

- **HarfBuzz text shaping** with native font rendering
- **Right-to-Left (RTL)** and bidirectional text
- **Emoji** support
- **Rich text** tags: `<b>`, `<i>`, `<u>`, `<s>`, `<sup>`, `<sub>`, `<size=N>`, `<color=#hex>`, `<color=name>`
- **Outline, Inner Glow, Drop Shadow** effects
- **Color gradients** with configurable angle and positions
- **Font fallbacks** for multi-language support
- **Auto-fit** vertical and horizontal sizing
- **Underline and Strikethrough** styles
- **Link rendering** with click callbacks
- **Background color**

### Adding to Scene

Use the menu: `GameObject` > `Skia UI (Canvas)` > `HB TextBlock`

This automatically creates a Canvas and EventSystem if needed.

### Key Properties

| Property | Description |
|---|---|
| `text` | The displayed text (supports rich text when enabled) |
| `FontSize` | Font size in pixels |
| `FontColor` | Text color |
| `Font` | Font asset (HBFontData or TextAsset .ttf/.otf) |
| `Bold` / `Italic` | Font style |
| `TextAlignment` | Left, Center, Right, Auto |
| `HaloWidth` | Outline width |
| `ShadowWidth` | Drop shadow width |
| `MaxWidth` / `MaxHeight` | Layout constraints |
| `LetterSpacing` | Additional spacing between characters |
| `alpha` | Transparency (via RawImage) |

### Rich Text Example

\code{.cs}
hbText.SetRichText("Hello <b>Bold</b> <i>Italic</i> <color=#FF0000>Red</color> <size=30>Big</size>");
\endcode

## HB InputField

`HBInputField` is a full-featured input field component similar to TMP_InputField, built on HB_TEXTBlock. It supports both desktop and mobile platforms.

### Adding to Scene

Use the menu: `GameObject` > `Skia UI (Canvas)` > `HB InputField`

This creates a complete hierarchy:
- **HB InputField** (background Image + HBInputField component)
  - **Text Area** (RectMask2D for clipping)
    - **Placeholder** (HB_TEXTBlock)
    - **Text** (HB_TEXTBlock)
      - **Caret** (Image)

### Features

- **Content types:** Standard, Integer, Decimal, Alphanumeric, Name, Email, Password
- **Line types:** SingleLine, MultiLine
- **Text settings:** Font, font size, color, bold, italic, alignment, rich text
- **Placeholder:** Customizable text and color
- **Caret:** Configurable color, blink rate, width, or hide completely
- **Selection:** Click, double-click (word), triple-click (all), Shift+arrows, Ctrl+A
- **Clipboard:** Ctrl+C, Ctrl+V, Ctrl+X
- **Navigation:** Arrow keys, Ctrl+arrows (word jump), Home, End
- **Tab navigation:** Tab/Shift+Tab to move between fields
- **Select all on focus:** Option to auto-select text on activation
- **Read-only mode:** Custom background and text color for read-only state
- **Focus highlight:** Change background color when focused
- **Auto-resize:** Automatically grow height for multiline content (with min/max bounds)
- **Text scrolling:** Horizontal scroll for single-line, vertical scroll for multiline
- **Mobile:** Hide native input overlay, TouchScreenKeyboard with content-type-specific keyboard
- **Password masking:** Configurable asterisk character
- **Character limit:** Max characters (0 = unlimited)
- **Events:** onValueChanged, onSubmit, onEndEdit, onFocus, onUnfocus

### Code Example

\code{.cs}
using SkiaSharp.Unity.HB;

public class MyUI : MonoBehaviour {
    [SerializeField] HBInputField inputField;

    void Start() {
        inputField.onValueChanged.AddListener(OnTextChanged);
        inputField.onSubmit.AddListener(OnSubmit);
    }

    void OnTextChanged(string value) {
        Debug.Log("Text: " + value);
    }

    void OnSubmit(string value) {
        Debug.Log("Submitted: " + value);
    }
}
\endcode

## HB Text Animator

`HBTextAnimator` adds sequential animation steps to any HB_TEXTBlock. Each step can combine multiple effects.

### Available Effects

| Effect | Description |
|---|---|
| Typewriter | Reveals text character by character |
| Fade | Animate alpha from/to |
| Color Lerp | Animate font color between two values |
| Scale | Animate transform scale |
| Slide | Slide in from an offset position |
| Shake | Shake with damping intensity |
| Gradient Angle | Animate gradient rotation |
| Outline Pulse | Animate outline width |
| Shadow Animate | Animate shadow offset |
| Font Size | Animate font size |
| Letter Spacing | Animate letter spacing |

### Multi-Step Animations

Each step has its own duration, delay, and ease curve. Steps play sequentially. Enable **Loop** to repeat the full sequence.

\code{.cs}
using SkiaSharp.Unity.HB;

public class AnimExample : MonoBehaviour {
    [SerializeField] HBTextAnimator animator;

    void Start() {
        animator.onComplete.AddListener(() => Debug.Log("All steps done"));
        animator.onStepComplete.AddListener(i => Debug.Log("Step " + i + " done"));
        animator.Play();
    }
}
\endcode

## Skottie Player

`SkottiePlayer` plays Lottie/Bodymovin JSON animations using the Skia Skottie library.

### Setup

1. Add a `RawImage` to your scene.
2. Add the `SkottiePlayer` component.
3. Assign your Lottie JSON file to the `lottieFile` field.

### Code Example

\code{.cs}
using SkiaSharp.Unity;

public class LottieExample : MonoBehaviour {
    [SerializeField] SkottiePlayer skottiePlayer;

    void Start() {
        skottiePlayer.SetState("MyState");
        skottiePlayer.PlayAnimation();
        skottiePlayer.OnAnimationFinished += OnFinished;
    }

    void OnFinished(string stateName) {
        Debug.Log("Animation finished: " + stateName);
    }
}
\endcode

## SVG Loader

SkiaForUnity supports rendering SVG graphics using SVG.Skia.

- Scalable vector graphics at any resolution
- Cross-platform SVG rendering
- Use `SKSvg` to load and draw SVG files

# Platform Support

| Platform | Status |
|---|---|
| Windows | Supported |
| macOS | Supported |
| Linux | Supported |
| iOS | Supported (requires `__UNIFIED__` + `__IOS__` defines) |
| Android | Supported |

# Requirements

- Unity 2019.3 or later
- `com.unity.nuget.newtonsoft-json` 3.2.0+ (auto-resolved)
