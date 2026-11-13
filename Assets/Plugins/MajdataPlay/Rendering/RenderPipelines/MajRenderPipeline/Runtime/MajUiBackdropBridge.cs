using System;
using UnityEngine;
using UnityEngine.Rendering;
#nullable enable

namespace MajdataPlay.Rendering
{
    /// <summary>Consumes scene color after designated background UI and before foreground camera-space UI.</summary>
    /// <remarks>The implementation belongs to its owning integration, never to the render pipeline.</remarks>
    public interface IMajUiBackdropCapture
    {
        /// <summary>Checks whether this camera requires a current-frame backdrop capture before culling begins.</summary>
        /// <param name="camera">The camera about to prepare its visible geometry.</param>
        /// <returns>True only while this camera has a visible, configured backdrop consumer.</returns>
        /// <remarks>Unavailable resources should return false. Any queue preparation must finish before this method returns.</remarks>
        bool IsActive(Camera camera);

        /// <summary>Records copies or filters from the resolved background without capturing foreground UI.</summary>
        /// <param name="buffer">The camera's ordered graphics command buffer, outside a native render pass.</param>
        /// <param name="camera">The camera whose current background has finished drawing.</param>
        /// <param name="resolvedColor">The borrowed single-sample scene color, valid only in this command stream.</param>
        /// <remarks>The pipeline restores color, depth, viewport and camera globals after this callback. Do not retain or destroy the source.</remarks>
        void Record(CommandBuffer buffer, Camera camera, RenderTexture resolvedColor);
    }

    /// <summary>Optionally refreshes the same shared backdrop after ordinary UI and before glass control bodies.</summary>
    /// <remarks>Existing consumers of the first backdrop stage do not need to implement this interface.</remarks>
    public interface IMajUiButtonBackdropCapture : IMajUiBackdropCapture
    {
        /// <summary>Checks whether this camera needs the shared peripheral-button background stage before culling begins.</summary>
        /// <param name="camera">The camera whose button materials and visible geometry are being prepared.</param>
        /// <returns>True only while visible buttons need the second current-frame snapshot.</returns>
        bool RequiresButtonCapture(Camera camera);

        /// <summary>Records the later backdrop after ordinary UI and optional categories, excluding peripheral buttons and their clear glyphs.</summary>
        /// <param name="buffer">The camera's ordered graphics command buffer, outside a native render pass.</param>
        /// <param name="camera">The camera whose ordinary UI has finished drawing.</param>
        /// <param name="resolvedColor">The borrowed single-sample scene color, valid only in this command stream.</param>
        /// <remarks>This source includes completed ordinary UI and excludes the peripheral surfaces themselves.</remarks>
        void RecordButtons(CommandBuffer buffer, Camera camera, RenderTexture resolvedColor);
    }

    /// <summary>Captures a separate category background after large boards and the background song cover.</summary>
    public interface IMajUiCategoryBackdropCapture : IMajUiButtonBackdropCapture
    {
        /// <summary>Checks whether this camera needs a category source between boards and category drawing.</summary>
        /// <param name="camera">The camera whose category consumers are being prepared.</param>
        /// <returns>True while visible category consumers need the middle snapshot.</returns>
        bool RequiresCategoryCapture(Camera camera);

        /// <summary>Records the middle source after boards and the background cover, before category controls.</summary>
        /// <param name="buffer">The ordered camera command buffer outside a native render pass.</param>
        /// <param name="camera">The camera whose boards and background cover have finished drawing.</param>
        /// <param name="resolvedColor">The borrowed single-sample scene color, valid only in this command stream.</param>
        void RecordCategories(CommandBuffer buffer, Camera camera, RenderTexture resolvedColor);
    }

    /// <summary>Registers an optional UI backdrop consumer without depending on game scripts or an effect implementation.</summary>
    public static class MajUiBackdropBridge
    {
        /// <summary>Ends the background and blue-ring stage before the large-board snapshot.</summary>
        public const int BackgroundQueueUpperBound = 2899;

        /// <summary>Contains large boards after their underlying blue ring has been captured.</summary>
        public const int BoardQueue = 2900;

        /// <summary>Begins the background song-cover stage after large boards.</summary>
        public const int CategoryBackgroundQueue = 2950;

        /// <summary>Ends the board/cover prefix before the category snapshot.</summary>
        public const int CategoryBackgroundQueueUpperBound = 2989;

        /// <summary>Contains complete category groups before default transparent UI, retaining native body/label order.</summary>
        public const int CategoryQueue = 2990;

        /// <summary>Contains peripheral glass button bodies after ordinary UI and complete category groups.</summary>
        public const int ButtonBodyQueue = 3500;

        /// <summary>Begins the clear foreground stage after glass button bodies have finished drawing.</summary>
        public const int ButtonGlyphQueueLowerBound = ButtonBodyQueue + 1;

        /// <summary>Retains the integration-owned consumer without allocating per-frame callback snapshots.</summary>
        private static IMajUiBackdropCapture? s_capture;

        /// <summary>Installs or replaces the current integration-owned backdrop consumer on the main thread.</summary>
        /// <param name="capture">The consumer that prepares and records backdrop captures.</param>
        /// <exception cref="ArgumentNullException">The supplied consumer is null.</exception>
        public static void Register(IMajUiBackdropCapture capture)
        {
            if (capture == null)
            {
                throw new ArgumentNullException(nameof(capture));
            }
            s_capture = capture;
        }

        /// <summary>Removes only the matching registration without disposing integration-owned state.</summary>
        /// <param name="capture">The original consumer whose registration is ending.</param>
        public static void Unregister(IMajUiBackdropCapture capture)
        {
            if (ReferenceEquals(s_capture, capture))
            {
                s_capture = null;
            }
        }

        /// <summary>Gets the current active consumer before the camera emits or culls native Canvas geometry.</summary>
        /// <param name="camera">The camera whose backdrop requirement is being prepared.</param>
        /// <returns>The active consumer, or null when no capture is required or preparation failed.</returns>
        internal static IMajUiBackdropCapture? GetActive(Camera camera)
        {
            var capture = s_capture;
            if (capture == null)
            {
                return null;
            }
            try
            {
                return capture.IsActive(camera) ? capture : null;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, camera);
                return null;
            }
        }

        /// <summary>Checks an optional second-stage requirement without changing the first-stage registration.</summary>
        /// <param name="capture">The already active first-stage consumer, or null when capture is inactive.</param>
        /// <param name="camera">The camera whose optional button capture is being prepared.</param>
        /// <returns>The active button-stage consumer, or null for legacy, inactive or failed consumers.</returns>
        internal static IMajUiButtonBackdropCapture? GetActiveButtons(IMajUiBackdropCapture? capture, Camera camera)
        {
            if (capture is not IMajUiButtonBackdropCapture buttons)
            {
                return null;
            }
            try
            {
                return buttons.RequiresButtonCapture(camera) ? buttons : null;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, camera);
                return null;
            }
        }

        /// <summary>Checks the optional middle-stage requirement independently of peripheral-button visibility.</summary>
        /// <param name="capture">The active first-stage consumer, or null when capture is inactive.</param>
        /// <param name="camera">The camera whose category background is being prepared.</param>
        /// <returns>The active category-stage consumer, or null for legacy, inactive or failed consumers.</returns>
        internal static IMajUiCategoryBackdropCapture? GetActiveCategories(IMajUiBackdropCapture? capture, Camera camera)
        {
            if (capture is not IMajUiCategoryBackdropCapture categories)
            {
                return null;
            }
            try
            {
                return categories.RequiresCategoryCapture(camera) ? categories : null;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, camera);
                return null;
            }
        }

        /// <summary>Clears registrations at session start, including domain-reload-free Play Mode.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Reset()
        {
            s_capture = null;
        }
    }
}
