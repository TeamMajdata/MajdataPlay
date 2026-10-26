using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace MajdataPlay.Diagnostics
{
    [IgnoredByDeepProfiler]
    public readonly ref struct UnityProfiler
    {
        public required string Name { get; init; }

        [HideInCallstack]
        [Conditional("ENABLE_PROFILER")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            Profiler.EndSample();
        }

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UnityProfiler Create(string name)
        {
            Profiler.BeginSample(name);
            return new UnityProfiler()
            {
                Name = name,
            };
        }
    }
}
