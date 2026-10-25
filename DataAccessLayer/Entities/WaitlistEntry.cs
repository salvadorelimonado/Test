using Common.Database.Interfaces;

namespace DataAccessLayer.Entities;

public class WaitlistEntry : IEntity
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public long ClassId { get; set; }

    public int Seats { get; set; }

    public WaitlistStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }
}
