using BusinessLayer.Extensions;
using BusinessLayer.Features.Enrollments;
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
            result => result.Result is OperationResult<EnrollmentCard>.Ok,
            ct);
        if (attempt.Result is OperationResult<EnrollmentCard>.Ok)
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
                return Failed(rejection);
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
            var promoted = await PromoteAsync(scheduledClass, now, ct);
            promotedUserIds.AddRange(promoted);
            if (promotedUserIds.Count > 0)
            {
                await _unitOfWork.SaveChangesAsync(ct);
            }
        }

        var card = await CardAsync(enrollment.Id, command.UserId, ct);
        var affectedUserIds = new List<long> { command.UserId };
        affectedUserIds.AddRange(promotedUserIds);
        return new ChangeAttempt(new OperationResult<EnrollmentCard>.Ok(card), affectedUserIds);
    }

    private static ChangeAttempt Failed(OperationResult<EnrollmentCard> result)
    {
        return new ChangeAttempt(result, []);
    }

    private sealed record ChangeAttempt(OperationResult<EnrollmentCard> Result, IReadOnlyList<long> AffectedUserIds);

    private async Task<OperationResult<EnrollmentCard>?> RejectIncreaseAsync(
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
            return new OperationResult<EnrollmentCard>.Conflict(ConflictReason.PassExpired);
        }

        if (pass.RemainingVisits < delta)
        {
            return new OperationResult<EnrollmentCard>.Conflict(ConflictReason.PassExhausted);
        }

        var takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(enrollment.ClassId, ct);
        var freeSeats = scheduledClass.Capacity - takenSeats;
        if (freeSeats < delta)
        {
            return new OperationResult<EnrollmentCard>.Conflict(ConflictReason.CapacityExceeded);
        }

        var waitingCount = await _unitOfWork.Waitlist.CountWaitingAsync(enrollment.ClassId, ct);
        if (waitingCount > 0)
        {
            return new OperationResult<EnrollmentCard>.Conflict(ConflictReason.WaitlistNotEmpty);
        }

        return null;
    }

    private async Task<IReadOnlyList<long>> PromoteAsync(Class scheduledClass, DateTime now, CancellationToken ct)
    {
        if (scheduledClass.IsCancelled)
        {
            return [];
        }

        var takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(scheduledClass.Id, ct);
        var freeSeats = scheduledClass.Capacity - takenSeats;
        if (freeSeats <= 0)
        {
            return [];
        }

        var waiting = await _unitOfWork.Waitlist.ListWaitingForUpdateAsync(scheduledClass.Id, ct);
        if (waiting.Count == 0)
        {
            return [];
        }

        var passes = await LockPassesAsync(waiting, freeSeats, ct);
        return await AssignAsync(scheduledClass, waiting, passes, freeSeats, now, ct);
    }

    private async Task<Dictionary<long, Pass>> LockPassesAsync(
        IReadOnlyList<WaitlistEntry> waiting,
        int freeSeats,
        CancellationToken ct)
    {
        var userIds = new List<long>();
        foreach (var entry in waiting)
        {
            if (entry.Seats > freeSeats)
            {
                break;
            }

            userIds.Add(entry.UserId);
        }

        var passes = new Dictionary<long, Pass>();
        foreach (var userId in userIds.Distinct().OrderBy(id => id))
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

    private async Task<List<long>> AssignAsync(
        Class scheduledClass,
        IReadOnlyList<WaitlistEntry> waiting,
        IReadOnlyDictionary<long, Pass> passes,
        int freeSeats,
        DateTime now,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now);
        var remainingSeats = freeSeats;
        var promotedUserIds = new List<long>();
        foreach (var entry in waiting)
        {
            if (entry.Seats > remainingSeats)
            {
                break;
            }

            if (!passes.TryGetValue(entry.UserId, out var pass))
            {
                throw new InvalidOperationException("Pass was not found.");
            }

            if (pass.ValidUntil < today || pass.RemainingVisits < entry.Seats)
            {
                continue;
            }

            pass.RemainingVisits -= entry.Seats;
            var enrollment = await _unitOfWork.Enrollment.CreateAsync(
                new Enrollment
                {
                    UserId = entry.UserId,
                    ClassId = scheduledClass.Id,
                    Seats = entry.Seats,
                    Status = EnrollmentStatus.Active,
                    CreatedAt = now
                },
                ct);
            entry.Status = WaitlistStatus.Promoted;
            entry.PromotedEnrollment = enrollment;
            remainingSeats -= entry.Seats;
            promotedUserIds.Add(entry.UserId);
        }

        return promotedUserIds;
    }

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
