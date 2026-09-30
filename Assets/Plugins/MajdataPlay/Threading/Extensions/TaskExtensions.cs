using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace MajdataPlay.Threading
{
    public static class TaskExtensions
    {
        public static Task Register(this Task source, string taskName = "", string sceneName = "")
        {
            return TaskTracker.Register(source, taskName, sceneName);
        }
        public static Task RegisterAsWorker(this Task source, string taskName = "", string sceneName = "")
        {
            return TaskTracker.Register(source, taskName, sceneName, true);
        }

        public static Task<T> Register<T>(this Task<T> source, string taskName = "", string sceneName = "")
        {
            return TaskTracker.Register(source, taskName, sceneName);
        }
        public static Task<T> RegisterAsWorker<T>(this Task<T> source, string taskName = "", string sceneName = "")
        {
            return TaskTracker.Register(source, taskName, sceneName, true);
        }
    }
}
