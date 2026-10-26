using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BusinessLayer.Features.Enrollments.ChangeSeats;

public sealed class ChangeSeatsService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public ChangeSeatsService(IUnitOfWork unitOfWork, TimeProvider clock)
    {
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public Task<ChangeSeatsResult> ChangeAsync(ChangeSeatsCommand command, CancellationToken ct)
    {
        var strategy = _unitOfWork.DbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(() => ChangeInTransactionAsync(command, ct));
    }

    private async Task<ChangeSeatsResult> ChangeInTransactionAsync(ChangeSeatsCommand command, CancellationToken ct)
    {
        _unitOfWork.ClearChangeTracker();
        await using var transaction = await _unitOfWork.DbContext.Database.BeginTransactionAsync(ct);
        var result = await ChangeLockedAsync(command, ct);
        if (result.Outcome == ChangeSeatsOutcome.Succeeded)
        {
            await transaction.CommitAsync(ct);
        }

        return result;
    }

    private async Task<ChangeSeatsResult> ChangeLockedAsync(ChangeSeatsCommand command, CancellationToken ct)
    {
        var owner = await _unitOfWork.Enrollment.FindOwnerAsync(command.EnrollmentId, ct);
        if (owner is null || owner.UserId != command.UserId)
        {
            return ChangeSeatsResult.NotFound();
        }

        var scheduledClass = await _unitOfWork.Class.GetByIdForUpdateAsync(owner.ClassId, ct);
        if (scheduledClass is null)
        {
            throw new InvalidOperationException("Class was not found.");
        }

        var pass = await _unitOfWork.Pass.GetByUserForUpdateAsync(command.UserId, ct);
        if (pass is null)
        {
            throw new InvalidOperationException("Pass was not found.");
        }

        var enrollment = await _unitOfWork.Enrollment.GetByIdForUpdateAsync(command.EnrollmentId, ct);
        if (enrollment is null || enrollment.UserId != command.UserId)
        {
            return ChangeSeatsResult.NotFound();
        }

        if (enrollment.Status == EnrollmentStatus.Cancelled)
        {
            return ChangeSeatsResult.Conflict(EnrollmentConflictConstants.EnrollmentCancelled);
        }

        if (scheduledClass.IsCancelled)
        {
            return ChangeSeatsResult.Conflict(EnrollmentConflictConstants.ClassCancelled);
        }

        var now = TruncateToMicroseconds(_clock.GetUtcNow().UtcDateTime);
        if (now >= scheduledClass.StartsAt)
        {
            return ChangeSeatsResult.Conflict(EnrollmentConflictConstants.ClassStarted);
        }

        var delta = command.Seats - enrollment.Seats;
        if (delta > 0)
        {
            var rejection = await RejectIncreaseAsync(scheduledClass, pass, enrollment, delta, now, ct);
            if (rejection is not null)
            {
                return rejection;
            }
        }

        if (delta != 0)
        {
            enrollment.Seats = command.Seats;
            pass.RemainingVisits -= delta;
            await _unitOfWork.SaveChangesAsync(ct);
        }

        var card = await CardAsync(enrollment, scheduledClass, pass, ct);
        return ChangeSeatsResult.Succeeded(card);
    }

    private async Task<ChangeSeatsResult?> RejectIncreaseAsync(
        Class scheduledClass,
        Pass pass,
        Enrollment enrollment,
        int delta,
        DateTime now,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now);
        if (pass.ValidUntil < today)
        {
            return ChangeSeatsResult.Conflict(EnrollmentConflictConstants.PassExpired);
        }

        if (pass.RemainingVisits < delta)
        {
            return ChangeSeatsResult.Conflict(EnrollmentConflictConstants.PassExhausted);
        }

        var takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(enrollment.ClassId, ct);
        if (takenSeats + delta > scheduledClass.Capacity)
        {
            return ChangeSeatsResult.Conflict(EnrollmentConflictConstants.CapacityExceeded);
        }

        return null;
    }

    private async Task<EnrollmentCard> CardAsync(
        Enrollment enrollment,
        Class scheduledClass,
        Pass pass,
        CancellationToken ct)
    {
        var room = await _unitOfWork.Room.GetByIdAsync(scheduledClass.RoomId, ct);
        if (room is null)
        {
            throw new InvalidOperationException("Room was not found.");
        }

        return new EnrollmentCard
        {
            Id = enrollment.Id,
            Status = EnrollmentStatusMapping.ToContract(enrollment.Status),
            Seats = enrollment.Seats,
            CreatedAt = enrollment.CreatedAt,
            Class = new EnrollmentCardClass
            {
                Id = scheduledClass.Id,
                Title = scheduledClass.Title,
                StartsAt = scheduledClass.StartsAt,
                DurationMinutes = scheduledClass.DurationMinutes,
                IsCancelled = scheduledClass.IsCancelled,
                Room = new EnrollmentCardRoom
                {
                    Id = room.Id,
                    Name = room.Name
                }
            },
            Pass = new EnrollmentCardPass
            {
                RemainingVisits = pass.RemainingVisits,
                ValidUntil = pass.ValidUntil
            }
        };
    }

    private static DateTime TruncateToMicroseconds(DateTime value)
    {
        var ticks = value.Ticks - value.Ticks % 10;
        return new DateTime(ticks, DateTimeKind.Utc);
    }
}
