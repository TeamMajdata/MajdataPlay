@page subsytem1 Skottie Tutorials
@tableofcontents

# Play Animation With State
To use the `SkottiePlayer` component, follow these steps:

1. **Create a RawImage:**

   In your Unity scene, create a `RawImage` object to display the Skottie animation. You can do this by right-clicking in the Hierarchy panel, selecting "UI," and choosing "Raw Image."

2. **Attach SkottiePlayer Component:**

   Select the `RawImage` you created. In the Inspector panel, click the "Add Component" button and search for "Skottie Player" to add the `SkottiePlayer` component to the `RawImage` object.

3. **Assign Animation JSON Data:**

   In the Inspector panel, you'll find the `SkottiePlayer` component you just added. Inside the `SkottiePlayer` component, you'll see a field named "lottieFile" This is where you can assign a TextAsset containing your animation JSON data. To do this, drag and drop your JSON file into the "Animation Json File" field.

4. **Implement Start Method:**

   In your script, you can implement the `Start` method to load the animation, set the desired animation state, and start playback. Here's an example:


\code{.java}

// Example usage within the Start method:
[SerializeField]
SkottiePlayer skottiePlayer;

void Start() {
// Set the desired animation state
skottiePlayer.SetState("YourStateName");

    // Start playing the animation
    skottiePlayer.PlayAnimation();
}
\endcode





# Using CallBack OnAnimationFinishedHandler

1. Attach the `AnimationController` script to a RawImage/SpriteRender object in your Unity scene.
2. Assign a reference to the SkottiePlayer component to the `skottiePlayer` field in the Unity Inspector.
3. Customize the `OnAnimationFinishedHandler` method to define actions to take when an animation finishes.
4. Run your Unity project, and the `AnimationController` will control animations using the SkottiePlayer component.

## Example

\code{.java}
// Attach the AnimationController script to a GameObject and assign the SkottiePlayer reference in the Inspector.

using UnityEngine;
using SkiaSharp.Unity;
public class AnimationController : MonoBehaviour {
    [SerializeField]
    private SkottiePlayer skottiePlayer; // Reference to the SkottiePlayer component

    private void Start() {
        // Subscribe to the OnAnimationFinished event
        skottiePlayer.OnAnimationFinished += OnAnimationFinishedHandler;

        // Play the animation (you can call this method when needed)
        skottiePlayer.PlayAnimation();
    }

    private void OnAnimationFinishedHandler(string animationStateName) {
        // This method will be called when the animation finishes playing

        // You can check the animationStateName if needed
        Debug.Log("Animation Finished: " + animationStateName);

        // Do something when the animation finishes, e.g., play another animation or trigger an event.
    }

    private void OnDestroy() {
        // Unsubscribe from the event when this GameObject is destroyed
        skottiePlayer.OnAnimationFinished -= OnAnimationFinishedHandler;
    }
}

\endcode

Read more about [SkottePlayer.cs](class_skia_sharp_1_1_unity_1_1_skottie_player.html)



