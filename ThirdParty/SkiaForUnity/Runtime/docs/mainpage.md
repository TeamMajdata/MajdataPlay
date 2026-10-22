@mainpage SkiaForUnity

# Getting Started

To get started with Skia Sharp for Unity, follow these steps:

1. Clone or download the Skia Sharp for Unity repository from [GitHub](https://github.com/ammariqais/SkiaForUnity). or you can use

**Package Manager**
1. Open your Unity project and navigate to `Windows`> `Package Manager`.
2. Click the `+` button in the top-left corner and select Add package from git URL
3. Enter the following URL and click `Add`

`https://github.com/ammariqais/SkiaForUnity.git?path=SkiaUnity/Assets/SkiaSharp`


3. Import the Skia Sharp package into your Unity project.

4. In Unity, go to Edit > Project Settings > Player and select the iOS platform.

5. Scroll down to the Other Settings section and locate the Scripting Define Symbols field.

6. Add `__UNIFIED__` and `__IOS__` to the Scripting Define Symbols. This is necessary to enable the SkiaSharp functionality specifically for iOS.

7. Create a new script or modify an existing script to leverage the SkiaSharp API for drawing graphics and the Skottie API for playing Lottie       animations.

8. Build and run your Unity project on an iOS device to see the Skia graphics and Lottie animations in action.
