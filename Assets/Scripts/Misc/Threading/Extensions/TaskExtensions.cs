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
            var currentScene = SceneSwitcher.CurrentScene;
            return TaskTracker.Register(source, taskName, currentScene.ToString());
        }
        public static Task RegisterAsWorker(this Task source, string taskName = "")
        {
            var currentScene = SceneSwitcher.CurrentScene;
            return TaskTracker.Register(source, taskName, currentScene.ToString(), true);
        }

        public static Task<T> Register<T>(this Task<T> source, string taskName = "")
        {
            var currentScene = SceneSwitcher.CurrentScene;
            return TaskTracker.Register(source, taskName, currentScene.ToString());
        }
        public static Task<T> RegisterAsWorker<T>(this Task<T> source, string taskName = "")
        {
            var currentScene = SceneSwitcher.CurrentScene;
            return TaskTracker.Register(source, taskName, currentScene.ToString(), true);
        }
    }
}
