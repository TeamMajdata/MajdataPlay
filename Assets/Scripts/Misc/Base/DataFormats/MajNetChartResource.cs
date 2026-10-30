using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using UnityEngine.Scripting;

#nullable enable
namespace MajdataPlay
{
    /// <summary>A downloadable file of an online chart, as returned by the <c>maichart/info</c> API.</summary>
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class MajNetChartResource
    {
        /// <summary>Gets or sets the absolute public URL of the file, or null when the server sent none.</summary>
        [Preserve]
        public Uri? Url { get; set; }
        /// <summary>Gets or sets the MD5 digest of the file content as lowercase hexadecimal.</summary>
        [Preserve]
        public string Md5 { get; set; } = string.Empty;
        /// <summary>Gets or sets the SHA-256 digest of the file content as lowercase hexadecimal.</summary>
        [Preserve]
        public string Sha256 { get; set; } = string.Empty;
        /// <summary>Gets or sets the length of the file content in bytes.</summary>
        [Preserve]
        public long Length { get; set; }
    }
}
