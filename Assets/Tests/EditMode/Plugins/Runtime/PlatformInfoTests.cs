using System;
using System.Linq;
using System.Threading;
using MajdataPlay.Runtime;
using MajdataPlay.Syscall.Win32;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Runtime
{
    /// <summary>Covers the platform, architecture and thread queries in <see cref="PlatformInfo"/>.</summary>
    public class PlatformInfoTests
    {
        /// <summary>Checks that exactly the Windows flag is set in the Windows Editor.</summary>
        [Test]
        public void PlatformFlags_IdentifyWindowsEditor()
        {
#if UNITY_STANDALONE_WIN
            Assert.That(PlatformInfo.IsWindows, Is.True);
#else
            Assert.That(PlatformInfo.IsWindows, Is.False);
#endif
            Assert.That(PlatformInfo.IsLinux, Is.False);
            Assert.That(PlatformInfo.IsMacOS, Is.False);
            Assert.That(PlatformInfo.IsAndroid, Is.False);
            Assert.That(PlatformInfo.IsIOS, Is.False);
        }

        /// <summary>Checks that the pointer width flags agree with the process and with each other.</summary>
        [Test]
        public void BitnessFlags_MatchProcess()
        {
            Assert.That(PlatformInfo.Is64bitProcessor, Is.EqualTo(Environment.Is64BitProcess));
            Assert.That(PlatformInfo.Is32bitProcessor, Is.Not.EqualTo(PlatformInfo.Is64bitProcessor));
        }

        /// <summary>Checks that exactly one architecture flag is set and that it matches the pointer width.</summary>
        [Test]
        public void ArchitectureFlags_IdentifyOneArchitecture()
        {
            var flags = new[]
            {
                PlatformInfo.IsX86Processor,
                PlatformInfo.IsX64Processor,
                PlatformInfo.IsArmV7Processor,
                PlatformInfo.IsArm64Processor,
            };

            Assert.That(flags.Count(x => x), Is.EqualTo(1));
            if (PlatformInfo.IsX64Processor || PlatformInfo.IsArm64Processor)
            {
                Assert.That(PlatformInfo.Is64bitProcessor, Is.True);
            }
            else
            {
                Assert.That(PlatformInfo.Is32bitProcessor, Is.True);
            }
        }

        /// <summary>Checks that the OS thread id is stable on one thread and differs between threads.</summary>
        [Test]
        public void GetCurrentOSThreadId_IdentifiesThreads()
        {
            var first = PlatformInfo.GetCurrentOSThreadId();
            var second = PlatformInfo.GetCurrentOSThreadId();
            var other = 0UL;
            var thread = new Thread(() => other = PlatformInfo.GetCurrentOSThreadId());

            thread.Start();
            thread.Join();

            Assert.That(first, Is.Not.EqualTo(0UL));
            Assert.That(second, Is.EqualTo(first));
            Assert.That(other, Is.Not.EqualTo(0UL));
            Assert.That(other, Is.Not.EqualTo(first));
        }

#if UNITY_STANDALONE_WIN
        /// <summary>Checks that the Windows implementation reports the kernel thread id.</summary>
        [Test]
        public void GetCurrentOSThreadId_MatchesWin32ThreadId()
        {
            Assert.That(PlatformInfo.GetCurrentOSThreadId(), Is.EqualTo((ulong)Win32API.GetCurrentThreadId()));
        }
#endif
    }
}
