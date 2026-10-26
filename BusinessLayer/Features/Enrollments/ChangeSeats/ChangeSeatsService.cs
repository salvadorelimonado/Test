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
        var promotedUserIds = new List<long>();
        if (delta < 0)
        {
            var promoted = await ReleaseSeatsAsync(scheduledClass, enrollment, command.Seats, delta, now, ct);
            promotedUserIds.AddRange(promoted);
        }
        else if (delta > 0)
        {
            var takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(scheduledClass.Id, ct);
            var freeNow = scheduledClass.Capacity - takenSeats;
            var pass = await LockOwnerAndQueueAsync(scheduledClass, command.UserId, QueueFreeSeats(delta, freeNow), ct);
            var rejection = await RejectIncreaseAsync(scheduledClass, pass, enrollment, delta, now, command.UserId, ct);
            if (rejection is not null)
            {
                return new ChangeAttempt(rejection.Result, rejection.Promotion.PromotedUserIds, rejection.Promotion.Changed);
            }

            enrollment.Seats = command.Seats;
            pass.RemainingVisits -= delta;
            await _unitOfWork.SaveChangesAsync(ct);
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
        var waiting = await _unitOfWork.Waitlist.ListWaitingForUpdateAsync(scheduledClass.Id, ct);

        foreach (var entry in waiting)
        {
            if (entry.UserId == enrollment.UserId)
            {
                entry.Status = WaitlistStatus.Cancelled;
            }
        }

        var stillWaiting = waiting.Where(entry => entry.Status == WaitlistStatus.Waiting).ToList();
        var freeSeats = QueueFreeSeats(delta, freeNow);
        var queueUserIds = freeSeats > 0
            ? WaitlistFillResult.UserIdsWithinFreeSeats(stillWaiting, freeSeats)
            : [];
        var passes = await LockPassesAsync(WaitlistFillResult.InLockOrder(queueUserIds, enrollment.UserId), ct);
        if (!passes.TryGetValue(enrollment.UserId, out var pass))
        {
            throw new InvalidOperationException("Pass was not found.");
        }

        enrollment.Seats = seats;
        pass.RemainingVisits -= delta;
        await _unitOfWork.SaveChangesAsync(ct);
        if (freeSeats <= 0 || stillWaiting.Count == 0)
        {
            return [];
        }

        var promotion = WaitlistFillResult.Fill(scheduledClass, stillWaiting, passes, freeSeats, now);
        if (promotion.Changed)
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }

        return promotion.PromotedUserIds;
    }

    private async Task<IncreaseRejection?> RejectIncreaseAsync(
        Class scheduledClass,
        Pass pass,
        Enrollment enrollment,
        int delta,
        DateTime now,
        long ownerUserId,
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

        var promotion = await PromoteWaitingAsync(scheduledClass, now, ownerUserId, ct);
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

    private static int QueueFreeSeats(int delta, int freeNow)
    {
        if (delta > 0)
        {
            return freeNow >= delta ? freeNow : 0;
        }

        var predicted = (long)freeNow - delta;
        if (predicted <= 0)
        {
            return 0;
        }

        if (predicted > int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)predicted;
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
