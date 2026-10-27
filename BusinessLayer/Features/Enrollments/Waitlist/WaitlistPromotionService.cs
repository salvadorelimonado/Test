using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BusinessLayer.Features.Enrollments.Waitlist;

public sealed class WaitlistPromotionService
{
    private readonly IUnitOfWork _unitOfWork;

    public WaitlistPromotionService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<long>> PromoteAsync(
        Class scheduledClass,
        DateTime now,
        long? includeUserId,
        long? excludeUserId,
        long? withdrawUserId,
        CancellationToken ct)
    {
        if (_unitOfWork.DbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A waitlist promotion requires a transaction.");
        }

        var takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(scheduledClass.Id, ct);
        var freeSeats = scheduledClass.Capacity - takenSeats;
        var waiting = await _unitOfWork.Waitlist.ListWaitingForUpdateAsync(scheduledClass.Id, ct);
        var ownRequestWithdrawn = false;
        if (withdrawUserId is long userIdToWithdraw)
        {
            ownRequestWithdrawn = MarkOwnRequestCancelled(waiting, userIdToWithdraw);
            waiting = waiting.Where(entry => entry.Status == WaitlistStatus.Waiting).ToList();
        }

        if (excludeUserId is long excludedUserId)
        {
            waiting = OmitOwnRequest(waiting, excludedUserId);
        }

        var passes = await _unitOfWork.Pass.GetByUsersForUpdateAsync(PassIdsToLock(waiting, includeUserId), ct);
        var filled = Fill(scheduledClass, waiting, passes, freeSeats, now);
        if (filled.Changed || ownRequestWithdrawn)
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }

        return filled.PromotedUserIds;
    }

    internal static bool MarkOwnRequestCancelled(IReadOnlyList<WaitlistEntry> waiting, long userId)
    {
        // решение: свою заявку на это занятие снимаем до продвижения, иначе клиент
        // сразу получит вторую бронь на места, которые только что освободил.
        var cancelled = false;
        foreach (var entry in waiting)
        {
            if (entry.UserId == userId)
            {
                entry.Status = WaitlistStatus.Cancelled;
                cancelled = true;
            }
        }

        return cancelled;
    }

    internal static List<WaitlistEntry> OmitOwnRequest(IReadOnlyList<WaitlistEntry> waiting, long userId)
    {
        // решение: при записи и увеличении мест свою заявку не продвигаем и не снимаем.
        // Контракт разрешает ждать ещё места при уже активной брони.
        return waiting.Where(entry => entry.UserId != userId).ToList();
    }

    internal static (IReadOnlyList<long> PromotedUserIds, bool Changed) Fill(
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
            if (!passes.TryGetValue(entry.UserId, out var pass))
            {
                throw new InvalidOperationException("Pass was not found.");
            }

            if (pass.ValidUntil < today || pass.RemainingVisits < entry.Seats)
            {
                // решение: заявку снимаем, как только посещений не хватает или истёк срок, даже если мест пока нет.
                // Позже вернуть посещения уже не восстановит место в очереди.
                entry.Status = WaitlistStatus.Skipped;
                changed = true;
                continue;
            }

            if (entry.Seats > remainingSeats)
            {
                // решение: берём первую заявку, которой хватает мест, а не строго голову очереди.
                // Большая заявка может ждать долго, зато каждой оставшейся нужно больше мест, чем свободно.
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

        return (promotedUserIds, changed);
    }

    private static List<long> PassIdsToLock(IReadOnlyList<WaitlistEntry> waiting, long? includeUserId)
    {
        var userIds = waiting.Select(entry => entry.UserId).Distinct().ToList();
        if (includeUserId is long userId && !userIds.Contains(userId))
        {
            userIds.Add(userId);
        }

        return userIds;
    }
}
