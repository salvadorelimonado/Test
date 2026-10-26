using System.Net;
using System.Text.Json;
using DataAccessLayer.Entities;
using BusinessLayer.Result;
using WebApi.Features.Enrollments;
using WebApi.Features.Enrollments.Waitlist;
using Shouldly;
using WebApi.Tests.Infrastructure;

namespace WebApi.Tests.Enrollments;

[TestClass]
public class LeaveWaitlistTests : ClubTest
{
    private const int SeatHolderNumber = 159;

    [TestMethod]
    public async Task Leave_RemovesTheEntryAndMovesTheQueueForward()
    {
        // Arrange
        var requestedSeats = 1;
        var movedUpPosition = 2;
        var validUntil = ClubTime.Today().AddDays(30);
        var lesson = await Host().Lesson()
            .Capacity(1)
            .StartsAt(DateTime.UtcNow.AddDays(40))
            .Pass(180, requestedSeats, validUntil)
            .TakenBy(SeatHolderNumber, 1)
            .Create();
        await Host().SetPass(181, requestedSeats, validUntil);
        await Host().SetPass(182, requestedSeats, validUntil);
        var firstEntry = await Host().User(180).Join(lesson, requestedSeats);
        var secondJoin = await Host().User(181).JoinWaitlist(lesson, requestedSeats);
        secondJoin.EnsureSuccessStatusCode();

        // Act
        var response = await Host().User(180).LeaveWaitlist(firstEntry.Id);
        var thirdJoin = await Host().User(182).JoinWaitlist(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadBody<LeaveWaitlistResponse>();
        body.Id.ShouldBe(firstEntry.Id);
        body.Status.ShouldBe(WaitlistStatus.Cancelled);
        (await Host().WaitlistStatusOf(firstEntry.Id)).ShouldBe(WaitlistStatus.Cancelled);
        thirdJoin.StatusCode.ShouldBe(HttpStatusCode.Created);
        var thirdBody = await thirdJoin.ReadBody<JoinWaitlistResponse>();
        thirdBody.Position.ShouldBe(movedUpPosition);
        (await Host().WaitingCount(lesson.ClassId)).ShouldBe(movedUpPosition);
    }

    [TestMethod]
    public async Task Leave_WhenTheEntryIsAlreadyCancelled_ReturnsTheSameResult()
    {
        // Arrange
        var lesson = await Host().Lesson()
            .Capacity(1)
            .StartsAt(DateTime.UtcNow.AddDays(41))
            .Pass(183, 3)
            .TakenBy(SeatHolderNumber, 1)
            .Create();
        var entry = await Host().User(183).Join(lesson, 1);
        var user = Host().User(183);
        var first = await user.LeaveWaitlist(entry.Id);
        first.EnsureSuccessStatusCode();
        var before = await Host().Waitlist(entry.Id);

        // Act
        var second = await user.LeaveWaitlist(entry.Id);

        // Assert
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstBody = await first.ReadBody<LeaveWaitlistResponse>();
        var secondBody = await second.ReadBody<LeaveWaitlistResponse>();
        secondBody.Id.ShouldBe(firstBody.Id);
        secondBody.Status.ShouldBe(WaitlistStatus.Cancelled);
        var after = await Host().Waitlist(entry.Id);
        after.Status.ShouldBe(WaitlistStatus.Cancelled);
        after.CreatedAt.ShouldBe(before.CreatedAt);
        (await Host().WaitlistRows(lesson.ClassId, lesson.UserId)).ShouldBe(1);
    }

    [TestMethod]
    public async Task Leave_WhenTheEntryWasSkipped_ReturnsCancelledAndKeepsTheRow()
    {
        // Arrange
        var seats = 1;
        var visits = 2;
        var validUntil = ClubTime.Today().AddDays(30);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(1)
            .StartsAt(DateTime.UtcNow.AddDays(44))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var entryId = await Host().AddWaiting(member.Number, lesson.ClassId, seats, DateTime.UtcNow.AddMinutes(-5));
        await Host().SetWaitlistStatus(entryId, WaitlistStatus.Skipped);
        var user = Host().User(member.Number);

        // Act
        var response = await user.LeaveWaitlist(entryId);
        var repeated = await user.LeaveWaitlist(entryId);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        repeated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadBody<LeaveWaitlistResponse>();
        var repeatedBody = await repeated.ReadBody<LeaveWaitlistResponse>();
        body.Id.ShouldBe(entryId);
        body.Status.ShouldBe(WaitlistStatus.Cancelled);
        repeatedBody.Id.ShouldBe(entryId);
        repeatedBody.Status.ShouldBe(WaitlistStatus.Cancelled);
        (await Host().WaitlistStatusOf(entryId)).ShouldBe(WaitlistStatus.Skipped);
    }

    [TestMethod]
    public async Task Leave_WhenTheEntryIsMissing_ReturnsNotFound()
    {
        // Arrange
        var user = Host().User(184);

        // Act
        var missing = await user.LeaveWaitlist(long.MaxValue);
        var zero = await user.LeaveWaitlist(0);

        // Assert
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        zero.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task Leave_WhenTheEntryBelongsToAnotherUser_ReturnsNotFound()
    {
        // Arrange
        var lesson = await Host().Lesson()
            .Capacity(1)
            .StartsAt(DateTime.UtcNow.AddDays(42))
            .Pass(186, 3)
            .TakenBy(SeatHolderNumber, 1)
            .Create();
        var entry = await Host().User(186).Join(lesson, 1);

        // Act
        var response = await Host().User(185).LeaveWaitlist(entry.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Host().WaitlistStatusOf(entry.Id)).ShouldBe(WaitlistStatus.Waiting);
    }

    [TestMethod]
    public async Task Leave_WhenTheEntryWasPromoted_ReturnsAlreadyPromoted()
    {
        // Arrange
        var lesson = await Host().Lesson()
            .Capacity(1)
            .StartsAt(DateTime.UtcNow.AddDays(43))
            .Pass(187, 3)
            .TakenBy(SeatHolderNumber, 1)
            .Create();
        var entry = await Host().User(187).Join(lesson, 1);
        await Host().SetWaitlistStatus(entry.Id, WaitlistStatus.Promoted);

        // Act
        var response = await Host().User(187).LeaveWaitlist(entry.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.AlreadyPromoted);
        (await Host().WaitlistStatusOf(entry.Id)).ShouldBe(WaitlistStatus.Promoted);
    }

    [TestMethod]
    public async Task Leave_WhenTheEntryWasPromoted_ReturnsTheCreatedEnrollmentId()
    {
        // Arrange
        var seats = 1;
        var visits = 4;
        var validUntil = ClubTime.Today().AddDays(30);
        var lesson = await Host().Lesson()
            .Capacity(1)
            .StartsAt(DateTime.UtcNow.AddDays(5))
            .Pass(195, visits, validUntil)
            .Create();
        await Host().SetPass(194, visits, validUntil);
        var booking = await Host().User(195).Book(lesson, seats);
        var entry = await Host().User(194).Join(lesson, seats);
        var cancel = await Host().User(195).CancelEnrollment(booking.Id);
        cancel.EnsureSuccessStatusCode();

        // Act
        var response = await Host().User(194).LeaveWaitlist(entry.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        document.RootElement.GetProperty("reason").Deserialize<ConflictReason>(HttpReads.Json).ShouldBe(ConflictReason.AlreadyPromoted);
        var promotedEnrollmentId = document.RootElement.GetProperty("promotedEnrollmentId").GetInt64();
        var cardResponse = await Host().User(194).GetEnrollment(promotedEnrollmentId);
        cardResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var card = await cardResponse.ReadBody<EnrollmentCard>();
        card.Id.ShouldBe(promotedEnrollmentId);
        card.Seats.ShouldBe(seats);
        card.Status.ShouldBe(EnrollmentStatus.Active);
        card.Class.Id.ShouldBe(lesson.ClassId);
        (await Host().WaitlistStatusOf(entry.Id)).ShouldBe(WaitlistStatus.Promoted);
    }

    [TestMethod]
    public async Task Leave_WhenTheClassHasStartedOrWasCancelled_StillRemovesTheEntry()
    {
        // Arrange
        var requestedSeats = 1;
        var started = await Host().Lesson()
            .Capacity(4)
            .StartsAt(DateTime.UtcNow.AddMinutes(-10))
            .Pass(188, 3)
            .Create();
        var cancelled = await Host().Lesson()
            .Capacity(4)
            .StartsAt(DateTime.UtcNow.AddDays(44))
            .Cancelled()
            .Pass(189, 3)
            .Create();
        var startedEntryId = await Host().AddWaiting(188, started.ClassId, requestedSeats, DateTime.UtcNow);
        var cancelledEntryId = await Host().AddWaiting(189, cancelled.ClassId, requestedSeats, DateTime.UtcNow);
        // Act
        var startedResponse = await Host().User(188).LeaveWaitlist(startedEntryId);
        var cancelledResponse = await Host().User(189).LeaveWaitlist(cancelledEntryId);

        // Assert
        startedResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        cancelledResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var startedBody = await startedResponse.ReadBody<LeaveWaitlistResponse>();
        var cancelledBody = await cancelledResponse.ReadBody<LeaveWaitlistResponse>();
        startedBody.Status.ShouldBe(WaitlistStatus.Cancelled);
        cancelledBody.Status.ShouldBe(WaitlistStatus.Cancelled);
        (await Host().WaitlistStatusOf(startedEntryId)).ShouldBe(WaitlistStatus.Cancelled);
        (await Host().WaitlistStatusOf(cancelledEntryId)).ShouldBe(WaitlistStatus.Cancelled);
    }

    [TestMethod]
    public async Task Leave_DoesNotReceiveASeatWhenOneFreesLater()
    {
        // Arrange
        var seats = 1;
        var validUntil = ClubTime.Today().AddDays(30);
        var booked = await Host().Lesson()
            .Capacity(1)
            .StartsAt(DateTime.UtcNow.AddDays(4))
            .Pass(192, 4, validUntil)
            .Create();
        await Host().SetPass(191, 4, validUntil);
        var enrollmentId = await Host().AddEnrollment(192, booked.ClassId, seats, EnrollmentStatus.Active);
        var entry = await Host().User(191).Join(booked, seats);
        var left = await Host().User(191).LeaveWaitlist(entry.Id);
        left.EnsureSuccessStatusCode();

        // Act
        var cancel = await Host().User(192).CancelEnrollment(enrollmentId);

        // Assert
        cancel.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Host().ActiveCount(booked.UserId, booked.ClassId)).ShouldBe(0);
        (await Host().ActiveCount(await Host().UserId(191), booked.ClassId)).ShouldBe(0);
        (await Host().WaitlistStatusOf(entry.Id)).ShouldBe(WaitlistStatus.Cancelled);
        (await Host().SumActiveSeats(booked.ClassId)).ShouldBe(0);
    }

    [TestMethod]
    public async Task Leave_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        var response = await Host().Anonymous().LeaveWaitlist(1);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task Leave_WhenTheMinuteLimitIsExceeded_ReturnsTooManyRequests()
    {
        // Arrange
        var user = Host().User(190);

        // Act
        var lastStatus = await user.Hammer(_ => user.LeaveWaitlist(0));

        // Assert
        lastStatus.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [TestMethod]
    public async Task Leave_WhenTheIdIsNotNumeric_ReturnsBadRequest()
    {
        // Arrange
        var user = Host().User(193);

        // Act
        var response = await user.LeaveWaitlist("abc");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
