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

        if (scheduledClass.IsCancelled)
        {
            var clubPass = await LockPassAsync(enrollment.UserId, ct);
            var clubRefund = Refund(scheduledClass, enrollment, clubPass, now);
            enrollment.Status = EnrollmentStatus.Cancelled;
            enrollment.CancelledAt = now;
            enrollment.RefundedVisits = clubRefund;
            await _unitOfWork.SaveChangesAsync(ct);
            return CancelAttempt.Done(new OperationResult<CancelledEnrollment>.Ok(ToResponse(enrollment)), enrollment.UserId);
        }

        var waiting = await _unitOfWork.Waitlist.ListWaitingForUpdateAsync(scheduledClass.Id, ct);
        foreach (var entry in waiting)
        {
            if (entry.UserId == enrollment.UserId)
            {
                entry.Status = WaitlistStatus.Cancelled;
            }
        }

        var stillWaiting = waiting.Where(entry => entry.Status == WaitlistStatus.Waiting).ToList();
        var takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(scheduledClass.Id, ct);
        var freeSeats = scheduledClass.Capacity - takenSeats + enrollment.Seats;
        var queueUserIds = freeSeats > 0
            ? WaitlistFillResult.UserIdsWithinFreeSeats(stillWaiting, freeSeats)
            : [];
        var passes = await LockPassesAsync(WaitlistFillResult.InLockOrder(queueUserIds, enrollment.UserId), ct);
        if (!passes.TryGetValue(enrollment.UserId, out var pass))
        {
            throw new InvalidOperationException("Pass was not found.");
        }

        var refundedVisits = Refund(scheduledClass, enrollment, pass, now);
        enrollment.Status = EnrollmentStatus.Cancelled;
        enrollment.CancelledAt = now;
        enrollment.RefundedVisits = refundedVisits;
        await _unitOfWork.SaveChangesAsync(ct);

        var promotion = freeSeats > 0 && stillWaiting.Count > 0
            ? WaitlistFillResult.Fill(scheduledClass, stillWaiting, passes, freeSeats, now)
            : WaitlistFillResult.None;
        if (promotion.Changed)
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }

        var affectedUserIds = new List<long> { enrollment.UserId };
        affectedUserIds.AddRange(promotion.PromotedUserIds);
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

    private async Task<Pass> LockPassAsync(long userId, CancellationToken ct)
    {
        var passes = await LockPassesAsync(WaitlistFillResult.InLockOrder([], userId), ct);
        if (!passes.TryGetValue(userId, out var pass))
        {
            throw new InvalidOperationException("Pass was not found.");
        }

        return pass;
    }

    private async Task<Dictionary<long, Pass>> LockPassesAsync(IReadOnlyList<long> userIds, CancellationToken ct)
    {
        var passes = new Dictionary<long, Pass>();
        foreach (var userId in userIds)
        {
            var pass = await _unitOfWork.Pass.GetByUserForUpdateAsync(userId, ct);
            if (pass is null)
            {
                throw new InvalidOperationException("Pass was not found.");
            }

            passes.Add(userId, pass);
        }

        return passes;
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
