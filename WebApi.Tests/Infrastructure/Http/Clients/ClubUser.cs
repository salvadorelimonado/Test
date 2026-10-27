namespace WebApi.Tests.Infrastructure.Http.Clients;

public sealed class ClubUser
{
    internal ClubUser(int number, string? email)
    {
        Number = number;
        Enrollments = new EnrollmentsClient(email);
        Waitlist = new WaitlistClient(email);
        Schedule = new ScheduleClient(email);
        Analytics = new AnalyticsClient(email);
    }

    public int Number { get; }

    public EnrollmentsClient Enrollments { get; }

    public WaitlistClient Waitlist { get; }

    public ScheduleClient Schedule { get; }

    public AnalyticsClient Analytics { get; }
}
