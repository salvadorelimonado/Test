namespace WebApi.Features.Enrollments.Waitlist;

public sealed class JoinWaitlistRequest
{
    public long ClassId { get; set; }

    public int Seats { get; set; }
}
