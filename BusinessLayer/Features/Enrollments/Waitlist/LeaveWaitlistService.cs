using BusinessLayer.Extensions;
using BusinessLayer.Features.Enrollments;
using BusinessLayer.Result;
using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;

namespace BusinessLayer.Features.Enrollments.Waitlist;

public sealed class LeaveWaitlistService
{
    private const string PromotedEnrollmentId = "promotedEnrollmentId";

    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;
    private readonly EnrollmentCardCache _cardCache;

    public LeaveWaitlistService(IUnitOfWork unitOfWork, TimeProvider clock, EnrollmentCardCache cardCache)
    {
        _unitOfWork = unitOfWork;
        _clock = clock;
        _cardCache = cardCache;
    }

    public async Task<OperationResult<LeftWaitlist>> LeaveAsync(LeaveWaitlistCommand command, CancellationToken ct)
    {
        var attempt = await _unitOfWork.ExecuteInTransactionAsync(
            token => LeaveLockedAsync(command, token),
            attempt => attempt.Result is OperationResult<LeftWaitlist>.Ok,
            ct);
        if (attempt.Result is OperationResult<LeftWaitlist>.Ok)
        {
            foreach (var userId in attempt.PromotedUserIds)
            {
                await _cardCache.InvalidateAsync(userId);
            }
        }

        return attempt.Result;
    }

    private async Task<LeaveAttempt> LeaveLockedAsync(LeaveWaitlistCommand command, CancellationToken ct)
    {
        var owner = await _unitOfWork.Waitlist.FindOwnerAsync(command.EntryId, ct);
        if (owner is null || owner.UserId != command.UserId)
        {
            return NotFound();
        }

        var scheduledClass = await _unitOfWork.Class.GetByIdForUpdateAsync(owner.ClassId, ct);
        if (scheduledClass is null)
        {
            throw new InvalidOperationException("Class was not found.");
        }

        var entry = await _unitOfWork.Waitlist.GetByIdForUpdateAsync(command.EntryId, ct);
        if (entry is null || entry.UserId != command.UserId)
        {
            return NotFound();
        }

        switch (entry.Status)
        {
            case WaitlistStatus.Promoted:
                // решение: своя причина already-promoted. Если бронь уже создана, в ответ добавляется promotedEnrollmentId.
                return Stop(AlreadyPromoted(entry.PromotedEnrollmentId));
            case WaitlistStatus.Cancelled:
                return Done(ToResponse(entry), []);
            case WaitlistStatus.Skipped:
                return Done(ToResponse(entry.Id, WaitlistStatus.Cancelled), []);
            case WaitlistStatus.Waiting:
                entry.Status = WaitlistStatus.Cancelled;
                await _unitOfWork.SaveChangesAsync(ct);
                var promotedUserIds = await PromoteIfTheClassIsOpenAsync(scheduledClass, ct);
                return Done(ToResponse(entry), promotedUserIds);
            default:
                throw new ArgumentOutOfRangeException(nameof(entry.Status), entry.Status, null);
        }
    }

    private static OperationResult<LeftWaitlist> AlreadyPromoted(long? promotedEnrollmentId)
    {
        if (promotedEnrollmentId is long enrollmentId)
        {
            return new OperationResult<LeftWaitlist>.Conflict(
                ConflictReason.AlreadyPromoted,
                new Dictionary<string, object>
                {
                    [PromotedEnrollmentId] = enrollmentId
                });
        }

        return new OperationResult<LeftWaitlist>.Conflict(ConflictReason.AlreadyPromoted);
    }

    private async Task<IReadOnlyList<long>> PromoteIfTheClassIsOpenAsync(Class scheduledClass, CancellationToken ct)
    {
        var now = _clock.TruncateToMicroseconds();
        if (scheduledClass.IsCancelled || now >= scheduledClass.StartsAt)
        {
            return [];
        }

        var promotion = await PromoteWaitingAsync(scheduledClass, now, ct);
        return promotion.PromotedUserIds;
    }

    private async Task<WaitlistFillResult> PromoteWaitingAsync(Class scheduledClass, DateTime now, CancellationToken ct)
    {
        if (scheduledClass.IsCancelled)
        {
            return WaitlistFillResult.None;
        }

        var takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(scheduledClass.Id, ct);
        var freeSeats = scheduledClass.Capacity - takenSeats;
        var waiting = await _unitOfWork.Waitlist.ListWaitingForUpdateAsync(scheduledClass.Id, ct);
        if (waiting.Count == 0)
        {
            return WaitlistFillResult.None;
        }

        var passes = await _unitOfWork.Pass.GetByUsersForUpdateAsync(
            WaitlistFillResult.PassIdsToLock(waiting, null),
            ct);
        var result = WaitlistFillResult.Fill(scheduledClass, waiting, passes, freeSeats, now);
        if (result.Changed)
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }

        return result;
    }

    private static LeaveAttempt Done(LeftWaitlist response, IReadOnlyList<long> promotedUserIds)
    {
        return new LeaveAttempt(new OperationResult<LeftWaitlist>.Ok(response), promotedUserIds);
    }

    private static LeaveAttempt Stop(OperationResult<LeftWaitlist> result)
    {
        return new LeaveAttempt(result, []);
    }

    private static LeaveAttempt NotFound()
    {
        return Stop(new OperationResult<LeftWaitlist>.NotFound());
    }

    private static LeftWaitlist ToResponse(WaitlistEntry entry)
    {
        return ToResponse(entry.Id, entry.Status);
    }

    private static LeftWaitlist ToResponse(long entryId, WaitlistStatus status)
    {
        return new LeftWaitlist
        {
            Id = entryId,
            Status = status
        };
    }

    private sealed record LeaveAttempt(OperationResult<LeftWaitlist> Result, IReadOnlyList<long> PromotedUserIds);
}
