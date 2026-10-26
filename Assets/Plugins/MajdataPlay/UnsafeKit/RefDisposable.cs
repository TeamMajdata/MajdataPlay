using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataPlay.UnsafeKit
{
    public readonly ref struct RefDisposable
    {
        public readonly ref struct Disposable<T> where T : IDisposable
        {
            public readonly ref T Instance;
            public readonly bool SetNullWhenDispose;

            internal Disposable(ref T instance, bool setNullWhenDispose)
            {
                Instance = ref instance;
                SetNullWhenDispose = setNullWhenDispose;
            }

            public readonly void Dispose()
            {
                Instance?.Dispose();
                if (SetNullWhenDispose)
                {
                    Instance = default;
                }
            }
        }
        public static Disposable<T> From<T>(ref T instance, bool setNullWhenDispose = false) where T : IDisposable
        {
            return new(ref instance, setNullWhenDispose);
        }
    }
}
