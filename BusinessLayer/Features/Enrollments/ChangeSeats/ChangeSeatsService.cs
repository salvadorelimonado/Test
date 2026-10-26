using BusinessLayer.Extensions;
using BusinessLayer.Features.Enrollments;
using BusinessLayer.Features.Enrollments.Waitlist;
using BusinessLayer.Result;
using DataAccessLayer.Entities;
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

    public async Task<OperationResult<EnrollmentCard>> ChangeAsync(ChangeSeatsCommand command, CancellationToken ct)
    {
        var attempt = await _unitOfWork.ExecuteInTransactionAsync(
            token => ChangeLockedAsync(command, token),
            result => result.Result is OperationResult<EnrollmentCard>.Ok || result.QueueChanged,
            ct);
        if (attempt.Result is OperationResult<EnrollmentCard>.Ok || attempt.QueueChanged)
        {
            foreach (var userId in attempt.AffectedUserIds.Distinct())
            {
                await _cardCache.InvalidateAsync(userId, ct);
            }
        }

        return attempt.Result;
    }

    private async Task<ChangeAttempt> ChangeLockedAsync(ChangeSeatsCommand command, CancellationToken ct)
    {
        var owner = await _unitOfWork.Enrollment.FindOwnerAsync(command.EnrollmentId, ct);
        if (owner is null || owner.UserId != command.UserId)
        {
            return Failed(new OperationResult<EnrollmentCard>.NotFound());
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
            return Failed(new OperationResult<EnrollmentCard>.NotFound());
        }

        if (enrollment.Status == EnrollmentStatus.Cancelled)
        {
            return Failed(new OperationResult<EnrollmentCard>.Conflict(ConflictReason.EnrollmentCancelled));
        }

        if (scheduledClass.IsCancelled)
        {
            return Failed(new OperationResult<EnrollmentCard>.Conflict(ConflictReason.ClassCancelled));
        }

        var now = _clock.TruncateToMicroseconds();
        if (now >= scheduledClass.StartsAt)
        {
            return Failed(new OperationResult<EnrollmentCard>.Conflict(ConflictReason.ClassStarted));
        }

        var delta = command.Seats - enrollment.Seats;
        if (delta > 0)
        {
            var rejection = await RejectIncreaseAsync(scheduledClass, pass, enrollment, delta, now, ct);
            if (rejection is not null)
            {
                return new ChangeAttempt(rejection.Result, rejection.Promotion.PromotedUserIds, rejection.Promotion.Changed);
            }
        }

        var promotedUserIds = new List<long>();
        if (delta != 0)
        {
            enrollment.Seats = command.Seats;
            pass.RemainingVisits -= delta;
            await _unitOfWork.SaveChangesAsync(ct);
        }

        if (delta < 0)
        {
            var promotion = await PromoteWaitingAsync(scheduledClass, now, ct);
            promotedUserIds.AddRange(promotion.PromotedUserIds);
        }

        var card = await CardAsync(enrollment.Id, command.UserId, ct);
        var affectedUserIds = new List<long> { command.UserId };
        affectedUserIds.AddRange(promotedUserIds);
        return new ChangeAttempt(new OperationResult<EnrollmentCard>.Ok(card), affectedUserIds, false);
    }

    private static ChangeAttempt Failed(OperationResult<EnrollmentCard> result)
    {
        return new ChangeAttempt(result, [], false);
    }

    private sealed record ChangeAttempt(
        OperationResult<EnrollmentCard> Result,
        IReadOnlyList<long> AffectedUserIds,
        bool QueueChanged);

    private async Task<IncreaseRejection?> RejectIncreaseAsync(
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
            return new IncreaseRejection(
                new OperationResult<EnrollmentCard>.Conflict(ConflictReason.PassExpired),
                WaitlistFillResult.None);
        }

        if (pass.RemainingVisits < delta)
        {
            return new IncreaseRejection(
                new OperationResult<EnrollmentCard>.Conflict(ConflictReason.PassExhausted),
                WaitlistFillResult.None);
        }

        var takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(enrollment.ClassId, ct);
        var freeSeats = scheduledClass.Capacity - takenSeats;
        if (freeSeats < delta)
        {
            return new IncreaseRejection(
                new OperationResult<EnrollmentCard>.Conflict(ConflictReason.CapacityExceeded),
                WaitlistFillResult.None);
        }

        var promotion = await PromoteWaitingAsync(scheduledClass, now, ct);
        takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(enrollment.ClassId, ct);
        freeSeats = scheduledClass.Capacity - takenSeats;
        if (freeSeats < delta)
        {
            return new IncreaseRejection(
                new OperationResult<EnrollmentCard>.Conflict(ConflictReason.CapacityExceeded),
                promotion);
        }

        var waitingCount = await _unitOfWork.Waitlist.CountWaitingAsync(enrollment.ClassId, ct);
        if (waitingCount > 0)
        {
            return new IncreaseRejection(
                new OperationResult<EnrollmentCard>.Conflict(ConflictReason.WaitlistNotEmpty),
                promotion);
        }

        return null;
    }

    private async Task<WaitlistFillResult> PromoteWaitingAsync(Class scheduledClass, DateTime now, CancellationToken ct)
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

        var passes = new Dictionary<long, Pass>();
        foreach (var userId in WaitlistFillResult.UserIdsWithinFreeSeats(waiting, freeSeats).Distinct().OrderBy(id => id))
        {
            var pass = await _unitOfWork.Pass.GetByUserForUpdateAsync(userId, ct);
            if (pass is null)
            {
                throw new InvalidOperationException("Pass was not found.");
            }

            passes.Add(userId, pass);
        }

        var result = WaitlistFillResult.Fill(scheduledClass, waiting, passes, freeSeats, now);
        if (result.Changed)
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }

        return result;
    }

    private sealed record IncreaseRejection(
        OperationResult<EnrollmentCard> Result,
        WaitlistFillResult Promotion);

    private async Task<EnrollmentCard> CardAsync(long enrollmentId, long userId, CancellationToken ct)
    {
        var row = await _unitOfWork.Enrollment.FindCardAsync(enrollmentId, userId, ct);
        if (row is null)
        {
            throw new InvalidOperationException("Enrollment was not found.");
        }

        return EnrollmentCard.Create(row);
    }
}
