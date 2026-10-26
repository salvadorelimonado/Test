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
                await _cardCache.InvalidateAsync(userId);
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
            // решение: на отменённом клубом занятии очередь не двигаем и свою заявку не снимаем.
            var clubPass = await _unitOfWork.Pass.GetByUserForUpdateAsync(enrollment.UserId, ct);
            if (clubPass is null)
            {
                throw new InvalidOperationException("Pass was not found.");
            }
            var clubRefund = Refund(scheduledClass, enrollment, clubPass, now);
            enrollment.Status = EnrollmentStatus.Cancelled;
            enrollment.CancelledAt = now;
            enrollment.RefundedVisits = clubRefund;
            await _unitOfWork.SaveChangesAsync(ct);
            return CancelAttempt.Done(new OperationResult<CancelledEnrollment>.Ok(ToResponse(enrollment)), enrollment.UserId);
        }

        var takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(scheduledClass.Id, ct);
        var freeSeats = scheduledClass.Capacity - takenSeats + enrollment.Seats;
        var waiting = await _unitOfWork.Waitlist.ListWaitingForUpdateAsync(scheduledClass.Id, ct);
        var stillWaiting = WaitlistFillResult.CancelOwnRequest(waiting, enrollment.UserId);
        var passes = await _unitOfWork.Pass.GetByUsersForUpdateAsync(
            WaitlistFillResult.PassIdsToLock(stillWaiting, enrollment.UserId),
            ct);
        var pass = WaitlistFillResult.Require(passes, enrollment.UserId);
        var refundedVisits = Refund(scheduledClass, enrollment, pass, now);
        enrollment.Status = EnrollmentStatus.Cancelled;
        enrollment.CancelledAt = now;
        enrollment.RefundedVisits = refundedVisits;
        await _unitOfWork.SaveChangesAsync(ct);
        // решение: места освобождаем и очередь двигаем даже если до начала меньше двух часов и визиты не вернулись.
        var promotion = WaitlistFillResult.TryFill(scheduledClass, stillWaiting, passes, freeSeats, now);
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
        // решение: полный возврат, если до начала не меньше двух часов или занятие отменил клуб.
        var refundsFully = scheduledClass.IsCancelled || scheduledClass.StartsAt - now >= FullRefundLeadTime;
        if (!refundsFully)
        {
            return 0;
        }

        pass.RemainingVisits += enrollment.Seats;
        return enrollment.Seats;
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
