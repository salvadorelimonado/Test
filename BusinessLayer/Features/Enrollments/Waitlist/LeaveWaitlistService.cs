using BusinessLayer.Extensions;
using BusinessLayer.Features.Enrollments;
using BusinessLayer.Result;
using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;

namespace BusinessLayer.Features.Enrollments.Waitlist;

public sealed class LeaveWaitlistService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;
    private readonly EnrollmentCardCache _cardCache;

    public LeaveWaitlistService(IUnitOfWork unitOfWork, TimeProvider clock, EnrollmentCardCache cardCache)
    {
        _unitOfWork = unitOfWork;
        _clock = clock;
        _cardCache = cardCache;
    }

    public async Task<LeaveWaitlistModel> LeaveAsync(LeaveWaitlistCommand command, CancellationToken ct)
    {
        var model = await _unitOfWork.ExecuteInTransactionAsync(
            token => LeaveLockedAsync(command, token),
            model => model.Result is OperationResult<LeftWaitlist>.Ok,
            ct);
        if (model.Result is OperationResult<LeftWaitlist>.Ok)
        {
            foreach (var userId in model.PromotedUserIds)
            {
                await _cardCache.InvalidateAsync(userId, ct);
            }
        }

        return model;
    }

    private async Task<LeaveWaitlistModel> LeaveLockedAsync(LeaveWaitlistCommand command, CancellationToken ct)
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
                return new LeaveWaitlistModel
                {
                    Result = new OperationResult<LeftWaitlist>.Conflict(
                        ConflictReason.AlreadyPromoted),
                    PromotedEnrollmentId = entry.PromotedEnrollmentId
                };
            case WaitlistStatus.Cancelled:
            case WaitlistStatus.Skipped:
                return Done(ToResponse(entry));
            case WaitlistStatus.Waiting:
                entry.Status = WaitlistStatus.Cancelled;
                await _unitOfWork.SaveChangesAsync(ct);
                var promotedUserIds = await PromoteIfTheClassIsOpenAsync(scheduledClass, ct);
                return new LeaveWaitlistModel
                {
                    Result = new OperationResult<LeftWaitlist>.Ok(ToResponse(entry)),
                    PromotedUserIds = promotedUserIds
                };
            default:
                throw new ArgumentOutOfRangeException(nameof(entry.Status), entry.Status, null);
        }
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
        if (freeSeats <= 0)
        {
            return WaitlistFillResult.None;
        }

        var waiting = await _unitOfWork.Waitlist.ListWaitingForUpdateAsync(scheduledClass.Id, ct);
        if (waiting.Count == 0)
        {
            return WaitlistFillResult.None;
        }

        var passes = new Dictionary<long, Pass>();
        foreach (var userId in WaitlistFillResult.UserIdsWithinFreeSeats(waiting, freeSeats).Distinct().OrderBy(id => id))
        {
            var pass = await _unitOfWork.Pass.GetByUserForUpdateAsync(userId, ct);
            if (pass is null)
            {
                throw new InvalidOperationException("Pass was not found.");
            }

            passes.Add(userId, pass);
        }

        var result = WaitlistFillResult.Fill(scheduledClass, waiting, passes, freeSeats, now);
        if (result.Changed)
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }

        return result;
    }

    private static LeaveWaitlistModel Done(LeftWaitlist response)
    {
        return new LeaveWaitlistModel
        {
            Result = new OperationResult<LeftWaitlist>.Ok(response)
        };
    }

    private static LeaveWaitlistModel NotFound()
    {
        return new LeaveWaitlistModel
        {
            Result = new OperationResult<LeftWaitlist>.NotFound()
        };
    }

    private static LeftWaitlist ToResponse(WaitlistEntry entry)
    {
        return new LeftWaitlist
        {
            Id = entry.Id,
            Status = entry.Status
        };
    }
}
