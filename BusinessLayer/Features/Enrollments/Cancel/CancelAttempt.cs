namespace BusinessLayer.Features.Enrollments.Cancel;

public sealed class CancelAttempt
{
    private CancelAttempt(CancelEnrollmentResult result, IReadOnlyList<long> affectedUserIds)
    {
        Result = result;
        AffectedUserIds = affectedUserIds;
    }

    public CancelEnrollmentResult Result { get; }

    public IReadOnlyList<long> AffectedUserIds { get; }

    public static CancelAttempt Stop(CancelEnrollmentResult result)
    {
        return new CancelAttempt(result, Array.Empty<long>());
    }

    public static CancelAttempt Done(CancelEnrollmentResult result, long userId)
    {
        return new CancelAttempt(result, new[] { userId });
    }

    public static CancelAttempt Done(CancelEnrollmentResult result, IReadOnlyList<long> affectedUserIds)
    {
        return new CancelAttempt(result, affectedUserIds);
    }
}
