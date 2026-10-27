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
    [TestMethod]
    public async Task Leave_RemovesTheEntryAndMovesTheQueueForward()
    {
        // Arrange
        var requestedSeats = 1;
        var heldSeats = 1;
        var movedUpPosition = 2;
        var capacity = 1;
        var leadDays = 40;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var holder = await Host().RegisterMember(heldSeats, validUntil);
        var firstMember = await Host().RegisterMember(requestedSeats, validUntil);
        var secondMember = await Host().RegisterMember(requestedSeats, validUntil);
        var thirdMember = await Host().RegisterMember(requestedSeats, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(firstMember.Number, requestedSeats, validUntil)
            .TakenBy(holder.Number, heldSeats)
            .Create();
        var firstEntry = await firstMember.Join(lesson, requestedSeats);
        var secondJoin = await secondMember.JoinWaitlist(lesson, requestedSeats);
        secondJoin.EnsureSuccessStatusCode();

        // Act
        var response = await firstMember.LeaveWaitlist(firstEntry.Id);
        var thirdJoin = await thirdMember.JoinWaitlist(lesson, requestedSeats);

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
        var requestedSeats = 1;
        var heldSeats = 1;
        var visits = 3;
        var capacity = 1;
        var leadDays = 41;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var holder = await Host().RegisterMember(heldSeats, validUntil);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .TakenBy(holder.Number, heldSeats)
            .Create();
        var entry = await member.Join(lesson, requestedSeats);
        var first = await member.LeaveWaitlist(entry.Id);
        first.EnsureSuccessStatusCode();
        var before = await Host().Waitlist(entry.Id);

        // Act
        var second = await member.LeaveWaitlist(entry.Id);

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
        var capacity = 1;
        var leadDays = 44;
        var queuedAgo = 5;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var entryId = await Host().AddWaiting(member.Number, lesson.ClassId, seats, Host().Now.AddMinutes(-queuedAgo));
        await Host().SetWaitlistStatus(entryId, WaitlistStatus.Skipped);

        // Act
        var response = await member.LeaveWaitlist(entryId);
        var repeated = await member.LeaveWaitlist(entryId);

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
        var visits = 1;
        var passValidityDays = 30;
        var member = await Host().RegisterMember(visits, ClubTime.Today().AddDays(passValidityDays));

        // Act
        var missing = await member.LeaveWaitlist(long.MaxValue);
        var zero = await member.LeaveWaitlist(0);

        // Assert
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        zero.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task Leave_WhenTheEntryBelongsToAnotherUser_ReturnsNotFound()
    {
        // Arrange
        var requestedSeats = 1;
        var heldSeats = 1;
        var visits = 3;
        var capacity = 1;
        var leadDays = 42;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var holder = await Host().RegisterMember(heldSeats, validUntil);
        var owner = await Host().RegisterMember(visits, validUntil);
        var other = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(owner.Number, visits, validUntil)
            .TakenBy(holder.Number, heldSeats)
            .Create();
        var entry = await owner.Join(lesson, requestedSeats);

        // Act
        var response = await other.LeaveWaitlist(entry.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Host().WaitlistStatusOf(entry.Id)).ShouldBe(WaitlistStatus.Waiting);
    }

    [TestMethod]
    public async Task Leave_WhenTheEntryWasPromoted_ReturnsAlreadyPromoted()
    {
        // Arrange
        var requestedSeats = 1;
        var heldSeats = 1;
        var visits = 3;
        var capacity = 1;
        var leadDays = 43;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var holder = await Host().RegisterMember(heldSeats, validUntil);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .TakenBy(holder.Number, heldSeats)
            .Create();
        var entry = await member.Join(lesson, requestedSeats);
        await Host().SetWaitlistStatus(entry.Id, WaitlistStatus.Promoted);

        // Act
        var response = await member.LeaveWaitlist(entry.Id);

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
        var capacity = 1;
        var leadDays = 5;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var owner = await Host().RegisterMember(visits, validUntil);
        var waiter = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(owner.Number, visits, validUntil)
            .Create();
        var booking = await owner.Book(lesson, seats);
        var entry = await waiter.Join(lesson, seats);
        var cancel = await owner.CancelEnrollment(booking.Id);
        cancel.EnsureSuccessStatusCode();

        // Act
        var response = await waiter.LeaveWaitlist(entry.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        document.RootElement.GetProperty("reason").Deserialize<ConflictReason>(HttpReads.Json).ShouldBe(ConflictReason.AlreadyPromoted);
        var promotedEnrollmentId = document.RootElement.GetProperty("promotedEnrollmentId").GetInt64();
        var cardResponse = await waiter.GetEnrollment(promotedEnrollmentId);
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
        var visits = 3;
        var capacity = 4;
        var alreadyStarted = 10;
        var leadDays = 44;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var startedMember = await Host().RegisterMember(visits, validUntil);
        var cancelledMember = await Host().RegisterMember(visits, validUntil);
        var started = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddMinutes(-alreadyStarted))
            .Pass(startedMember.Number, visits, validUntil)
            .Create();
        var cancelled = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Cancelled()
            .Pass(cancelledMember.Number, visits, validUntil)
            .Create();
        var startedEntryId = await Host().AddWaiting(startedMember.Number, started.ClassId, requestedSeats, Host().Now);
        var cancelledEntryId = await Host().AddWaiting(cancelledMember.Number, cancelled.ClassId, requestedSeats, Host().Now);

        // Act
        var startedResponse = await startedMember.LeaveWaitlist(startedEntryId);
        var cancelledResponse = await cancelledMember.LeaveWaitlist(cancelledEntryId);

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
        var visits = 4;
        var capacity = 1;
        var leadDays = 4;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var owner = await Host().RegisterMember(visits, validUntil);
        var waiter = await Host().RegisterMember(visits, validUntil);
        var booked = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(owner.Number, visits, validUntil)
            .Create();
        var enrollmentId = await Host().AddEnrollment(owner.Number, booked.ClassId, seats, EnrollmentStatus.Active);
        var entry = await waiter.Join(booked, seats);
        var left = await waiter.LeaveWaitlist(entry.Id);
        left.EnsureSuccessStatusCode();

        // Act
        var cancel = await owner.CancelEnrollment(enrollmentId);

        // Assert
        cancel.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Host().ActiveCount(booked.UserId, booked.ClassId)).ShouldBe(0);
        (await Host().ActiveCount(await Host().UserId(waiter.Number), booked.ClassId)).ShouldBe(0);
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
        var visits = 1;
        var passValidityDays = 30;
        var member = await Host().RegisterMember(visits, ClubTime.Today().AddDays(passValidityDays));

        // Act
        var lastStatus = await member.Hammer(_ => member.LeaveWaitlist(0));

        // Assert
        lastStatus.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [TestMethod]
    public async Task Leave_WhenTheIdIsNotNumeric_ReturnsBadRequest()
    {
        // Arrange
        var visits = 1;
        var passValidityDays = 30;
        var member = await Host().RegisterMember(visits, ClubTime.Today().AddDays(passValidityDays));

        // Act
        var response = await member.LeaveWaitlist("abc");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
