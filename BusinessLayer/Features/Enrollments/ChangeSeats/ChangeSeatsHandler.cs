using BusinessLayer.Extensions;
using BusinessLayer.Features.Enrollments;
using BusinessLayer.Features.Enrollments.Waitlist;
using BusinessLayer.Result;
using DataAccessLayer.Entities;
using DataAccessLayer.Repositories;
using DataAccessLayer.UnitOfWork;

namespace BusinessLayer.Features.Enrollments.ChangeSeats;

public sealed class ChangeSeatsHandler
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;
    private readonly WaitlistPromotionService _waitlistPromotion;
    private readonly EnrollmentCardCache _cardCache;

    public ChangeSeatsHandler(
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        WaitlistPromotionService waitlistPromotion,
        EnrollmentCardCache cardCache)
    {
        _unitOfWork = unitOfWork;
        _clock = clock;
        _waitlistPromotion = waitlistPromotion;
        _cardCache = cardCache;
    }

    public async Task<OperationResult<EnrollmentCardRow>> ChangeAsync(ChangeSeatsCommand command, CancellationToken ct)
    {
        var attempt = await _unitOfWork.ExecuteInTransactionAsync(
            token => ChangeLockedAsync(command, token),
            result => result.Result is OperationResult<EnrollmentCardRow>.Ok,
            ct);
        if (attempt.Result is OperationResult<EnrollmentCardRow>.Ok)
        {
            foreach (var userId in attempt.AffectedUserIds.Distinct())
            {
                await _cardCache.InvalidateAsync(userId);
            }
        }

        return attempt.Result;
    }

    private async Task<ChangeAttempt> ChangeLockedAsync(ChangeSeatsCommand command, CancellationToken ct)
    {
        var owner = await _unitOfWork.Enrollment.FindOwnerAsync(command.EnrollmentId, ct);
        if (owner is null || owner.UserId != command.UserId)
        {
            return Failed(new OperationResult<EnrollmentCardRow>.NotFound());
        }

        var scheduledClass = await _unitOfWork.Class.GetByIdForUpdateAsync(owner.ClassId, ct);
        if (scheduledClass is null)
        {
            throw new InvalidOperationException("Class was not found.");
        }

        var enrollment = await _unitOfWork.Enrollment.GetByIdForUpdateAsync(command.EnrollmentId, ct);
        if (enrollment is null || enrollment.UserId != command.UserId)
        {
            return Failed(new OperationResult<EnrollmentCardRow>.NotFound());
        }

        if (enrollment.Status == EnrollmentStatus.Cancelled)
        {
            return Failed(new OperationResult<EnrollmentCardRow>.Conflict(ConflictReason.EnrollmentCancelled));
        }

        if (scheduledClass.IsCancelled)
        {
            return Failed(new OperationResult<EnrollmentCardRow>.Conflict(ConflictReason.ClassCancelled));
        }

        var now = _clock.TruncateToMicroseconds();
        if (now >= scheduledClass.StartsAt)
        {
            return Failed(new OperationResult<EnrollmentCardRow>.Conflict(ConflictReason.ClassStarted));
        }

        var delta = command.Seats - enrollment.Seats;
        var promotedUserIds = new List<long>();
        if (delta < 0)
        {
            var promoted = await ReleaseSeatsAsync(scheduledClass, enrollment, command.Seats, delta, now, ct);
            promotedUserIds.AddRange(promoted);
        }
        else if (delta > 0)
        {
            // решение: сначала догоняем очередь, потом проверяем абонемент и места уже после неё.
            // Отказ откатывает продвижение: неудавшееся увеличение не сдвигает места и посещения.
            var promoted = await _waitlistPromotion.PromoteAsync(
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

            var takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(scheduledClass.Id, ct);
            var freeSeats = scheduledClass.Capacity - takenSeats;
            var today = DateOnly.FromDateTime(now);
            if (pass.ValidUntil < today)
            {
                return Failed(new OperationResult<EnrollmentCardRow>.Conflict(ConflictReason.PassExpired));
            }

            if (pass.RemainingVisits < delta)
            {
                return Failed(new OperationResult<EnrollmentCardRow>.Conflict(ConflictReason.PassExhausted));
            }

            if (freeSeats < delta)
            {
                return Failed(new OperationResult<EnrollmentCardRow>.Conflict(ConflictReason.CapacityExceeded));
            }

            promotedUserIds.AddRange(promoted);
            enrollment.Seats = command.Seats;
            pass.RemainingVisits -= delta;
            await _unitOfWork.SaveChangesAsync(ct);
        }

        var card = await CardAsync(enrollment.Id, command.UserId, ct);
        var affectedUserIds = new List<long> { command.UserId };
        affectedUserIds.AddRange(promotedUserIds);
        return new ChangeAttempt(new OperationResult<EnrollmentCardRow>.Ok(card), affectedUserIds);
    }

    private static ChangeAttempt Failed(OperationResult<EnrollmentCardRow> result)
    {
        return new ChangeAttempt(result, []);
    }

    private sealed record ChangeAttempt(
        OperationResult<EnrollmentCardRow> Result,
        IReadOnlyList<long> AffectedUserIds);

    private async Task<IReadOnlyList<long>> ReleaseSeatsAsync(
        Class scheduledClass,
        Enrollment enrollment,
        int seats,
        int delta,
        DateTime now,
        CancellationToken ct)
    {
        enrollment.Seats = seats;
        await _unitOfWork.SaveChangesAsync(ct);
        var promotedUserIds = await _waitlistPromotion.PromoteAsync(
            scheduledClass,
            now,
            enrollment.UserId,
            null,
            enrollment.UserId,
            ct);
        var pass = await _unitOfWork.Pass.GetByUserForUpdateAsync(enrollment.UserId, ct);
        if (pass is null)
        {
            throw new InvalidOperationException("Pass was not found.");
        }

        // решение: уменьшение возвращает посещения в любой момент до начала, в том числе менее чем за два часа.
        pass.RemainingVisits -= delta;
        await _unitOfWork.SaveChangesAsync(ct);
        return promotedUserIds;
    }

    private async Task<EnrollmentCardRow> CardAsync(long enrollmentId, long userId, CancellationToken ct)
    {
        var row = await _unitOfWork.Enrollment.FindCardAsync(enrollmentId, userId, ct);
        if (row is null)
        {
            throw new InvalidOperationException("Enrollment was not found.");
        }

        if (row.Pass is null)
        {
            throw new InvalidOperationException("Pass was not found.");
        }

        return row;
    }
}
