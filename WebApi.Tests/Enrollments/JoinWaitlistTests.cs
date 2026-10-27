using System.Net;
using BusinessLayer.Result;
using Shouldly;
using WebApi.Features.Enrollments.Waitlist;
using WebApi.Tests.Infrastructure;

namespace WebApi.Tests.Enrollments;

[TestClass]
public class JoinWaitlistTests : ClubTest
{
    [TestMethod]
    public async Task Join_WhenFreeSeatsAreFewerThanRequested_ReturnsCreatedWithoutSpendingVisits()
    {
        // Arrange
        var requestedSeats = 3;
        var heldSeats = 2;
        var visits = 6;
        var position = 1;
        var capacity = 4;
        var leadDays = 20;
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

        // Act
        var response = await member.JoinWaitlist(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.ReadBody<JoinWaitlistResponse>();
        body.ClassId.ShouldBe(lesson.ClassId);
        body.Seats.ShouldBe(requestedSeats);
        body.Position.ShouldBe(position);
        body.CreatedAt.ShouldNotBe(default);
        var stored = await Host().Waiting(lesson.ClassId, lesson.UserId);
        stored.WaitingCount.ShouldBe(1);
        stored.EntryId.ShouldBe(body.Id);
        stored.Seats.ShouldBe(requestedSeats);
        stored.RemainingVisits.ShouldBe(visits);
    }

    [TestMethod]
    public async Task Join_AssignsPositionByCreationOrder()
    {
        // Arrange
        var requestedSeats = 1;
        var heldSeats = 1;
        var capacity = 1;
        var leadDays = 21;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var holder = await Host().RegisterMember(heldSeats, validUntil);
        var firstMember = await Host().RegisterMember(requestedSeats, validUntil);
        var secondMember = await Host().RegisterMember(requestedSeats, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(firstMember.Number, requestedSeats, validUntil)
            .TakenBy(holder.Number, heldSeats)
            .Create();

        // Act
        var firstResponse = await firstMember.JoinWaitlist(lesson, requestedSeats);
        var secondResponse = await secondMember.JoinWaitlist(lesson, requestedSeats);

        // Assert
        firstResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var firstBody = await firstResponse.ReadBody<JoinWaitlistResponse>();
        var secondBody = await secondResponse.ReadBody<JoinWaitlistResponse>();
        firstBody.Position.ShouldBe(1);
        secondBody.Position.ShouldBe(2);
        secondBody.Id.ShouldNotBe(firstBody.Id);
    }

    [TestMethod]
    public async Task Join_WhenTheRequestedSeatsFit_ReturnsSeatsAvailable()
    {
        // Arrange
        var requestedSeats = 2;
        var heldSeats = 2;
        var visits = 5;
        var capacity = 4;
        var leadDays = 22;
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

        // Act
        var response = await member.JoinWaitlist(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.SeatsAvailable);
        var stored = await Host().Waiting(lesson.ClassId, lesson.UserId);
        stored.WaitingCount.ShouldBe(0);
    }

    [TestMethod]
    public async Task Join_WhenTheUserIsAlreadyWaiting_ReturnsAlreadyWaiting()
    {
        // Arrange
        var requestedSeats = 1;
        var heldSeats = 1;
        var visits = 4;
        var capacity = 1;
        var leadDays = 23;
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
        var created = await member.JoinWaitlist(lesson, requestedSeats);
        created.EnsureSuccessStatusCode();

        // Act
        var response = await member.JoinWaitlist(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.AlreadyWaiting);
        var stored = await Host().Waiting(lesson.ClassId, lesson.UserId);
        stored.WaitingCount.ShouldBe(1);
    }

    [TestMethod]
    public async Task Join_WhenTheUserAlreadyHasABooking_StillJoinsForTheExtraSeats()
    {
        // Arrange
        var requestedSeats = 4;
        var heldSeats = 2;
        var visits = 4;
        var capacity = 5;
        var leadDays = 24;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .TakenBy(member.Number, heldSeats)
            .Create();

        // Act
        var response = await member.JoinWaitlist(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.ReadBody<JoinWaitlistResponse>();
        body.Seats.ShouldBe(requestedSeats);
        var stored = await Host().Waiting(lesson.ClassId, lesson.UserId);
        stored.WaitingCount.ShouldBe(1);
        stored.RemainingVisits.ShouldBe(visits);
    }

    [TestMethod]
    public async Task Join_WhenThePassIsExpired_ReturnsPassExpired()
    {
        // Arrange
        var requestedSeats = 1;
        var heldSeats = 1;
        var visits = 4;
        var capacity = 1;
        var leadDays = 24;
        var leadHours = 1;
        var expiredDays = 1;
        var expiredOn = ClubTime.Today().AddDays(-expiredDays);
        var holder = await Host().RegisterMember(heldSeats, expiredOn);
        var member = await Host().RegisterMember(visits, expiredOn);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays).AddHours(leadHours))
            .Pass(member.Number, visits, expiredOn)
            .TakenBy(holder.Number, heldSeats)
            .Create();

        // Act
        var response = await member.JoinWaitlist(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.PassExpired);
        var stored = await Host().Waiting(lesson.ClassId, lesson.UserId);
        stored.WaitingCount.ShouldBe(0);
        stored.RemainingVisits.ShouldBe(visits);
    }

    [TestMethod]
    public async Task Join_WhenThePassHasTooFewVisits_ReturnsPassExhausted()
    {
        // Arrange
        var requestedSeats = 2;
        var heldSeats = 2;
        var visits = 1;
        var capacity = 2;
        var leadDays = 24;
        var leadHours = 2;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var holder = await Host().RegisterMember(heldSeats, validUntil);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays).AddHours(leadHours))
            .Pass(member.Number, visits, validUntil)
            .TakenBy(holder.Number, heldSeats)
            .Create();

        // Act
        var response = await member.JoinWaitlist(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.PassExhausted);
        var stored = await Host().Waiting(lesson.ClassId, lesson.UserId);
        stored.WaitingCount.ShouldBe(0);
        stored.RemainingVisits.ShouldBe(visits);
    }

    [TestMethod]
    public async Task Join_WhenTheClassHasStarted_ReturnsClassStarted()
    {
        // Arrange
        var requestedSeats = 1;
        var visits = 4;
        var capacity = 4;
        var alreadyStarted = 5;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddMinutes(-alreadyStarted))
            .Pass(member.Number, visits, validUntil)
            .Create();

        // Act
        var response = await member.JoinWaitlist(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.ClassStarted);
        var stored = await Host().Waiting(lesson.ClassId, lesson.UserId);
        stored.WaitingCount.ShouldBe(0);
    }

    [TestMethod]
    public async Task Join_WhenTheClassIsCancelled_ReturnsClassCancelled()
    {
        // Arrange
        var requestedSeats = 1;
        var visits = 4;
        var capacity = 4;
        var leadDays = 25;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Cancelled()
            .Pass(member.Number, visits, validUntil)
            .Create();

        // Act
        var response = await member.JoinWaitlist(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.ClassCancelled);
        var stored = await Host().Waiting(lesson.ClassId, lesson.UserId);
        stored.WaitingCount.ShouldBe(0);
    }

    [TestMethod]
    public async Task Join_WhenTheClassDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        var missingClassId = long.MaxValue;
        var zeroClassId = 0L;
        var seats = 1;
        var visits = 1;
        var passValidityDays = 30;
        var member = await Host().RegisterMember(visits, ClubTime.Today().AddDays(passValidityDays));

        // Act
        var missing = await member.JoinWaitlist(missingClassId, seats);
        var zero = await member.JoinWaitlist(zeroClassId, seats);

        // Assert
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        zero.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task Join_WhenTheRequestIsInvalid_ReturnsBadRequest()
    {
        // Arrange
        var visits = 1;
        var passValidityDays = 30;
        var noSeats = 0;
        var seats = 1;
        var member = await Host().RegisterMember(visits, ClubTime.Today().AddDays(passValidityDays));
        var missingClassKey = Guid.NewGuid().ToString("N");
        var missingClass = $$"""{"seats":{{seats}}}""";

        // Act
        var noSeatsResponse = await member.JoinWaitlist(1, noSeats);
        var missingClassBody = await member.Post("/enrollments/waitlist", missingClass, missingClassKey);
        var missingKey = await member.JoinWaitlistWithoutKey(1, seats);
        var blankKey = await member.JoinWaitlist(1, seats, "   ");

        // Assert
        noSeatsResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        missingClassBody.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        missingKey.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        blankKey.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task Join_WithTheSameKeyAndBody_ReturnsTheFirstEntry()
    {
        // Arrange
        var requestedSeats = 1;
        var heldSeats = 1;
        var visits = 4;
        var capacity = 1;
        var leadDays = 26;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var key = Guid.NewGuid().ToString("N");
        var holder = await Host().RegisterMember(heldSeats, validUntil);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .TakenBy(holder.Number, heldSeats)
            .Create();

        // Act
        var first = await member.JoinWaitlist(lesson, requestedSeats, key);
        var second = await member.JoinWaitlist(lesson, requestedSeats, key);

        // Assert
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        var firstBody = await first.ReadBody<JoinWaitlistResponse>();
        var secondBody = await second.ReadBody<JoinWaitlistResponse>();
        secondBody.Id.ShouldBe(firstBody.Id);
        secondBody.Position.ShouldBe(firstBody.Position);
        var stored = await Host().Waiting(lesson.ClassId, lesson.UserId);
        stored.WaitingCount.ShouldBe(1);
    }

    [TestMethod]
    public async Task Join_WhenTheSameKeyIsSentTwiceAtOnce_ReturnsTheFirstEntry()
    {
        // Arrange
        var requestedSeats = 1;
        var heldSeats = 1;
        var visits = 4;
        var capacity = 1;
        var leadDays = 26;
        var leadHours = 2;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var key = Guid.NewGuid().ToString("N");
        var holder = await Host().RegisterMember(heldSeats, validUntil);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays).AddHours(leadHours))
            .Pass(member.Number, visits, validUntil)
            .TakenBy(holder.Number, heldSeats)
            .Create();
        await member.GetEnrollment(1);

        // Act
        var responses = await Task.WhenAll(
            member.JoinWaitlist(lesson, requestedSeats, key),
            member.JoinWaitlist(lesson, requestedSeats, key));

        // Assert
        responses[0].StatusCode.ShouldBe(HttpStatusCode.Created);
        responses[1].StatusCode.ShouldBe(HttpStatusCode.Created);
        var firstBody = await responses[0].ReadBody<JoinWaitlistResponse>();
        var secondBody = await responses[1].ReadBody<JoinWaitlistResponse>();
        secondBody.Id.ShouldBe(firstBody.Id);
        secondBody.Position.ShouldBe(firstBody.Position);
        var stored = await Host().Waiting(lesson.ClassId, lesson.UserId);
        stored.WaitingCount.ShouldBe(1);
    }

    [TestMethod]
    public async Task Join_WithTheSameKeyAndADifferentBody_ReturnsIdempotencyMismatch()
    {
        // Arrange
        var firstSeats = 1;
        var changedSeats = 2;
        var heldSeats = 1;
        var visits = 4;
        var capacity = 1;
        var leadDays = 27;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var key = Guid.NewGuid().ToString("N");
        var holder = await Host().RegisterMember(heldSeats, validUntil);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .TakenBy(holder.Number, heldSeats)
            .Create();
        await member.JoinWaitlist(lesson, firstSeats, key);

        // Act
        var response = await member.JoinWaitlist(lesson, changedSeats, key);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.IdempotencyMismatch);
        var stored = await Host().Waiting(lesson.ClassId, lesson.UserId);
        stored.WaitingCount.ShouldBe(1);
        stored.Seats.ShouldBe(firstSeats);
    }

    [TestMethod]
    public async Task Join_AfterThePreviousEntryLeftTheQueue_JoinsAgain()
    {
        // Arrange
        var requestedSeats = 1;
        var heldSeats = 1;
        var visits = 4;
        var capacity = 1;
        var leadDays = 28;
        var leadHours = 1;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var holder = await Host().RegisterMember(heldSeats, validUntil);
        var cancelledMember = await Host().RegisterMember(visits, validUntil);
        var promotedMember = await Host().RegisterMember(visits, validUntil);
        var startsAt = Host().Now.AddDays(leadDays);
        var cancelledLesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(startsAt)
            .Pass(cancelledMember.Number, visits, validUntil)
            .TakenBy(holder.Number, heldSeats)
            .Create();
        var promotedLesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(startsAt.AddHours(leadHours))
            .Pass(promotedMember.Number, visits, validUntil)
            .TakenBy(holder.Number, heldSeats)
            .Create();
        var cancelledJoin = await cancelledMember.Join(cancelledLesson, requestedSeats);
        var promotedJoin = await promotedMember.Join(promotedLesson, requestedSeats);
        await Host().SetWaitlistStatus(cancelledJoin.Id, DataAccessLayer.Entities.WaitlistStatus.Cancelled);
        await Host().SetWaitlistStatus(promotedJoin.Id, DataAccessLayer.Entities.WaitlistStatus.Promoted);

        // Act
        var cancelledAgain = await cancelledMember.JoinWaitlist(cancelledLesson, requestedSeats);
        var promotedAgain = await promotedMember.JoinWaitlist(promotedLesson, requestedSeats);

        // Assert
        cancelledAgain.StatusCode.ShouldBe(HttpStatusCode.Created);
        promotedAgain.StatusCode.ShouldBe(HttpStatusCode.Created);
        var cancelledAgainBody = await cancelledAgain.ReadBody<JoinWaitlistResponse>();
        var promotedAgainBody = await promotedAgain.ReadBody<JoinWaitlistResponse>();
        cancelledAgainBody.Id.ShouldNotBe(cancelledJoin.Id);
        promotedAgainBody.Id.ShouldNotBe(promotedJoin.Id);
        cancelledAgainBody.Position.ShouldBe(1);
        promotedAgainBody.Position.ShouldBe(1);
        var cancelledStored = await Host().Waiting(cancelledLesson.ClassId, cancelledLesson.UserId);
        var promotedStored = await Host().Waiting(promotedLesson.ClassId, promotedLesson.UserId);
        cancelledStored.WaitingCount.ShouldBe(1);
        promotedStored.WaitingCount.ShouldBe(1);
    }

    [TestMethod]
    public async Task Join_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange
        var seats = 1;

        // Act
        var response = await Host().Anonymous().JoinWaitlist(1, seats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task Join_WhenTheMinuteLimitIsExceeded_ReturnsTooManyRequests()
    {
        // Arrange
        var visits = 1;
        var seats = 0;
        var passValidityDays = 30;
        var member = await Host().RegisterMember(visits, ClubTime.Today().AddDays(passValidityDays));

        // Act
        var lastStatus = await member.Hammer(requestNumber => member.JoinWaitlist(1, seats, $"waitlist-limit-{member.Number}-{requestNumber}"));

        // Assert
        lastStatus.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [TestMethod]
    public async Task Join_WhenTheRequestExceedsCapacity_ReturnsExceedsCapacity()
    {
        // Arrange
        var capacity = 4;
        var requestedSeats = capacity + 1;
        var visits = 8;
        var leadDays = 29;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .Create();

        // Act
        var response = await member.JoinWaitlist(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.ExceedsCapacity);
        var stored = await Host().Waiting(lesson.ClassId, lesson.UserId);
        stored.WaitingCount.ShouldBe(0);
    }

    [TestMethod]
    public async Task Join_WhenFreeSeatsFitBehindAnOversizedRequest_ReturnsSeatsAvailable()
    {
        // Arrange
        var headSeats = 3;
        var requestedSeats = 1;
        var heldSeats = 2;
        var visits = 4;
        var capacity = 4;
        var leadDays = 29;
        var queuedAgo = 5;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var holder = await Host().RegisterMember(heldSeats, validUntil);
        var head = await Host().RegisterMember(visits, validUntil);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .Create();
        await holder.Book(lesson, heldSeats);
        var headId = await Host().AddWaiting(head.Number, lesson.ClassId, headSeats, Host().Now.AddMinutes(-queuedAgo));

        // Act
        var response = await member.JoinWaitlist(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.SeatsAvailable);
        (await Host().WaitlistStatusOf(headId)).ShouldBe(DataAccessLayer.Entities.WaitlistStatus.Waiting);
        var stored = await Host().Waiting(lesson.ClassId, lesson.UserId);
        stored.WaitingCount.ShouldBe(0);
    }
}
