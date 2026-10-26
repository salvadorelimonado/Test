using DataAccessLayer.Entities;

namespace BusinessLayer.Features.Enrollments.Waitlist;

public sealed class WaitlistFillResult
{
    public static WaitlistFillResult None { get; } = new([], false);

    public WaitlistFillResult(IReadOnlyList<long> promotedUserIds, bool changed)
    {
        PromotedUserIds = promotedUserIds;
        Changed = changed;
    }

    public IReadOnlyList<long> PromotedUserIds { get; }

    public bool Changed { get; }

    public static IReadOnlyList<long> UserIdsWithinFreeSeats(IReadOnlyList<WaitlistEntry> waiting, int freeSeats)
    {
        var userIds = new List<long>();
        foreach (var entry in waiting)
        {
            if (entry.Seats > freeSeats)
            {
                break;
            }

            userIds.Add(entry.UserId);
        }

        return userIds;
    }

    public static WaitlistFillResult Fill(
        Class scheduledClass,
        IReadOnlyList<WaitlistEntry> waiting,
        IReadOnlyDictionary<long, Pass> passes,
        int freeSeats,
        DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var remainingSeats = freeSeats;
        var promotedUserIds = new List<long>();
        var changed = false;
        foreach (var entry in waiting)
        {
            if (entry.Seats > remainingSeats)
            {
                break;
            }

            if (!passes.TryGetValue(entry.UserId, out var pass))
            {
                throw new InvalidOperationException("Pass was not found.");
            }

            if (pass.ValidUntil < today || pass.RemainingVisits < entry.Seats)
            {
                entry.Status = WaitlistStatus.Skipped;
                changed = true;
                continue;
            }

            pass.RemainingVisits -= entry.Seats;
            entry.PromotedEnrollment = new Enrollment
            {
                UserId = entry.UserId,
                ClassId = scheduledClass.Id,
                Seats = entry.Seats,
                Status = EnrollmentStatus.Active,
                CreatedAt = now
            };
            entry.Status = WaitlistStatus.Promoted;
            remainingSeats -= entry.Seats;
            promotedUserIds.Add(entry.UserId);
            changed = true;
        }

        return new WaitlistFillResult(promotedUserIds, changed);
    }
}
