using BusinessLayer.Result;
using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;

namespace BusinessLayer.Features.Enrollments.Waitlist;

public sealed class LeaveWaitlistService
{
    private readonly IUnitOfWork _unitOfWork;

    public LeaveWaitlistService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task<LeaveWaitlistModel> LeaveAsync(LeaveWaitlistCommand command, CancellationToken ct)
    {
        return _unitOfWork.ExecuteInTransactionAsync(
            token => LeaveLockedAsync(command, token),
            model => model.Result is OperationResult<LeftWaitlist>.Ok,
            ct);
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
                        EnrollmentConflictConstants.AlreadyPromoted),
                    PromotedEnrollmentId = entry.PromotedEnrollmentId
                };
            case WaitlistStatus.Cancelled:
                return Done(ToResponse(entry));
            case WaitlistStatus.Waiting:
                entry.Status = WaitlistStatus.Cancelled;
                await _unitOfWork.SaveChangesAsync(ct);
                return Done(ToResponse(entry));
            default:
                throw new ArgumentOutOfRangeException(nameof(entry.Status), entry.Status, null);
        }
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
            Status = WaitlistStatus.Cancelled
        };
    }
}
