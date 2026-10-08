# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).


## [5-r.5] - 2026-04-02

### Added

* Added Cubism Core for ARM64 iOS Simulator.
  * See `CHANGELOG.md` in Core.

### Changed

* Update each model's prefab to match environment updates.
* Change to allow specifying `AnimatorController` in scenes under `Assets/Live2D/Cubism/Samples/AsyncBenchmark`.
* Change to explicitly set texture sampler settings.
* Change the version of the development project to `6000.0.68f1`.
* Change to work with the `Input System` package.
* Change property names related to multiply color and screen color.

### Fixed

* Fix an issue in URP where Cubism models could not be picked in the Scene View.
* Fix an issue in URP where selected Cubism models were not highlighted with an outline in the Scene View.
* Fix an issue where the scaling reference position was set to the center of the rendering area.
* Fix incorrect quaternion composition in `CubismRenderer`.
* Fix an issue where the model would disappear when regaining Editor focus if `Run In Background` was disabled.
* Fix an issue where the import process would run for models imported under the `StreamingAssets` folder. by [@redwyre](https://github.com/Live2D/CubismUnityComponents/pull/90)
* Fix an issue where the `Koharu` model used in scenes under `Assets/Live2D/Cubism/Samples` was outdated.
* Fix an issue where `CubismPartColorsEditor` was using incorrect flags for determination.
* Fix an issue where the re-import process for a model did not support increasing or decreasing parameters and other elements.
* Fix CubismImporterBase.Save so that it no longer performs a reimport when saving.
* Fix the importer to import motion3.json first, followed by model3.json, and then all other assets.
* Fix PoseMotionImporter processing to accommodate the new import order.
* Fix an issue where specifying an override in `CubismParameterExtensionMethods.BlendToValue()` caused the applied value to be the current value of that parameter. by [@LoS-Light](https://github.com/Live2D/CubismUnityComponents/pull/89)
* Fix an issue where offscreen rendering of models was not functioning correctly with Z Buffer in Reversed Z environments.


## [5-r.5-beta.3] - 2026-01-08

### Added

* Add scripts and assets for using the `Universal Render Pipeline` and custom render passes to `Assets/Live2D/Cubism/Rendering/URP`.
* Add a feature to allow rendering of arbitrary objects before and after the rendering of model draw objects.
* Add `CubismLookCenterTransform` and `CubismLookCenterArtMesh` to allow setting the look-at center using either a Transform or an ArtMesh.

### Changed

* Change to work based on the `Universal Render Pipeline`.
  * `Built-in Render Pipeline` and `High Definition Render Pipeline` are not supported.
    * `Cubism 5 SDK for Unity R4_1` and its derivative packages will be maintained as `Built-in Render Pipeline` compatible versions.
      * Models and features from `Cubism 5.3` and later cannot be used.
  * The use of custom render passes is required.

### Fixed

* Fix an issue where `ReleaseStaleRenderTextures()` could release `RenderTexture` in use depending on the timing of its invocation.
* Fix an issue where the texture settings were not as intended when imported.
* Fix an issue where look-at tracking was misaligned when using `CubismLookController`.
* Fix an issue where the Koharu model's .model3.json was missing references to .motion3.json.
* Fix Koharu's corrupted .motion3.json files.
* Fix an issue where Raycast was functional even when `CubismRaycastable` was inactive.

### Removed

* Remove unused shaders and materials.
* Remove `Assets/Live2D/Cubism/Rendering/CubismCommonRenderFrameBuffer.cs` and `Assets/Live2D/Cubism/Rendering/ArrayExtensionMethods.cs`.
* Remove `Assets/Live2D/Cubism/Rendering/Resources/Live2D/Cubism/Prefabs/ModelCanvas.prefab`.
* Remove scripts and related processes under `Assets/Live2D/Cubism/Rendering/Masking` and `Assets/Live2D/Cubism/Samples/Masking`.
  * Remove `Assets/Live2D/Cubism/Editor/Inspectors/CubismMaskControllerInspector.cs`, `Assets/Live2D/Cubism/Editor/Inspectors/CubismMaskTextureInspector.cs`, and `Assets/Live2D/Cubism/Rendering/Resources/Live2D/Cubism/GlobalMaskTexture.asset` accordingly.
  * Mask processing has been integrated into `CubismRenderer`.


## [5-r.5-beta.2] - 2025-10-14

### Changed

* Change to a method where `RenderTexture` for offscreen is pre-created in small numbers and reused.
* Change the version of the development project to `6000.0.58f2`.

### Fixed

* Fix an issue where objects were not rendered when redisplayed.
* Fix an issue where offscreen was not affected by `OverrideFlagForModelMultiplyColors` and `OverrideFlagForModelScreenColors`.
* Fix an issue where collision detection remained active even when the collision detection Drawable was hidden.
* Fix an issue where pressing the reset button in the `CubismParametersInspector` would revert to the state before the reset when running the scene.
* Fix a bug that hit detection does not match the model’s visual appearance.
* Fix a bug that the model may be partially clipped when rendered.
* Fix the size of models using features introduced in `Cubism 5.3` differed from models created in prior to `Cubism 5.3`.
* Fixed an issue where models for `Cubism 5.3` were not rendered correctly in environments where `UNITY_REVERSED_Z` is enabled.


## [5-r.5-beta.1] - 2025-08-26

### Added

* Add support for Blend mode and Offscreen drawing.
  * If the model uses blend mode or offscreen drawing, it will be rendered using a new drawing method that differs from `Cubism 5.2` and earlier.
* Add high-precision mask method.
  * This is only available with the new rendering method in `Cubism 5.3` and later, and masks in the new rendering method from `Cubism 5.3` onward will automatically use this method.

### Changed

* Change the version of the development project to `6000.0.55f1`.
* Change `_isOverriddenDrawableScreenColors` and `_isOverriddenDrawableMultiplyColors` to `_isOverriddenDrawObjectScreenColors` and `_isOverriddenDrawObjectMultiplyColors`.
  * The property names that use these backing fields have also been updated accordingly.
  * Serialization of `_isOverriddenDrawableScreenColors` and `_isOverriddenDrawableMultiplyColors` is maintained by the `FormerlySerializedAs` attribute.

### Removed

* Remove `CubismCoreDll.GetDrawableRenderOrders` and `CubismUnmanagedDrawables.RenderOrders`.
  * If you need to use these functions, please use `CubismModel.AllDrawObjectsRenderOrder` instead.
    * See `CHANGELOG.md` in Core.
* Remove Chrome OS from the tested environment.


## [5-r.4.1] - 2025-07-17

### Changed

* Implement support for Android 16KB page size.

### Fixed

* Fix a bug that prevented builds from working under certain settings.


## [5-r.4] - 2025-05-29

### Added

* Add an empty `AnimatorController` at the same hierarchy level as each model's Prefab.
* Add an API to `CubismMotionJson` for verifying the consistency of `motion3.json`. by [@pillowtrucker](https://github.com/Live2D/CubismNativeFramework/pull/57)
* Add parameter repeat processing that connects the right and left ends of the parameter to create a loop, allowing the motion to repeat.
  * Add the variable `_isOverriddenParameterRepeat` to the `CubismModel` class for managing parameter repeat flags at the model level.
  * Add variables `_isOverriddenUserParameterRepeat` to the `CubismModel` class and `_isParameterRepeated` to the `CubismParameter` class for managing parameter repeat flags for each parameter.

### Changed

* Change the version of the development project to `2022.3.59f1`.
* Change CubismMath class not to inherit from MonoBehaviour.
* Change `CubismMoc._bytes` not to be displayed in Inspector.

### Fixed

* Fix `CubismPhysicsController` to be in the correct disabled state.
* When performing a Reimport Fixed a problem that `.cdi3.json` updates were not reflected in `CubismDisplayInfoParameterName`,`CubismDisplayInfoPartName`.
* Fix a problem in which the slider of CubismParametersInspector and CubismPartsInspector in the Inspector window did not move when operated.
* Fix a bug that CubismParametersInspector and CubismPartsInspector do not display the names of parameters and parts if .cdi3.json is not set on the model.
* Fix `CubismParameterInspector` and `CubismPartInspector` can be operated in play mode.
* Fix a bug that prevented MotionFade fade-out from working properly.
* Fix unnecessary processing in `CubismFadeController`.
* Fix so that end events are sent for all clips played by `CubismMotionController`.
* Fix an error that occurred when playing a expression that uses parameters that do not exist in the model.


## [5-r.3] - 2024-11-28

### Added

* Add processing to parse `CombinedParameters` from `.cdi3.json` file.
  * Also add `CubismDisplayInfoCombinedParameterInfo` component to store this information.
* Add `AnimationBeganHandler`, which calls a function when the motion playback starts, to both `CubismMotionController` and `CubismMotionLayer`.
* Add the definition of the function `GetLogFunction` to `CubismCoreDll`.

### Changed

* Change an expression "overwrite" to "override" for multiply color, screen color, and culling to adapt the actual behavior.
* Change `OnGeneratedCSProjectFiles` function to be disabled in Unity 2017.3 and later by [@moz-moz](https://github.com/moz-moz)
* Change to a single function The initialization process in CubismModel class. by [@KT](https://creatorsforum.live2d.com/t/topic/2356/)
* Change to optimize update process for Multiply Color and Screen Color.
* Change argument type of `CubismMotionLayer.AnimationEndHandler` and `CubismMotionController.AnimationEndHandler` to match the current usage.
* Move the definition of `csmSetLogFunction` from `CubismLogging` to `CubismCoreDll` and rename to `SetLogFunction`.
* Raycast processing now supports 3D intersection determination.
* In the `AsyncBenchmark` scene and `AutomaticAsyncBenchmark`, changed it so that asynchronous processing is not performed in WebGL.

### Fixed

* The fade in and fade out functionality has been improved to better maintain compatibility with other CubismSDK implementations.
* Fix an issue in which when the number of textures for masks was increased, masks were not generated for the increased number of textures from the time of execution.
* Fix an issue that caused an error when parsing Json if the name of the model had certain characters.
* Fix the condition for setting the fade-out end time for Expression.

## [5-r.2] - 2024-04-04

### Added

* Add HarmonyOS NEXT from the tested environment.

### Fixed

* Fix an issue where the `Allow 'unsafe' Code` property was not enabled in the `Live2D.Cubism.asmdef`.


## [5-r.1] - 2024-03-26

### Added

* Add `CubismMath` class in namespace `Live2D.Cubism.Framework.Utils`.
* Add function `ModF()` to compute floating-point remainder in `CubismMath` class.

### Changed

* Change the message to output the latest Moc version supported by the CubismCore when the `.moc3` file fails to load correctly.
* Change the version of the development project to `2021.3.36f1`.

### Deprecated

* The `ToIndex()` and `ReturnTiles()` functions of the `CubismMaskTilePool` class are not used.

### Fixed

* Fix fade calculation bug in MotionFade.
* Fix a bug in which masks were not generated correctly when using multiple render textures and displaying two or more models.
* Fix an issue where normal processing could not be performed when `CubismMaskTilePool.Subdivisions` is less than `1`.

### Removed

* Remove `CubismWebGLPluginProcessor.cs`.
 * This change is due to the removal of Cubism Core built with `Emscripten 1.38.48`.
 * See `CHANGELOG.md` in Core.


## [5-r.1-beta.4] - 2024-01-18

### Added

* Add `CubismDrawableInspector` and parent Part display.
* Add `CubismPartInspector` to show parent and descendant trees.
* Add expression to check for index out of range with `CubismRenderer.OnMaskPropertiesDidChange()`.

### Changed

* Change `CubismPartsInspectorInspector` to be hierarchical.

### Fixed

* Fix an issue where models with a specific number of masks could not be drawn correctly.

## [5-r.1-beta.3] - 2023-11-14

### Added

* Add `HarmonicMotion` sample scenes.

### Changed

* Change the version of the development project to `2021.3.30f1`.
* Change the value of `Editor` to `AnyCPU` in the `Platform settings` of `Live2DCubismCore.bundle`.
  * Apple Silicon version of the Unity Editor will work without the need to change `Platform settings`.

### Fixed

* Fix an error when displaying CubismRendererInspector for uninitialized models.
* Fix condition for clearing AnimationCurve when Reimporting .motion3.json.
* Fix fade calculation bug in MotionFade.


## [5-r.1-beta.2] - 2023-09-28

### Added

* Added configuration file so that assemblies are split.

### Changed

* Replace the sample model `Mao` with the updated version that is compatible with Cubism 5.0.

### Fixed

* Fix an issue where 1 byte of memory was allocated unnecessarily.
* Fix a bug where automatic eye blinking did not close the eyes fully.


## [5-r.1-beta.1] - 2023-08-17

### Changed

* When importing a Cubism Model in Unity Workflow, the AnimatorController is now set to the Animator in the Model Prefab.
* Change so that multiply and screen colors applied to parent parts are propagated to child parts.

### Fixed

* Fix an issue where information was being got using indexes instead of IDs when getting cdi3.json data.
* Fix a bug that prevented proper operation when the Unity Transition was longer than the motion fade.


## [4-r.7] - 2023-05-25

### Added

* The number of render textures used can now be increased arbitrarily.
  * The maximum number of masks when using multiple render textures has been increased to "number of render textures * 32".
  * You can also continue to use the pre-R6_2 method.
* Importing a model now generates a MaskTexture asset containing the model's name.
  * It is generated only if the model prefab has not been generated.
* Add the function of checking consistency on importing a MOC3. (`CubismMoc.CreateFrom`)
  * This feature is enabled by default.Please see the following manual for more information.
    * https://docs.live2d.com/cubism-sdk-manual/moc3-consistency/
* Add component for changing Multiply Color / Screen Color from parent part.
  * Components are automatically added to each part object of the model when the model is imported.

### Fixed

* Fix to improve physics and rendering performance. by [@ppcuni](https://github.com/ppcuni)
* Fix an issue with `ResetSwapInfoFlags` function where flags were not initialized correctly. by [@ppcuni](https://github.com/ppcuni)


## [4-r.6.2] - 2023-03-16

### Fixed

* Fix some problems related to Cubism Core.
  * See `CHANGELOG.md` in Core.


## [4-r.6.1] - 2023-03-10

### Added

* Add function to validate MOC3 files.


## [4-r.6] - 2023-01-26

### Added

* Add the feature to search for Subrig objects by the name of the physics group. by [@ppcuni](https://github.com/ppcuni)
* Add the feature to set the magnification of the output of the physics group uniformly to an arbitrary value. by [@ppcuni](https://github.com/ppcuni)
* Add the feature to uniformly set the inversion flag for the output of the physics group. by [@ppcuni](https://github.com/ppcuni)
* Add editor extension to the Inspector of CubismPhysicsController to display physics group names. by [@ppcuni](https://github.com/ppcuni)
  * To apply this feature to sample models, we regenerated sample models prefab, etc.

### Changed

* Sample model assets are brought up to date.
* Change the version of the development project to `2020.3.41f1`.

### Fixed

* Fix a bug that caused a warning to appear when importing a model with an unmatched Object Name.
* Fix an issue where unwanted curves on `.anim` were not correctly erased when re-importing a model where Pose3.json exists.
* Fix wrong condition for getter in HandlerInterface of Multiply Color and Screen Color. by [@qualice-hirakawa](https://github.com/qualice-hirakawa)
* After setting the multiply color (or screen color) and related flags on the inspector, the changes are now retained when starting play mode. by [@qualice-hirakawa](https://github.com/qualice-hirakawa)
* Fix a bug that caused `.fadeMotionList` to be created incorrectly when loading a model containing `.pose3.json`, depending on the order in which the `CubismPoseMotionImporter` was executed.


## [4-r.5] - 2022-09-08

### Added

* Add Unity 2022 to the development environment.
* Add the multilingual supported documents.
* Add immediate stabilization of physics.

### Removed

* Remove Unity 2019 from the development environment.


## [4-r.5-beta.5] - 2022-08-04

### Added

* Add a feature to change the value of components in batches.

### Changed

* Update `Mao` model.

### Fixed

* Fix crash with exception when reading .moc3 files of unsupported versions.
  * The console now produces an explicit error log instead of an exception.
* Fix physics system input to be split by the physics setting time.
* Fix a bug in which processing was interrupted when an invalid vertex was specified in the middle of a physics operation.


## [4-r.5-beta.4] - 2022-07-07

### Added

* Add properties to get the latest .moc3 version and the .moc3 version of the loaded model in CubismMoc.
* Add `Mao` model.
* Add `Demo4.2` scene to run `Mao` model.
* Add a function to get the type of parameters of the model.
* Add a function to get the parent part of the model's Drawable.

### Fixed

* Fix `GetMocVersion` function argument in `CoreDll` class that was different from the original Core API argument.
* Fix that does not occur error when Sorting Mode of CubismRenderController is changed in the Project window.


## [4-r.5-beta.3] - 2022-06-16

### Changed

* Change the version of the development project to `2019.4.39f1`.

### Fixed

* Fix physics system behaviour when exists Physics Fps Setting in .physics3.json.


## [4-r.5-beta.2] - 2022-06-02

### Fixed

* Fixed a bug that caused Multiply Color / Screen Color of different objects to be applied.
  * See `CHANGELOG.md` in Core.
  * No modifications to Samples and Framework.


## [4-r.5-beta.1] - 2022-05-19

### Added

* Add support to prevent exceptions depending on the presence or absence of parameters. by [@ppcuni](https://github.com/ppcuni)
* Add Weight argument to BlendToValue function. by [@ppcuni](https://github.com/ppcuni)
* Add processing related to Multiply Color / Screen Color added in Cubism 4.2.

### Fixed

* Fix for changes `EditorUserBuildSettings` in 2021.2 and later.


## [4-r.4.2] - 2022-03-09

### Fixed

* Fix an issue where inversion masks did not work properly and the mask limit was different from normal.


## [4-r.4.1] - 2022-02-10

### Added

* Added an editor extension that switches the library depending on Unity version when exporting to WebGL.


## [4-r.4] - 2021-12-09

### Added

* Add Unity 2021 to the development environment.
* Add the function to set assets to a component when importing a model.
* Add Chrome OS from the tested environment.

### Changed

* Change the version of the development project to `2019.4.29f1`.
* Change `Enable Async` in `AsyncBenchmark` scene to be disabled by default.
* Change the sample scene in `Samples/OW/Expression` so that the expressions are displayed in the order of the elements in `expressionList.asset`.
* Change `UserData` that it can be edited from Inspector.
* Change multiple `UserData` can be edited in bulk from the inspector.

### Fixed

* Fix an issue with duplicate elements in the Expression List.
* Fix the elements of `expressionList.asset` to be empty when importing in Unity 2021 and 2020.
* Fix to keep `Layer` at reimport.
* Fix `CubismMotionController` do not be removed at reimport.

### Removed

* Remove Unity 2018 from the development environment.


## [4-r.3] - 2021-06-10

### Added

* Add a function to read data from `.cdi3.json`. by [@ShigemoriHakura](https://github.com/ShigemoriHakura)
* Add a function to display the names of parameters and parts described in `.cdi3.json`. by [@ShigemoriHakura](https://github.com/ShigemoriHakura)
* Add a function to change the display name of parameters and parts to any name.
* Add a display of elapsed time to the sample scene `AsyncBenchmark`.
* Add a sample scene that manipulates the number of models to be displayed to achieve the specified frame rate.

### Changed

* Change to continue importing when an error occurs. [@TakahiroSato](https://github.com/TakahiroSato)

### Fixed

* Fix failing the model generation when importing with Unity 2020.
* Fix registering the dynamically selection of the index for `PlayerLoopSystem.subSystemList`.
* Fix the updating process for InstanceId; Update the InstanceId without adding the element of .fadeMotionList when the InstanceId that is registered the AnimationClip was changed.
* Fix import of models with invalid masks. by [@DenchiSoft](https://github.com/DenchiSoft), [@ShigemoriHakura](https://github.com/ShigemoriHakura).


## [4-r.2] - 2021-01-12

### Added

* Add Unity 2020 to the development environment.
* Add a process that applies the Culling configuration from Model Data.
* Add an `AnimatorController` generator that is already set `CubismFadeStateObserver`.

### Changed

* Change the setting of Player Loop customized by other assets not to be rewritten in Unity 2019.3 or later.
* Change to get the path of the audio files associated with `.motion3.json` from `.model3.json`.
* Change to enable the appropriate Cubism iOS plugin before building.
* Change the handling of `UnmanagedArrayView` pointers due to improve the performance. [@ppcuni](https://github.com/ppcuni)
* Change the default importing mode to `Original Workflow`.
  * Note: Importing this version into an older version SDK will override this setting.
    see [Cubism SDK Manual page](https://docs.live2d.com/cubism-sdk-manual/unity-for-ow/)

### Fixed

* Fix the bug of the weight calculation of `MotionFade`.
* Fix the bug of the weight calculation of `Expression`.
* Fix the bug when playing a motion that is shorter than the fade time or transitioning to a motion with a different fade value.
* Fix fixed value for the generated .exp3.asset fade value when there is no fade value in .exp3.json.
* Fix registering the delegate duplicately on calling `CubismUpdateController.Refresh()`.
* Fix checking bounding box hit every time before checking mesh hit by [@DenchiSoft](https://github.com/Live2D/CubismUnityComponents/pull/42).

### Removed

* Remove Unity 2017 from the development environment.


## [4-r.1] - 2020-01-30

### Changed

* Change registration of `CubismModel.OnRenderObject()` to `Player Loop` on Unity 2018.
* Change access modifier of `CubismFadeController.Refresh()` to `public`.
* Restructure scene maintaining Unity 2017-2019 compatibility.
* Change to manage .meta files, samples and models in the repository.
* Separate license file from `README.md`.
* Change to use gravity and wind settings from `physics3.json`.
* Reformat some code according to the coding standards.
* Change the shorten type name `Action` to full name `System.Action`.

### Fixed

* Fix occurring runtime error when importing the model exist with `.moc3.asset`.
* Fix only `EndTime` update with latest motion when motion fading.
* Fix the path of `.motion3.json` to save `.fade.asset`.
* Fix the `InstanceId`.
* Fix to reuse `InstanceId` recorded in generated `AnimationClip`.
* Fix condition to clear existing `AnimationClip` curve on reimporting.
* Fix the script of the `Original Workflow sample` scene.


## [4-beta.2] - 2019-11-14

### Fixed

* Fix sample scene sourcecode: `CubismSampleController.cs`
* Fix a bug when switching multiple motions in the same layer of `Motion` component.
* Fix priority not being set when playing idle motion.
* Fix importing process of `.pose3.json` on `Original Workflow`.


## [4-beta.1] - 2019-09-04

### Added

* Support new Inverted Masking features.
* Add `.editorconfig` and `.gitattributes` to manage file formats.
* Add `UWP/ARM64` Core binary file for experimental use.
* Add method to know playing motion or not [#35](https://github.com/Live2D/CubismUnityComponents/pull/35).
* Add sample model and sample scene.(`./Assets/Live2D/Cubism/Samples/OriginalWorkflow/DemoCubism4`)


### Changed

* Upgrade Core version to 04.00.0000 (67108864).
* Move to `Plugin/Experimental/UWP` from `Plugin/Experimental/uwp/Windows`.
* Convert all file formats according to `.editorconfig`.
* `LICENSE.txt` file has been integrated into `README.md`
* Remove changelog and regenerate `CHANGELOG.md`.
* What was `Package.json` is currently being changed to`cubism-info.yml`.
* Improve CubismUpdateController [#34](https://github.com/Live2D/CubismUnityComponents/pull/34).

### Fixed

* Fix issue of `Demo` and `Motion` sample in `OriginalWorkflow`.
* Fix issue that mesh remain when deleting model.
* Fix issue where Priority value was not reset after playing motion with CubismMotionController.


[5-r.5]: https://github.com/Live2D/CubismUnityComponents/compare/5-r.5-beta.3...5-r.5
[5-r.5-beta.3]: https://github.com/Live2D/CubismUnityComponents/compare/5-r.5-beta.2...5-r.5-beta.3
[5-r.5-beta.2]: https://github.com/Live2D/CubismUnityComponents/compare/5-r.5-beta.1...5-r.5-beta.2
[5-r.5-beta.1]: https://github.com/Live2D/CubismUnityComponents/compare/5-r.4.1...5-r.5-beta.1
[5-r.4.1]: https://github.com/Live2D/CubismUnityComponents/compare/5-r.4...5-r.4.1
[5-r.4]: https://github.com/Live2D/CubismUnityComponents/compare/5-r.3...5-r.4
[5-r.3]: https://github.com/Live2D/CubismUnityComponents/compare/5-r.2...5-r.3
[5-r.2]: https://github.com/Live2D/CubismUnityComponents/compare/5-r.1...5-r.2
[5-r.1]: https://github.com/Live2D/CubismUnityComponents/compare/5-r.1-beta.4...5-r.1
[5-r.1-beta.4]: https://github.com/Live2D/CubismUnityComponents/compare/5-r.1-beta.3...5-r.1-beta.4
[5-r.1-beta.3]: https://github.com/Live2D/CubismUnityComponents/compare/5-r.1-beta.2...5-r.1-beta.3
[5-r.1-beta.2]: https://github.com/Live2D/CubismUnityComponents/compare/5-r.1-beta.1...5-r.1-beta.2
[5-r.1-beta.1]: https://github.com/Live2D/CubismUnityComponents/compare/4-r.7...5-r.1-beta.1
[4-r.7]: https://github.com/Live2D/CubismUnityComponents/compare/4-r.6.2...4-r.7
[4-r.6.2]: https://github.com/Live2D/CubismUnityComponents/compare/4-r.6.1...4-r.6.2
[4-r.6.1]: https://github.com/Live2D/CubismUnityComponents/compare/4-r.6...4-r.6.1
[4-r.6]: https://github.com/Live2D/CubismUnityComponents/compare/4-r.5...4-r.6
[4-r.5]: https://github.com/Live2D/CubismUnityComponents/compare/4-r.5-beta.5...4-r.5
[4-r.5-beta.5]: https://github.com/Live2D/CubismUnityComponents/compare/4-r.5-beta.4...4-r.5-beta.5
[4-r.5-beta.4]: https://github.com/Live2D/CubismUnityComponents/compare/4-r.5-beta.3...4-r.5-beta.4
[4-r.5-beta.3]: https://github.com/Live2D/CubismUnityComponents/compare/4-r.5-beta.2...4-r.5-beta.3
[4-r.5-beta.2]: https://github.com/Live2D/CubismUnityComponents/compare/4-r.5-beta.1...4-r.5-beta.2
[4-r.5-beta.1]: https://github.com/Live2D/CubismUnityComponents/compare/4-r.4.2...4-r.5-beta.1
[4-r.4.2]: https://github.com/Live2D/CubismUnityComponents/compare/4-r.4.1...4-r.4.2
[4-r.4.1]: https://github.com/Live2D/CubismUnityComponents/compare/4-r.4...4-r.4.1
[4-r.4]: https://github.com/Live2D/CubismUnityComponents/compare/4-r.3...4-r.4
[4-r.3]: https://github.com/Live2D/CubismUnityComponents/compare/4-r.2...4-r.3
[4-r.2]: https://github.com/Live2D/CubismUnityComponents/compare/4-r.1...4-r.2
[4-r.1]: https://github.com/Live2D/CubismUnityComponents/compare/4-beta.2...4-r.1
[4-beta.2]: https://github.com/Live2D/CubismUnityComponents/compare/4-beta.1...4-beta.2
[4-beta.1]: https://github.com/Live2D/CubismUnityComponents/compare/86e5b07702f74d00b4ab52b7d6c15ba3464b8b85...4-beta.1
