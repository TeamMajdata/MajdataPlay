using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Applies platform-specific JSON exclusions while preserving the default Newtonsoft settings contracts.
    /// </summary>
    /// <remarks>
    /// Each instance keeps its platform fixed so cached contracts cannot retain the exclusions of another platform.
    /// Reuse the instance for both reading and writing settings.
    /// </remarks>
    internal sealed class SettingContractResolver : DefaultContractResolver
    {
        /// <summary>The build target platform whose exclusions apply to this resolver's cached contracts.</summary>
        private readonly SettingPlatform _platform;

        /// <summary>
        /// Initializes a resolver for the current Unity build target.
        /// </summary>
        internal SettingContractResolver() : this(SettingPlatformInfo.Current)
        {
        }

        /// <summary>
        /// Initializes a resolver for a fixed platform.
        /// </summary>
        /// <param name="platform">The platform whose JSON exclusions apply.</param>
        internal SettingContractResolver(SettingPlatform platform)
        {
            _platform = platform;
        }

        /// <summary>
        /// Creates the default property contract and ignores members whose platform exclusion matches this resolver.
        /// </summary>
        /// <param name="member">The field or property described by the contract.</param>
        /// <param name="memberSerialization">The containing type's member serialization mode.</param>
        /// <returns>The property contract, excluded from both reading and writing when its platform rule matches.</returns>
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);
            var attribute = member.GetCustomAttribute<JsonIgnoreOnPlatformAttribute>(true);
            if (attribute is not null && (attribute.Platforms & _platform) != SettingPlatform.None)
            {
                property.Ignored = true;
            }
            return property;
        }
    }
}
