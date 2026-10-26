using System.Net;
using Shouldly;
using WebApi.Tests.Infrastructure;

namespace WebApi.Tests.Enrollments;

[TestClass]
public class EnrollmentRaceTests : ClubTest
{
    [TestMethod]
    public async Task Create_WhenTwoBookingsRaceForTheLastSeat_KeepsCapacity()
    {
        // Arrange
        var seats = 1;
        var visits = 4;
        var validUntil = ClubTime.Today().AddDays(25);
        var first = await Host().RegisterMember(visits, validUntil);
        var second = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(seats)
            .StartsAt(DateTime.UtcNow.AddDays(16))
            .Pass(first.Number, visits, validUntil)
            .Create();
        var firstKey = Guid.NewGuid().ToString("N");
        var secondKey = Guid.NewGuid().ToString("N");

        // Act
        var responses = await Task.WhenAll(
            Host().User(first.Number).Enroll(lesson, seats, firstKey),
            Host().User(second.Number).Enroll(lesson, seats, secondKey));

        // Assert
        var created = responses.Single(response => response.StatusCode == HttpStatusCode.Created);
        var rejected = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await rejected.ReadReasonText()).ShouldBe("capacity-exceeded");
        (await Host().SumActiveSeats(lesson.ClassId)).ShouldBe(seats);
        (await Host().RemainingVisits(await Host().UserId(first.Number))).ShouldBeGreaterThanOrEqualTo(0);
        (await Host().RemainingVisits(await Host().UserId(second.Number))).ShouldBeGreaterThanOrEqualTo(0);
    }

    [TestMethod]
    public async Task Create_WhenTwoBookingsRaceForTheLastVisit_DoesNotGoNegative()
    {
        // Arrange
        var seats = 1;
        var visits = 1;
        var validUntil = ClubTime.Today().AddDays(25);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(4)
            .StartsAt(DateTime.UtcNow.AddDays(17))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var firstKey = Guid.NewGuid().ToString("N");
        var secondKey = Guid.NewGuid().ToString("N");

        // Act
        var responses = await Task.WhenAll(
            Host().User(member.Number).Enroll(lesson, seats, firstKey),
            Host().User(member.Number).Enroll(lesson, seats, secondKey));

        // Assert
        responses.Count(response => response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        var rejected = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
        (await rejected.ReadReasonText()).ShouldBe("pass-exhausted");
        (await Host().RemainingVisits(lesson.UserId)).ShouldBe(0);
        (await Host().SumActiveSeats(lesson.ClassId)).ShouldBe(seats);
    }
}
