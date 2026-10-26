namespace WebApi.Features.Enrollments.Create;

public sealed class CreateEnrollmentRequest
{
    public required long ClassId { get; set; }

    public int Seats { get; set; }
}
