using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace MajdataPlay.Threading
{
    public static class ValueTaskExtensions
    {
        public static ValueTask Register(this ValueTask source, string taskName = "", string sceneName = "")
        {
            return TaskTracker.Register(source, taskName, sceneName);
        }
        public static ValueTask RegisterAsWorker(this ValueTask source, string taskName = "", string sceneName = "")
        {
            return TaskTracker.Register(source, taskName, sceneName, true);
        }

        public static ValueTask<T> Register<T>(this ValueTask<T> source, string taskName = "", string sceneName = "")
        {
            return TaskTracker.Register(source, taskName, sceneName);
        }
        public static ValueTask<T> RegisterAsWorker<T>(this ValueTask<T> source, string taskName = "", string sceneName = "")
        {
            return TaskTracker.Register(source, taskName, sceneName, true);
        }
    }
}
