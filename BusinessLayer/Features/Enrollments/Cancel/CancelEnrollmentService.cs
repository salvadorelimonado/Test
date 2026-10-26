using BusinessLayer.Extensions;
using BusinessLayer.Features.Enrollments;
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

    public async Task<OperationResult<CancelEnrollmentResponse>> CancelAsync(CancelEnrollmentCommand command, CancellationToken ct)
    {
        var attempt = await _unitOfWork.ExecuteInTransactionAsync(
            token => CancelLockedAsync(command, token),
            result => result.Result is OperationResult<CancelEnrollmentResponse>.Ok,
            ct);
        if (attempt.Result is OperationResult<CancelEnrollmentResponse>.Ok)
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
            return CancelAttempt.Stop(new OperationResult<CancelEnrollmentResponse>.NotFound());
        }

        var scheduledClass = await _unitOfWork.Class.GetByIdForUpdateAsync(owner.ClassId, ct);
        if (scheduledClass is null)
        {
            throw new InvalidOperationException("Class was not found.");
        }

        var enrollment = await _unitOfWork.Enrollment.GetByIdForUpdateAsync(command.EnrollmentId, ct);
        if (enrollment is null || enrollment.UserId != command.UserId)
        {
            return CancelAttempt.Stop(new OperationResult<CancelEnrollmentResponse>.NotFound());
        }

        if (enrollment.Status == EnrollmentStatus.Cancelled)
        {
            return CancelAttempt.Done(new OperationResult<CancelEnrollmentResponse>.Ok(ToResponse(enrollment)), enrollment.UserId);
        }

        var now = _clock.TruncateToMicroseconds();
        if (!scheduledClass.IsCancelled && now >= scheduledClass.StartsAt)
        {
            return CancelAttempt.Stop(new OperationResult<CancelEnrollmentResponse>.Conflict(EnrollmentConflictConstants.ClassStarted));
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

        var promotedUserIds = await PromoteAsync(scheduledClass, enrollment.UserId, now, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        var affectedUserIds = new List<long> { enrollment.UserId };
        affectedUserIds.AddRange(promotedUserIds);
        return CancelAttempt.Done(new OperationResult<CancelEnrollmentResponse>.Ok(ToResponse(enrollment)), affectedUserIds);
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

    private async Task<IReadOnlyList<long>> PromoteAsync(
        Class scheduledClass,
        long cancellingUserId,
        DateTime now,
        CancellationToken ct)
    {
        if (scheduledClass.IsCancelled)
        {
            return [];
        }

        var waiting = await _unitOfWork.Waitlist.ListWaitingForUpdateAsync(scheduledClass.Id, ct);
        foreach (var entry in waiting)
        {
            if (entry.UserId == cancellingUserId)
            {
                entry.Status = WaitlistStatus.Cancelled;
            }
        }

        var stillWaiting = waiting.Where(entry => entry.Status == WaitlistStatus.Waiting).ToList();
        if (stillWaiting.Count == 0)
        {
            return [];
        }

        var takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(scheduledClass.Id, ct);
        var freeSeats = scheduledClass.Capacity - takenSeats;
        if (freeSeats <= 0)
        {
            return [];
        }

        var passes = await LockPassesAsync(stillWaiting, ct);
        return await AssignAsync(scheduledClass, stillWaiting, passes, freeSeats, now, ct);
    }

    private async Task<Dictionary<long, Pass>> LockPassesAsync(IReadOnlyList<WaitlistEntry> waiting, CancellationToken ct)
    {
        var passes = new Dictionary<long, Pass>();
        foreach (var userId in waiting.Select(entry => entry.UserId).Distinct().OrderBy(userId => userId))
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

    private async Task<List<long>> AssignAsync(
        Class scheduledClass,
        IReadOnlyList<WaitlistEntry> waiting,
        IReadOnlyDictionary<long, Pass> passes,
        int freeSeats,
        DateTime now,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now);
        var remainingSeats = freeSeats;
        var promotedUserIds = new List<long>();
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
                continue;
            }

            pass.RemainingVisits -= entry.Seats;
            var enrollment = await _unitOfWork.Enrollment.CreateAsync(
                new Enrollment
                {
                    UserId = entry.UserId,
                    ClassId = scheduledClass.Id,
                    Seats = entry.Seats,
                    Status = EnrollmentStatus.Active,
                    CreatedAt = now
                },
                ct);
            entry.Status = WaitlistStatus.Promoted;
            entry.PromotedEnrollment = enrollment;
            remainingSeats -= entry.Seats;
            promotedUserIds.Add(entry.UserId);
        }

        return promotedUserIds;
    }

    private static CancelEnrollmentResponse ToResponse(Enrollment enrollment)
    {
        return new CancelEnrollmentResponse
        {
            Id = enrollment.Id,
            Status = EnrollmentStatusMapping.ToContract(enrollment.Status),
            RefundedVisits = enrollment.RefundedVisits
        };
    }

    private sealed record CancelAttempt(OperationResult<CancelEnrollmentResponse> Result, IReadOnlyList<long> AffectedUserIds)
    {
        public static CancelAttempt Stop(OperationResult<CancelEnrollmentResponse> result)
        {
            return new CancelAttempt(result, []);
        }

        public static CancelAttempt Done(OperationResult<CancelEnrollmentResponse> result, long userId)
        {
            return new CancelAttempt(result, [userId]);
        }

        public static CancelAttempt Done(
            OperationResult<CancelEnrollmentResponse> result,
            IReadOnlyList<long> affectedUserIds)
        {
            return new CancelAttempt(result, affectedUserIds);
        }
    }
}
