/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using System;
using UnityEngine;

namespace Live2D.Cubism.Rendering.URP.RenderingInterceptor
{
    public class CubismRenderingInterceptorsManager
    {
        /// <summary>
        /// Singleton instance.
        /// </summary>
        private static CubismRenderingInterceptorsManager _instance;

        /// <summary>
        /// Singleton instance of the <see cref="CubismRenderingInterceptorsManager"/>.
        /// </summary>
        public static CubismRenderingInterceptorsManager GetInstance()
        {
            if (_instance == null)
            {
                _instance = new CubismRenderingInterceptorsManager();
            }

            return _instance;
        }

        /// <summary>
        /// <see cref="Interceptors"/>'s backing field.
        /// </summary>
        private ICubismRenderingInterceptor[] _interceptors;

        /// <summary>
        /// Registered rendering interceptors.
        /// </summary>
        public ICubismRenderingInterceptor[] Interceptors
        {
            get { return _interceptors; }
            set { _interceptors = value; }
        }

        /// <summary>
        /// Private constructor to enforce singleton pattern.
        /// </summary>
        private CubismRenderingInterceptorsManager()
        {
            _interceptors = Array.Empty<ICubismRenderingInterceptor>();
        }

        /// <summary>
        /// Adds the given interceptor to the manager.
        /// </summary>
        /// <param name="interceptor"> The interceptor to add. </param>
        public void AddInterceptors(ICubismRenderingInterceptor interceptor)
        {
            RemoveInterceptors(interceptor);

            for (var interceptorIndex = 0; interceptorIndex < Interceptors.Length; interceptorIndex++)
            {
                var current = Interceptors[interceptorIndex];
                if (interceptor != current)
                {
                    continue;
                }

                Debug.LogWarning($"This component is already registered.");
                return;
            }

            Array.Resize(ref _interceptors, Interceptors.Length + 1);

            Interceptors[^1] = interceptor;
        }

        /// <summary>
        /// Removes the given interceptor from the manager.
        /// </summary>
        /// <param name="interceptor"> The interceptor to remove. </param>
        public void RemoveInterceptors(ICubismRenderingInterceptor interceptor)
        {
            var targetIndex = -1;
            for (var interceptorIndex = 0; interceptorIndex < Interceptors.Length; interceptorIndex++)
            {
                if (Interceptors[interceptorIndex] != interceptor)
                {
                    continue;
                }

                targetIndex = interceptorIndex;
                break;
            }

            if (targetIndex < 0)
            {
                return;
            }

            for (var interceptorIndex = targetIndex; interceptorIndex < Interceptors.Length - 1; interceptorIndex++)
            {
                Interceptors[interceptorIndex] = Interceptors[interceptorIndex + 1];
            }

            Array.Resize(ref _interceptors, Interceptors.Length - 1);
        }

        /// <summary>
        /// Reorders the given interceptor to the new index.
        /// </summary>
        /// <param name="interceptor"> The interceptor to reorder. </param>
        /// <param name="newIndex"> The new index to move the interceptor to. </param>
        public void ReorderIndices(ICubismRenderingInterceptor interceptor, int newIndex)
        {
            // Find the current index of the interceptor.
            var currentIndex = Array.IndexOf(Interceptors, interceptor);

            if (currentIndex < 0
                || newIndex < 0
                || newIndex >= Interceptors.Length
                || currentIndex == newIndex)
            {
                // Return silently.
                return;
            }

            var temp = Interceptors[currentIndex];
            if (currentIndex < newIndex)
            {
                // Shift elements to the left.
                for (var i = currentIndex; i < newIndex; i++)
                {
                    Interceptors[i] = Interceptors[i + 1];
                }
            }
            else
            {
                // Shift elements to the right.
                for (var i = currentIndex; i > newIndex; i--)
                {
                    Interceptors[i] = Interceptors[i - 1];
                }
            }

            // Place the interceptor at the new index.
            Interceptors[newIndex] = temp;
        }

        /// <summary>
        /// Called before rendering.
        /// </summary>
        /// <param name="args"> Rendering event arguments. </param>
        public void OnPreRendering(CubismRenderedEventArgs args)
        {
            for (var index = 0; index < Interceptors.Length; index++)
            {
                Interceptors[index].OnPreRenderingForPass(args);
            }
        }

        /// <summary>
        /// Called after rendering.
        /// </summary>
        /// <param name="args"> Rendering event arguments. </param>
        public void OnPostRendering(CubismRenderedEventArgs args)
        {
            for (var index = 0; index < Interceptors.Length; index++)
            {
                Interceptors[index].OnPostRenderingForPass(args);
            }
        }
    }
}
