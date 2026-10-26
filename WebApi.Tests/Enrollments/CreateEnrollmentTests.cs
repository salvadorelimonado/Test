using System.Net;
using BusinessLayer.Result;
using DataAccessLayer.Entities;
using WebApi.Features.Enrollments.Create;
using Shouldly;
using WebApi.Features.Enrollments;
using WebApi.Tests.Infrastructure;

namespace WebApi.Tests.Enrollments;

[TestClass]
public class CreateEnrollmentTests : ClubTest
{
    [TestMethod]
    public async Task Create_WithFreeSeatsAndVisits_ReturnsCreatedEnrollmentAndSpendsVisits()
    {
        // Arrange
        var seats = 2;
        var visits = 10;
        var lesson = await Host().Lesson().Capacity(8).StartsAt(DateTime.UtcNow.AddDays(2)).Pass(1, visits).Create();

        // Act
        var response = await Host().User(1).Enroll(lesson, seats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.ReadBody<CreateEnrollmentResponse>();
        body.ClassId.ShouldBe(lesson.ClassId);
        body.Seats.ShouldBe(seats);
        body.Status.ShouldBe(EnrollmentStatus.Active);
        body.CreatedAt.ShouldNotBe(default);
        var stored = await Host().ActiveBooking(lesson.ClassId, lesson.UserId);
        stored.Count.ShouldBe(1);
        stored.RemainingVisits.ShouldBe(visits - seats);
    }

    [TestMethod]
    public async Task Create_WithSameKeyAndBody_ReturnsTheFirstEnrollment()
    {
        // Arrange
        var seats = 1;
        var visits = 5;
        var key = Guid.NewGuid().ToString("N");
        var lesson = await Host().Lesson().Capacity(6).StartsAt(DateTime.UtcNow.AddDays(3)).Pass(2, visits).Create();
        var user = Host().User(2);

        // Act
        var first = await user.Enroll(lesson, seats, key);
        var second = await user.Enroll(lesson, seats, key);

        // Assert
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        var firstBody = await first.ReadBody<CreateEnrollmentResponse>();
        var secondBody = await second.ReadBody<CreateEnrollmentResponse>();
        secondBody.Id.ShouldBe(firstBody.Id);
        var stored = await Host().ActiveBooking(lesson.ClassId, lesson.UserId);
        stored.Count.ShouldBe(1);
        stored.RemainingVisits.ShouldBe(visits - seats);
    }

    [TestMethod]
    public async Task Create_WhenTheSameKeyIsSentTwiceAtOnce_ReturnsTheFirstEnrollment()
    {
        // Arrange
        var seats = 1;
        var visits = 1;
        var key = Guid.NewGuid().ToString("N");
        var lesson = await Host().Lesson().Capacity(1).StartsAt(DateTime.UtcNow.AddDays(4)).Pass(18, visits).Create();
        var user = Host().User(18);
        await user.GetEnrollment(1);

        // Act
        var responses = await Task.WhenAll(user.Enroll(lesson, seats, key), user.Enroll(lesson, seats, key));

        // Assert
        responses[0].StatusCode.ShouldBe(HttpStatusCode.Created);
        responses[1].StatusCode.ShouldBe(HttpStatusCode.Created);
        var firstBody = await responses[0].ReadBody<CreateEnrollmentResponse>();
        var secondBody = await responses[1].ReadBody<CreateEnrollmentResponse>();
        secondBody.Id.ShouldBe(firstBody.Id);
        var stored = await Host().ActiveBooking(lesson.ClassId, lesson.UserId);
        stored.Count.ShouldBe(1);
        stored.RemainingVisits.ShouldBe(visits - seats);
    }

    [TestMethod]
    public async Task Create_WithSameKeyAndDifferentBody_ReturnsIdempotencyMismatch()
    {
        // Arrange
        var firstSeats = 1;
        var visits = 5;
        var key = Guid.NewGuid().ToString("N");
        var lesson = await Host().Lesson().Capacity(6).StartsAt(DateTime.UtcNow.AddDays(3)).Pass(3, visits).Create();
        var user = Host().User(3);
        await user.Enroll(lesson, firstSeats, key);

        // Act
        var response = await user.Enroll(lesson, 2, key);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.IdempotencyMismatch);
        var stored = await Host().ActiveBooking(lesson.ClassId, lesson.UserId);
        stored.Count.ShouldBe(1);
        stored.RemainingVisits.ShouldBe(visits - firstSeats);
    }

    [TestMethod]
    public async Task Create_WithoutIdempotencyKey_ReturnsBadRequest()
    {
        // Act
        var response = await Host().User(4).EnrollWithoutKey(1, 1);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task Create_WithBlankIdempotencyKey_ReturnsBadRequest()
    {
        // Act
        var response = await Host().User(5).Enroll(1, 1, "   ");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task Create_WithSeatsBelowOne_ReturnsBadRequest()
    {
        // Act
        var response = await Host().User(6).Enroll(1, 0);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task Create_WhenClassDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        var missingClassId = long.MaxValue;
        var zeroClassId = 0L;
        var seats = 1;
        var user = Host().User(7);

        // Act
        var missing = await user.Enroll(missingClassId, seats);
        var zero = await user.Enroll(zeroClassId, seats);

        // Assert
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        zero.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task Create_WhenClassIsCancelled_ReturnsClassCancelled()
    {
        // Arrange
        var lesson = await Host().Lesson().Capacity(8).StartsAt(DateTime.UtcNow.AddHours(-1)).Cancelled().Pass(8, 5).Create();

        // Act
        var response = await Host().User(8).Enroll(lesson, 1);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.ClassCancelled);
    }

    [TestMethod]
    public async Task Create_WhenClassHasStarted_ReturnsClassStarted()
    {
        // Arrange
        var lesson = await Host().Lesson().Capacity(8).StartsAt(DateTime.UtcNow.AddMinutes(-5)).Pass(9, 5).Create();

        // Act
        var response = await Host().User(9).Enroll(lesson, 1);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.ClassStarted);
    }

    [TestMethod]
    public async Task Create_WhenPassIsExpired_ReturnsPassExpired()
    {
        // Arrange
        var lesson = await Host().Lesson()
            .Capacity(8)
            .StartsAt(DateTime.UtcNow.AddDays(2))
            .Pass(10, 5, ClubTime.Today().AddDays(-1))
            .Create();

        // Act
        var response = await Host().User(10).Enroll(lesson, 1);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.PassExpired);
    }

    [TestMethod]
    public async Task Create_WhenPassHasTooFewVisits_ReturnsPassExhausted()
    {
        // Arrange
        var visits = 1;
        var lesson = await Host().Lesson().Capacity(8).StartsAt(DateTime.UtcNow.AddDays(2)).Pass(11, visits).Create();

        // Act
        var response = await Host().User(11).Enroll(lesson, 2);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.PassExhausted);
        var stored = await Host().ActiveBooking(lesson.ClassId, lesson.UserId);
        stored.Count.ShouldBe(0);
        stored.RemainingVisits.ShouldBe(visits);
    }

    [TestMethod]
    public async Task Create_WhenClassHasTooFewSeats_ReturnsCapacityExceeded()
    {
        // Arrange
        var visits = 10;
        var lesson = await Host().Lesson().Capacity(2).StartsAt(DateTime.UtcNow.AddDays(2)).Pass(12, visits).Create();

        // Act
        var response = await Host().User(12).Enroll(lesson, 3);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.CapacityExceeded);
        var stored = await Host().ActiveBooking(lesson.ClassId, lesson.UserId);
        stored.Count.ShouldBe(0);
        stored.RemainingVisits.ShouldBe(visits);
    }

    [TestMethod]
    public async Task Create_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        var response = await Host().Anonymous().Enroll(1, 1);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task Create_WhenMinuteLimitIsExceeded_ReturnsTooManyRequests()
    {
        // Arrange
        var user = Host().User(13);

        // Act
        var lastStatus = await user.Hammer(requestNumber => user.Enroll(1, 0, $"limit-13-{requestNumber}"));

        // Assert
        lastStatus.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [TestMethod]
    public async Task Create_WhenFreeSeatsFitBehindAnOversizedRequest_Books()
    {
        // Arrange
        var visits = 6;
        var heldSeats = 2;
        var headSeats = 3;
        var requestedSeats = 1;
        var validUntil = ClubTime.Today().AddDays(30);
        var holder = await Host().RegisterMember(heldSeats, validUntil);
        var lesson = await Host().Lesson().Capacity(4).StartsAt(DateTime.UtcNow.AddDays(2)).Pass(14, visits, validUntil).Create();
        await Host().User(holder.Number).Book(lesson, heldSeats);
        await Host().SetPass(15, visits, validUntil);
        var headId = await Host().AddWaiting(15, lesson.ClassId, headSeats, DateTime.UtcNow.AddMinutes(-5));

        // Act
        var response = await Host().User(14).Enroll(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.ReadBody<CreateEnrollmentResponse>();
        body.Seats.ShouldBe(requestedSeats);
        body.Status.ShouldBe(EnrollmentStatus.Active);
        var stored = await Host().ActiveBooking(lesson.ClassId, lesson.UserId);
        stored.Count.ShouldBe(1);
        stored.RemainingVisits.ShouldBe(visits - requestedSeats);
        (await Host().SumActiveSeats(lesson.ClassId)).ShouldBe(heldSeats + requestedSeats);
        (await Host().WaitlistStatusOf(headId)).ShouldBe(WaitlistStatus.Waiting);
    }

    [TestMethod]
    public async Task Create_WhenTheQueueIsNotEmptyAndSeatsDoNotFit_ReturnsCapacityExceeded()
    {
        // Arrange
        var visits = 6;
        var lesson = await Host().Lesson().Capacity(2).StartsAt(DateTime.UtcNow.AddDays(2)).Pass(16, visits).Create();
        await Host().AddWaiting(17, lesson.ClassId, 1, DateTime.UtcNow.AddMinutes(-5));

        // Act
        var response = await Host().User(16).Enroll(lesson, 3);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.CapacityExceeded);
        var stored = await Host().ActiveBooking(lesson.ClassId, lesson.UserId);
        stored.Count.ShouldBe(0);
        stored.RemainingVisits.ShouldBe(visits);
    }

    [TestMethod]
    public async Task Create_WhenTheBodyIsUnreadableOrClassIdIsMissing_ReturnsBadRequest()
    {
        // Arrange
        var member = await Host().RegisterMember(4, ClubTime.Today().AddDays(30));
        var user = Host().User(member.Number);
        var missingKey = Guid.NewGuid().ToString("N");
        var brokenKey = Guid.NewGuid().ToString("N");
        var missingClassKey = Guid.NewGuid().ToString("N");
        var brokenJson = "{";
        var missingClass = """{"seats":1}""";

        // Act
        var missingBody = await user.Post("/enrollments", null, missingKey);
        var brokenBody = await user.Post("/enrollments", brokenJson, brokenKey);
        var missingClassBody = await user.Post("/enrollments", missingClass, missingClassKey);

        // Assert
        missingBody.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        brokenBody.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        missingClassBody.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task Create_WhenTheWaitingHeadCannotPay_SkipsThatRequestAndBooks()
    {
        // Arrange
        var requestedSeats = 1;
        var bookerVisits = 2;
        var waiterVisits = 0;
        var validUntil = ClubTime.Today().AddDays(30);
        var waiter = await Host().RegisterMember(waiterVisits, validUntil);
        var booker = await Host().RegisterMember(bookerVisits, validUntil);
        var lesson = await Host().Lesson().Capacity(1).StartsAt(DateTime.UtcNow.AddDays(3)).Pass(booker.Number, bookerVisits, validUntil).Create();
        var headId = await Host().AddWaiting(waiter.Number, lesson.ClassId, requestedSeats, DateTime.UtcNow.AddMinutes(-5));

        // Act
        var response = await booker.Enroll(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Host().WaitlistStatusOf(headId)).ShouldBe(WaitlistStatus.Skipped);
        (await Host().ActiveSeats(lesson.UserId, lesson.ClassId)).ShouldBe(requestedSeats);
        (await Host().RemainingVisits(lesson.UserId)).ShouldBe(bookerVisits - requestedSeats);
        (await Host().ActiveCount(await Host().UserId(waiter.Number), lesson.ClassId)).ShouldBe(0);
        (await Host().RemainingVisits(await Host().UserId(waiter.Number))).ShouldBe(waiterVisits);
    }

    [TestMethod]
    public async Task Create_WhenTheWaitingHeadFits_RejectsTheNewcomerWithoutPromoting()
    {
        // Arrange
        var requestedSeats = 1;
        var visits = 2;
        var validUntil = ClubTime.Today().AddDays(30);
        var waiter = await Host().RegisterMember(visits, validUntil);
        var booker = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson().Capacity(1).StartsAt(DateTime.UtcNow.AddDays(3)).Pass(booker.Number, visits, validUntil).Create();
        var headId = await Host().AddWaiting(waiter.Number, lesson.ClassId, requestedSeats, DateTime.UtcNow.AddMinutes(-5));

        // Act
        var response = await booker.Enroll(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.CapacityExceeded);
        (await Host().WaitlistStatusOf(headId)).ShouldBe(WaitlistStatus.Waiting);
        var waiterUserId = await Host().UserId(waiter.Number);
        (await Host().ActiveCount(waiterUserId, lesson.ClassId)).ShouldBe(0);
        (await Host().RemainingVisits(waiterUserId)).ShouldBe(visits);
        (await Host().ActiveCount(lesson.UserId, lesson.ClassId)).ShouldBe(0);
        (await Host().RemainingVisits(lesson.UserId)).ShouldBe(visits);
    }

    [TestMethod]
    public async Task Create_WhenTheCallersOwnRequestWouldBePromoted_LeavesTheQueueUntouched()
    {
        // Arrange
        var requestedSeats = 1;
        var waitingSeats = 2;
        var visits = 2;
        var validUntil = ClubTime.Today().AddDays(30);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(3)
            .StartsAt(DateTime.UtcNow.AddDays(4))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var entryId = await Host().AddWaiting(member.Number, lesson.ClassId, waitingSeats, DateTime.UtcNow.AddMinutes(-5));

        // Act
        var response = await member.Enroll(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.PassExhausted);
        (await Host().WaitlistStatusOf(entryId)).ShouldBe(WaitlistStatus.Waiting);
        (await Host().ActiveCount(lesson.UserId, lesson.ClassId)).ShouldBe(0);
        (await Host().RemainingVisits(lesson.UserId)).ShouldBe(visits);
    }

    [TestMethod]
    public async Task Create_WhenTheOversizedHeadCannotPay_SkipsThatRequestAndBooksTheNext()
    {
        // Arrange
        var heldSeats = 2;
        var headSeats = 3;
        var nextSeats = 1;
        var requestedSeats = 1;
        var bookerVisits = 2;
        var nextVisits = 2;
        var headVisits = 4;
        var validUntil = ClubTime.Today().AddDays(30);
        var expired = ClubTime.Today().AddDays(-1);
        var holder = await Host().RegisterMember(heldSeats, validUntil);
        var head = await Host().RegisterMember(headVisits, expired);
        var next = await Host().RegisterMember(nextVisits, validUntil);
        var booker = await Host().RegisterMember(bookerVisits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(4)
            .StartsAt(DateTime.UtcNow.AddDays(4))
            .Pass(booker.Number, bookerVisits, validUntil)
            .Create();
        await Host().User(holder.Number).Book(lesson, heldSeats);
        var headId = await Host().AddWaiting(head.Number, lesson.ClassId, headSeats, DateTime.UtcNow.AddMinutes(-10));
        var nextId = await Host().AddWaiting(next.Number, lesson.ClassId, nextSeats, DateTime.UtcNow.AddMinutes(-5));

        // Act
        var response = await booker.Enroll(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Host().WaitlistStatusOf(headId)).ShouldBe(WaitlistStatus.Skipped);
        (await Host().WaitlistStatusOf(nextId)).ShouldBe(WaitlistStatus.Promoted);
        (await Host().ActiveSeats(await Host().UserId(next.Number), lesson.ClassId)).ShouldBe(nextSeats);
        (await Host().ActiveSeats(lesson.UserId, lesson.ClassId)).ShouldBe(requestedSeats);
        (await Host().RemainingVisits(lesson.UserId)).ShouldBe(bookerVisits - requestedSeats);
    }

    [TestMethod]
    public async Task Create_WhenAnExistingBookingUsesTheFrameworkStatus_CountsItsSeats()
    {
        // Arrange
        var takenSeats = 1;
        var requestedSeats = 1;
        var visits = 4;
        var storedStatus = "active";
        var validUntil = ClubTime.Today().AddDays(30);
        var holder = await Host().RegisterMember(visits, validUntil);
        var booker = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(takenSeats)
            .StartsAt(DateTime.UtcNow.AddDays(2))
            .Pass(booker.Number, visits, validUntil)
            .Create();
        var holderId = await Host().UserId(holder.Number);
        await Host().InsertEnrollmentRow(holderId, lesson.ClassId, takenSeats, storedStatus);

        // Act
        var response = await booker.Enroll(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.CapacityExceeded);
        (await Host().SumActiveSeats(lesson.ClassId)).ShouldBe(takenSeats);
        (await Host().RemainingVisits(await Host().UserId(booker.Number))).ShouldBe(visits);
    }

    [TestMethod]
    public async Task Create_StoresTheEnrollmentStatusInTheFrameworkFormat()
    {
        // Arrange
        var seats = 1;
        var visits = 2;
        var frameworkStatus = "active";
        var member = await Host().RegisterMember(visits, ClubTime.Today().AddDays(30));
        var lesson = await Host().Lesson()
            .Capacity(2)
            .StartsAt(DateTime.UtcNow.AddDays(2))
            .Pass(member.Number, visits)
            .Create();

        // Act
        var created = await member.Book(lesson, seats);

        // Assert
        (await Host().EnrollmentStatusText(created.Id)).ShouldBe(frameworkStatus);
    }
}
