using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BusinessLayer.Features.Enrollments.Cancel;

public sealed class CancelEnrollmentService
{
    private static readonly TimeSpan FullRefundLeadTime = TimeSpan.FromHours(2);

    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public CancelEnrollmentService(IUnitOfWork unitOfWork, TimeProvider clock)
    {
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public Task<CancelEnrollmentResult> CancelAsync(CancelEnrollmentCommand command, CancellationToken ct)
    {
        var strategy = _unitOfWork.DbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(() => CancelInTransactionAsync(command, ct));
    }

    private async Task<CancelEnrollmentResult> CancelInTransactionAsync(CancelEnrollmentCommand command, CancellationToken ct)
    {
        _unitOfWork.ClearChangeTracker();
        await using var transaction = await _unitOfWork.DbContext.Database.BeginTransactionAsync(ct);
        var result = await CancelLockedAsync(command, ct);
        if (result.Outcome == CancelEnrollmentOutcome.Succeeded)
        {
            await transaction.CommitAsync(ct);
        }

        return result;
    }

    private async Task<CancelEnrollmentResult> CancelLockedAsync(CancelEnrollmentCommand command, CancellationToken ct)
    {
        var owner = await _unitOfWork.Enrollment.FindOwnerAsync(command.EnrollmentId, ct);
        if (owner is null || owner.UserId != command.UserId)
        {
            return CancelEnrollmentResult.NotFound();
        }

        var scheduledClass = await _unitOfWork.Class.GetByIdForUpdateAsync(owner.ClassId, ct);
        if (scheduledClass is null)
        {
            throw new InvalidOperationException("Class was not found.");
        }

        var enrollment = await _unitOfWork.Enrollment.GetByIdForUpdateAsync(command.EnrollmentId, ct);
        if (enrollment is null || enrollment.UserId != command.UserId)
        {
            return CancelEnrollmentResult.NotFound();
        }

        if (enrollment.Status == EnrollmentStatus.Cancelled)
        {
            return CancelEnrollmentResult.Succeeded(ToResponse(enrollment));
        }

        var now = TruncateToMicroseconds(_clock.GetUtcNow().UtcDateTime);
        if (!scheduledClass.IsCancelled && now >= scheduledClass.StartsAt)
        {
            return CancelEnrollmentResult.Conflict(EnrollmentConflictConstants.ClassStarted);
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

        if (!scheduledClass.IsCancelled)
        {
            await PromoteAsync(scheduledClass, waiting, passes, enrollment.Seats, now, ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return CancelEnrollmentResult.Succeeded(ToResponse(enrollment));
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

    private async Task PromoteAsync(
        Class scheduledClass,
        IReadOnlyList<WaitlistEntry> waiting,
        IReadOnlyDictionary<long, Pass> passes,
        int freedSeats,
        DateTime now,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now);
        var remainingSeats = freedSeats;
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
        }
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
