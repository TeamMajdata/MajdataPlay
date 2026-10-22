using MajdataPlay.Diagnostics;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Diagnostics
{
    /// <summary>Covers the profiler sample scope <see cref="UnityProfiler"/>.</summary>
    public class UnityProfilerTests
    {
        /// <summary>
        /// Checks that Create opens a named sample that the using scope closes again; an unbalanced sample would make
        /// Unity log an error and fail the test.
        /// </summary>
        [Test]
        public void Create_OpensNamedSampleClosedByDispose()
        {
            using (var sample = UnityProfiler.Create("MajdataPlay.Tests.Outer"))
            {
                Assert.That(sample.Name, Is.EqualTo("MajdataPlay.Tests.Outer"));
                using (var nested = UnityProfiler.Create("MajdataPlay.Tests.Inner"))
                {
                    Assert.That(nested.Name, Is.EqualTo("MajdataPlay.Tests.Inner"));
                }
            }
        }
    }
}
