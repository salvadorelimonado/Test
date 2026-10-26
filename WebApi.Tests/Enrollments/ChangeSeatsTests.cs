using System.Net;
using System.Text.Json;
using DataAccessLayer.Entities;
using BusinessLayer.Result;
using Shouldly;
using WebApi.Features.Enrollments;
using WebApi.Tests.Infrastructure;

namespace WebApi.Tests.Enrollments;

[TestClass]
public class ChangeSeatsTests : ClubTest
{
    private const long MissingEnrollmentId = 9_000_000_000;

    [TestMethod]
    public async Task ChangeSeats_WhenIncreasing_ReturnsCardAndSpendsTheDifference()
    {
        // Arrange
        var bookedSeats = 2;
        var requestedSeats = 4;
        var visits = 10;
        var startsAt = ClubTime.WholeSecond(DateTime.UtcNow.AddDays(2));
        var lesson = await Host().Lesson().Capacity(8).StartsAt(startsAt).Pass(14, visits).Create();
        var created = await Host().User(14).Book(lesson, bookedSeats);

        // Act
        var response = await Host().User(14).ChangeSeats(created.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var card = await response.ReadBody<EnrollmentCard>();
        card.Id.ShouldBe(created.Id);
        card.Status.ShouldBe(EnrollmentStatus.Active);
        card.Seats.ShouldBe(requestedSeats);
        card.CreatedAt.ShouldBe(created.CreatedAt);
        card.Class.Id.ShouldBe(lesson.ClassId);
        card.Class.Title.ShouldBe(Lesson.DefaultTitle);
        card.Class.StartsAt.ShouldBe(startsAt);
        card.Class.DurationMinutes.ShouldBe(Lesson.DefaultDurationMinutes);
        card.Class.IsCancelled.ShouldBeFalse();
        card.Class.Room.Id.ShouldBe(lesson.RoomId);
        card.Class.Room.Name.ShouldBe(lesson.RoomName);
        card.Pass.RemainingVisits.ShouldBe(visits - requestedSeats);
        card.Pass.ValidUntil.ShouldBe(lesson.ValidUntil);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(requestedSeats);
        stored.Status.ShouldBe(EnrollmentStatus.Active);
        stored.RemainingVisits.ShouldBe(visits - requestedSeats);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenDecreasingExpiredPass_ReturnsVisits()
    {
        // Arrange
        var bookedSeats = 2;
        var requestedSeats = 1;
        var visits = 2;
        var expiredOn = ClubTime.Today().AddDays(-1);
        var lesson = await Host().Lesson().Capacity(2).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddDays(2))).Pass(15, visits).Create();
        var created = await Host().User(15).Book(lesson, bookedSeats);
        await Host().SetValidUntil(lesson.UserId, expiredOn);

        // Act
        var response = await Host().User(15).ChangeSeats(created.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var card = await response.ReadBody<EnrollmentCard>();
        card.Seats.ShouldBe(requestedSeats);
        card.Pass.RemainingVisits.ShouldBe(visits - requestedSeats);
        card.Pass.ValidUntil.ShouldBe(expiredOn);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(requestedSeats);
        stored.RemainingVisits.ShouldBe(visits - requestedSeats);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenClassIsFull_DecreaseStillReturnsVisits()
    {
        // Arrange
        var ownerSeats = 2;
        var otherSeats = 1;
        var requestedSeats = 1;
        var ownerVisits = 5;
        var otherVisits = 5;
        var validUntil = ClubTime.Today().AddDays(30);
        var lesson = await Host().Lesson()
            .Capacity(3)
            .StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddDays(2)))
            .Pass(30, ownerVisits, validUntil)
            .Create();
        await Host().SetPass(31, otherVisits, validUntil);
        var ownerEnrollment = await Host().User(30).Book(lesson, ownerSeats);
        var otherEnrollment = await Host().User(31).Book(lesson, otherSeats);

        // Act
        var response = await Host().User(30).ChangeSeats(ownerEnrollment.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var card = await response.ReadBody<EnrollmentCard>();
        card.Seats.ShouldBe(requestedSeats);
        card.Pass.RemainingVisits.ShouldBe(ownerVisits - requestedSeats);
        var ownerStored = await Host().Enrollment(ownerEnrollment.Id, lesson.UserId);
        ownerStored.Seats.ShouldBe(requestedSeats);
        ownerStored.RemainingVisits.ShouldBe(ownerVisits - requestedSeats);
        var otherUserId = await Host().UserId(31);
        var otherStored = await Host().Enrollment(otherEnrollment.Id, otherUserId);
        otherStored.Seats.ShouldBe(otherSeats);
        otherStored.RemainingVisits.ShouldBe(otherVisits - otherSeats);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenSeatsStayTheSame_ReturnsCardWithoutChangingVisits()
    {
        // Arrange
        var seats = 2;
        var visits = 10;
        var lesson = await Host().Lesson().Capacity(8).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddDays(2))).Pass(16, visits).Create();
        var created = await Host().User(16).Book(lesson, seats);

        // Act
        var response = await Host().User(16).ChangeSeats(created.Id, seats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var card = await response.ReadBody<EnrollmentCard>();
        card.Seats.ShouldBe(seats);
        card.Pass.RemainingVisits.ShouldBe(visits - seats);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(seats);
        stored.RemainingVisits.ShouldBe(visits - seats);
    }

    [TestMethod]
    public async Task ChangeSeats_WithSeatsBelowOne_ReturnsBadRequest()
    {
        // Act
        var response = await Host().User(17).ChangeSeats(MissingEnrollmentId, 0);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenEnrollmentIsMissing_ReturnsNotFound()
    {
        // Act
        var response = await Host().User(18).ChangeSeats(MissingEnrollmentId, 1);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenEnrollmentBelongsToAnotherUser_ReturnsNotFound()
    {
        // Arrange
        var seats = 2;
        var visits = 10;
        var lesson = await Host().Lesson().Capacity(8).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddDays(2))).Pass(19, visits).Create();
        var created = await Host().User(19).Book(lesson, seats);

        // Act
        var response = await Host().User(20).ChangeSeats(created.Id, 1);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(seats);
        stored.RemainingVisits.ShouldBe(visits - seats);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenEnrollmentIsCancelled_ReturnsEnrollmentCancelled()
    {
        // Arrange
        var seats = 2;
        var visits = 10;
        var lesson = await Host().Lesson().Capacity(8).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddDays(2))).Pass(21, visits).Create();
        var created = await Host().User(21).Book(lesson, seats);
        await Host().SetEnrollmentStatus(created.Id, EnrollmentStatus.Cancelled);

        // Act
        var response = await Host().User(21).ChangeSeats(created.Id, 1);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.EnrollmentCancelled);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(seats);
        stored.Status.ShouldBe(EnrollmentStatus.Cancelled);
        stored.RemainingVisits.ShouldBe(visits - seats);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenClassIsCancelled_ReturnsClassCancelled()
    {
        // Arrange
        var seats = 2;
        var visits = 10;
        var lesson = await Host().Lesson().Capacity(8).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddDays(2))).Pass(22, visits).Create();
        var created = await Host().User(22).Book(lesson, seats);
        await Host().CancelClass(lesson.ClassId);

        // Act
        var response = await Host().User(22).ChangeSeats(created.Id, 1);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.ClassCancelled);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(seats);
        stored.RemainingVisits.ShouldBe(visits - seats);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenClassHasStarted_ReturnsClassStarted()
    {
        // Arrange
        var seats = 2;
        var visits = 10;
        var lesson = await Host().Lesson().Capacity(8).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddDays(2))).Pass(23, visits).Create();
        var created = await Host().User(23).Book(lesson, seats);
        await Host().SetStartsAt(lesson.ClassId, ClubTime.WholeSecond(DateTime.UtcNow.AddHours(-1)));

        // Act
        var response = await Host().User(23).ChangeSeats(created.Id, 3);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.ClassStarted);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(seats);
        stored.RemainingVisits.ShouldBe(visits - seats);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenSeatsStayTheSameAfterStart_ReturnsClassStarted()
    {
        // Arrange
        var seats = 2;
        var visits = 10;
        var lesson = await Host().Lesson().Capacity(8).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddDays(2))).Pass(24, visits).Create();
        var created = await Host().User(24).Book(lesson, seats);
        await Host().SetStartsAt(lesson.ClassId, ClubTime.WholeSecond(DateTime.UtcNow.AddHours(-1)));

        // Act
        var response = await Host().User(24).ChangeSeats(created.Id, seats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.ClassStarted);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(seats);
        stored.RemainingVisits.ShouldBe(visits - seats);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenIncreaseAndPassExpired_ReturnsPassExpired()
    {
        // Arrange
        var bookedSeats = 2;
        var visits = 2;
        var lesson = await Host().Lesson().Capacity(2).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddDays(2))).Pass(25, visits).Create();
        var created = await Host().User(25).Book(lesson, bookedSeats);
        await Host().SetValidUntil(lesson.UserId, ClubTime.Today().AddDays(-1));

        // Act
        var response = await Host().User(25).ChangeSeats(created.Id, 3);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.PassExpired);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(bookedSeats);
        stored.RemainingVisits.ShouldBe(visits - bookedSeats);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenIncreaseAndVisitsAreShort_ReturnsPassExhausted()
    {
        // Arrange
        var bookedSeats = 2;
        var visits = 2;
        var lesson = await Host().Lesson().Capacity(10).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddDays(2))).Pass(26, visits).Create();
        var created = await Host().User(26).Book(lesson, bookedSeats);

        // Act
        var response = await Host().User(26).ChangeSeats(created.Id, 3);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.PassExhausted);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(bookedSeats);
        stored.RemainingVisits.ShouldBe(visits - bookedSeats);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenIncreaseAndClassIsFull_ReturnsCapacityExceeded()
    {
        // Arrange
        var bookedSeats = 2;
        var visits = 10;
        var lesson = await Host().Lesson().Capacity(2).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddDays(2))).Pass(27, visits).Create();
        var created = await Host().User(27).Book(lesson, bookedSeats);

        // Act
        var response = await Host().User(27).ChangeSeats(created.Id, 3);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.CapacityExceeded);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(bookedSeats);
        stored.RemainingVisits.ShouldBe(visits - bookedSeats);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenCancelledEnrollmentIsOnCancelledStartedClass_ReturnsEnrollmentCancelled()
    {
        // Arrange
        var lesson = await Host().Lesson().Capacity(8).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddDays(2))).Pass(28, 10).Create();
        var created = await Host().User(28).Book(lesson, 2);
        await Host().SetEnrollmentStatus(created.Id, EnrollmentStatus.Cancelled);
        await Host().CancelClass(lesson.ClassId);
        await Host().SetStartsAt(lesson.ClassId, ClubTime.WholeSecond(DateTime.UtcNow.AddHours(-1)));

        // Act
        var response = await Host().User(28).ChangeSeats(created.Id, 1);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.EnrollmentCancelled);
    }

    [TestMethod]
    public async Task ChangeSeats_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange
        var seats = 2;
        var visits = 10;
        var lesson = await Host().Lesson().Capacity(8).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddDays(2))).Pass(29, visits).Create();
        var created = await Host().User(29).Book(lesson, seats);

        // Act
        var response = await Host().Anonymous().ChangeSeats(created.Id, 1);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(seats);
        stored.RemainingVisits.ShouldBe(visits - seats);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenDecreasing_PromotesAWaiterWhoFitsInTheFreeSeats()
    {
        // Arrange
        var bookedSeats = 2;
        var requestedSeats = 1;
        var waiterSeats = 2;
        var ownerVisits = 10;
        var waiterVisits = 4;
        var validUntil = ClubTime.Today().AddDays(30);
        var lesson = await Host().Lesson()
            .Capacity(3)
            .StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddDays(2)))
            .Pass(33, ownerVisits, validUntil)
            .Create();
        await Host().SetPass(34, waiterVisits, validUntil);
        var created = await Host().User(33).Book(lesson, bookedSeats);
        var waiterId = await Host().AddWaiting(34, lesson.ClassId, waiterSeats, DateTime.UtcNow.AddMinutes(-5));

        // Act
        var response = await Host().User(33).ChangeSeats(created.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var card = await response.ReadBody<EnrollmentCard>();
        card.Seats.ShouldBe(requestedSeats);
        card.Pass.RemainingVisits.ShouldBe(ownerVisits - requestedSeats);
        (await Host().WaitlistStatusOf(waiterId)).ShouldBe(WaitlistStatus.Promoted);
        var waiterUserId = await Host().UserId(34);
        (await Host().ActiveSeats(waiterUserId, lesson.ClassId)).ShouldBe(waiterSeats);
        (await Host().RemainingVisits(waiterUserId)).ShouldBe(waiterVisits - waiterSeats);
        (await Host().SumActiveSeats(lesson.ClassId)).ShouldBe(requestedSeats + waiterSeats);
        var leave = await Host().User(34).LeaveWaitlist(waiterId);
        leave.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await using var stream = await leave.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        document.RootElement.GetProperty("reason").Deserialize<ConflictReason>(HttpReads.Json).ShouldBe(ConflictReason.AlreadyPromoted);
        var promotedEnrollmentId = document.RootElement.GetProperty("promotedEnrollmentId").GetInt64();
        var waiterCard = await Host().User(34).GetEnrollment(promotedEnrollmentId);
        waiterCard.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await waiterCard.ReadBody<EnrollmentCard>()).Id.ShouldBe(promotedEnrollmentId);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenTheIdIsNotNumeric_ReturnsBadRequest()
    {
        // Arrange
        var user = Host().User(32);

        // Act
        var response = await user.ChangeSeats("abc", 1);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenTheIncreaseFitsBehindAnOversizedRequest_ChangesTheSeats()
    {
        // Arrange
        var bookedSeats = 1;
        var requestedSeats = 2;
        var headSeats = 4;
        var visits = 6;
        var validUntil = ClubTime.Today().AddDays(30);
        var lesson = await Host().Lesson().Capacity(4).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddDays(2))).Pass(35, visits, validUntil).Create();
        var created = await Host().User(35).Book(lesson, bookedSeats);
        await Host().SetPass(36, visits, validUntil);
        var headId = await Host().AddWaiting(36, lesson.ClassId, headSeats, DateTime.UtcNow.AddMinutes(-5));

        // Act
        var response = await Host().User(35).ChangeSeats(created.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(requestedSeats);
        stored.RemainingVisits.ShouldBe(visits - requestedSeats);
        (await Host().SumActiveSeats(lesson.ClassId)).ShouldBe(requestedSeats);
        (await Host().WaitlistStatusOf(headId)).ShouldBe(WaitlistStatus.Waiting);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenIncreasingPastCapacityAndTheQueueIsNotEmpty_ReturnsCapacityExceeded()
    {
        // Arrange
        var bookedSeats = 2;
        var requestedSeats = 3;
        var visits = 6;
        var lesson = await Host().Lesson().Capacity(2).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddDays(2))).Pass(37, visits).Create();
        var created = await Host().User(37).Book(lesson, bookedSeats);
        await Host().AddWaiting(38, lesson.ClassId, 1, DateTime.UtcNow.AddMinutes(-5));

        // Act
        var response = await Host().User(37).ChangeSeats(created.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.CapacityExceeded);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(bookedSeats);
        stored.RemainingVisits.ShouldBe(visits - bookedSeats);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenIncreasingAndTheCallerIsAlsoWaiting_KeepsThatRequest()
    {
        // Arrange
        var bookedSeats = 1;
        var requestedSeats = 2;
        var ownRequestSeats = 2;
        var visits = 3;
        var capacity = 4;
        var validUntil = ClubTime.Today().AddDays(30);
        var owner = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddDays(3)))
            .Pass(owner.Number, visits, validUntil)
            .Create();
        var created = await owner.Book(lesson, bookedSeats);
        var ownEntryId = await Host().AddWaiting(
            owner.Number,
            lesson.ClassId,
            ownRequestSeats,
            DateTime.UtcNow.AddMinutes(-5));

        // Act
        var response = await owner.ChangeSeats(created.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(requestedSeats);
        stored.RemainingVisits.ShouldBe(visits - requestedSeats);
        (await Host().WaitlistStatusOf(ownEntryId)).ShouldBe(WaitlistStatus.Waiting);
        (await Host().SumActiveSeats(lesson.ClassId)).ShouldBe(requestedSeats);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenDecreasingAndTheCallerIsAlsoWaiting_DropsThatRequestAndPromotesTheNext()
    {
        // Arrange
        var bookedSeats = 3;
        var requestedSeats = 1;
        var ownRequestSeats = 2;
        var nextRequestSeats = 2;
        var ownerVisits = 6;
        var nextVisits = 4;
        var validUntil = ClubTime.Today().AddDays(30);
        var owner = await Host().RegisterMember(ownerVisits, validUntil);
        var next = await Host().RegisterMember(nextVisits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(bookedSeats)
            .StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddDays(2)))
            .Pass(owner.Number, ownerVisits, validUntil)
            .Create();
        var created = await owner.Book(lesson, bookedSeats);
        var ownEntryId = await Host().AddWaiting(owner.Number, lesson.ClassId, ownRequestSeats, DateTime.UtcNow.AddMinutes(-10));
        var nextEntryId = await Host().AddWaiting(next.Number, lesson.ClassId, nextRequestSeats, DateTime.UtcNow.AddMinutes(-5));

        // Act
        var response = await owner.ChangeSeats(created.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var card = await response.ReadBody<EnrollmentCard>();
        card.Seats.ShouldBe(requestedSeats);
        card.Pass.RemainingVisits.ShouldBe(ownerVisits - requestedSeats);
        var ownerId = await Host().UserId(owner.Number);
        (await Host().WaitlistStatusOf(ownEntryId)).ShouldBe(WaitlistStatus.Cancelled);
        (await Host().ActiveCount(ownerId, lesson.ClassId)).ShouldBe(1);
        (await Host().ActiveSeats(ownerId, lesson.ClassId)).ShouldBe(requestedSeats);
        (await Host().RemainingVisits(ownerId)).ShouldBe(ownerVisits - requestedSeats);
        (await Host().WaitlistStatusOf(nextEntryId)).ShouldBe(WaitlistStatus.Promoted);
        var nextUserId = await Host().UserId(next.Number);
        (await Host().ActiveSeats(nextUserId, lesson.ClassId)).ShouldBe(nextRequestSeats);
        (await Host().RemainingVisits(nextUserId)).ShouldBe(nextVisits - nextRequestSeats);
    }
}
