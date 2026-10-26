using System;
using System.IO;

#nullable enable

namespace MajdataPlay.Tests.AndroidJavaWrapperGeneratorValidation
{
    /// <summary>Identifies the independent validation toolchains.</summary>
    internal sealed class ValidationSettings
    {
        /// <summary>Gets or sets the installed Unity Editor data directory.</summary>
        internal string UnityData { get; set; } = "C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data";

        /// <summary>Gets or sets the Android SDK root.</summary>
        internal string Sdk { get; set; } = string.Empty;

        /// <summary>Gets or sets the JDK root.</summary>
        internal string Jdk { get; set; } = string.Empty;

        /// <summary>Gets or sets the .NET Standard 2.1 reference assembly directory.</summary>
        internal string NetStandardReferences { get; set; } = string.Empty;

        /// <summary>Gets or sets whether the entire Android SDK output is compiled.</summary>
        internal bool FullSdk { get; set; }

        /// <summary>Gets or sets whether only full bytecode SDK compilation is requested.</summary>
        internal bool SdkOnly { get; set; }

        /// <summary>Gets or sets whether only real Editor inputs and managed Editor integration are validated.</summary>
        internal bool EditorInputsOnly { get; set; }

        /// <summary>Gets or sets the directory containing downloaded official HTML validation samples.</summary>
        internal string? OfficialSamples { get; set; }

        /// <summary>Reads optional host-specific paths and validates prerequisites.</summary>
        /// <param name="arguments">The validation command arguments.</param>
        /// <returns>The available toolchains and requested coverage.</returns>
        /// <exception cref="ArgumentException">An option or value is invalid.</exception>
        /// <exception cref="DirectoryNotFoundException">A required toolchain is missing.</exception>
        internal static ValidationSettings Parse(string[] arguments)
        {
            var settings = new ValidationSettings();
            for (var i = 0; i < arguments.Length; i++)
            {
                var option = arguments[i];
                if (option == "--editor-inputs")
                {
                    settings.EditorInputsOnly = true;
                    continue;
                }

                if (option == "--sdk-only")
                {
                    settings.SdkOnly = true;
                    settings.FullSdk = true;
                    continue;
                }

                if (option == "--full-sdk")
                {
                    settings.FullSdk = true;
                    continue;
                }

                if (++i >= arguments.Length)
                {
                    throw new ArgumentException("Missing validation value for " + option);
                }

                var value = Path.GetFullPath(arguments[i]);
                switch (option)
                {
                    case "--official-samples":
                        settings.OfficialSamples = value;
                        break;
                    case "--unity-data":
                        settings.UnityData = value;
                        break;
                    case "--sdk":
                        settings.Sdk = value;
                        break;
                    case "--jdk":
                        settings.Jdk = value;
                        break;
                    case "--netstandard-refs":
                        settings.NetStandardReferences = value;
                        break;
                    default:
                        throw new ArgumentException("Unknown validation option: " + option);
                }
            }

            if (settings.Sdk.Length == 0)
            {
                settings.Sdk = Path.Combine(settings.UnityData, "PlaybackEngines", "AndroidPlayer", "SDK");
            }

            if (settings.Jdk.Length == 0)
            {
                settings.Jdk = Path.Combine(settings.UnityData, "PlaybackEngines", "AndroidPlayer", "OpenJDK");
            }

            if (settings.NetStandardReferences.Length == 0)
            {
                var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? (OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet") : "/usr/share/dotnet");
                settings.NetStandardReferences = Path.Combine(dotnetRoot, "packs", "NETStandard.Library.Ref", "2.1.0", "ref", "netstandard2.1");
            }

            foreach (var directory in (settings.SdkOnly || settings.EditorInputsOnly ? new[] { settings.UnityData, settings.Sdk, settings.NetStandardReferences } : new[] { settings.UnityData, settings.Sdk, settings.Jdk, settings.NetStandardReferences }))
            {
                if (!Directory.Exists(directory))
                {
                    throw new DirectoryNotFoundException("Validation prerequisite is missing: " + directory);
                }
            }

            return settings;
        }
    }
}
