using System.Globalization;
using BusinessLayer.Idempotency;
using BusinessLayer.Result;
using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;

namespace BusinessLayer.Features.Enrollments.Waitlist;

public sealed class JoinWaitlistService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;
    private readonly IdempotencyExecution _idempotency;

    public JoinWaitlistService(IUnitOfWork unitOfWork, TimeProvider clock, IdempotencyExecution idempotency)
    {
        _unitOfWork = unitOfWork;
        _clock = clock;
        _idempotency = idempotency;
    }

    public Task<OperationResult<JoinWaitlistResponse>> JoinAsync(JoinWaitlistCommand command, CancellationToken ct)
    {
        return _idempotency.RunAsync(
            command.UserId,
            IdempotencyOperation.Waitlist,
            command.IdempotencyKey,
            CanonicalRequest(command.ClassId, command.Seats),
            () => new OperationResult<JoinWaitlistResponse>.Conflict(EnrollmentConflictConstants.IdempotencyMismatch),
            token => JoinCoreAsync(command, token),
            ct);
    }

    private async Task<OperationResult<JoinWaitlistResponse>> JoinCoreAsync(
        JoinWaitlistCommand command,
        CancellationToken ct)
    {
        _unitOfWork.ClearChangeTracker();
        var scheduledClass = await _unitOfWork.Class.GetByIdForUpdateAsync(command.ClassId, ct);
        if (scheduledClass is null)
        {
            return new OperationResult<JoinWaitlistResponse>.NotFound();
        }

        if (scheduledClass.IsCancelled)
        {
            return new OperationResult<JoinWaitlistResponse>.Conflict(EnrollmentConflictConstants.ClassCancelled);
        }

        var now = TruncateToMicroseconds(_clock.GetUtcNow().UtcDateTime);
        if (now >= scheduledClass.StartsAt)
        {
            return new OperationResult<JoinWaitlistResponse>.Conflict(EnrollmentConflictConstants.ClassStarted);
        }

        var existing = await _unitOfWork.Waitlist.FindWaitingAsync(command.UserId, command.ClassId, ct);
        if (existing is not null)
        {
            return new OperationResult<JoinWaitlistResponse>.Conflict(EnrollmentConflictConstants.AlreadyWaiting);
        }

        if (command.Seats > scheduledClass.Capacity)
        {
            return new OperationResult<JoinWaitlistResponse>.Conflict(EnrollmentConflictConstants.ExceedsCapacity);
        }

        var takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(command.ClassId, ct);
        var freeSeats = scheduledClass.Capacity - takenSeats;
        var waitingCount = await _unitOfWork.Waitlist.CountWaitingAsync(command.ClassId, ct);
        if (waitingCount == 0 && freeSeats >= command.Seats)
        {
            return new OperationResult<JoinWaitlistResponse>.Conflict(EnrollmentConflictConstants.SeatsAvailable);
        }

        var pass = await _unitOfWork.Pass.GetByUserForUpdateAsync(command.UserId, ct);
        if (pass is null)
        {
            throw new InvalidOperationException("Pass was not found.");
        }

        var today = DateOnly.FromDateTime(now);
        if (pass.ValidUntil < today)
        {
            return new OperationResult<JoinWaitlistResponse>.Conflict(EnrollmentConflictConstants.PassExpired);
        }

        if (pass.RemainingVisits < command.Seats)
        {
            return new OperationResult<JoinWaitlistResponse>.Conflict(EnrollmentConflictConstants.PassExhausted);
        }

        var entry = new WaitlistEntry
        {
            UserId = command.UserId,
            ClassId = command.ClassId,
            Seats = command.Seats,
            Status = WaitlistStatus.Waiting,
            CreatedAt = now
        };
        await _unitOfWork.Waitlist.CreateAsync(entry, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var position = await _unitOfWork.Waitlist.CountWaitingPositionAsync(command.ClassId, entry.CreatedAt, entry.Id, ct);
        if (position < 1)
        {
            throw new InvalidOperationException("Waitlist position was not found.");
        }

        return new OperationResult<JoinWaitlistResponse>.Ok(new JoinWaitlistResponse
        {
            Id = entry.Id,
            ClassId = entry.ClassId,
            Seats = entry.Seats,
            Position = position,
            CreatedAt = entry.CreatedAt
        });
    }

    private static string CanonicalRequest(long classId, int seats)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{classId}:{seats}");
    }

    private static DateTime TruncateToMicroseconds(DateTime value)
    {
        var ticks = value.Ticks - value.Ticks % 10;
        return new DateTime(ticks, DateTimeKind.Utc);
    }
}
