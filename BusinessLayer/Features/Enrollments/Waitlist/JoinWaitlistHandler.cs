using BusinessLayer.Extensions;
using BusinessLayer.Features.Enrollments;
using BusinessLayer.Idempotency;
using BusinessLayer.Result;
using DataAccessLayer.Database;
using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BusinessLayer.Features.Enrollments.Waitlist;

public sealed class JoinWaitlistHandler
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;
    private readonly IdempotencyService _idempotency;
    private readonly WaitlistPromotionService _waitlistPromotion;
    private readonly EnrollmentCardCache _cardCache;

    public JoinWaitlistHandler(
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        IdempotencyService idempotency,
        WaitlistPromotionService waitlistPromotion,
        EnrollmentCardCache cardCache)
    {
        _unitOfWork = unitOfWork;
        _clock = clock;
        _idempotency = idempotency;
        _waitlistPromotion = waitlistPromotion;
        _cardCache = cardCache;
    }

    public async Task<OperationResult<JoinedWaitlist>> JoinAsync(JoinWaitlistCommand command, CancellationToken ct)
    {
        JoinAttempt attempt;
        try
        {
            attempt = await _unitOfWork.ExecuteInTransactionAsync(
                token => JoinInTransactionAsync(command, token),
                joined => joined.Result is OperationResult<JoinedWaitlist>.Ok,
                ct);
        }
        catch (DbUpdateException exception) when (UniqueConstraintViolation.IsRaisedBy(
            exception,
            IdempotencyRecord.KeyIndexName))
        {
            _unitOfWork.ClearChangeTracker();
            return await ReplayAsync(command, ct);
        }

        if (attempt.Result is OperationResult<JoinedWaitlist>.Ok)
        {
            foreach (var userId in attempt.PromotedUserIds.Distinct())
            {
                await _cardCache.InvalidateAsync(userId);
            }
        }

        return attempt.Result;
    }

    private async Task<JoinAttempt> JoinInTransactionAsync(
        JoinWaitlistCommand command,
        CancellationToken ct)
    {
        await _idempotency.LockAsync(
            command.UserId,
            IdempotencyOperation.Waitlist,
            command.IdempotencyKey,
            ct);
        var stored = await FindAsync(command, ct);
        if (stored is not null)
        {
            return new JoinAttempt(stored, []);
        }

        var joined = await JoinCoreAsync(command, ct);
        // решение: 404 и 409 под тем же ключом не запоминаем, чтобы запрос можно было повторить.
        if (joined.Result is OperationResult<JoinedWaitlist>.Ok ok)
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

    private async Task<JoinAttempt> JoinCoreAsync(
        JoinWaitlistCommand command,
        CancellationToken ct)
    {
        _unitOfWork.ClearChangeTracker();
        var scheduledClass = await _unitOfWork.Class.GetByIdForUpdateAsync(command.ClassId, ct);
        if (scheduledClass is null)
        {
            return JoinAttempt.Stop(new OperationResult<JoinedWaitlist>.NotFound());
        }

        if (scheduledClass.IsCancelled)
        {
            return JoinAttempt.Stop(new OperationResult<JoinedWaitlist>.Conflict(ConflictReason.ClassCancelled));
        }

        var now = _clock.TruncateToMicroseconds();
        if (now >= scheduledClass.StartsAt)
        {
            return JoinAttempt.Stop(new OperationResult<JoinedWaitlist>.Conflict(ConflictReason.ClassStarted));
        }

        var existing = await _unitOfWork.Waitlist.FindWaitingAsync(command.UserId, command.ClassId, ct);
        if (existing is not null)
        {
            return JoinAttempt.Stop(new OperationResult<JoinedWaitlist>.Conflict(ConflictReason.AlreadyWaiting));
        }

        if (command.Seats > scheduledClass.Capacity)
        {
            // решение: своя причина exceeds-capacity, когда запрошено больше вместимости.
            // Мест при этом ещё может не быть — это отдельный отказ от capacity-exceeded.
            return JoinAttempt.Stop(new OperationResult<JoinedWaitlist>.Conflict(ConflictReason.ExceedsCapacity));
        }

        var promotedUserIds = await _waitlistPromotion.PromoteAsync(scheduledClass, now, command.UserId, null, null, ct);
        var takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(command.ClassId, ct);
        var freeSeats = scheduledClass.Capacity - takenSeats;
        if (freeSeats >= command.Seats)
        {
            return JoinAttempt.Stop(new OperationResult<JoinedWaitlist>.Conflict(ConflictReason.SeatsAvailable));
        }

        var pass = await _unitOfWork.Pass.GetByUserForUpdateAsync(command.UserId, ct);
        if (pass is null)
        {
            throw new InvalidOperationException("Pass was not found.");
        }

        var today = DateOnly.FromDateTime(now);
        if (pass.ValidUntil < today)
        {
            return JoinAttempt.Stop(new OperationResult<JoinedWaitlist>.Conflict(ConflictReason.PassExpired));
        }

        if (pass.RemainingVisits < command.Seats)
        {
            return JoinAttempt.Stop(new OperationResult<JoinedWaitlist>.Conflict(ConflictReason.PassExhausted));
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

        return new JoinAttempt(
            new OperationResult<JoinedWaitlist>.Ok(new JoinedWaitlist
            {
                Id = entry.Id,
                ClassId = entry.ClassId,
                Seats = entry.Seats,
                Position = position,
                CreatedAt = entry.CreatedAt
            }),
            promotedUserIds);
    }

    private sealed record JoinAttempt(
        OperationResult<JoinedWaitlist> Result,
        IReadOnlyList<long> PromotedUserIds)
    {
        public static JoinAttempt Stop(OperationResult<JoinedWaitlist> result)
        {
            return new JoinAttempt(result, []);
        }
    }
}
