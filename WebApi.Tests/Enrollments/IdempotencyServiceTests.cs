using BusinessLayer.Features.Enrollments.Create;
using BusinessLayer.Idempotency;
using BusinessLayer.Result;
using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using WebApi.Tests.Infrastructure;

namespace WebApi.Tests.Enrollments;

[TestClass]
public class IdempotencyServiceTests : ClubTest
{
    [TestMethod]
    public async Task Lock_WithoutATransaction_Throws()
    {
        // Arrange
        var userId = 1L;
        var key = "lock-without-transaction";
        // Act
        await Host().InScope(async provider =>
        {
            var idempotency = provider.GetRequiredService<IdempotencyService>();
            var act = () => idempotency.LockAsync(userId, IdempotencyOperation.Enroll, key, CancellationToken.None);

            // Assert
            await Should.ThrowAsync<InvalidOperationException>(act);
        });
    }

    [TestMethod]
    public async Task Find_ReturnsTheStoredResponseAndRejectsADifferentBody()
    {
        // Arrange
        var visits = 2;
        var passValidityDays = 20;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var classId = 80L;
        var seats = 1;
        var otherSeats = 2;
        var enrollmentId = 90L;
        var key = "stored-enrollment";
        var createdAt = new DateTime(2032, 7, 1, 12, 0, 0, DateTimeKind.Utc);
        var member = await Host().RegisterMember(visits, validUntil);
        var userId = await Host().UserId(member.Number);
        var operation = IdempotencyOperation.Enroll;
        var canonical = IdempotencyService.CanonicalRequest(classId, seats);
        var otherCanonical = IdempotencyService.CanonicalRequest(classId, otherSeats);
        var storedEnrollment = new CreatedEnrollment
        {
            Id = enrollmentId,
            ClassId = classId,
            Seats = seats,
            Status = EnrollmentStatus.Active,
            CreatedAt = createdAt
        };

        OperationResult<CreatedEnrollment>? sameBody = null;
        OperationResult<CreatedEnrollment>? differentBody = null;

        // Act
        await Host().InScope(async provider =>
        {
            var unitOfWork = provider.GetRequiredService<IUnitOfWork>();
            var idempotency = provider.GetRequiredService<IdempotencyService>();
            await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    await idempotency.LockAsync(userId, operation, key, token);
                    await idempotency.SaveAsync(userId, operation, key, canonical, storedEnrollment, token);
                    sameBody = await idempotency.FindAsync<CreatedEnrollment>(userId, operation, key, canonical, token);
                    differentBody = await idempotency.FindAsync<CreatedEnrollment>(
                        userId,
                        operation,
                        key,
                        otherCanonical,
                        token);
                    return true;
                },
                shouldCommit => shouldCommit,
                CancellationToken.None);
        });

        // Assert
        if (sameBody is null || differentBody is null)
        {
            throw new InvalidOperationException("Idempotency lookup did not return a result.");
        }

        var found = sameBody.ShouldBeOfType<OperationResult<CreatedEnrollment>.Ok>();
        found.Value.Id.ShouldBe(enrollmentId);
        found.Value.Seats.ShouldBe(seats);
        found.Value.Status.ShouldBe(EnrollmentStatus.Active);
        var mismatch = differentBody.ShouldBeOfType<OperationResult<CreatedEnrollment>.Conflict>();
        mismatch.Reason.ShouldBe(ConflictReason.IdempotencyMismatch);
    }
}
