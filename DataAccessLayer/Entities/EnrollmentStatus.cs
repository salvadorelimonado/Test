using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataAccessLayer.Entities;

[JsonConverter(typeof(KebabCaseEnumConverter<EnrollmentStatus>))]
public enum EnrollmentStatus
{
    Active,
    Cancelled
}

public static class EnrollmentStatusMapping
{
    public static string ToDatabase(EnrollmentStatus status)
    {
        return JsonNamingPolicy.KebabCaseLower.ConvertName(status.ToString());
    }

    public static EnrollmentStatus FromDatabase(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (!Enum.TryParse<EnrollmentStatus>(value, ignoreCase: true, out var status))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, null);
        }

        return status;
    }
}
