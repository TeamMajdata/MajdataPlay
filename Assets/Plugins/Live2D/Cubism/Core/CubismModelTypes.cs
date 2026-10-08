using Live2D.Cubism.Core;

/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


public class CubismModelTypes
{
    /// <summary>
    /// Type of draw object in a model.
    /// </summary>
    public enum DrawObjectType
    {
        Drawable,
        Offscreen,
        Unknown = -1
    }

    /// <summary>
    /// Type of child object in a part.
    /// </summary>
    public enum PartChildObjectType
    {
        Drawable,
        Parts,
        Unknown = -1
    }

    /// <summary>
    /// Information about a single part in a <see cref="CubismModel"/>.
    /// </summary>
    public struct PartInfo
    {
        public int PartUnmanagedIndex;
        public PartChildObjectInfo[] ChildObjects;
        public PartDrawObjectInfo DrawObjects;

        /// <summary>
        /// Number of child objects in this part.
        /// </summary>
        public int ChildCount
        {
            get
            {
                if (ChildObjects == null)
                {
                    return 0;
                }

                return ChildObjects.Length;
            }
        }

        /// <summary>
        /// Number of draw object in this part.
        /// </summary>
        public int DrawObjectCount
        {
            get
            {
                if (DrawObjects.Drawables == null)
                {
                    return 0;
                }

                return DrawObjects.Drawables.Length;
            }
        }
    }

    /// <summary>
    /// Information about a child object in a part.
    /// </summary>
    public struct PartChildObjectInfo
    {
        public PartChildObjectType ChildObjectType;
        public int ChildObjectIndex;
    }

    /// <summary>
    /// Information about draw objects in a part.
    /// </summary>
    public struct PartDrawObjectInfo
    {
        public CubismDrawable[] Drawables;
        public CubismOffscreen[] Offscreens;
    }
}
