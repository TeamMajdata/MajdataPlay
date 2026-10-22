using System;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Runtime
{
    public delegate bool GCCallback();
    public delegate bool GCCallbackWithObject(object @object);
    public static class GCCallbackRegistration
    {
        class GCCallbackCriticalSource: CriticalFinalizerObject
        {
            ~GCCallbackCriticalSource()
            {
                RunCallbackAndReRegister(this);
            }
        }
        class GCCallbackSource
        {
            ~GCCallbackSource()
            {
                RunCallbackAndReRegister(this);
            }
        }
        class GCCallbackTask
        {
            public GCCallbackTask? Next { get; set; }

            readonly int _mode = -1;
            readonly GCHandle _handle;
            readonly GCCallback? _callback0;
            readonly GCCallbackWithObject? _callback1;

            public GCCallbackTask(GCCallback callback)
            {
                _callback0 = callback;
                _mode = 0;
            }
            public GCCallbackTask(GCCallbackWithObject callback, object @object)
            {
                _callback1 = callback;
                _handle = GCHandle.Alloc(@object, GCHandleType.Weak);
                _mode = 1;
            }

            public bool OnCallback()
            {
                switch (_mode)
                {
                    case 0:
                        {
                            try
                            {
                                if (!_callback0!())
                                {
                                    return false;
                                }
                            }
                            catch
                            {
#if DEBUG
                                throw;
#endif
                            }
                        }
                        break;
                    case 1:
                        {
                            var @object = _handle.Target;
                            if (@object is not null)
                            {
                                try
                                {
                                    if (!_callback1!(@object))
                                    {
                                        _handle.Free();
                                        return false;
                                    }
                                }
                                catch
                                {
#if DEBUG
                                    throw;
#endif
                                }
                            }
                            else
                            {
                                _handle.Free();
                                return false;
                            }
                        }
                        break;
                }
                return true;
            }
        }

        static GCCallbackTask? _callbackTaskHead;
        static GCCallbackTask? _callbackTaskTail;

        readonly static GCHandle _criticalSourceHandle;
        readonly static GCHandle _sourceHandle;
        readonly static object _lock = new object();
        static volatile bool _isShuttingDown;

        static GCCallbackRegistration()
        {
            Application.quitting += OnApplicationQuitting;
            //var source = new GCCallbackSource();
            var criticalSource = new GCCallbackCriticalSource();
            //_sourceHandle = GCHandle.Alloc(source, GCHandleType.Weak);
            _criticalSourceHandle = GCHandle.Alloc(criticalSource, GCHandleType.Weak);
        }

        static void OnApplicationQuitting()
        {
            _isShuttingDown = true;
        }

        static void RunCallbackAndReRegister(object source)
        {
            if (_isShuttingDown || Environment.HasShutdownStarted)
            {
                return;
            }

            OnGCCallback();

            if (!_isShuttingDown && !Environment.HasShutdownStarted)
            {
                GC.ReRegisterForFinalize(source);
            }
        }
        
        

        public static void RegisterGCCallback(GCCallback callback)
        {
            if(callback is null)
            {
                throw new ArgumentNullException(nameof(callback));
            }
            var task = new GCCallbackTask(callback);
            AddToLinkedList(task);
        }
        public static void RegisterGCCallback(GCCallbackWithObject callback, object @object)
        {
            if (callback is null)
            {
                throw new ArgumentNullException(nameof(callback));
            }
            var task = new GCCallbackTask(callback, @object);
            AddToLinkedList(task);
        }
        static void AddToLinkedList(GCCallbackTask task)
        {
            lock (_lock)
            {
                if(_callbackTaskHead is null)
                {
                    _callbackTaskHead = task;
                    _callbackTaskTail = task;
                }
                else
                {
                    _callbackTaskTail!.Next = task;
                    _callbackTaskTail = task;
                }
            }
        }
        static void OnGCCallback()
        {
            if (_callbackTaskHead is null)
            {
                return;
            }
            var taskList = default(GCCallbackTask?);

            lock (_lock)
            {
                taskList = _callbackTaskHead;
                _callbackTaskHead = null;
                _callbackTaskTail = null;
            }

            while (taskList != null)
            {
                var next = taskList.Next;
                taskList.Next = default;

                if (taskList.OnCallback())
                {
                    AddToLinkedList(taskList);
                }

                taskList = next;
            }
        }
    }
}
