using System.Text.Json.Serialization;

namespace Hsin.Media.Installation;

[JsonConverter(typeof(JsonStringEnumConverter<MediaPackageSource>))]
public enum MediaPackageSource
{
    [JsonStringEnumMemberName("official")]
    Official,

    [JsonStringEnumMemberName("user-package")]
    UserPackage
}
