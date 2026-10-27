using System.Net;
using DataAccessLayer.Entities;
using Shouldly;
using WebApi.Tests.Infrastructure.DTO;
using WebApi.Tests.Infrastructure;
using WebApi.Tests.Infrastructure.Http;
using WebApi.Tests.Infrastructure.Time;

namespace WebApi.Tests.Features.Enrollments;

[TestClass]
public class EnrollmentRaceTests : HostTest
{
    [TestMethod]
    public async Task Create_WhenTwoBookingsRaceForTheLastSeat_KeepsCapacity()
    {
        // Arrange
        var seats = 1;
        var visits = 4;
        var passValidityDays = 25;
        var leadDays = 16;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var first = await Users.RegisterMember(visits, validUntil);
        var second = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(seats)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(first.Number, visits, validUntil)
            .Create();
        var firstKey = Guid.NewGuid().ToString("N");
        var secondKey = Guid.NewGuid().ToString("N");

        // Act
        var responses = await Task.WhenAll(
            first.Enrollments.Enroll(lesson, seats, firstKey),
            second.Enrollments.Enroll(lesson, seats, secondKey));

        // Assert
        var created = responses.Single(response => response.StatusCode == HttpStatusCode.Created);
        var rejected = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await rejected.ReadReasonText()).ShouldBe("capacity-exceeded");
        (await Enrollments.SumActiveSeats(lesson.ClassId)).ShouldBe(seats);
        (await Passes.RemainingVisits(await Users.UserId(first.Number))).ShouldBeGreaterThanOrEqualTo(0);
        (await Passes.RemainingVisits(await Users.UserId(second.Number))).ShouldBeGreaterThanOrEqualTo(0);
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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var firstKey = Guid.NewGuid().ToString("N");
        var secondKey = Guid.NewGuid().ToString("N");

        // Act
        var responses = await Task.WhenAll(
            member.Enrollments.Enroll(lesson, seats, firstKey),
            member.Enrollments.Enroll(lesson, seats, secondKey));

        // Assert
        responses.Count(response => response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        var rejected = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
        (await rejected.ReadReasonText()).ShouldBe("pass-exhausted");
        (await Passes.RemainingVisits(lesson.UserId)).ShouldBe(0);
        (await Enrollments.SumActiveSeats(lesson.ClassId)).ShouldBe(seats);
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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var startsAt = Session.Now.AddDays(leadDays).WholeSecond();
        var first = await Users.RegisterMember(visits, validUntil);
        var second = await Users.RegisterMember(visits, validUntil);
        var firstLesson = await Classes.Lesson()
            .Capacity(seats)
            .StartsAt(startsAt)
            .Pass(first.Number, visits, validUntil)
            .Create();
        var secondLesson = await Classes.Lesson()
            .Capacity(seats)
            .StartsAt(startsAt.AddHours(hoursUntilNextClass))
            .Pass(second.Number, visits, validUntil)
            .Create();
        var firstBooking = await first.Enrollments.Book(firstLesson, seats);
        var secondBooking = await second.Enrollments.Book(secondLesson, seats);
        var firstWaiting = await first.Waitlist.Join(secondLesson, seats);
        var secondWaiting = await second.Waitlist.Join(firstLesson, seats);

        // Act
        var responses = await Task.WhenAll(
            first.Enrollments.CancelEnrollment(firstBooking.Id),
            second.Enrollments.CancelEnrollment(secondBooking.Id));

        // Assert
        responses[0].StatusCode.ShouldBe(HttpStatusCode.OK);
        responses[1].StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Waitlist.WaitlistStatusOf(firstWaiting.Id)).ShouldBe(WaitlistStatus.Promoted);
        (await Waitlist.WaitlistStatusOf(secondWaiting.Id)).ShouldBe(WaitlistStatus.Promoted);
    }
}
