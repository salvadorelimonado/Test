using BusinessLayer.Extensions;
using BusinessLayer.Features.Enrollments;
using BusinessLayer.Idempotency;
using BusinessLayer.Result;
using DataAccessLayer.Database.Errors;
using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BusinessLayer.Features.Enrollments.Create;

public sealed class CreateEnrollmentHandler
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;
    private readonly IdempotencyService _idempotency;
    private readonly WaitlistPromotionService _waitlistPromotion;
    private readonly EnrollmentCardCache _cardCache;

    public CreateEnrollmentHandler(
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

    public async Task<OperationResult<CreatedEnrollment>> CreateAsync(CreateEnrollmentCommand command, CancellationToken ct)
    {
        BookingAttempt attempt;
        try
        {
            attempt = await _unitOfWork.ExecuteInTransactionAsync(
                token => CreateInTransactionAsync(command, token),
                created => created.Result is OperationResult<CreatedEnrollment>.Ok,
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
            foreach (var userId in attempt.PromotedUserIds.Distinct())
            {
                await _cardCache.InvalidateAsync(userId);
            }
        }

        return attempt.Result;
    }

    private async Task<BookingAttempt> CreateInTransactionAsync(
        CreateEnrollmentCommand command,
        CancellationToken ct)
    {
        await _idempotency.LockAsync(
            command.UserId,
            IdempotencyOperation.Enroll,
            command.IdempotencyKey,
            ct);
        var stored = await FindAsync(command, ct);
        if (stored is not null)
        {
            return new BookingAttempt(stored, []);
        }

        var booked = await BookAsync(command, ct);
        // решение: 404 и 409 под тем же ключом не запоминаем, чтобы запрос можно было повторить.
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

        return new BookingAttempt(booked.Result, booked.PromotedUserIds);
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

        // решение: чужую очередь догоняем до проверки абонемента и мест.
        var promotedUserIds = await _waitlistPromotion.PromoteAsync(
            scheduledClass,
            now,
            command.UserId,
            command.UserId,
            null,
            ct);
        var pass = await _unitOfWork.Pass.GetByUserForUpdateAsync(command.UserId, ct);
        if (pass is null)
        {
            throw new InvalidOperationException("Pass was not found.");
        }

        var takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(command.ClassId, ct);
        var freeSeats = scheduledClass.Capacity - takenSeats;
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

        return new BookedEnrollment(new OperationResult<CreatedEnrollment>.Ok(response), promotedUserIds);
    }

    private sealed record BookingAttempt(
        OperationResult<CreatedEnrollment> Result,
        IReadOnlyList<long> PromotedUserIds);

    private sealed record BookedEnrollment(
        OperationResult<CreatedEnrollment> Result,
        IReadOnlyList<long> PromotedUserIds)
    {
        public static BookedEnrollment Stop(OperationResult<CreatedEnrollment> result)
        {
            return new BookedEnrollment(result, []);
        }
    }

}
