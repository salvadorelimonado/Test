namespace WebApi.Features.Enrollments.Waitlist;

public sealed class JoinWaitlistResponse
{
    public long Id { get; set; }

    public long ClassId { get; set; }

    public int Seats { get; set; }

    public int Position { get; set; }

    public DateTime CreatedAt { get; set; }
}
