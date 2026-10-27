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
        var passValidityDays = 25;
        var leadDays = 16;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var first = await Host().RegisterMember(visits, validUntil);
        var second = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(seats)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(first.Number, visits, validUntil)
            .Create();
        var firstKey = Guid.NewGuid().ToString("N");
        var secondKey = Guid.NewGuid().ToString("N");

        // Act
        var responses = await Task.WhenAll(
            first.Enroll(lesson, seats, firstKey),
            second.Enroll(lesson, seats, secondKey));

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
        var capacity = 4;
        var passValidityDays = 25;
        var leadDays = 17;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var firstKey = Guid.NewGuid().ToString("N");
        var secondKey = Guid.NewGuid().ToString("N");

        // Act
        var responses = await Task.WhenAll(
            member.Enroll(lesson, seats, firstKey),
            member.Enroll(lesson, seats, secondKey));

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
        var passValidityDays = 25;
        var leadDays = 18;
        var hoursUntilNextClass = 1;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var startsAt = ClubTime.WholeSecond(Host().Now.AddDays(leadDays));
        var first = await Host().RegisterMember(visits, validUntil);
        var second = await Host().RegisterMember(visits, validUntil);
        var firstLesson = await Host().Lesson()
            .Capacity(seats)
            .StartsAt(startsAt)
            .Pass(first.Number, visits, validUntil)
            .Create();
        var secondLesson = await Host().Lesson()
            .Capacity(seats)
            .StartsAt(startsAt.AddHours(hoursUntilNextClass))
            .Pass(second.Number, visits, validUntil)
            .Create();
        var firstBooking = await first.Book(firstLesson, seats);
        var secondBooking = await second.Book(secondLesson, seats);
        var firstWaiting = await first.Join(secondLesson, seats);
        var secondWaiting = await second.Join(firstLesson, seats);

        // Act
        var responses = await Task.WhenAll(
            first.CancelEnrollment(firstBooking.Id),
            second.CancelEnrollment(secondBooking.Id));

        // Assert
        responses[0].StatusCode.ShouldBe(HttpStatusCode.OK);
        responses[1].StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Host().WaitlistStatusOf(firstWaiting.Id)).ShouldBe(WaitlistStatus.Promoted);
        (await Host().WaitlistStatusOf(secondWaiting.Id)).ShouldBe(WaitlistStatus.Promoted);
    }
}
