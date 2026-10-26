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
                created => created.Result is OperationResult<CreatedEnrollment>.Ok,
                ct);
        }
        catch (DbUpdateException exception) when (UniqueConstraintViolation.IsRaisedBy(
            exception,
            IdempotencyRecord.KeyIndexName))
        {
            // решение: advisory lock сериализует один и тот же ключ, поэтому эта ветка почти недостижима.
            // Оставлена как страховка, если уникальный индекс всё же сработает.
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

        return new BookingAttempt(booked.Result, booked.Promotion.PromotedUserIds);
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

        // решение: сначала догоняем очередь, потом проверяем абонемент и места уже после неё.
        // Отказ откатывает продвижение: неудавшаяся запись не сдвигает места и посещения.
        var takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(command.ClassId, ct);
        var freeSeats = scheduledClass.Capacity - takenSeats;
        var waiting = await _unitOfWork.Waitlist.ListWaitingForUpdateAsync(command.ClassId, ct);
        var passes = await _unitOfWork.Pass.GetByUsersForUpdateAsync(
            WaitlistFillResult.PassIdsToLock(waiting, command.UserId),
            ct);
        var pass = WaitlistFillResult.Require(passes, command.UserId);
        var promotion = WaitlistFillResult.Fill(scheduledClass, waiting, passes, freeSeats, now);
        if (promotion.Changed)
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }

        takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(command.ClassId, ct);
        freeSeats = scheduledClass.Capacity - takenSeats;
        var today = DateOnly.FromDateTime(now);
        if (pass.ValidUntil < today)
        {
            return new BookedEnrollment(
                new OperationResult<CreatedEnrollment>.Conflict(ConflictReason.PassExpired),
                promotion);
        }

        if (pass.RemainingVisits < command.Seats)
        {
            return new BookedEnrollment(
                new OperationResult<CreatedEnrollment>.Conflict(ConflictReason.PassExhausted),
                promotion);
        }

        if (freeSeats < command.Seats)
        {
            return new BookedEnrollment(
                new OperationResult<CreatedEnrollment>.Conflict(ConflictReason.CapacityExceeded),
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

    private sealed record BookingAttempt(
        OperationResult<CreatedEnrollment> Result,
        IReadOnlyList<long> PromotedUserIds);

    private sealed record BookedEnrollment(OperationResult<CreatedEnrollment> Result, WaitlistFillResult Promotion)
    {
        public static BookedEnrollment Stop(OperationResult<CreatedEnrollment> result)
        {
            return new BookedEnrollment(result, WaitlistFillResult.None);
        }
    }

}
