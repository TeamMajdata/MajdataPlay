/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using System;
using UnityEngine;

namespace Live2D.Cubism.Rendering
{
    /// <summary>
    /// Manager for managing multiple <see cref="CubismRenderController"/>.
    /// </summary>
    public class CubismRenderControllerGroup
    {
        /// <summary>
        /// Instance.
        /// </summary>
        private static CubismRenderControllerGroup _instance;

        /// <summary>
        /// Creates an instance if it does not exist.
        /// </summary>
        public static CubismRenderControllerGroup GetInstance()
        {
            if (_instance == null)
            {
                _instance = new CubismRenderControllerGroup
                {
                    IsCopiedToCameraTexture = true
                };
            }
            return _instance;
        }

        /// <summary>
        /// <see cref="RenderControllers"/>'s backing field.
        /// </summary>
        private CubismRenderController[] _renderControllers;

        /// <summary>
        /// Render controllers managed by this controller.
        /// </summary>
        public CubismRenderController[] RenderControllers
        {
            get
            {
                if (_renderControllers == null)
                {
                    _renderControllers = Array.Empty<CubismRenderController>();
                }

                return _renderControllers;
            }
        }

        /// <summary>
        /// Struct for grouping render controllers by sorting index.
        /// </summary>
        public struct RenderControllerGroupData
        {
            public int SortingGroupIndex;
            public CubismRenderController[] Controllers;
        }

        /// <summary>
        /// <see cref="GroupDataArray"/>'s backing field.
        /// </summary>
        private RenderControllerGroupData[] _groupDataArray;

        /// <summary>
        /// Array of render controller group data.
        /// </summary>
        public RenderControllerGroupData[] GroupDataArray
        {
            get
            {
                if (_groupDataArray == null)
                {
                    InitializeGroupDataArray();
                }

                return _groupDataArray;
            }
        }

        /// <summary>
        /// Sorts the render controller groups by their sorting index.
        /// </summary>
        public void SortingRenderControllerGroups()
        {
            Array.Sort(_groupDataArray,
                (a, b) => a.SortingGroupIndex.CompareTo(b.SortingGroupIndex));
            DidChangeSortingRenderControllerGroup = true;
        }

        /// <summary>
        /// Whether the sorting of <see cref="GroupDataArray"/> has changed.
        /// </summary>
        internal bool DidChangeSortingRenderControllerGroup;

        /// <summary>
        /// Whether blend mode is affected by sorting group index.
        /// default: true
        /// </summary>
        public bool IsCopiedToCameraTexture;

        /// <summary>
        /// Initializes render controller groups.
        /// </summary>
        public void InitializeGroupDataArray()
        {
            // Clear existing groups.
            _groupDataArray = Array.Empty<RenderControllerGroupData>();

            for (var renderControllerIndex = 0;
                 renderControllerIndex < RenderControllers.Length;
                 renderControllerIndex++)
            {
                var renderController = RenderControllers[renderControllerIndex];
                if (GroupDataArray.Length < 1)
                {
                    Array.Resize(ref _groupDataArray, 1);
                    GroupDataArray[0] = new RenderControllerGroupData
                    {
                        SortingGroupIndex = renderController.GroupedSortingIndex,
                        Controllers = new CubismRenderController[] { renderController }
                    };

                    continue;
                }

                // Add to existing groups or create new group.
                AddRenderControllerGroups(renderController, true);
            }

            // Sort groups by sorting index.
            SortingRenderControllerGroups();
        }

        /// <summary>
        /// Adds a render controller to its group.
        /// </summary>
        /// <param name="renderController">Added render controller.</param>
        /// <param name="skipSorting">Whether called from initialization or change.</param>
        public void AddRenderControllerGroups(CubismRenderController renderController, bool skipSorting = false)
        {
            if (!renderController)
            {
                return;
            }

            // Remove from existing group first.
            RemoveRenderControllerFromGroups(renderController, true);

            // Check for existing group.
            var isIndexFound = false;
            for (var groupedRenderControllerIndex = 0;
                 groupedRenderControllerIndex < GroupDataArray.Length;
                 groupedRenderControllerIndex++)
            {
                var groupedDara = GroupDataArray[groupedRenderControllerIndex];

                if (groupedDara.SortingGroupIndex != renderController.GroupedSortingIndex)
                {
                    continue;
                }

                // Add to existing group.
                Array.Resize(ref groupedDara.Controllers,
                    groupedDara.Controllers.Length + 1);
                groupedDara.Controllers[^1] = renderController;
                _groupDataArray[groupedRenderControllerIndex] = groupedDara;
                isIndexFound = true;
                break;
            }

            // Exit if existing group found.
            if (isIndexFound)
            {
                DidChangeSortingRenderControllerGroup = true;
                return;
            }

            // Create new group if no existing group found.
            Array.Resize(ref _groupDataArray, GroupDataArray.Length + 1);
            GroupDataArray[^1] = new RenderControllerGroupData
            {
                SortingGroupIndex = renderController.GroupedSortingIndex,
                Controllers = new CubismRenderController[] { renderController }
            };

            // Exit if called from initialization.
            if (skipSorting)
            {
                return;
            }

            // Sort groups by sorting index.
            SortingRenderControllerGroups();
        }

        /// <summary>
        /// Removes a render controller from its group.
        /// </summary>
        /// <param name="renderController">Removed render controller.</param>
        /// <param name="skipSorting">Whether called from initialization or change.</param>
        public void RemoveRenderControllerFromGroups(CubismRenderController renderController, bool skipSorting = false)
        {
            if (renderController == null)
            {
                return;
            }

            var targetSortingIndex = renderController.GroupedSortingIndex;

            for (var index = 0; index < GroupDataArray.Length; index++)
            {
                var renderControllerGroupData = GroupDataArray[index];
                if (renderControllerGroupData.SortingGroupIndex != targetSortingIndex)
                {
                    continue;
                }

                // Find and remove the controller by shifting elements down.
                for (var controllerIndex = 0; controllerIndex < renderControllerGroupData.Controllers.Length; controllerIndex++)
                {
                    if (renderControllerGroupData.Controllers[controllerIndex] != renderController)
                    {
                        continue;
                    }

                    // Shift elements down.
                    for (var shiftedIndex = controllerIndex; shiftedIndex < renderControllerGroupData.Controllers.Length - 1; shiftedIndex++)
                    {
                        renderControllerGroupData.Controllers[shiftedIndex] = renderControllerGroupData.Controllers[shiftedIndex + 1];
                    }

                    // Resize the array.
                    Array.Resize(ref renderControllerGroupData.Controllers, renderControllerGroupData.Controllers.Length - 1);
                    break;
                }

                // Update the group.
                if (renderControllerGroupData.Controllers.Length > 0)
                {
                    // Update the group if it still has controllers.
                    _groupDataArray[index] = renderControllerGroupData;
                    return;
                }

                // Remove the group if it has no controllers left.
                for (var shiftedIndex = index; shiftedIndex < GroupDataArray.Length - 1; shiftedIndex++)
                {
                    _groupDataArray[shiftedIndex] = _groupDataArray[shiftedIndex + 1];
                }

                // Resize the array.
                Array.Resize(ref _groupDataArray, GroupDataArray.Length - 1);

                if (skipSorting)
                {
                    return;
                }

                // Sort groups by sorting index.
                SortingRenderControllerGroups();
                break;
            }
        }

        /// <summary>
        /// Adds a render controller to be managed.
        /// </summary>
        /// <param name="controller">Added controller.</param>
        public void AddRenderController(CubismRenderController controller)
        {
            if (!controller)
            {
                return;
            }

            // Avoid duplicate registrations.
            for (var index = 0; index < RenderControllers.Length; index++)
            {
                var existingController = RenderControllers[index];
                if (existingController == controller)
                {
                    Debug.LogWarning($"CubismRenderController '{controller.name}' is already registered.");
                    return;
                }
            }

            // Add the controller.
            Array.Resize(ref _renderControllers, RenderControllers.Length + 1);
            _renderControllers[^1] = controller;

            AddRenderControllerGroups(controller);
        }

        /// <summary>
        /// Removes a render controller from management.
        /// </summary>
        /// <param name="controller">Removed controller.</param>
        public void RemoveRenderController(CubismRenderController controller)
        {
            if (!controller)
            {
                return;
            }

            // Find the controller and remove it by shifting elements down.
            for (var i = 0; i < RenderControllers.Length; i++)
            {
                if (RenderControllers[i] == controller)
                {
                    for (var j = i; j < RenderControllers.Length - 1; j++)
                    {
                        _renderControllers[j] = _renderControllers[j + 1];
                    }
                    Array.Resize(ref _renderControllers, RenderControllers.Length - 1);

                    // Also remove from groups.
                    RemoveRenderControllerFromGroups(controller);
                    return;
                }
            }
        }
    }
}
