using BusinessLayer.Features.Enrollments;
using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using Microsoft.EntityFrameworkCore;

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

    public async Task<CancelEnrollmentResult> CancelAsync(CancelEnrollmentCommand command, CancellationToken ct)
    {
        var strategy = _unitOfWork.DbContext.Database.CreateExecutionStrategy();
        var attempt = await strategy.ExecuteAsync(() => CancelInTransactionAsync(command, ct));
        if (attempt.Result.Outcome == CancelEnrollmentOutcome.Succeeded)
        {
            foreach (var userId in attempt.AffectedUserIds.Distinct())
            {
                await _cardCache.InvalidateAsync(userId, ct);
            }
        }

        return attempt.Result;
    }

    private async Task<CancelAttempt> CancelInTransactionAsync(CancelEnrollmentCommand command, CancellationToken ct)
    {
        _unitOfWork.ClearChangeTracker();
        await using var transaction = await _unitOfWork.DbContext.Database.BeginTransactionAsync(ct);
        var attempt = await CancelLockedAsync(command, ct);
        if (attempt.Result.Outcome == CancelEnrollmentOutcome.Succeeded)
        {
            await transaction.CommitAsync(ct);
        }

        return attempt;
    }

    private async Task<CancelAttempt> CancelLockedAsync(CancelEnrollmentCommand command, CancellationToken ct)
    {
        var owner = await _unitOfWork.Enrollment.FindOwnerAsync(command.EnrollmentId, ct);
        if (owner is null || owner.UserId != command.UserId)
        {
            return CancelAttempt.Stop(CancelEnrollmentResult.NotFound());
        }

        var scheduledClass = await _unitOfWork.Class.GetByIdForUpdateAsync(owner.ClassId, ct);
        if (scheduledClass is null)
        {
            throw new InvalidOperationException("Class was not found.");
        }

        var enrollment = await _unitOfWork.Enrollment.GetByIdForUpdateAsync(command.EnrollmentId, ct);
        if (enrollment is null || enrollment.UserId != command.UserId)
        {
            return CancelAttempt.Stop(CancelEnrollmentResult.NotFound());
        }

        if (enrollment.Status == EnrollmentStatus.Cancelled)
        {
            return CancelAttempt.Done(CancelEnrollmentResult.Succeeded(ToResponse(enrollment)), enrollment.UserId);
        }

        var now = TruncateToMicroseconds(_clock.GetUtcNow().UtcDateTime);
        if (!scheduledClass.IsCancelled && now >= scheduledClass.StartsAt)
        {
            return CancelAttempt.Stop(CancelEnrollmentResult.Conflict(EnrollmentConflictConstants.ClassStarted));
        }

        var waiting = scheduledClass.IsCancelled
            ? new List<WaitlistEntry>()
            : await _unitOfWork.Waitlist.ListWaitingForUpdateAsync(scheduledClass.Id, ct);
        var passes = await LockPassesAsync(enrollment.UserId, waiting, ct);
        if (!passes.TryGetValue(enrollment.UserId, out var pass))
        {
            throw new InvalidOperationException("Pass was not found.");
        }

        var refundedVisits = Refund(scheduledClass, enrollment, pass, now);
        enrollment.Status = EnrollmentStatus.Cancelled;
        enrollment.CancelledAt = now;
        enrollment.RefundedVisits = refundedVisits;

        var promotedUserIds = scheduledClass.IsCancelled
            ? new List<long>()
            : await PromoteAsync(scheduledClass, waiting, passes, enrollment.Seats, now, ct);

        await _unitOfWork.SaveChangesAsync(ct);
        var affectedUserIds = new List<long> { enrollment.UserId };
        affectedUserIds.AddRange(promotedUserIds);
        return CancelAttempt.Done(CancelEnrollmentResult.Succeeded(ToResponse(enrollment)), affectedUserIds);
    }

    private async Task<Dictionary<long, Pass>> LockPassesAsync(
        long ownerUserId,
        IReadOnlyList<WaitlistEntry> waiting,
        CancellationToken ct)
    {
        var userIds = waiting
            .Select(entry => entry.UserId)
            .Append(ownerUserId)
            .Distinct()
            .OrderBy(userId => userId);
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

    private async Task<List<long>> PromoteAsync(
        Class scheduledClass,
        IReadOnlyList<WaitlistEntry> waiting,
        IReadOnlyDictionary<long, Pass> passes,
        int freedSeats,
        DateTime now,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now);
        var remainingSeats = freedSeats;
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
            entry.Status = WaitlistStatus.Promoted;
            await _unitOfWork.Enrollment.CreateAsync(
                new Enrollment
                {
                    UserId = entry.UserId,
                    ClassId = scheduledClass.Id,
                    Seats = entry.Seats,
                    Status = EnrollmentStatus.Active,
                    CreatedAt = now
                },
                ct);
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

    private static DateTime TruncateToMicroseconds(DateTime value)
    {
        var ticks = value.Ticks - value.Ticks % 10;
        return new DateTime(ticks, DateTimeKind.Utc);
    }
}
