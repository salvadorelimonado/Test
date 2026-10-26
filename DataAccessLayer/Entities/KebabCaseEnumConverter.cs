using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataAccessLayer.Entities;

public sealed class KebabCaseEnumConverter<TEnum> : JsonStringEnumConverter<TEnum>
    where TEnum : struct, Enum
{
    public KebabCaseEnumConverter()
        : base(JsonNamingPolicy.KebabCaseLower)
    {
    }
}
