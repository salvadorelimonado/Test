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
        var capacity = 8;
        var leadDays = 2;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .Create();

        // Act
        var response = await member.Enroll(lesson, seats);

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
        var capacity = 6;
        var leadDays = 3;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var key = Guid.NewGuid().ToString("N");
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .Create();

        // Act
        var first = await member.Enroll(lesson, seats, key);
        var second = await member.Enroll(lesson, seats, key);

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
        var capacity = 1;
        var leadDays = 4;
        var passValidityDays = 30;
        var missingEnrollmentId = 1L;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var key = Guid.NewGuid().ToString("N");
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .Create();
        await member.GetEnrollment(missingEnrollmentId);

        // Act
        var responses = await Task.WhenAll(member.Enroll(lesson, seats, key), member.Enroll(lesson, seats, key));

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
        var changedSeats = 2;
        var visits = 5;
        var capacity = 6;
        var leadDays = 3;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var key = Guid.NewGuid().ToString("N");
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .Create();
        await member.Enroll(lesson, firstSeats, key);

        // Act
        var response = await member.Enroll(lesson, changedSeats, key);

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
        // Arrange
        var classId = 1L;
        var seats = 1;
        var visits = 1;
        var passValidityDays = 30;
        var member = await Host().RegisterMember(visits, ClubTime.Today().AddDays(passValidityDays));

        // Act
        var response = await member.EnrollWithoutKey(classId, seats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task Create_WithBlankIdempotencyKey_ReturnsBadRequest()
    {
        // Arrange
        var classId = 1L;
        var seats = 1;
        var blankKey = "   ";
        var visits = 1;
        var passValidityDays = 30;
        var member = await Host().RegisterMember(visits, ClubTime.Today().AddDays(passValidityDays));

        // Act
        var response = await member.Enroll(classId, seats, blankKey);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task Create_WithSeatsBelowOne_ReturnsBadRequest()
    {
        // Arrange
        var classId = 1L;
        var seats = 0;
        var visits = 1;
        var passValidityDays = 30;
        var member = await Host().RegisterMember(visits, ClubTime.Today().AddDays(passValidityDays));

        // Act
        var response = await member.Enroll(classId, seats);

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
        var visits = 1;
        var passValidityDays = 30;
        var member = await Host().RegisterMember(visits, ClubTime.Today().AddDays(passValidityDays));

        // Act
        var missing = await member.Enroll(missingClassId, seats);
        var zero = await member.Enroll(zeroClassId, seats);

        // Assert
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        zero.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task Create_WhenClassIsCancelled_ReturnsClassCancelled()
    {
        // Arrange
        var capacity = 8;
        var visits = 5;
        var seats = 1;
        var alreadyStartedAgo = TimeSpan.FromHours(1);
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now - alreadyStartedAgo)
            .Cancelled()
            .Pass(member.Number, visits, validUntil)
            .Create();

        // Act
        var response = await member.Enroll(lesson, seats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.ClassCancelled);
    }

    [TestMethod]
    public async Task Create_WhenClassHasStarted_ReturnsClassStarted()
    {
        // Arrange
        var capacity = 8;
        var visits = 5;
        var seats = 1;
        var alreadyStartedAgo = TimeSpan.FromMinutes(5);
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now - alreadyStartedAgo)
            .Pass(member.Number, visits, validUntil)
            .Create();

        // Act
        var response = await member.Enroll(lesson, seats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.ClassStarted);
    }

    [TestMethod]
    public async Task Create_WhenPassIsExpired_ReturnsPassExpired()
    {
        // Arrange
        var capacity = 8;
        var visits = 5;
        var seats = 1;
        var leadDays = 2;
        var expiredDaysAgo = 1;
        var expired = ClubTime.Today().AddDays(-expiredDaysAgo);
        var member = await Host().RegisterMember(visits, expired);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(member.Number, visits, expired)
            .Create();

        // Act
        var response = await member.Enroll(lesson, seats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.PassExpired);
    }

    [TestMethod]
    public async Task Create_WhenPassHasTooFewVisits_ReturnsPassExhausted()
    {
        // Arrange
        var visits = 1;
        var requestedSeats = 2;
        var capacity = 8;
        var leadDays = 2;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .Create();

        // Act
        var response = await member.Enroll(lesson, requestedSeats);

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
        var capacity = 2;
        var requestedSeats = 3;
        var leadDays = 2;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .Create();

        // Act
        var response = await member.Enroll(lesson, requestedSeats);

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
        // Arrange
        var classId = 1L;
        var seats = 1;

        // Act
        var response = await Host().Anonymous().Enroll(classId, seats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task Create_WhenMinuteLimitIsExceeded_ReturnsTooManyRequests()
    {
        // Arrange
        var classId = 1L;
        var seats = 0;
        var visits = 1;
        var passValidityDays = 30;
        var member = await Host().RegisterMember(visits, ClubTime.Today().AddDays(passValidityDays));

        // Act
        var lastStatus = await member.Hammer(requestNumber => member.Enroll(classId, seats, $"limit-{member.Number}-{requestNumber}"));

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
        var capacity = 4;
        var leadDays = 2;
        var passValidityDays = 30;
        var queuedAgo = TimeSpan.FromMinutes(5);
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var holder = await Host().RegisterMember(heldSeats, validUntil);
        var booker = await Host().RegisterMember(visits, validUntil);
        var head = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(booker.Number, visits, validUntil)
            .Create();
        await holder.Book(lesson, heldSeats);
        var headId = await Host().AddWaiting(head.Number, lesson.ClassId, headSeats, Host().Now - queuedAgo);

        // Act
        var response = await booker.Enroll(lesson, requestedSeats);

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
        var capacity = 2;
        var waitingSeats = 1;
        var requestedSeats = 3;
        var leadDays = 2;
        var passValidityDays = 30;
        var queuedAgo = TimeSpan.FromMinutes(5);
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var booker = await Host().RegisterMember(visits, validUntil);
        var waiter = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(booker.Number, visits, validUntil)
            .Create();
        await Host().AddWaiting(waiter.Number, lesson.ClassId, waitingSeats, Host().Now - queuedAgo);

        // Act
        var response = await booker.Enroll(lesson, requestedSeats);

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
        var visits = 4;
        var seats = 1;
        var passValidityDays = 30;
        var member = await Host().RegisterMember(visits, ClubTime.Today().AddDays(passValidityDays));
        var missingKey = Guid.NewGuid().ToString("N");
        var brokenKey = Guid.NewGuid().ToString("N");
        var missingClassKey = Guid.NewGuid().ToString("N");
        var brokenJson = "{";
        var missingClass = $$"""{"seats":{{seats}}}""";

        // Act
        var missingBody = await member.Post("/enrollments", null, missingKey);
        var brokenBody = await member.Post("/enrollments", brokenJson, brokenKey);
        var missingClassBody = await member.Post("/enrollments", missingClass, missingClassKey);

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
        var capacity = 1;
        var leadDays = 3;
        var passValidityDays = 30;
        var queuedAgo = TimeSpan.FromMinutes(5);
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var waiter = await Host().RegisterMember(waiterVisits, validUntil);
        var booker = await Host().RegisterMember(bookerVisits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(booker.Number, bookerVisits, validUntil)
            .Create();
        var headId = await Host().AddWaiting(waiter.Number, lesson.ClassId, requestedSeats, Host().Now - queuedAgo);

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
        var capacity = 1;
        var leadDays = 3;
        var passValidityDays = 30;
        var queuedAgo = TimeSpan.FromMinutes(5);
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var waiter = await Host().RegisterMember(visits, validUntil);
        var booker = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(booker.Number, visits, validUntil)
            .Create();
        var headId = await Host().AddWaiting(waiter.Number, lesson.ClassId, requestedSeats, Host().Now - queuedAgo);

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
    public async Task Create_WhenTheCallerIsAlsoWaiting_BooksAndLeavesThatRequest()
    {
        // Arrange
        var requestedSeats = 1;
        var waitingSeats = 2;
        var visits = 2;
        var capacity = 3;
        var leadDays = 4;
        var passValidityDays = 30;
        var queuedAgo = TimeSpan.FromMinutes(5);
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var entryId = await Host().AddWaiting(member.Number, lesson.ClassId, waitingSeats, Host().Now - queuedAgo);

        // Act
        var response = await member.Enroll(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.ReadBody<CreateEnrollmentResponse>();
        body.Seats.ShouldBe(requestedSeats);
        body.Status.ShouldBe(EnrollmentStatus.Active);
        (await Host().WaitlistStatusOf(entryId)).ShouldBe(WaitlistStatus.Waiting);
        (await Host().ActiveSeats(lesson.UserId, lesson.ClassId)).ShouldBe(requestedSeats);
        (await Host().RemainingVisits(lesson.UserId)).ShouldBe(visits - requestedSeats);
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
        var capacity = 4;
        var leadDays = 4;
        var passValidityDays = 30;
        var expiredDaysAgo = 1;
        var headQueuedAgo = TimeSpan.FromMinutes(10);
        var nextQueuedAgo = TimeSpan.FromMinutes(5);
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var expired = ClubTime.Today().AddDays(-expiredDaysAgo);
        var holder = await Host().RegisterMember(heldSeats, validUntil);
        var head = await Host().RegisterMember(headVisits, expired);
        var next = await Host().RegisterMember(nextVisits, validUntil);
        var booker = await Host().RegisterMember(bookerVisits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(booker.Number, bookerVisits, validUntil)
            .Create();
        await holder.Book(lesson, heldSeats);
        var headId = await Host().AddWaiting(head.Number, lesson.ClassId, headSeats, Host().Now - headQueuedAgo);
        var nextId = await Host().AddWaiting(next.Number, lesson.ClassId, nextSeats, Host().Now - nextQueuedAgo);

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
        var leadDays = 2;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var holder = await Host().RegisterMember(visits, validUntil);
        var booker = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(takenSeats)
            .StartsAt(Host().Now.AddDays(leadDays))
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
        var capacity = 2;
        var leadDays = 2;
        var passValidityDays = 30;
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(Host().Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .Create();

        // Act
        var created = await member.Book(lesson, seats);

        // Assert
        (await Host().EnrollmentStatusText(created.Id)).ShouldBe(frameworkStatus);
    }
}
