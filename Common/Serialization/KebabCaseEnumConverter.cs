using System.Text.Json;
using System.Text.Json.Serialization;

namespace Common.Serialization;

public sealed class KebabCaseEnumConverter<TEnum> : JsonStringEnumConverter<TEnum>
    where TEnum : struct, Enum
{
    public KebabCaseEnumConverter()
        : base(JsonNamingPolicy.KebabCaseLower)
    {
    }
}
