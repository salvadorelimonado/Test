namespace DataAccessLayer.Entities;

public enum WaitlistStatus
{
    Waiting,
    Promoted,
    Cancelled
}

public static class WaitlistStatusMapping
{
    public static string ToContract(WaitlistStatus status)
    {
        return status.ToString().ToLowerInvariant();
    }
}
