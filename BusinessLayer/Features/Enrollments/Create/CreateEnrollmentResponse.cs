namespace BusinessLayer.Features.Enrollments.Create;

public sealed class CreateEnrollmentResponse
{
    public long Id { get; set; }

    public long ClassId { get; set; }

    public int Seats { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
