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

    public async Task<OperationResult<JoinedWaitlist>> JoinAsync(JoinWaitlistCommand command, CancellationToken ct)
    {
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(
                token => JoinInTransactionAsync(command, token),
                joined => joined is OperationResult<JoinedWaitlist>.Ok,
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

    private async Task<OperationResult<JoinedWaitlist>> JoinInTransactionAsync(
        JoinWaitlistCommand command,
        CancellationToken ct)
    {
        var stored = await FindAsync(command, ct);
        if (stored is not null)
        {
            return stored;
        }

        var joined = await JoinCoreAsync(command, ct);
        if (joined is OperationResult<JoinedWaitlist>.Ok ok)
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

    private Task<OperationResult<JoinedWaitlist>?> FindAsync(
        JoinWaitlistCommand command,
        CancellationToken ct)
    {
        return _idempotency.FindAsync<JoinedWaitlist>(
            command.UserId,
            IdempotencyOperation.Waitlist,
            command.IdempotencyKey,
            IdempotencyService.CanonicalRequest(command.ClassId, command.Seats),
            ConflictReason.IdempotencyMismatch,
            ct);
    }

    private async Task<OperationResult<JoinedWaitlist>> ReplayAsync(
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

    private async Task<OperationResult<JoinedWaitlist>> JoinCoreAsync(
        JoinWaitlistCommand command,
        CancellationToken ct)
    {
        _unitOfWork.ClearChangeTracker();
        var scheduledClass = await _unitOfWork.Class.GetByIdForUpdateAsync(command.ClassId, ct);
        if (scheduledClass is null)
        {
            return new OperationResult<JoinedWaitlist>.NotFound();
        }

        if (scheduledClass.IsCancelled)
        {
            return new OperationResult<JoinedWaitlist>.Conflict(ConflictReason.ClassCancelled);
        }

        var now = _clock.TruncateToMicroseconds();
        if (now >= scheduledClass.StartsAt)
        {
            return new OperationResult<JoinedWaitlist>.Conflict(ConflictReason.ClassStarted);
        }

        var existing = await _unitOfWork.Waitlist.FindWaitingAsync(command.UserId, command.ClassId, ct);
        if (existing is not null)
        {
            return new OperationResult<JoinedWaitlist>.Conflict(ConflictReason.AlreadyWaiting);
        }

        if (command.Seats > scheduledClass.Capacity)
        {
            return new OperationResult<JoinedWaitlist>.Conflict(ConflictReason.ExceedsCapacity);
        }

        var takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(command.ClassId, ct);
        var freeSeats = scheduledClass.Capacity - takenSeats;
        var waitingCount = await _unitOfWork.Waitlist.CountWaitingAsync(command.ClassId, ct);
        if (waitingCount == 0 && freeSeats >= command.Seats)
        {
            return new OperationResult<JoinedWaitlist>.Conflict(ConflictReason.SeatsAvailable);
        }

        var pass = await _unitOfWork.Pass.GetByUserForUpdateAsync(command.UserId, ct);
        if (pass is null)
        {
            throw new InvalidOperationException("Pass was not found.");
        }

        var today = DateOnly.FromDateTime(now);
        if (pass.ValidUntil < today)
        {
            return new OperationResult<JoinedWaitlist>.Conflict(ConflictReason.PassExpired);
        }

        if (pass.RemainingVisits < command.Seats)
        {
            return new OperationResult<JoinedWaitlist>.Conflict(ConflictReason.PassExhausted);
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

        return new OperationResult<JoinedWaitlist>.Ok(new JoinedWaitlist
        {
            Id = entry.Id,
            ClassId = entry.ClassId,
            Seats = entry.Seats,
            Position = position,
            CreatedAt = entry.CreatedAt
        });
    }
}
