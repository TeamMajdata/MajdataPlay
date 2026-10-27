using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace MajdataPlay.Threading
{
    public static class TaskExtensions
    {
        public static Task Register(this Task source, string taskName = "")
        {
            return TaskTracker.Register(source, taskName, TaskRegistrationScene.GetName());
        }
        public static Task RegisterAsWorker(this Task source, string taskName = "")
        {
            return TaskTracker.Register(source, taskName, TaskRegistrationScene.GetName(), true);
        }

        public static Task<T> Register<T>(this Task<T> source, string taskName = "")
        {
            return TaskTracker.Register(source, taskName, TaskRegistrationScene.GetName());
        }
        public static Task<T> RegisterAsWorker<T>(this Task<T> source, string taskName = "")
        {
            return TaskTracker.Register(source, taskName, TaskRegistrationScene.GetName(), true);
        }
    }

    /// <summary>Supplies the scene name attached to task registrations made through the game extensions.</summary>
    internal static class TaskRegistrationScene
    {
        /// <summary>
        /// Gets the current game scene name for a registration, or null when <see cref="TaskTracker.IsTrackingSupported"/>
        /// is false, so player builds skip the enum-to-string allocation of a record that is never kept.
        /// </summary>
        /// <returns>The scene name, or null when tracking is not supported.</returns>
        internal static string GetName()
        {
            if (!TaskTracker.IsTrackingSupported)
            {
                return null;
            }

            return SceneSwitcher.CurrentScene.ToString();
        }
    }
}
