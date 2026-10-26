using DataAccessLayer.Entities;
using DataAccessLayer.Repositories;

namespace BusinessLayer.Features.Enrollments;

public sealed class EnrollmentCard
{
    public required long Id { get; init; }

    public required EnrollmentStatus Status { get; init; }

    public required int Seats { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required EnrollmentCardClass Class { get; init; }

    public required EnrollmentCardPass Pass { get; init; }

    public static EnrollmentCard Create(EnrollmentCardRow row)
    {
        if (row is null)
        {
            throw new ArgumentNullException(nameof(row));
        }

        var pass = row.Pass ?? throw new InvalidOperationException("Pass was not found.");
        return new EnrollmentCard
        {
            Id = row.Id,
            Status = row.Status,
            Seats = row.Seats,
            CreatedAt = row.CreatedAt,
            Class = new EnrollmentCardClass
            {
                Id = row.ClassId,
                Title = row.Title,
                StartsAt = row.StartsAt,
                DurationMinutes = row.DurationMinutes,
                IsCancelled = row.IsCancelled,
                Room = new EnrollmentCardRoom
                {
                    Id = row.RoomId,
                    Name = row.RoomName
                }
            },
            Pass = new EnrollmentCardPass
            {
                RemainingVisits = pass.RemainingVisits,
                ValidUntil = pass.ValidUntil
            }
        };
    }
}

public sealed class EnrollmentCardClass
{
    public required long Id { get; init; }

    public required string Title { get; init; }

    public required DateTime StartsAt { get; init; }

    public required int DurationMinutes { get; init; }

    public required bool IsCancelled { get; init; }

    public required EnrollmentCardRoom Room { get; init; }
}

public sealed class EnrollmentCardRoom
{
    public required long Id { get; init; }

    public required string Name { get; init; }
}

public sealed class EnrollmentCardPass
{
    public required int RemainingVisits { get; init; }

    public required DateOnly ValidUntil { get; init; }
}
