namespace DataAccessLayer.Entities;

public enum EnrollmentStatus
{
    Active,
    Cancelled
}

public static class EnrollmentStatusMapping
{
    public static string ToContract(EnrollmentStatus status)
    {
        return status.ToString().ToLowerInvariant();
    }

    public static EnrollmentStatus FromContract(string value)
    {
        return Enum.Parse<EnrollmentStatus>(value, ignoreCase: true);
    }
}
