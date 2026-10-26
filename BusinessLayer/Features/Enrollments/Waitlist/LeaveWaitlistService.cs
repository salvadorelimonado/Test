using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BusinessLayer.Features.Enrollments.Waitlist;

public sealed class LeaveWaitlistService
{
    private readonly IUnitOfWork _unitOfWork;

    public LeaveWaitlistService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<LeaveWaitlistResult> LeaveAsync(LeaveWaitlistCommand command, CancellationToken ct)
    {
        return await LeaveInTransactionAsync(command, ct);
    }

    private async Task<LeaveWaitlistResult> LeaveInTransactionAsync(LeaveWaitlistCommand command, CancellationToken ct)
    {
        _unitOfWork.ClearChangeTracker();
        await using var transaction = await _unitOfWork.DbContext.Database.BeginTransactionAsync(ct);
        var result = await LeaveLockedAsync(command, ct);
        if (result.Outcome == LeaveWaitlistOutcome.Succeeded)
        {
            await transaction.CommitAsync(ct);
        }

        return result;
    }

    private async Task<LeaveWaitlistResult> LeaveLockedAsync(LeaveWaitlistCommand command, CancellationToken ct)
    {
        var owner = await _unitOfWork.Waitlist.FindOwnerAsync(command.EntryId, ct);
        if (owner is null || owner.UserId != command.UserId)
        {
            return LeaveWaitlistResult.NotFound();
        }

        var scheduledClass = await _unitOfWork.Class.GetByIdForUpdateAsync(owner.ClassId, ct);
        if (scheduledClass is null)
        {
            throw new InvalidOperationException("Class was not found.");
        }

        var entry = await _unitOfWork.Waitlist.GetByIdForUpdateAsync(command.EntryId, ct);
        if (entry is null || entry.UserId != command.UserId)
        {
            return LeaveWaitlistResult.NotFound();
        }

        switch (entry.Status)
        {
            case WaitlistStatus.Promoted:
                return LeaveWaitlistResult.Conflict(
                    EnrollmentConflictConstants.AlreadyPromoted,
                    entry.PromotedEnrollmentId);
            case WaitlistStatus.Cancelled:
                return LeaveWaitlistResult.Succeeded(ToResponse(entry));
            case WaitlistStatus.Waiting:
                entry.Status = WaitlistStatus.Cancelled;
                await _unitOfWork.SaveChangesAsync(ct);
                return LeaveWaitlistResult.Succeeded(ToResponse(entry));
            default:
                throw new ArgumentOutOfRangeException(nameof(entry.Status), entry.Status, null);
        }
    }

    private static LeaveWaitlistResponse ToResponse(WaitlistEntry entry)
    {
        return new LeaveWaitlistResponse
        {
            Id = entry.Id,
            Status = WaitlistStatusMapping.ToContract(WaitlistStatus.Cancelled)
        };
    }
}
