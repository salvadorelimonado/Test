using BusinessLayer.Extensions;
using BusinessLayer.Features.Enrollments;
using BusinessLayer.Features.Enrollments.Waitlist;
using BusinessLayer.Idempotency;
using BusinessLayer.Result;
using DataAccessLayer.Database;
using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BusinessLayer.Features.Enrollments.Create;

public sealed class CreateEnrollmentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;
    private readonly IdempotencyService _idempotency;
    private readonly EnrollmentCardCache _cardCache;

    public CreateEnrollmentService(
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        IdempotencyService idempotency,
        EnrollmentCardCache cardCache)
    {
        _unitOfWork = unitOfWork;
        _clock = clock;
        _idempotency = idempotency;
        _cardCache = cardCache;
    }

    public async Task<OperationResult<CreatedEnrollment>> CreateAsync(CreateEnrollmentCommand command, CancellationToken ct)
    {
        BookingAttempt attempt;
        try
        {
            attempt = await _unitOfWork.ExecuteInTransactionAsync(
                token => CreateInTransactionAsync(command, token),
                created => created.Result is OperationResult<CreatedEnrollment>.Ok || created.QueueChanged,
                ct);
        }
        catch (DbUpdateException exception) when (UniqueConstraintViolation.IsRaisedBy(
            exception,
            IdempotencyRecord.KeyIndexName))
        {
            _unitOfWork.ClearChangeTracker();
            var replayed = await ReplayAsync(command, ct);
            if (replayed is OperationResult<CreatedEnrollment>.Ok)
            {
                await _cardCache.InvalidateAsync(command.UserId);
            }

            return replayed;
        }

        if (attempt.Result is OperationResult<CreatedEnrollment>.Ok)
        {
            await _cardCache.InvalidateAsync(command.UserId);
        }

        foreach (var userId in attempt.PromotedUserIds.Distinct())
        {
            await _cardCache.InvalidateAsync(userId);
        }

        return attempt.Result;
    }

    private async Task<BookingAttempt> CreateInTransactionAsync(
        CreateEnrollmentCommand command,
        CancellationToken ct)
    {
        var stored = await FindAsync(command, ct);
        if (stored is not null)
        {
            return new BookingAttempt(stored, [], false);
        }

        var booked = await BookAsync(command, ct);
        if (booked.Result is OperationResult<CreatedEnrollment>.Ok ok)
        {
            await _idempotency.SaveAsync(
                command.UserId,
                IdempotencyOperation.Enroll,
                command.IdempotencyKey,
                IdempotencyService.CanonicalRequest(command.ClassId, command.Seats),
                ok.Value,
                ct);
        }

        return new BookingAttempt(booked.Result, booked.Promotion.PromotedUserIds, booked.Promotion.Changed);
    }

    private Task<OperationResult<CreatedEnrollment>?> FindAsync(
        CreateEnrollmentCommand command,
        CancellationToken ct)
    {
        return _idempotency.FindAsync<CreatedEnrollment>(
            command.UserId,
            IdempotencyOperation.Enroll,
            command.IdempotencyKey,
            IdempotencyService.CanonicalRequest(command.ClassId, command.Seats),
            ConflictReason.IdempotencyMismatch,
            ct);
    }

    private async Task<OperationResult<CreatedEnrollment>> ReplayAsync(
        CreateEnrollmentCommand command,
        CancellationToken ct)
    {
        var stored = await FindAsync(command, ct);
        if (stored is null)
        {
            throw new InvalidOperationException("Idempotency record was not found after a unique constraint violation.");
        }

        return stored;
    }

    private async Task<BookedEnrollment> BookAsync(
        CreateEnrollmentCommand command,
        CancellationToken ct)
    {
        _unitOfWork.ClearChangeTracker();
        var scheduledClass = await _unitOfWork.Class.GetByIdForUpdateAsync(command.ClassId, ct);
        if (scheduledClass is null)
        {
            return BookedEnrollment.Stop(new OperationResult<CreatedEnrollment>.NotFound());
        }

        if (scheduledClass.IsCancelled)
        {
            return BookedEnrollment.Stop(new OperationResult<CreatedEnrollment>.Conflict(ConflictReason.ClassCancelled));
        }

        var now = _clock.TruncateToMicroseconds();
        if (now >= scheduledClass.StartsAt)
        {
            return BookedEnrollment.Stop(new OperationResult<CreatedEnrollment>.Conflict(ConflictReason.ClassStarted));
        }

        var takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(command.ClassId, ct);
        var freeSeats = scheduledClass.Capacity - takenSeats;
        var pass = await LockOwnerAndQueueAsync(
            scheduledClass,
            command.UserId,
            freeSeats >= command.Seats ? freeSeats : 0,
            ct);

        var today = DateOnly.FromDateTime(now);
        if (pass.ValidUntil < today)
        {
            return BookedEnrollment.Stop(new OperationResult<CreatedEnrollment>.Conflict(ConflictReason.PassExpired));
        }

        if (pass.RemainingVisits < command.Seats)
        {
            return BookedEnrollment.Stop(new OperationResult<CreatedEnrollment>.Conflict(ConflictReason.PassExhausted));
        }

        if (freeSeats < command.Seats)
        {
            return BookedEnrollment.Stop(new OperationResult<CreatedEnrollment>.Conflict(ConflictReason.CapacityExceeded));
        }

        var promotion = await PromoteWaitingAsync(scheduledClass, now, command.UserId, ct);
        takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(command.ClassId, ct);
        freeSeats = scheduledClass.Capacity - takenSeats;
        if (freeSeats < command.Seats)
        {
            return new BookedEnrollment(
                new OperationResult<CreatedEnrollment>.Conflict(ConflictReason.CapacityExceeded),
                promotion);
        }

        var waitingCount = await _unitOfWork.Waitlist.CountWaitingAsync(command.ClassId, ct);
        if (waitingCount > 0)
        {
            return new BookedEnrollment(
                new OperationResult<CreatedEnrollment>.Conflict(ConflictReason.WaitlistNotEmpty),
                promotion);
        }

        var enrollment = new Enrollment
        {
            UserId = command.UserId,
            ClassId = command.ClassId,
            Seats = command.Seats,
            Status = EnrollmentStatus.Active,
            CreatedAt = now
        };

        await _unitOfWork.Enrollment.CreateAsync(enrollment, ct);
        pass.RemainingVisits -= command.Seats;
        await _unitOfWork.SaveChangesAsync(ct);

        var response = new CreatedEnrollment
        {
            Id = enrollment.Id,
            ClassId = command.ClassId,
            Seats = command.Seats,
            Status = EnrollmentStatus.Active,
            CreatedAt = now
        };

        return new BookedEnrollment(new OperationResult<CreatedEnrollment>.Ok(response), promotion);
    }

    private async Task<WaitlistFillResult> PromoteWaitingAsync(
        Class scheduledClass,
        DateTime now,
        long ownerUserId,
        CancellationToken ct)
    {
        if (scheduledClass.IsCancelled)
        {
            return WaitlistFillResult.None;
        }

        var takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(scheduledClass.Id, ct);
        var freeSeats = scheduledClass.Capacity - takenSeats;
        if (freeSeats <= 0)
        {
            return WaitlistFillResult.None;
        }

        var waiting = await _unitOfWork.Waitlist.ListWaitingForUpdateAsync(scheduledClass.Id, ct);
        if (waiting.Count == 0)
        {
            return WaitlistFillResult.None;
        }

        var passes = await LockPassesAsync(
            WaitlistFillResult.InLockOrder(WaitlistFillResult.UserIdsWithinFreeSeats(waiting, freeSeats), ownerUserId),
            ct);
        var result = WaitlistFillResult.Fill(scheduledClass, waiting, passes, freeSeats, now);
        if (result.Changed)
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }

        return result;
    }

    private async Task<Pass> LockOwnerAndQueueAsync(
        Class scheduledClass,
        long ownerUserId,
        int freeSeats,
        CancellationToken ct)
    {
        IReadOnlyList<long> queueUserIds = [];
        if (freeSeats > 0)
        {
            var waiting = await _unitOfWork.Waitlist.ListWaitingForUpdateAsync(scheduledClass.Id, ct);
            queueUserIds = WaitlistFillResult.UserIdsWithinFreeSeats(waiting, freeSeats);
        }

        var passes = await LockPassesAsync(WaitlistFillResult.InLockOrder(queueUserIds, ownerUserId), ct);
        if (!passes.TryGetValue(ownerUserId, out var pass))
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

    private sealed record BookingAttempt(
        OperationResult<CreatedEnrollment> Result,
        IReadOnlyList<long> PromotedUserIds,
        bool QueueChanged);

    private sealed record BookedEnrollment(OperationResult<CreatedEnrollment> Result, WaitlistFillResult Promotion)
    {
        public static BookedEnrollment Stop(OperationResult<CreatedEnrollment> result)
        {
            return new BookedEnrollment(result, WaitlistFillResult.None);
        }
    }

}
