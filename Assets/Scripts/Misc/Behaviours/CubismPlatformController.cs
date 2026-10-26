using MajdataPlay.Runtime;
using System;
using System.Collections.Generic;
using System.Text;

namespace MajdataPlay.Behaviours
{
    public sealed class CubismPlatformController : MajBehaviour
    {
        protected override void Awake()
        {
            base.Awake();
            if ((PlatformInfo.IsAndroid && PlatformInfo.Is32bitProcessor) ||
                (PlatformInfo.IsWindows && PlatformInfo.IsArm64Processor) ||
                (PlatformInfo.IsLinux && !PlatformInfo.IsX64Processor))
            {
                gameObject.SetActive(false);
            }
        }
    }
}
