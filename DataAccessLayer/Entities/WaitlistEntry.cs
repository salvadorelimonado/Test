using Common.Database.Interfaces;

namespace DataAccessLayer.Entities;

public class WaitlistEntry : IEntity
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public long ClassId { get; set; }

    public int Seats { get; set; }

    public WaitlistStatus Status { get; set; }

    public long? PromotedEnrollmentId { get; set; }

    public Enrollment? PromotedEnrollment { get; set; }

    public DateTime CreatedAt { get; set; }
}
