/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;


namespace Live2D.Cubism.Plugins.Editor
{
    /// <summary>
    /// Configure iOS plugins before the build.
    /// </summary>
    public class CubismiOSPluginProcessor : IPreprocessBuildWithReport
    {
        /// <summary>
        /// Execution order.
        /// </summary>
        public int callbackOrder
        {
            get { return 0; }
        }

        /// <summary>
        /// Enable the appropriate plugins from the SDK Type and SDK Version in the iOS Build Target before building.
        /// </summary>
        public void OnPreprocessBuild(BuildReport report)
        {
            // Skip the process if the build is not for iOS.
            if (report.summary.platform != BuildTarget.iOS)
            {
                return;
            }


            // Detect the type of iOS plugin by SDK type and SDK version in the build settings.
            CubismiOSPlugin targetPlugin;

#if  UNITY_2021_2_OR_NEWER
            if (EditorUserBuildSettings.iOSXcodeBuildConfig == XcodeBuildConfig.Debug)
#else
            if (EditorUserBuildSettings.iOSBuildConfigType == iOSBuildType.Debug)
#endif
            {
                targetPlugin = PlayerSettings.iOS.sdkVersion == iOSSdkVersion.DeviceSDK
                    ? CubismiOSPlugin.DebugIphoneos
                    : PlayerSettings.iOS.simulatorSdkArchitecture == AppleMobileArchitectureSimulator.X86_64
                        ? CubismiOSPlugin.DebugIphoneSimulatorX64
                        : CubismiOSPlugin.DebugIphoneSimulatorArm64;
            }
            else
            {
                targetPlugin = PlayerSettings.iOS.sdkVersion == iOSSdkVersion.DeviceSDK
                    ? CubismiOSPlugin.ReleaseIphoneos
                    : PlayerSettings.iOS.simulatorSdkArchitecture == AppleMobileArchitectureSimulator.X86_64
                        ? CubismiOSPlugin.ReleaseIphoneSimulatorX64
                        : CubismiOSPlugin.ReleaseIphoneSimulatorArm64;
            }

            if (PlayerSettings.iOS.sdkVersion ==iOSSdkVersion.SimulatorSDK
                && PlayerSettings.iOS.simulatorSdkArchitecture == AppleMobileArchitectureSimulator.Universal)
            {
                Debug.LogWarning("Arm64 Core will be used for `Universal`. If you want to check on x86_64 devices(Rosetta 2), please build for `x86_64`.");
            }


            // Extract the Cubism iOS plugin from the plugin.
            var pluginImporters = PluginImporter.GetAllImporters()
                .Where(pluginImporter =>
                    Regex.IsMatch(
                        pluginImporter.assetPath,
                        @"^.*/iOS/.*/libLive2DCubismCore.a$"
                    )
                )
                .ToArray();


            // Enable only the appropriate plugins.
            foreach (var pluginImporter in pluginImporters)
            {
                pluginImporter.SetCompatibleWithPlatform(
                    BuildTarget.iOS,
                    pluginImporter.assetPath.Contains(targetPlugin.DirectoryName)
                );
            }
        }


        /// <summary>
        /// Defines the type of plugin for iOS.
        /// </summary>
        private class CubismiOSPlugin
        {
            public readonly string DirectoryName;

            public static CubismiOSPlugin DebugIphoneos
            {
                get { return new CubismiOSPlugin("Debug-iphoneos"); }
            }
            public static CubismiOSPlugin DebugIphoneSimulatorArm64
            {
                get { return new CubismiOSPlugin("Debug-iphonesimulator-arm64"); }
            }
            public static CubismiOSPlugin DebugIphoneSimulatorX64
            {
                get { return new CubismiOSPlugin("Debug-iphonesimulator-x86_64"); }
            }
            public static CubismiOSPlugin ReleaseIphoneos
            {
                get { return new CubismiOSPlugin("Release-iphoneos"); }
            }
            public static CubismiOSPlugin ReleaseIphoneSimulatorArm64
            {
                get { return new CubismiOSPlugin("Release-iphonesimulator-arm64"); }
            }
            public static CubismiOSPlugin ReleaseIphoneSimulatorX64
            {
                get { return new CubismiOSPlugin("Release-iphonesimulator-x86_64"); }
            }

            private CubismiOSPlugin(string directoryName)
            {
                DirectoryName = directoryName;
            }
        }
    }
}
