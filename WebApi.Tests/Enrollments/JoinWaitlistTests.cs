using System.Net;
using BusinessLayer.Result;
using Shouldly;
using WebApi.Features.Enrollments.Waitlist;
using WebApi.Tests.Infrastructure;

namespace WebApi.Tests.Enrollments;

[TestClass]
public class JoinWaitlistTests : ClubTest
{
    private const int SeatHolderNumber = 159;

    [TestMethod]
    public async Task Join_WhenFreeSeatsAreFewerThanRequested_ReturnsCreatedWithoutSpendingVisits()
    {
        // Arrange
        var requestedSeats = 3;
        var visits = 6;
        var position = 1;
        var lesson = await Host().Lesson()
            .Capacity(4)
            .StartsAt(DateTime.UtcNow.AddDays(20))
            .Pass(160, visits)
            .TakenBy(SeatHolderNumber, 2)
            .Create();

        // Act
        var response = await Host().User(160).JoinWaitlist(lesson, requestedSeats);

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
        var validUntil = ClubTime.Today().AddDays(30);
        var lesson = await Host().Lesson()
            .Capacity(1)
            .StartsAt(DateTime.UtcNow.AddDays(21))
            .Pass(161, requestedSeats, validUntil)
            .TakenBy(SeatHolderNumber, 1)
            .Create();
        await Host().SetPass(162, requestedSeats, validUntil);

        // Act
        var firstResponse = await Host().User(161).JoinWaitlist(lesson, requestedSeats);
        var secondResponse = await Host().User(162).JoinWaitlist(lesson, requestedSeats);

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
        var lesson = await Host().Lesson()
            .Capacity(4)
            .StartsAt(DateTime.UtcNow.AddDays(22))
            .Pass(163, 5)
            .TakenBy(SeatHolderNumber, 2)
            .Create();

        // Act
        var response = await Host().User(163).JoinWaitlist(lesson, 2);

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
        var lesson = await Host().Lesson()
            .Capacity(1)
            .StartsAt(DateTime.UtcNow.AddDays(23))
            .Pass(164, 4)
            .TakenBy(SeatHolderNumber, 1)
            .Create();
        var user = Host().User(164);
        var created = await user.JoinWaitlist(lesson, requestedSeats);
        created.EnsureSuccessStatusCode();

        // Act
        var response = await user.JoinWaitlist(lesson, requestedSeats);

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
        var visits = 4;
        var lesson = await Host().Lesson()
            .Capacity(5)
            .StartsAt(DateTime.UtcNow.AddDays(24))
            .Pass(165, visits)
            .TakenBy(165, 2)
            .Create();

        // Act
        var response = await Host().User(165).JoinWaitlist(lesson, requestedSeats);

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
        var visits = 4;
        var lesson = await Host().Lesson()
            .Capacity(1)
            .StartsAt(DateTime.UtcNow.AddDays(24).AddHours(1))
            .Pass(182, visits, ClubTime.Today().AddDays(-1))
            .TakenBy(SeatHolderNumber, 1)
            .Create();

        // Act
        var response = await Host().User(182).JoinWaitlist(lesson, 1);

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
        var visits = 1;
        var lesson = await Host().Lesson()
            .Capacity(2)
            .StartsAt(DateTime.UtcNow.AddDays(24).AddHours(2))
            .Pass(183, visits)
            .TakenBy(SeatHolderNumber, 2)
            .Create();

        // Act
        var response = await Host().User(183).JoinWaitlist(lesson, 2);

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
        var lesson = await Host().Lesson()
            .Capacity(4)
            .StartsAt(DateTime.UtcNow.AddMinutes(-5))
            .Pass(166, 4)
            .Create();

        // Act
        var response = await Host().User(166).JoinWaitlist(lesson, 1);

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
        var lesson = await Host().Lesson()
            .Capacity(4)
            .StartsAt(DateTime.UtcNow.AddDays(25))
            .Cancelled()
            .Pass(167, 4)
            .Create();

        // Act
        var response = await Host().User(167).JoinWaitlist(lesson, 1);

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
        var user = Host().User(168);

        // Act
        var missing = await user.JoinWaitlist(missingClassId, seats);
        var zero = await user.JoinWaitlist(zeroClassId, seats);

        // Assert
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        zero.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task Join_WhenTheRequestIsInvalid_ReturnsBadRequest()
    {
        // Arrange
        var user = Host().User(169);
        var missingClassKey = Guid.NewGuid().ToString("N");
        var missingClass = """{"seats":1}""";

        // Act
        var noSeats = await user.JoinWaitlist(1, 0);
        var missingClassBody = await user.Post("/enrollments/waitlist", missingClass, missingClassKey);
        var missingKey = await user.JoinWaitlistWithoutKey(1, 1);
        var blankKey = await user.JoinWaitlist(1, 1, "   ");

        // Assert
        noSeats.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        missingClassBody.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        missingKey.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        blankKey.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task Join_WithTheSameKeyAndBody_ReturnsTheFirstEntry()
    {
        // Arrange
        var key = Guid.NewGuid().ToString("N");
        var lesson = await Host().Lesson()
            .Capacity(1)
            .StartsAt(DateTime.UtcNow.AddDays(26))
            .Pass(170, 4)
            .TakenBy(SeatHolderNumber, 1)
            .Create();
        var user = Host().User(170);

        // Act
        var first = await user.JoinWaitlist(lesson, 1, key);
        var second = await user.JoinWaitlist(lesson, 1, key);

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
        var key = Guid.NewGuid().ToString("N");
        var lesson = await Host().Lesson()
            .Capacity(1)
            .StartsAt(DateTime.UtcNow.AddDays(26).AddHours(2))
            .Pass(181, 4)
            .TakenBy(SeatHolderNumber, 1)
            .Create();
        var user = Host().User(181);
        await user.GetEnrollment(1);

        // Act
        var responses = await Task.WhenAll(user.JoinWaitlist(lesson, 1, key), user.JoinWaitlist(lesson, 1, key));

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
        var key = Guid.NewGuid().ToString("N");
        var lesson = await Host().Lesson()
            .Capacity(1)
            .StartsAt(DateTime.UtcNow.AddDays(27))
            .Pass(171, 4)
            .TakenBy(SeatHolderNumber, 1)
            .Create();
        var user = Host().User(171);
        await user.JoinWaitlist(lesson, firstSeats, key);

        // Act
        var response = await user.JoinWaitlist(lesson, 2, key);

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
        var startsAt = DateTime.UtcNow.AddDays(28);
        var cancelledLesson = await Host().Lesson()
            .Capacity(1)
            .StartsAt(startsAt)
            .Pass(172, 4)
            .TakenBy(SeatHolderNumber, 1)
            .Create();
        var promotedLesson = await Host().Lesson()
            .Capacity(1)
            .StartsAt(startsAt.AddHours(1))
            .Pass(173, 4)
            .TakenBy(SeatHolderNumber, 1)
            .Create();
        var cancelledJoin = await Host().User(172).Join(cancelledLesson, requestedSeats);
        var promotedJoin = await Host().User(173).Join(promotedLesson, requestedSeats);
        await Host().SetWaitlistStatus(cancelledJoin.Id, DataAccessLayer.Entities.WaitlistStatus.Cancelled);
        await Host().SetWaitlistStatus(promotedJoin.Id, DataAccessLayer.Entities.WaitlistStatus.Promoted);

        // Act
        var cancelledAgain = await Host().User(172).JoinWaitlist(cancelledLesson, requestedSeats);
        var promotedAgain = await Host().User(173).JoinWaitlist(promotedLesson, requestedSeats);

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
        // Act
        var response = await Host().Anonymous().JoinWaitlist(1, 1);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task Join_WhenTheMinuteLimitIsExceeded_ReturnsTooManyRequests()
    {
        // Arrange
        var user = Host().User(175);

        // Act
        var lastStatus = await user.Hammer(requestNumber => user.JoinWaitlist(1, 0, $"waitlist-limit-175-{requestNumber}"));

        // Assert
        lastStatus.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [TestMethod]
    public async Task Join_WhenTheRequestExceedsCapacity_ReturnsExceedsCapacity()
    {
        // Arrange
        var capacity = 4;
        var requestedSeats = capacity + 1;
        var lesson = await Host().Lesson().Capacity(capacity).StartsAt(DateTime.UtcNow.AddDays(29)).Pass(176, 8).Create();

        // Act
        var response = await Host().User(176).JoinWaitlist(lesson, requestedSeats);

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
        var validUntil = ClubTime.Today().AddDays(30);
        var holder = await Host().RegisterMember(heldSeats, validUntil);
        var lesson = await Host().Lesson().Capacity(4).StartsAt(DateTime.UtcNow.AddDays(29)).Pass(178, visits, validUntil).Create();
        await Host().User(holder.Number).Book(lesson, heldSeats);
        await Host().SetPass(177, visits, validUntil);
        var headId = await Host().AddWaiting(177, lesson.ClassId, headSeats, DateTime.UtcNow.AddMinutes(-5));

        // Act
        var response = await Host().User(178).JoinWaitlist(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.SeatsAvailable);
        (await Host().WaitlistStatusOf(headId)).ShouldBe(DataAccessLayer.Entities.WaitlistStatus.Waiting);
        var stored = await Host().Waiting(lesson.ClassId, lesson.UserId);
        stored.WaitingCount.ShouldBe(0);
    }
}
