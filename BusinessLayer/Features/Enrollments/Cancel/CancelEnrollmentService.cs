using BusinessLayer.Extensions;
using BusinessLayer.Features.Enrollments;
using BusinessLayer.Features.Enrollments.Waitlist;
using BusinessLayer.Result;
using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;

namespace BusinessLayer.Features.Enrollments.Cancel;

public sealed class CancelEnrollmentService
{
    private static readonly TimeSpan FullRefundLeadTime = TimeSpan.FromHours(2);

    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;
    private readonly EnrollmentCardCache _cardCache;

    public CancelEnrollmentService(IUnitOfWork unitOfWork, TimeProvider clock, EnrollmentCardCache cardCache)
    {
        _unitOfWork = unitOfWork;
        _clock = clock;
        _cardCache = cardCache;
    }

    public async Task<OperationResult<CancelledEnrollment>> CancelAsync(CancelEnrollmentCommand command, CancellationToken ct)
    {
        var attempt = await _unitOfWork.ExecuteInTransactionAsync(
            token => CancelLockedAsync(command, token),
            result => result.Result is OperationResult<CancelledEnrollment>.Ok,
            ct);
        if (attempt.Result is OperationResult<CancelledEnrollment>.Ok)
        {
            foreach (var userId in attempt.AffectedUserIds.Distinct())
            {
                await _cardCache.InvalidateAsync(userId, ct);
            }
        }

        return attempt.Result;
    }

    private async Task<CancelAttempt> CancelLockedAsync(CancelEnrollmentCommand command, CancellationToken ct)
    {
        var owner = await _unitOfWork.Enrollment.FindOwnerAsync(command.EnrollmentId, ct);
        if (owner is null || owner.UserId != command.UserId)
        {
            return CancelAttempt.Stop(new OperationResult<CancelledEnrollment>.NotFound());
        }

        var scheduledClass = await _unitOfWork.Class.GetByIdForUpdateAsync(owner.ClassId, ct);
        if (scheduledClass is null)
        {
            throw new InvalidOperationException("Class was not found.");
        }

        var enrollment = await _unitOfWork.Enrollment.GetByIdForUpdateAsync(command.EnrollmentId, ct);
        if (enrollment is null || enrollment.UserId != command.UserId)
        {
            return CancelAttempt.Stop(new OperationResult<CancelledEnrollment>.NotFound());
        }

        if (enrollment.Status == EnrollmentStatus.Cancelled)
        {
            return CancelAttempt.Done(new OperationResult<CancelledEnrollment>.Ok(ToResponse(enrollment)), enrollment.UserId);
        }

        var now = _clock.TruncateToMicroseconds();
        if (!scheduledClass.IsCancelled && now >= scheduledClass.StartsAt)
        {
            return CancelAttempt.Stop(new OperationResult<CancelledEnrollment>.Conflict(ConflictReason.ClassStarted));
        }

        var pass = await _unitOfWork.Pass.GetByUserForUpdateAsync(enrollment.UserId, ct);
        if (pass is null)
        {
            throw new InvalidOperationException("Pass was not found.");
        }

        var refundedVisits = Refund(scheduledClass, enrollment, pass, now);
        enrollment.Status = EnrollmentStatus.Cancelled;
        enrollment.CancelledAt = now;
        enrollment.RefundedVisits = refundedVisits;
        await _unitOfWork.SaveChangesAsync(ct);

        var promotedUserIds = new List<long>();
        if (!scheduledClass.IsCancelled)
        {
            await WithdrawOwnWaitingAsync(scheduledClass.Id, enrollment.UserId, ct);
            var promotion = await PromoteWaitingAsync(scheduledClass, now, ct);
            promotedUserIds.AddRange(promotion.PromotedUserIds);
        }
        var affectedUserIds = new List<long> { enrollment.UserId };
        affectedUserIds.AddRange(promotedUserIds);
        return CancelAttempt.Done(new OperationResult<CancelledEnrollment>.Ok(ToResponse(enrollment)), affectedUserIds);
    }

    private static int Refund(Class scheduledClass, Enrollment enrollment, Pass pass, DateTime now)
    {
        var refundsFully = scheduledClass.IsCancelled || scheduledClass.StartsAt - now >= FullRefundLeadTime;
        if (!refundsFully)
        {
            return 0;
        }

        pass.RemainingVisits += enrollment.Seats;
        return enrollment.Seats;
    }

    private async Task WithdrawOwnWaitingAsync(long classId, long userId, CancellationToken ct)
    {
        var waiting = await _unitOfWork.Waitlist.ListWaitingForUpdateAsync(classId, ct);
        var changed = false;
        foreach (var entry in waiting)
        {
            if (entry.UserId == userId)
            {
                entry.Status = WaitlistStatus.Cancelled;
                changed = true;
            }
        }

        if (changed)
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
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

    private static CancelledEnrollment ToResponse(Enrollment enrollment)
    {
        return new CancelledEnrollment
        {
            Id = enrollment.Id,
            Status = enrollment.Status,
            RefundedVisits = enrollment.RefundedVisits
        };
    }

    private sealed record CancelAttempt(OperationResult<CancelledEnrollment> Result, IReadOnlyList<long> AffectedUserIds)
    {
        public static CancelAttempt Stop(OperationResult<CancelledEnrollment> result)
        {
            return new CancelAttempt(result, []);
        }

        public static CancelAttempt Done(OperationResult<CancelledEnrollment> result, long userId)
        {
            return new CancelAttempt(result, [userId]);
        }

        public static CancelAttempt Done(
            OperationResult<CancelledEnrollment> result,
            IReadOnlyList<long> affectedUserIds)
        {
            return new CancelAttempt(result, affectedUserIds);
        }
    }
}
