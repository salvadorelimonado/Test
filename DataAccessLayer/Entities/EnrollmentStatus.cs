namespace DataAccessLayer.Entities;

public enum EnrollmentStatus
{
    Active,
    Cancelled
}

public static class EnrollmentStatusMapping
{
    public static string ToDatabase(EnrollmentStatus status)
    {
        return status switch
        {
            EnrollmentStatus.Active => "active",
            EnrollmentStatus.Cancelled => "cancelled",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
    }

    public static EnrollmentStatus FromDatabase(string value)
    {
        return value switch
        {
            "active" => EnrollmentStatus.Active,
            "cancelled" => EnrollmentStatus.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };
    }
}
