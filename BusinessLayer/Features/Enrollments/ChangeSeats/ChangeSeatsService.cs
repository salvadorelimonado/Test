using BusinessLayer.Extensions;
using BusinessLayer.Features.Enrollments;
using BusinessLayer.Features.Enrollments.Waitlist;
using BusinessLayer.Result;
using DataAccessLayer.Entities;
using DataAccessLayer.Repositories;
using DataAccessLayer.UnitOfWork;

namespace BusinessLayer.Features.Enrollments.ChangeSeats;

public sealed class ChangeSeatsService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;
    private readonly EnrollmentCardCache _cardCache;

    public ChangeSeatsService(IUnitOfWork unitOfWork, TimeProvider clock, EnrollmentCardCache cardCache)
    {
        _unitOfWork = unitOfWork;
        _clock = clock;
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
            var takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(scheduledClass.Id, ct);
            var freeSeats = scheduledClass.Capacity - takenSeats;
            var waiting = await _unitOfWork.Waitlist.ListWaitingForUpdateAsync(scheduledClass.Id, ct);
            var others = WaitlistFillResult.ExceptOwnRequest(waiting, command.UserId);
            var passes = await _unitOfWork.Pass.GetByUsersForUpdateAsync(
                WaitlistFillResult.PassIdsToLock(others, command.UserId),
                ct);
            var pass = WaitlistFillResult.Require(passes, command.UserId);
            var promotion = WaitlistFillResult.Fill(scheduledClass, others, passes, freeSeats, now);
            if (promotion.Changed)
            {
                await _unitOfWork.SaveChangesAsync(ct);
            }

            takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(scheduledClass.Id, ct);
            freeSeats = scheduledClass.Capacity - takenSeats;
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

            promotedUserIds.AddRange(promotion.PromotedUserIds);
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
        var takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(scheduledClass.Id, ct);
        var freeNow = scheduledClass.Capacity - takenSeats;
        // решение: уменьшение возвращает посещения в любой момент до начала, в том числе менее чем за два часа.
        var freeSeats = freeNow - delta;
        var waiting = await _unitOfWork.Waitlist.ListWaitingForUpdateAsync(scheduledClass.Id, ct);
        var stillWaiting = WaitlistFillResult.CancelOwnRequest(waiting, enrollment.UserId);
        var passes = await _unitOfWork.Pass.GetByUsersForUpdateAsync(
            WaitlistFillResult.PassIdsToLock(stillWaiting, enrollment.UserId),
            ct);
        var pass = WaitlistFillResult.Require(passes, enrollment.UserId);
        enrollment.Seats = seats;
        pass.RemainingVisits -= delta;
        await _unitOfWork.SaveChangesAsync(ct);
        var promotion = WaitlistFillResult.TryFill(scheduledClass, stillWaiting, passes, freeSeats, now);
        if (promotion.Changed)
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }

        return promotion.PromotedUserIds;
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
