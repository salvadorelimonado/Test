using BusinessLayer.Extensions;
using BusinessLayer.Features.Enrollments;
using BusinessLayer.Idempotency;
using BusinessLayer.Result;
using DataAccessLayer.Database;
using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BusinessLayer.Features.Enrollments.Waitlist;

public sealed class JoinWaitlistService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;
    private readonly IdempotencyService _idempotency;

    public JoinWaitlistService(IUnitOfWork unitOfWork, TimeProvider clock, IdempotencyService idempotency)
    {
        _unitOfWork = unitOfWork;
        _clock = clock;
        _idempotency = idempotency;
    }

    public async Task<OperationResult<JoinWaitlistResponse>> JoinAsync(JoinWaitlistCommand command, CancellationToken ct)
    {
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(
                token => JoinInTransactionAsync(command, token),
                joined => joined is OperationResult<JoinWaitlistResponse>.Ok,
                ct);
        }
        catch (DbUpdateException exception) when (UniqueConstraintViolation.IsRaisedBy(
            exception,
            IdempotencyRecord.KeyIndexName))
        {
            _unitOfWork.ClearChangeTracker();
            return await ReplayAsync(command, ct);
        }
    }

    private async Task<OperationResult<JoinWaitlistResponse>> JoinInTransactionAsync(
        JoinWaitlistCommand command,
        CancellationToken ct)
    {
        var stored = await FindAsync(command, ct);
        if (stored is not null)
        {
            return stored;
        }

        var joined = await JoinCoreAsync(command, ct);
        if (joined is OperationResult<JoinWaitlistResponse>.Ok ok)
        {
            await _idempotency.SaveAsync(
                command.UserId,
                IdempotencyOperation.Waitlist,
                command.IdempotencyKey,
                IdempotencyService.CanonicalRequest(command.ClassId, command.Seats),
                ok.Value,
                ct);
        }

        return joined;
    }

    private Task<OperationResult<JoinWaitlistResponse>?> FindAsync(
        JoinWaitlistCommand command,
        CancellationToken ct)
    {
        return _idempotency.FindAsync<JoinWaitlistResponse>(
            command.UserId,
            IdempotencyOperation.Waitlist,
            command.IdempotencyKey,
            IdempotencyService.CanonicalRequest(command.ClassId, command.Seats),
            EnrollmentConflictConstants.IdempotencyMismatch,
            ct);
    }

    private async Task<OperationResult<JoinWaitlistResponse>> ReplayAsync(
        JoinWaitlistCommand command,
        CancellationToken ct)
    {
        var stored = await FindAsync(command, ct);
        if (stored is null)
        {
            throw new InvalidOperationException("Idempotency record was not found after a unique constraint violation.");
        }

        return stored;
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

        var now = _clock.TruncateToMicroseconds();
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
}
