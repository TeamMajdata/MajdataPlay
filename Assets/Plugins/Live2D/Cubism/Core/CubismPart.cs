/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using Live2D.Cubism.Core.Unmanaged;
using Live2D.Cubism.Framework;
using System;
using UnityEngine;


namespace Live2D.Cubism.Core
{
    /// <summary>
    /// Single <see cref="CubismModel"/> part.
    /// </summary>
    [CubismDontMoveOnReimport]
    public sealed class CubismPart : MonoBehaviour
    {
        #region Factory Methods

        /// <summary>
        /// Creates parts for a <see cref="CubismModel"/>.
        /// </summary>
        /// <param name="unmanagedModel">Handle to unmanaged model.</param>
        /// <returns>Parts root.</returns>
        internal static GameObject CreateParts(CubismUnmanagedModel unmanagedModel)
        {
            var root = new GameObject("Parts");


            // Create parts.
            var unmanagedParts = unmanagedModel.Parts;
            var buffer = new CubismPart[unmanagedParts.Count];


            for (var i = 0; i < buffer.Length; ++i)
            {
                var proxy = new GameObject();


                buffer[i] = proxy.AddComponent<CubismPart>();


                buffer[i].transform.SetParent(root.transform);
                buffer[i].Reset(unmanagedModel, i);
            }


            return root;
        }

        #endregion


        /// <summary>
        /// Unmanaged parts from unmanaged model.
        /// </summary>
        private CubismUnmanagedParts UnmanagedParts { get; set; }


        /// <summary>
        /// <see cref="UnmanagedIndex"/> backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private int _unmanagedIndex = -1;

        /// <summary>
        /// Position in unmanaged arrays.
        /// </summary>
        public int UnmanagedIndex
        {
            get { return _unmanagedIndex; }
            private set { _unmanagedIndex = value; }
        }


        /// <summary>
        /// Copy of Id.
        /// </summary>
        public string Id
        {
            get
            {
                // Pull data.
                return UnmanagedParts.Ids[UnmanagedIndex];
            }
        }

        /// <summary>
        /// Current opacity.
        /// </summary>
        [SerializeField, HideInInspector]
        public float Opacity;

        /// <summary>
        /// Parent part position in unmanaged arrays.
        /// </summary>
        public int UnmanagedParentIndex
        {
            get
            {
                if (UnmanagedIndex > 0)
                {
                    // Pull data.
                    return UnmanagedParts.ParentIndices[UnmanagedIndex];
                }
                return -1;
            }
        }

        /// <summary>
        /// Offscreen index.
        /// </summary>
        public int OffscreenIndex
        {
            get
            {
                return UnmanagedParts.OffscreenIndices[UnmanagedIndex];
            }
        }

        /// <summary>
        /// <see cref="AllChildOffscreens"/>'s backing field.
        /// </summary>
        private CubismOffscreen[] _allChildOffscreens = null;

        /// <summary>
        /// All child drawables of this part, including children of children.
        /// </summary>
        public CubismOffscreen[] AllChildOffscreens
        {
            get
            {
                if (_allChildOffscreens == null)
                {
                    var model = this.FindCubismModel(true);
                    if (!model)
                    {
                        return null;
                    }

                    if (model.Offscreens == null)
                    {
                        return null;
                    }

                    var parts = model.Parts;

                    var childrenParts = Array.Empty<CubismPart>();

                    for (var index = 0; index < parts.Length; index++)
                    {
                        if (parts[index].UnmanagedParentIndex == UnmanagedIndex)
                        {
                            Array.Resize(ref childrenParts, childrenParts.Length + 1);
                            childrenParts[^1] = parts[index];
                        }
                    }

                    // Initialize the array of all children drawables.
                    _allChildOffscreens = Array.Empty<CubismOffscreen>();

                    // Add the drawables of this part.
                    Array.Resize(ref _allChildOffscreens, ChildOffscreens.Length);
                    Array.Copy(ChildOffscreens, _allChildOffscreens, ChildOffscreens.Length);

                    // Collect all children drawables from child parts.
                    for (var index = 0; index < childrenParts.Length; index++)
                    {
                        Array.Resize(ref _allChildOffscreens, _allChildOffscreens.Length + childrenParts[index].AllChildOffscreens.Length);
                        Array.Copy(childrenParts[index].AllChildOffscreens,
                            0,
                            _allChildOffscreens,
                            _allChildOffscreens.Length - childrenParts[index].AllChildOffscreens.Length,
                            childrenParts[index].AllChildOffscreens.Length);
                    }
                }

                return _allChildOffscreens;
            }
        }

        /// <summary>
        /// <see cref="AllChildDrawables"/>'s backing field.
        /// </summary>
        private CubismDrawable[] _allChildDrawables = null;

        /// <summary>
        /// All child drawables of this part, including children of children.
        /// </summary>
        public CubismDrawable[] AllChildDrawables
        {
            get
            {
                if (_allChildDrawables == null)
                {
                    var model = this.FindCubismModel(true);
                    if (!model || (model.Drawables?.Length ?? -1) < 1)
                    {
                        return null;
                    }

                    var parts = model.Parts;

                    var childrenParts = Array.Empty<CubismPart>();

                    for (var index = 0; index < parts.Length; index++)
                    {
                        if (parts[index].UnmanagedParentIndex == UnmanagedIndex)
                        {
                            Array.Resize(ref childrenParts, childrenParts.Length + 1);
                            childrenParts[^1] = parts[index];
                        }
                    }

                    // Initialize the array of all children drawables.
                    _allChildDrawables = Array.Empty<CubismDrawable>();

                    // Add the drawables of this part.
                    Array.Resize(ref _allChildDrawables, ChildDrawables.Length);
                    Array.Copy(ChildDrawables, _allChildDrawables, ChildDrawables.Length);

                    // Collect all children drawables from child parts.
                    for (var index = 0; index < childrenParts.Length; index++)
                    {
                        if ((childrenParts[index]?.AllChildDrawables?.Length ?? -1) < 1)
                        {
                            continue;
                        }

                        Array.Resize(ref _allChildDrawables, _allChildDrawables.Length + childrenParts[index].AllChildDrawables.Length);
                        Array.Copy(childrenParts[index].AllChildDrawables,
                            0,
                            _allChildDrawables,
                            _allChildDrawables.Length - childrenParts[index].AllChildDrawables.Length,
                            childrenParts[index].AllChildDrawables.Length);
                    }
                }

                return _allChildDrawables;
            }
        }

        /// <summary>
        /// <see cref="ChildParts"/>'s backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private CubismPart[] _childParts;

        /// <summary>
        /// Child parts of this part.
        /// </summary>
        public CubismPart[] ChildParts
        {
            get
            {
                if (_childParts == null)
                {
                    var model = this.FindCubismModel(true);
                    if (!model)
                    {
                        return null;
                    }

                    var parts = model.Parts;
                    _childParts = Array.Empty<CubismPart>();
                    for (var index = 0; index < parts.Length; index++)
                    {
                        // When this object is the parent part.
                        if (parts[index].UnmanagedParentIndex == UnmanagedIndex)
                        {
                            Array.Resize(ref _childParts, _childParts.Length + 1);
                            _childParts[^1] = parts[index];
                        }
                    }
                }
                return _childParts;
            }
        }

        /// <summary>
        /// <see cref="ChildDrawables"/>'s backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private CubismDrawable[] _childDrawables;

        /// <summary>
        /// Child drawables of this part.
        /// </summary>
        public CubismDrawable[] ChildDrawables
        {
            get
            {
                if (_childDrawables == null)
                {
                    var model = this.FindCubismModel(true);
                    if (!model)
                    {
                        return null;
                    }

                    var drawables = model.Drawables;
                    _childDrawables = Array.Empty<CubismDrawable>();
                    for (var index = 0; index < drawables.Length; index++)
                    {
                        // When this object is the parent part.
                        if (drawables[index].ParentPartIndex == UnmanagedIndex)
                        {
                            Array.Resize(ref _childDrawables, _childDrawables.Length + 1);
                            _childDrawables[^1] = drawables[index];
                        }
                    }
                }
                return _childDrawables;
            }
        }

        /// <summary>
        /// <see cref="ChildOffscreens"/>'s backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private CubismOffscreen[] _childOffscreens;

        /// <summary>
        /// Array of offscreen from child parts;
        /// </summary>
        public CubismOffscreen[] ChildOffscreens
        {
            get
            {
                if (_childOffscreens == null)
                {
                    var model = this.FindCubismModel(true);
                    if (!model)
                    {
                        return null;
                    }

                    var offscreens = model.Offscreens;

                    if (offscreens == null)
                    {
                        return null;
                    }

                    _childOffscreens = Array.Empty<CubismOffscreen>();

                    for (var index = 0; index < offscreens.Length; index++)
                    {
                        var part = model.Parts[offscreens[index].OwnerIndex];

                        // When this object is the parent part.
                        if (part.UnmanagedParentIndex < 0
                            || part.UnmanagedParentIndex != UnmanagedIndex)
                        {
                            continue;
                        }

                        Array.Resize(ref _childOffscreens, _childOffscreens.Length + 1);
                        _childOffscreens[^1] = offscreens[index];
                    }
                }
                return _childOffscreens;
            }
        }

        /// <summary>
        /// <see cref="PartInfo"/>'s backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private CubismModelTypes.PartInfo? _partInfo;

        /// <summary>
        /// Information about this part.
        /// </summary>
        public CubismModelTypes.PartInfo? PartInfo
        {
            get
            {
                if (_partInfo == null)
                {
                    InitializePartInfo();
                }
                return _partInfo;
            }
        }

        /// <summary>
        /// Initializes <see cref="PartInfo"/>.
        /// </summary>
        private void InitializePartInfo()
        {
            if (ChildDrawables == null
                || ChildParts == null
                || ChildOffscreens == null)
            {
                // Failed silently...
                return;
            }

            var childObjectCount = ChildDrawables.Length + ChildParts.Length;
            _partInfo = new CubismModelTypes.PartInfo
            {
                PartUnmanagedIndex = UnmanagedIndex,
                ChildObjects = new CubismModelTypes.PartChildObjectInfo[childObjectCount],
                DrawObjects = new CubismModelTypes.PartDrawObjectInfo
                {
                    Drawables = AllChildDrawables,
                    Offscreens = AllChildOffscreens
                }
            };

            for (var i = 0; i < _partInfo.Value.ChildObjects.Length; i++)
            {
                var childInfo = new CubismModelTypes.PartChildObjectInfo
                {
                    ChildObjectType = CubismModelTypes.PartChildObjectType.Unknown,
                    ChildObjectIndex = -1
                };

                if (i >= childObjectCount)
                {
                    childInfo.ChildObjectType = CubismModelTypes.PartChildObjectType.Unknown;
                    childInfo.ChildObjectIndex = -1;
                }
                else if (i < ChildDrawables.Length)
                {
                    childInfo.ChildObjectType = CubismModelTypes.PartChildObjectType.Drawable;
                    childInfo.ChildObjectIndex = ChildDrawables[i].UnmanagedIndex;
                }
                else
                {
                    childInfo.ChildObjectType = CubismModelTypes.PartChildObjectType.Parts;
                    childInfo.ChildObjectIndex = ChildParts[i - ChildDrawables.Length].UnmanagedIndex;
                }

                _partInfo.Value.ChildObjects[i] = childInfo;
            }
        }

        /// <summary>
        /// Revives instance.
        /// </summary>
        /// <param name="unmanagedModel">TaskableModel to unmanaged unmanagedModel.</param>
        internal void Revive(CubismUnmanagedModel unmanagedModel)
        {
            UnmanagedParts = unmanagedModel.Parts;
        }

        /// <summary>
        /// Restores instance to initial state.
        /// </summary>
        /// <param name="unmanagedModel">TaskableModel to unmanaged unmanagedModel.</param>
        /// <param name="unmanagedIndex">Position in unmanaged arrays.</param>
        private void Reset(CubismUnmanagedModel unmanagedModel, int unmanagedIndex)
        {
            Revive(unmanagedModel);


            UnmanagedIndex = unmanagedIndex;
            name = Id;
            Opacity = UnmanagedParts.Opacities[unmanagedIndex];

            _allChildDrawables = null;
            _allChildOffscreens = null;
            _childParts = null;
            _childDrawables = null;
            _childOffscreens = null;
            InitializePartInfo();
        }
    }
}
