using BusinessLayer.Features.Enrollments.Cancel;
using BusinessLayer.Result;
using DataAccessLayer.Entities;
using Shouldly;
using WebApi.Tests.Infrastructure;

namespace WebApi.Tests.Enrollments;

[TestClass]
public class EnrollmentCardCacheTests : ClubTest
{
    [TestMethod]
    public async Task Cancel_WhenRedisIsStopped_ReturnsTheCommittedCancellation()
    {
        // Arrange
        var seats = 1;
        var visits = 4;
        var redisPort = 6380;
        var validUntil = ClubTime.Today().AddDays(30);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(4)
            .StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddDays(2)))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Book(lesson, seats);
        var userId = await Host().UserId(member.Number);
        var containerId = await RedisContainer.FindByPublishedPort(redisPort);

        try
        {
            await RedisContainer.Stop(containerId);

            // Act
            var result = await Host().CancelThroughTheService(userId, created.Id);

            // Assert
            result.ShouldBeOfType<OperationResult<CancelledEnrollment>.Ok>();
            var stored = await Host().Enrollment(created.Id, userId);
            stored.Status.ShouldBe(EnrollmentStatus.Cancelled);
            stored.RefundedVisits.ShouldBe(seats);
        }
        finally
        {
            await RedisContainer.Start(containerId);
            await Host().WaitUntilRedisResponds();
        }
    }
}
