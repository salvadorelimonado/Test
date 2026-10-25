namespace WebApi.Features.Enrollments.Create;

public sealed class CreateEnrollmentRequest
{
    public long ClassId { get; set; }

    public int Seats { get; set; }
}
