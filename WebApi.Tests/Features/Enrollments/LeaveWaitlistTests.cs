using System.Net;
using System.Text.Json;
using DataAccessLayer.Entities;
using BusinessLayer.Result;
using WebApi.Features.Enrollments;
using WebApi.Features.Enrollments.Waitlist;
using Shouldly;
using WebApi.Tests.Infrastructure.DTO;
using WebApi.Tests.Infrastructure;
using WebApi.Tests.Infrastructure.Http;
using WebApi.Tests.Infrastructure.Http.Clients;
using WebApi.Tests.Infrastructure.Time;

namespace WebApi.Tests.Features.Enrollments;

[TestClass]
public class LeaveWaitlistTests : HostTest
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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var holder = await Users.RegisterMember(heldSeats, validUntil);
        var firstMember = await Users.RegisterMember(requestedSeats, validUntil);
        var secondMember = await Users.RegisterMember(requestedSeats, validUntil);
        var thirdMember = await Users.RegisterMember(requestedSeats, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(firstMember.Number, requestedSeats, validUntil)
            .TakenBy(holder.Number, heldSeats)
            .Create();
        var firstEntry = await firstMember.Waitlist.Join(lesson, requestedSeats);
        var secondJoin = await secondMember.Waitlist.JoinWaitlist(lesson, requestedSeats);
        secondJoin.EnsureSuccessStatusCode();

        // Act
        var response = await firstMember.Waitlist.LeaveWaitlist(firstEntry.Id);
        var thirdJoin = await thirdMember.Waitlist.JoinWaitlist(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadBody<LeaveWaitlistResponse>();
        body.Id.ShouldBe(firstEntry.Id);
        body.Status.ShouldBe(WaitlistStatus.Cancelled);
        (await Waitlist.WaitlistStatusOf(firstEntry.Id)).ShouldBe(WaitlistStatus.Cancelled);
        thirdJoin.StatusCode.ShouldBe(HttpStatusCode.Created);
        var thirdBody = await thirdJoin.ReadBody<JoinWaitlistResponse>();
        thirdBody.Position.ShouldBe(movedUpPosition);
        (await Waitlist.WaitingCount(lesson.ClassId)).ShouldBe(movedUpPosition);
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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var holder = await Users.RegisterMember(heldSeats, validUntil);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .TakenBy(holder.Number, heldSeats)
            .Create();
        var entry = await member.Waitlist.Join(lesson, requestedSeats);
        var first = await member.Waitlist.LeaveWaitlist(entry.Id);
        first.EnsureSuccessStatusCode();
        var before = await Waitlist.Entry(entry.Id);

        // Act
        var second = await member.Waitlist.LeaveWaitlist(entry.Id);

        // Assert
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstBody = await first.ReadBody<LeaveWaitlistResponse>();
        var secondBody = await second.ReadBody<LeaveWaitlistResponse>();
        secondBody.Id.ShouldBe(firstBody.Id);
        secondBody.Status.ShouldBe(WaitlistStatus.Cancelled);
        var after = await Waitlist.Entry(entry.Id);
        after.Status.ShouldBe(WaitlistStatus.Cancelled);
        after.CreatedAt.ShouldBe(before.CreatedAt);
        (await Waitlist.WaitlistRows(lesson.ClassId, lesson.UserId)).ShouldBe(1);
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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var entryId = await Waitlist.AddWaiting(member.Number, lesson.ClassId, seats, Session.Now.AddMinutes(-queuedAgo));
        await Waitlist.SetWaitlistStatus(entryId, WaitlistStatus.Skipped);

        // Act
        var response = await member.Waitlist.LeaveWaitlist(entryId);
        var repeated = await member.Waitlist.LeaveWaitlist(entryId);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        repeated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadBody<LeaveWaitlistResponse>();
        var repeatedBody = await repeated.ReadBody<LeaveWaitlistResponse>();
        body.Id.ShouldBe(entryId);
        body.Status.ShouldBe(WaitlistStatus.Cancelled);
        repeatedBody.Id.ShouldBe(entryId);
        repeatedBody.Status.ShouldBe(WaitlistStatus.Cancelled);
        (await Waitlist.WaitlistStatusOf(entryId)).ShouldBe(WaitlistStatus.Skipped);
    }

    [TestMethod]
    public async Task Leave_WhenTheEntryIsMissing_ReturnsNotFound()
    {
        // Arrange
        var visits = 1;
        var passValidityDays = 30;
        var member = await Users.RegisterMember(visits, Session.Today.AddDays(passValidityDays));

        // Act
        var missing = await member.Waitlist.LeaveWaitlist(long.MaxValue);
        var zero = await member.Waitlist.LeaveWaitlist(0);

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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var holder = await Users.RegisterMember(heldSeats, validUntil);
        var owner = await Users.RegisterMember(visits, validUntil);
        var other = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(owner.Number, visits, validUntil)
            .TakenBy(holder.Number, heldSeats)
            .Create();
        var entry = await owner.Waitlist.Join(lesson, requestedSeats);

        // Act
        var response = await other.Waitlist.LeaveWaitlist(entry.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Waitlist.WaitlistStatusOf(entry.Id)).ShouldBe(WaitlistStatus.Waiting);
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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var holder = await Users.RegisterMember(heldSeats, validUntil);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .TakenBy(holder.Number, heldSeats)
            .Create();
        var entry = await member.Waitlist.Join(lesson, requestedSeats);
        await Waitlist.SetWaitlistStatus(entry.Id, WaitlistStatus.Promoted);

        // Act
        var response = await member.Waitlist.LeaveWaitlist(entry.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.AlreadyPromoted);
        (await Waitlist.WaitlistStatusOf(entry.Id)).ShouldBe(WaitlistStatus.Promoted);
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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var owner = await Users.RegisterMember(visits, validUntil);
        var waiter = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(owner.Number, visits, validUntil)
            .Create();
        var booking = await owner.Enrollments.Book(lesson, seats);
        var entry = await waiter.Waitlist.Join(lesson, seats);
        var cancel = await owner.Enrollments.CancelEnrollment(booking.Id);
        cancel.EnsureSuccessStatusCode();

        // Act
        var response = await waiter.Waitlist.LeaveWaitlist(entry.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        document.RootElement.GetProperty("reason").Deserialize<ConflictReason>(HttpResponseMessageExtensions.Json).ShouldBe(ConflictReason.AlreadyPromoted);
        var promotedEnrollmentId = document.RootElement.GetProperty("promotedEnrollmentId").GetInt64();
        var cardResponse = await waiter.Enrollments.GetEnrollment(promotedEnrollmentId);
        cardResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var card = await cardResponse.ReadBody<EnrollmentCard>();
        card.Id.ShouldBe(promotedEnrollmentId);
        card.Seats.ShouldBe(seats);
        card.Status.ShouldBe(EnrollmentStatus.Active);
        card.Class.Id.ShouldBe(lesson.ClassId);
        (await Waitlist.WaitlistStatusOf(entry.Id)).ShouldBe(WaitlistStatus.Promoted);
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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var startedMember = await Users.RegisterMember(visits, validUntil);
        var cancelledMember = await Users.RegisterMember(visits, validUntil);
        var started = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddMinutes(-alreadyStarted))
            .Pass(startedMember.Number, visits, validUntil)
            .Create();
        var cancelled = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Cancelled()
            .Pass(cancelledMember.Number, visits, validUntil)
            .Create();
        var startedEntryId = await Waitlist.AddWaiting(startedMember.Number, started.ClassId, requestedSeats, Session.Now);
        var cancelledEntryId = await Waitlist.AddWaiting(cancelledMember.Number, cancelled.ClassId, requestedSeats, Session.Now);

        // Act
        var startedResponse = await startedMember.Waitlist.LeaveWaitlist(startedEntryId);
        var cancelledResponse = await cancelledMember.Waitlist.LeaveWaitlist(cancelledEntryId);

        // Assert
        startedResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        cancelledResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var startedBody = await startedResponse.ReadBody<LeaveWaitlistResponse>();
        var cancelledBody = await cancelledResponse.ReadBody<LeaveWaitlistResponse>();
        startedBody.Status.ShouldBe(WaitlistStatus.Cancelled);
        cancelledBody.Status.ShouldBe(WaitlistStatus.Cancelled);
        (await Waitlist.WaitlistStatusOf(startedEntryId)).ShouldBe(WaitlistStatus.Cancelled);
        (await Waitlist.WaitlistStatusOf(cancelledEntryId)).ShouldBe(WaitlistStatus.Cancelled);
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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var owner = await Users.RegisterMember(visits, validUntil);
        var waiter = await Users.RegisterMember(visits, validUntil);
        var booked = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(owner.Number, visits, validUntil)
            .Create();
        var enrollmentId = await Enrollments.AddEnrollment(owner.Number, booked.ClassId, seats, EnrollmentStatus.Active);
        var entry = await waiter.Waitlist.Join(booked, seats);
        var left = await waiter.Waitlist.LeaveWaitlist(entry.Id);
        left.EnsureSuccessStatusCode();

        // Act
        var cancel = await owner.Enrollments.CancelEnrollment(enrollmentId);

        // Assert
        cancel.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Enrollments.ActiveCount(booked.UserId, booked.ClassId)).ShouldBe(0);
        (await Enrollments.ActiveCount(await Users.UserId(waiter.Number), booked.ClassId)).ShouldBe(0);
        (await Waitlist.WaitlistStatusOf(entry.Id)).ShouldBe(WaitlistStatus.Cancelled);
        (await Enrollments.SumActiveSeats(booked.ClassId)).ShouldBe(0);
    }

    [TestMethod]
    public async Task Leave_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        var response = await Users.Anonymous().Waitlist.LeaveWaitlist(1);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task Leave_WhenTheMinuteLimitIsExceeded_ReturnsTooManyRequests()
    {
        // Arrange
        var visits = 1;
        var passValidityDays = 30;
        var member = await Users.RegisterMember(visits, Session.Today.AddDays(passValidityDays));

        // Act
        var lastStatus = await AuthClient.Hammer(_ => member.Waitlist.LeaveWaitlist(0));

        // Assert
        lastStatus.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [TestMethod]
    public async Task Leave_WhenTheIdIsNotNumeric_ReturnsBadRequest()
    {
        // Arrange
        var visits = 1;
        var passValidityDays = 30;
        var member = await Users.RegisterMember(visits, Session.Today.AddDays(passValidityDays));

        // Act
        var response = await member.Waitlist.LeaveWaitlist("abc");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
