/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using Live2D.Cubism.Core;
using Live2D.Cubism.Rendering;
using System.Collections.Generic;
using UnityEngine;


namespace Live2D.Cubism.Framework.Raycasting
{
    /// <summary>
    /// Allows casting rays against <see cref="CubismRaycastable"/>s.
    /// </summary>
    public sealed class CubismRaycaster : MonoBehaviour
    {
        /// <summary>
        /// <see cref="CubismRenderer"/>s with <see cref="CubismRaycastable"/>s attached.
        /// </summary>
        private CubismRenderer[] Raycastables { get; set; }

        /// <summary>
        /// <see cref="CubismRaycastablePrecision"/>s with <see cref="CubismRaycastable"/>s attached.
        /// </summary>
        private CubismRaycastablePrecision[] RaycastablePrecisions { get; set; }


        /// <summary>
        /// Refreshes the controller. Call this method after adding and/or removing <see cref="CubismRaycastable"/>.
        /// </summary>
        private void Refresh()
        {
            var candidates = this
                .FindCubismModel()
                .Drawables;


            // Find raycastable drawables.
            var raycastables = new List<CubismRenderer>();
            var raycastablePrecisions = new List<CubismRaycastablePrecision>();


            for (var i = 0; i < candidates.Length; i++)
            {
                var raycastable = candidates[i].GetComponent<CubismRaycastable>();
                // Skip non-raycastables.
                if (!raycastable
                    || !raycastable.isActiveAndEnabled)
                {
                    continue;
                }


                raycastables.Add(candidates[i].GetComponent<CubismRenderer>());
                raycastablePrecisions.Add(candidates[i].GetComponent<CubismRaycastable>().Precision);
            }


            // Cache raycastables.
            Raycastables = raycastables.ToArray();
            RaycastablePrecisions = raycastablePrecisions.ToArray();
        }

        #region Unity Event Handling

        /// <summary>
        /// Called by Unity. Makes sure cache is initialized.
        /// </summary>
        private void Start()
        {
            // Initialize cache.
            Refresh();
        }

        #endregion

        /// <summary>
        /// Casts a ray.
        /// </summary>
        /// <param name="origin">The origin of the ray.</param>
        /// <param name="direction">The direction of the ray.</param>
        /// <param name="result">The result of the cast.</param>
        /// <param name="maximumDistance">[Optional] The maximum distance of the ray.</param>
        /// <returns><see langword="true"/> in case of a hit; <see langword="false"/> otherwise.</returns>
        /// <returns>The numbers of drawables had hit</returns>
        public int Raycast(Vector3 origin, Vector3 direction, CubismRaycastHit[] result, float maximumDistance = 10000.0f)
        {
            return Raycast(new Ray(origin, direction), result, maximumDistance);
        }

        /// <summary>
        /// Casts a ray.
        /// </summary>
        /// <param name="ray"></param>
        /// <param name="result">The result of the cast.</param>
        /// <param name="maximumDistance">[Optional] The maximum distance of the ray.</param>
        /// <returns><see langword="true"/> in case of a hit; <see langword="false"/> otherwise.</returns>
        /// <returns>The numbers of drawables had hit</returns>
        public int Raycast(Ray ray, CubismRaycastHit[] result, float maximumDistance = 10000.0f)
        {
            var origin = ray.origin;

            for (var i = 0; i < result.Length; i++)
            {
                result[i] = new CubismRaycastHit();
            }

            // Cast against each raycastable.
            var hitCount = 0;

            for (var i = 0; i < Raycastables.Length; i++)
            {
                var raycastable = Raycastables[i];
                var precision = RaycastablePrecisions[i];
                if (!raycastable.MeshRenderer.enabled)
                {
                    continue;
                }

                if (RaycastDrawable(origin, ray.direction.normalized, maximumDistance, precision, raycastable, out var hitPosition, out var hitNormal, out var hitTime))
                {
                    CubismRaycastHit raycastHit;

                    raycastHit.Drawable = raycastable.GetComponent<CubismDrawable>();
                    raycastHit.Distance = hitTime * maximumDistance;
                    raycastHit.WorldPosition = hitPosition;
                    raycastHit.LocalPosition = transform.InverseTransformPoint(hitPosition);

                    result[hitCount] = raycastHit;

                    ++hitCount;

                    // Exit if result buffer is full.
                    if (hitCount == result.Length)
                    {
                        break;
                    }
                }
            }

            return hitCount;
        }

        /// <summary>
        /// The function to perform the raycast on the drawable.
        /// </summary>
        /// <param name="origin">The origin vector of the ray.</param>
        /// <param name="normalizedDirection">The direction vector of the ray.</param>
        /// <param name="length">The max length of the ray from the origin.</param>
        /// <param name="precision">The precision of the raycast.</param>
        /// <param name="renderer">The renderer to perform the raycast.</param>
        /// <param name="hitPosition">The hit position of the ray.</param>
        /// <param name="hitNormal">The hit normal of the ray.</param>
        /// <param name="hitTime">The [0, 1] parameter of the ray where the hit point is between `Origin` and `Origin + Direction`.</param>
        /// <returns>Did the Intersection Occur.</returns>
        private bool RaycastDrawable(Vector3 origin, Vector3 normalizedDirection, float length, CubismRaycastablePrecision precision, CubismRenderer renderer, out Vector3 hitPosition, out Vector3 hitNormal, out float hitTime)
        {
            var bounds = renderer.Mesh.bounds;
            // Transform the ray into the coordinate system of the bounds to account for bounds rotation.
            var start = renderer.transform.InverseTransformPoint(origin);
            var end = renderer.transform.InverseTransformPoint(origin + normalizedDirection * length);
            if (!LineExtentBoxIntersection(bounds, start, end, Vector3.zero, out hitPosition, out hitNormal, out hitTime))
            {
                return false;
            }
            // Convert the hit location back to the global coordinate system.
            hitPosition = renderer.transform.TransformPoint(hitPosition);

            switch (precision)
            {
                case CubismRaycastablePrecision.BoundingBox:
                    {
                        // already checked
                        break;
                    }
                case CubismRaycastablePrecision.Triangles:
                    {
                        var indices = renderer.Mesh.triangles;
                        var positions = new Vector3[renderer.Mesh.vertices.Length];

                        for (var i = 0; i < renderer.Mesh.vertices.Length; i++)
                        {
                            positions[i] = renderer.transform.TransformPoint(renderer.Mesh.vertices[i]);
                        }

                        if (!RayIntersectMesh(origin, normalizedDirection, length, positions, indices, out hitPosition, out hitTime))
                        {
                            return false;
                        }

                        break;
                    }
                default:
                    {
                        return false;
                    }
            }

            return true;
        }

        /// <summary>
        /// The function to check the intersection between the ray and the mesh.
        /// </summary>
        /// <param name="origin">The origin vector of the ray.</param>
        /// <param name="direction">The direction vector of the ray.</param>
        /// <param name="length">The max length of the ray from the origin.</param>
        /// <param name="positions">The vertex positions of the mesh.</param>
        /// <param name="indices">The vertex indices of the mesh.</param>
        /// <param name="hitPosition">The hit position of the ray.</param>
        /// <param name="hitTime">The [0, 1] parameter of the ray where the hit point is between `Start` and `End`.</param>
        /// <returns>Did the Intersection Occur.</returns>
        private bool RayIntersectMesh(Vector3 origin, Vector3 direction, float length, IReadOnlyList<Vector3> positions, int[] indices, out Vector3 hitPosition, out float hitTime)
        {
            hitPosition = Vector3.zero;
            hitTime = 0.0f;
            for (var i = 0; i < indices.Length; i += 3)
            {
                var t0 = positions[indices[i]];
                var t1 = positions[indices[i + 1]];
                var t2 = positions[indices[i + 2]];

                if (RayIntersectTriangle(origin, direction, length, t0, t1, t2, out hitPosition, out hitTime))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The function to check the intersection between the ray and the triangle.
        /// </summary>
        /// <param name="origin">The origin vector of the ray.</param>
        /// <param name="direction">The direction vector of the ray.</param>
        /// <param name="length">The max length of the ray from the origin.</param>
        /// <param name="t0">The first vertex of the triangle.</param>
        /// <param name="t1">The second vertex of the triangle.</param>
        /// <param name="t2">The third vertex of the triangle.</param>
        /// <param name="hitPosition">The hit position of the ray.</param>
        /// <param name="hitTime">The [0, 1] parameter of the ray where the hit point is between `Start` and `End`.</param>
        /// <returns>Did the Intersection Occur.</returns>
        private bool RayIntersectTriangle(Vector3 origin, Vector3 direction, float length, Vector3 t0, Vector3 t1, Vector3 t2, out Vector3 hitPosition, out float hitTime)
        {
            hitPosition = Vector3.zero;
            hitTime = 0.0f;

            var e1 = t1 - t0;
            var e2 = t2 - t0;

            var p = Vector3.Cross(direction, e2);

            var det = Vector3.Dot(e1, p);

            if (Mathf.Approximately(det, 0))
            {
                return false;
            }

            var invDet = 1.0f / det;
            var t = origin - t0;

            var u = Vector3.Dot(t, p) * invDet;

            if (u < 0.0f || u > 1.0f)
            {
                return false;
            }


            var q = Vector3.Cross(t, e1);

            var v = Vector3.Dot(direction, q) * invDet;

            if (v < 0.0f || u + v > 1.0f)
            {
                return false;
            }

            var w = Vector3.Dot(e2, q) * invDet;

            hitTime = w / length;

            if (hitTime < 0.0f || hitTime > 1.0f)
            {
                return false;
            }

            hitPosition = origin + direction * w;

            return true;
        }

        /// <summary>
        /// Line-extent/Box Test Util
        /// </summary>
        /// <param name="inBox">The box bounds.</param>
        /// <param name="start">Start of line segment.</param>
        /// <param name="end">End of line segment.</param>
        /// <param name="extent">The box bounds extent.</param>
        /// <param name="hitLocation">The hit position of the ray.</param>
        /// <param name="hitNormal">The hit normal of the ray.</param>
        /// <param name="hitTime">The [0, 1] parameter of the ray where the hit point is between `Origin` and `Origin + Direction`.</param>
        /// <returns></returns>
        private static bool LineExtentBoxIntersection(Bounds inBox, Vector3 start, Vector3 end, Vector3 extent, out Vector3 hitLocation, out Vector3 hitNormal, out float hitTime)
        {
            hitLocation = Vector3.zero;
            hitNormal = Vector3.zero;
            hitTime = 0.0f;

            var box = inBox;
            box.max += extent;
            box.min -= extent;

            var direction = (end - start);

            Vector3 time;
            var inside = true;
            var faceDirection = Vector3.one;

            if (start.x < box.min.x)
            {
                if (direction.x <= 0.0f)
                {
                    return false;
                }
                else
                {
                    inside = false;
                    faceDirection[0] = -1;
                    time.x = (box.min.x - start.x) / direction.x;
                }
            }
            else if (start.x > box.max.x)
            {
                if (direction.x >= 0.0f)
                {
                    return false;
                }
                else
                {
                    inside = false;
                    time.x = (box.max.x - start.x) / direction.x;
                }
            }
            else
            {
                time.x = 0.0f;
            }

            if (start.y < box.min.y)
            {
                if (direction.y <= 0.0f)
                {
                    return false;
                }
                else
                {
                    inside = false;
                    faceDirection[1] = -1;
                    time.y = (box.min.y - start.y) / direction.y;
                }
            }
            else if (start.y > box.max.y)
            {
                if (direction.y >= 0.0f)
                {
                    return false;
                }
                else
                {
                    inside = false;
                    time.y = (box.max.y - start.y) / direction.y;
                }
            }
            else
            {
                time.y = 0.0f;
            }

            if (start.z < box.min.z)
            {
                if (direction.z <= 0.0f)
                {
                    return false;
                }
                else
                {
                    inside = false;
                    faceDirection[2] = -1;
                    time.z = (box.min.z - start.z) / direction.z;
                }
            }
            else if (start.z > box.max.z)
            {
                if (direction.z >= 0.0f)
                {
                    return false;
                }
                else
                {
                    inside = false;
                    time.z = (box.max.z - start.z) / direction.z;
                }
            }
            else
            {
                time.z = 0.0f;
            }

            // If the line started inside the box (ie. player started in contact with the fluid)
            if (inside)
            {
                hitLocation = start;
                hitNormal.z = 0;
                return true;
            }
            // Otherwise, calculate when hit occured
            else
            {
                if (time.y > time.z)
                {
                    hitTime = time.y;
                    hitNormal.y = faceDirection[1];
                }
                else
                {
                    hitTime = time.z;
                    hitNormal.z = faceDirection[2];
                }

                if (time.x > hitTime)
                {
                    hitTime = time.x;
                    hitNormal.x = faceDirection[0];
                }

                if (hitTime >= 0.0f && hitTime <= 1.0f)
                {
                    hitLocation = start + direction * hitTime;
                    const float BOX_SIDE_THRESHOLD = 0.1f;
                    if (hitLocation.x > box.min.x - BOX_SIDE_THRESHOLD && hitLocation.x < box.max.x + BOX_SIDE_THRESHOLD &&
                        hitLocation.y > box.min.y - BOX_SIDE_THRESHOLD && hitLocation.y < box.max.y + BOX_SIDE_THRESHOLD &&
                        hitLocation.z > box.min.z - BOX_SIDE_THRESHOLD && hitLocation.z < box.max.z + BOX_SIDE_THRESHOLD)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
