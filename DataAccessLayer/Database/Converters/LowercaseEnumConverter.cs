using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DataAccessLayer.Database.Converters;

public static class LowercaseEnumConverter
{
    public static ValueConverter<TEnum, string> Create<TEnum>()
        where TEnum : struct, Enum
    {
        return new ValueConverter<TEnum, string>(
            value => value.ToString().ToLowerInvariant(),
            value => Enum.Parse<TEnum>(value, true));
    }

    public static string ToColumn<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        return value.ToString().ToLowerInvariant();
    }
}
