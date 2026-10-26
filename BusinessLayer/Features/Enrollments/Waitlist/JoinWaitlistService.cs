using System.Globalization;
using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using BusinessLayer.Idempotency;

namespace BusinessLayer.Features.Enrollments.Waitlist;

public sealed class JoinWaitlistService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;
    private readonly IdempotencyExecution _idempotency;

    public JoinWaitlistService(IUnitOfWork unitOfWork, TimeProvider clock, IdempotencyExecution idempotency)
    {
        _unitOfWork = unitOfWork;
        _clock = clock;
        _idempotency = idempotency;
    }

    public Task<JoinWaitlistResult> JoinAsync(JoinWaitlistCommand command, CancellationToken ct)
    {
        return _idempotency.RunAsync(
            command.UserId,
            IdempotencyOperation.Waitlist,
            command.IdempotencyKey,
            CanonicalRequest(command.ClassId, command.Seats),
            JoinWaitlistResult.Created,
            () => JoinWaitlistResult.Conflict(EnrollmentConflictConstants.IdempotencyMismatch),
            token => JoinCoreAsync(command, token),
            ct);
    }

    private async Task<IdempotencyWork<JoinWaitlistResponse, JoinWaitlistResult>> JoinCoreAsync(
        JoinWaitlistCommand command,
        CancellationToken ct)
    {
        _unitOfWork.ClearChangeTracker();
        var scheduledClass = await _unitOfWork.Class.GetByIdForUpdateAsync(command.ClassId, ct);
        if (scheduledClass is null)
        {
            return Reject(JoinWaitlistResult.NotFound());
        }

        if (scheduledClass.IsCancelled)
        {
            return Reject(JoinWaitlistResult.Conflict(EnrollmentConflictConstants.ClassCancelled));
        }

        var now = TruncateToMicroseconds(_clock.GetUtcNow().UtcDateTime);
        if (now >= scheduledClass.StartsAt)
        {
            return Reject(JoinWaitlistResult.Conflict(EnrollmentConflictConstants.ClassStarted));
        }

        var existing = await _unitOfWork.Waitlist.FindWaitingAsync(command.UserId, command.ClassId, ct);
        if (existing is not null)
        {
            return Reject(JoinWaitlistResult.Conflict(EnrollmentConflictConstants.AlreadyWaiting));
        }

        var takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(command.ClassId, ct);
        if (takenSeats + command.Seats <= scheduledClass.Capacity)
        {
            return Reject(JoinWaitlistResult.Conflict(EnrollmentConflictConstants.SeatsAvailable));
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

        return Done(new JoinWaitlistResponse
        {
            Id = entry.Id,
            ClassId = entry.ClassId,
            Seats = entry.Seats,
            Position = position,
            CreatedAt = entry.CreatedAt
        });
    }

    private static IdempotencyWork<JoinWaitlistResponse, JoinWaitlistResult> Done(JoinWaitlistResponse response)
    {
        return IdempotencyWork<JoinWaitlistResponse, JoinWaitlistResult>.Done(response);
    }

    private static IdempotencyWork<JoinWaitlistResponse, JoinWaitlistResult> Reject(JoinWaitlistResult result)
    {
        return IdempotencyWork<JoinWaitlistResponse, JoinWaitlistResult>.Reject(result);
    }

    private static string CanonicalRequest(long classId, int seats)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{classId}:{seats}");
    }

    private static DateTime TruncateToMicroseconds(DateTime value)
    {
        var ticks = value.Ticks - value.Ticks % 10;
        return new DateTime(ticks, DateTimeKind.Utc);
    }
}
