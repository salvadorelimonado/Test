using System.Globalization;
using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using BusinessLayer.Features.Enrollments;
using BusinessLayer.Idempotency;

namespace BusinessLayer.Features.Enrollments.Create;

public sealed class CreateEnrollmentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;
    private readonly IdempotencyExecution _idempotency;
    private readonly EnrollmentCardCache _cardCache;

    public CreateEnrollmentService(
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        IdempotencyExecution idempotency,
        EnrollmentCardCache cardCache)
    {
        _unitOfWork = unitOfWork;
        _clock = clock;
        _idempotency = idempotency;
        _cardCache = cardCache;
    }

    public async Task<CreateEnrollmentResult> CreateAsync(CreateEnrollmentCommand command, CancellationToken ct)
    {
        var result = await _idempotency.RunAsync(
            command.UserId,
            IdempotencyOperation.Enroll,
            command.IdempotencyKey,
            CanonicalRequest(command.ClassId, command.Seats),
            CreateEnrollmentResult.Created,
            () => CreateEnrollmentResult.Conflict(EnrollmentConflictConstants.IdempotencyMismatch),
            token => BookAsync(command, token),
            ct);

        if (result.Outcome == CreateEnrollmentOutcome.Created)
        {
            await _cardCache.InvalidateAsync(command.UserId, ct);
        }

        return result;
    }

    private async Task<IdempotencyWork<CreateEnrollmentResponse, CreateEnrollmentResult>> BookAsync(
        CreateEnrollmentCommand command,
        CancellationToken ct)
    {
        var scheduledClass = await _unitOfWork.Class.GetByIdForUpdateAsync(command.ClassId, ct);
        if (scheduledClass is null)
        {
            return Reject(CreateEnrollmentResult.NotFound());
        }

        if (scheduledClass.IsCancelled)
        {
            return Reject(CreateEnrollmentResult.Conflict(EnrollmentConflictConstants.ClassCancelled));
        }

        var now = TruncateToMicroseconds(_clock.GetUtcNow().UtcDateTime);
        if (now >= scheduledClass.StartsAt)
        {
            return Reject(CreateEnrollmentResult.Conflict(EnrollmentConflictConstants.ClassStarted));
        }

        var pass = await _unitOfWork.Pass.GetByUserForUpdateAsync(command.UserId, ct);
        if (pass is null)
        {
            throw new InvalidOperationException("Pass was not found.");
        }

        var today = DateOnly.FromDateTime(now);
        if (pass.ValidUntil < today)
        {
            return Reject(CreateEnrollmentResult.Conflict(EnrollmentConflictConstants.PassExpired));
        }

        if (pass.RemainingVisits < command.Seats)
        {
            return Reject(CreateEnrollmentResult.Conflict(EnrollmentConflictConstants.PassExhausted));
        }

        var takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(command.ClassId, ct);
        var freeSeats = scheduledClass.Capacity - takenSeats;
        if (freeSeats < command.Seats)
        {
            return Reject(CreateEnrollmentResult.Conflict(EnrollmentConflictConstants.CapacityExceeded));
        }

        // решение: пока очередь не пуста, новая бронь не забирает места, которые копятся для её головы.
        var waitingCount = await _unitOfWork.Waitlist.CountWaitingAsync(command.ClassId, ct);
        if (waitingCount > 0)
        {
            return Reject(CreateEnrollmentResult.Conflict(EnrollmentConflictConstants.WaitlistNotEmpty));
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

        var response = new CreateEnrollmentResponse
        {
            Id = enrollment.Id,
            ClassId = command.ClassId,
            Seats = command.Seats,
            Status = EnrollmentStatusMapping.ToContract(EnrollmentStatus.Active),
            CreatedAt = now
        };

        return Done(response);
    }

    private static IdempotencyWork<CreateEnrollmentResponse, CreateEnrollmentResult> Done(CreateEnrollmentResponse response)
    {
        return IdempotencyWork<CreateEnrollmentResponse, CreateEnrollmentResult>.Done(response);
    }

    private static IdempotencyWork<CreateEnrollmentResponse, CreateEnrollmentResult> Reject(CreateEnrollmentResult result)
    {
        return IdempotencyWork<CreateEnrollmentResponse, CreateEnrollmentResult>.Reject(result);
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
