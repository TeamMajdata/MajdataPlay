using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace MajdataPlay.Threading
{
    public static class UniTaskExtensions
    {
        public static UniTask Register(this UniTask source, string taskName = "", string sceneName = "")
        {
            return TaskTracker.Register(source, taskName, sceneName);
        }
        public static UniTask RegisterAsWorker(this UniTask source, string taskName = "", string sceneName = "")
        {
            return TaskTracker.Register(source, taskName, sceneName, true);
        }

        public static UniTask<T> Register<T>(this UniTask<T> source, string taskName = "", string sceneName = "")
        {
            return TaskTracker.Register(source, taskName, sceneName);
        }
        public static UniTask<T> RegisterAsWorker<T>(this UniTask<T> source, string taskName = "", string sceneName = "")
        {
            return TaskTracker.Register(source, taskName, sceneName, true);
        }
    }
}
