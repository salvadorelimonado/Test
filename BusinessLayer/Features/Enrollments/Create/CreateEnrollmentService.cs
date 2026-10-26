using System.Globalization;
using BusinessLayer.Features.Enrollments;
using BusinessLayer.Idempotency;
using BusinessLayer.Result;
using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;

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

    public async Task<OperationResult<CreateEnrollmentResponse>> CreateAsync(CreateEnrollmentCommand command, CancellationToken ct)
    {
        var result = await _idempotency.RunAsync(
            command.UserId,
            IdempotencyOperation.Enroll,
            command.IdempotencyKey,
            CanonicalRequest(command.ClassId, command.Seats),
            () => new OperationResult<CreateEnrollmentResponse>.Conflict(EnrollmentConflictConstants.IdempotencyMismatch),
            token => BookAsync(command, token),
            ct);

        if (result is OperationResult<CreateEnrollmentResponse>.Ok)
        {
            await _cardCache.InvalidateAsync(command.UserId, ct);
        }

        return result;
    }

    private async Task<OperationResult<CreateEnrollmentResponse>> BookAsync(
        CreateEnrollmentCommand command,
        CancellationToken ct)
    {
        _unitOfWork.ClearChangeTracker();
        var scheduledClass = await _unitOfWork.Class.GetByIdForUpdateAsync(command.ClassId, ct);
        if (scheduledClass is null)
        {
            return new OperationResult<CreateEnrollmentResponse>.NotFound();
        }

        if (scheduledClass.IsCancelled)
        {
            return new OperationResult<CreateEnrollmentResponse>.Conflict(EnrollmentConflictConstants.ClassCancelled);
        }

        var now = TruncateToMicroseconds(_clock.GetUtcNow().UtcDateTime);
        if (now >= scheduledClass.StartsAt)
        {
            return new OperationResult<CreateEnrollmentResponse>.Conflict(EnrollmentConflictConstants.ClassStarted);
        }

        var pass = await _unitOfWork.Pass.GetByUserForUpdateAsync(command.UserId, ct);
        if (pass is null)
        {
            throw new InvalidOperationException("Pass was not found.");
        }

        var today = DateOnly.FromDateTime(now);
        if (pass.ValidUntil < today)
        {
            return new OperationResult<CreateEnrollmentResponse>.Conflict(EnrollmentConflictConstants.PassExpired);
        }

        if (pass.RemainingVisits < command.Seats)
        {
            return new OperationResult<CreateEnrollmentResponse>.Conflict(EnrollmentConflictConstants.PassExhausted);
        }

        var takenSeats = await _unitOfWork.Enrollment.SumActiveSeatsAsync(command.ClassId, ct);
        var freeSeats = scheduledClass.Capacity - takenSeats;
        if (freeSeats < command.Seats)
        {
            return new OperationResult<CreateEnrollmentResponse>.Conflict(EnrollmentConflictConstants.CapacityExceeded);
        }

        var waitingCount = await _unitOfWork.Waitlist.CountWaitingAsync(command.ClassId, ct);
        if (waitingCount > 0)
        {
            return new OperationResult<CreateEnrollmentResponse>.Conflict(EnrollmentConflictConstants.WaitlistNotEmpty);
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

        return new OperationResult<CreateEnrollmentResponse>.Ok(response);
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
