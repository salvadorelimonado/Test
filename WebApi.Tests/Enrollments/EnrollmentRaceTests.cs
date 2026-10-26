using System.Net;
using DataAccessLayer.Entities;
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

    [TestMethod]
    public async Task Cancel_WhenTwoClassesPromoteEachOthersPass_BothSucceed()
    {
        // Arrange
        var seats = 1;
        var visits = 2;
        var validUntil = ClubTime.Today().AddDays(25);
        var startsAt = ClubTime.WholeSecond(DateTime.UtcNow.AddDays(18));
        var first = await Host().RegisterMember(visits, validUntil);
        var second = await Host().RegisterMember(visits, validUntil);
        var firstLesson = await Host().Lesson()
            .Capacity(seats)
            .StartsAt(startsAt)
            .Pass(first.Number, visits, validUntil)
            .Create();
        var secondLesson = await Host().Lesson()
            .Capacity(seats)
            .StartsAt(startsAt.AddHours(1))
            .Pass(second.Number, visits, validUntil)
            .Create();
        var firstBooking = await Host().User(first.Number).Book(firstLesson, seats);
        var secondBooking = await Host().User(second.Number).Book(secondLesson, seats);
        var firstWaiting = await Host().User(first.Number).Join(secondLesson, seats);
        var secondWaiting = await Host().User(second.Number).Join(firstLesson, seats);

        // Act
        var responses = await Task.WhenAll(
            Host().User(first.Number).CancelEnrollment(firstBooking.Id),
            Host().User(second.Number).CancelEnrollment(secondBooking.Id));

        // Assert
        responses[0].StatusCode.ShouldBe(HttpStatusCode.OK);
        responses[1].StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Host().WaitlistStatusOf(firstWaiting.Id)).ShouldBe(WaitlistStatus.Promoted);
        (await Host().WaitlistStatusOf(secondWaiting.Id)).ShouldBe(WaitlistStatus.Promoted);
    }
}
