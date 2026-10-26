@page subsytem1 Tutorials
@tableofcontents

# HB TextBlock

## Basic Text Rendering

1. In your scene, go to `GameObject` > `Skia UI (Canvas)` > `HB TextBlock`.
2. Select the created object and type your text in the **Text** field.
3. Adjust **Font Size**, **Font Color**, and **Alignment** in the Inspector.

## Using a Custom Font

1. Import a `.ttf` or `.otf` font file into your project.
2. Right-click the font file and select `Create` > `HBFontData` to create a font asset.
3. Drag the HBFontData asset onto the **Font** field of the HB_TEXTBlock component.

You can also drag a `.ttf`/`.otf` file directly onto the Font field and an HBFontData asset will be created automatically.

## Font Fallbacks

For multi-language text (e.g., mixing Latin and Arabic), add additional font assets to the **Fallback Fonts** array. HarfBuzz will automatically select the correct font for each character.

## Rich Text

Enable the **Rich Text** checkbox, then use tags in your text:

\code{.cs}
hbText.SetRichText("Normal <b>Bold</b> <i>Italic</i> <u>Underline</u> <s>Strike</s>");
hbText.SetRichText("<color=#FF0000>Red</color> <color=blue>Blue</color>");
hbText.SetRichText("<size=40>Big</size> and <size=12>small</size>");
hbText.SetRichText("H<sub>2</sub>O and x<sup>2</sup>");
\endcode

**Supported tags:**
- `<b>` Bold
- `<i>` Italic
- `<u>` Underline
- `<s>` Strikethrough
- `<sup>` Superscript
- `<sub>` Subscript
- `<size=N>` Font size
- `<color=#RRGGBB>` Hex color
- `<color=name>` Named color (red, green, blue, white, black, yellow, cyan, magenta, orange, grey)

## Text Effects

### Outline
Set **Outline Width** > 0 to add a text outline. Configure **Outline Color** (gradient) and **Outline Blur**.

### Drop Shadow
Set **Shadow Width** > 0. Adjust **Shadow Offset X/Y** and **Shadow Color**.

### Inner Glow
Set **Inner Glow Width** > 0 and choose an **Inner Glow Color**.

### Color Gradient
Enable **Color Gradient** and set gradient colors, positions, and angle.

## Scripting

\code{.cs}
using SkiaSharp.Unity.HB;
using UnityEngine;

public class TextExample : MonoBehaviour {
    [SerializeField] HB_TEXTBlock hbText;

    void Start() {
        hbText.text = "Hello World";
        hbText.FontSize = 32;
        hbText.FontColor = Color.white;
        hbText.Bold = true;

        // Listen for text changes
        hbText.onTextChanged.AddListener(() => Debug.Log("Text updated"));
    }
}
\endcode

---

# HB InputField

## Creating an InputField

1. Go to `GameObject` > `Skia UI (Canvas)` > `HB InputField`.
2. A complete input field hierarchy is created automatically.

## Inspector Sections

### Input Settings
- **Text** - The current input value
- **Character Limit** - Max characters (0 = unlimited)
- **Content Type** - Standard, Integer, Decimal, Alphanumeric, Name, Email, Password
- **Line Type** - SingleLine or MultiLine
- **Read Only** - Prevents editing, allows selection/copy

### Text Settings
- **Font** - Font asset
- **Font Size** - Size in pixels
- **Font Color** - Text color
- **Style** - Bold (B) and Italic (I) toggle buttons
- **Alignment** - Left, Center, Right, Auto
- **Rich Text** - Enable rich text tag rendering

### Placeholder
- **Placeholder Text** - Shown when input is empty
- **Placeholder Color** - Color of placeholder text

### Behavior
- **Select All on Focus** - Auto-select all text when field is activated
- **Tab Navigation** - Tab/Shift+Tab moves to next/previous field

### Appearance
- **Read-Only Colors** - Custom background and font color for read-only state
- **Highlight on Focus** - Change background color when focused

### Auto Resize (MultiLine only)
- **Auto Resize** - Grow height to fit text content
- **Min Height / Max Height** - Bounds for auto-resizing

### Mobile
- **Hide Mobile Input** - Hides the native input overlay on iOS/Android (recommended)

### Caret
- **Hide Caret** - Completely hide the blinking cursor
- **Color, Blink Rate, Width** - Caret appearance
- **Selection Color** - Highlight color for selected text

### Events
- **onValueChanged** - Fired when text changes
- **onSubmit** - Fired on Enter (single-line) or Ctrl+Enter (multi-line)
- **onEndEdit** - Fired when the field loses focus
- **onFocus** - Fired when the field gains focus
- **onUnfocus** - Fired when the field loses focus

## Scripting

\code{.cs}
using SkiaSharp.Unity.HB;
using UnityEngine;

public class InputExample : MonoBehaviour {
    [SerializeField] HBInputField inputField;

    void Start() {
        inputField.text = "Initial text";

        inputField.onValueChanged.AddListener(value => {
            Debug.Log("Changed: " + value);
        });

        inputField.onSubmit.AddListener(value => {
            Debug.Log("Submitted: " + value);
        });

        inputField.onFocus.AddListener(() => {
            Debug.Log("Input focused");
        });
    }
}
\endcode

## Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| Ctrl+A | Select all |
| Ctrl+C | Copy |
| Ctrl+V | Paste |
| Ctrl+X | Cut |
| Ctrl+Left/Right | Jump by word |
| Shift+Arrows | Extend selection |
| Home / End | Move to start/end |
| Tab / Shift+Tab | Navigate to next/previous field |
| Double-click | Select word |
| Triple-click | Select all |

---

# HB Text Animator

## Adding Animations

1. Select a GameObject with an `HB_TEXTBlock` component.
2. Add the `HBTextAnimator` component (or use `Add Component` > `Skia UI (Canvas)` > `HB Text Animator`).

## Animation Steps

Each animation is composed of one or more **steps** that play sequentially. Each step can combine multiple effects.

### Step Settings
- **Name** - Label for the step (for organization)
- **Duration** - How long the step takes (seconds)
- **Delay** - Wait before starting this step
- **Ease Curve** - AnimationCurve for easing

### Effects per Step

| Effect | Fields |
|---|---|
| **Typewriter** | Reveals text character by character |
| **Fade** | From alpha, To alpha |
| **Color Lerp** | From color, To color |
| **Scale** | From scale (Vector3), To scale |
| **Slide** | Offset (Vector2) - slides in from offset to current position |
| **Shake** | Intensity - decaying random offset |
| **Gradient Angle** | Start angle, End angle |
| **Outline Pulse** | From width, To width |
| **Shadow Animate** | From offset (Vector2), To offset |
| **Font Size** | From size, To size |
| **Letter Spacing** | From spacing, To spacing |

## Example: Two-Step Animation

1. **Step 1** - Fade in (0 to 1) + Slide in from left over 0.5s
2. **Step 2** - Color lerp (white to gold) + Scale pulse over 1s

Steps play one after another. Enable **Loop** to repeat the full sequence.

## Editor Preview

Click **Preview** in the Inspector to preview the animation without entering Play Mode. The preview runs through all steps with a progress bar showing the current step.

## Scripting

\code{.cs}
using SkiaSharp.Unity.HB;
using UnityEngine;

public class AnimatorExample : MonoBehaviour {
    [SerializeField] HBTextAnimator animator;

    void Start() {
        // Auto-play is on by default, or call manually:
        animator.Play();

        animator.onStepComplete.AddListener(stepIndex => {
            Debug.Log("Step " + stepIndex + " completed");
        });

        animator.onComplete.AddListener(() => {
            Debug.Log("All steps completed");
        });
    }

    public void StopAnimation() {
        animator.Stop(); // Restores original state
    }
}
\endcode

---

# Skottie (Lottie) Player

## Play Animation With State

1. Create a `RawImage` in your scene (right-click Hierarchy > UI > Raw Image).
2. Add the `SkottiePlayer` component to the RawImage.
3. Assign your Lottie JSON file to the **lottieFile** field.

\code{.cs}
using SkiaSharp.Unity;

[SerializeField] SkottiePlayer skottiePlayer;

void Start() {
    skottiePlayer.SetState("YourStateName");
    skottiePlayer.PlayAnimation();
}
\endcode

## Animation Finished Callback

\code{.cs}
using UnityEngine;
using SkiaSharp.Unity;

public class AnimationController : MonoBehaviour {
    [SerializeField] SkottiePlayer skottiePlayer;

    void Start() {
        skottiePlayer.OnAnimationFinished += OnAnimationFinished;
        skottiePlayer.PlayAnimation();
    }

    void OnAnimationFinished(string stateName) {
        Debug.Log("Animation Finished: " + stateName);
    }

    void OnDestroy() {
        skottiePlayer.OnAnimationFinished -= OnAnimationFinished;
    }
}
\endcode

Read more about [SkottiePlayer](class_skia_sharp_1_1_unity_1_1_skottie_player.html)

---

# SVG Rendering

SkiaForUnity supports SVG rendering via SVG.Skia. SVG graphics scale to any resolution without quality loss.

---

# Menu Reference

All SkiaForUnity components are available under `GameObject` > `Skia UI (Canvas)`:

| Menu Item | Component |
|---|---|
| HB TextBlock | Creates an HB_TEXTBlock with Canvas/EventSystem |
| HB InputField | Creates a full input field hierarchy |

Components can also be added via `Add Component`:

| Menu Path | Component |
|---|---|
| Skia UI (Canvas) / HB TextBlock | HB_TEXTBlock |
| Skia UI (Canvas) / HB InputField | HBInputField |
| Skia UI (Canvas) / HB Text Animator | HBTextAnimator |
