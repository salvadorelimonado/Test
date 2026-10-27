using System.Net;
using BusinessLayer.Result;
using DataAccessLayer.Entities;
using WebApi.Features.Enrollments.Create;
using Shouldly;
using WebApi.Features.Enrollments;
using WebApi.Tests.Infrastructure.DTO;
using WebApi.Tests.Infrastructure;
using WebApi.Tests.Infrastructure.Http;
using WebApi.Tests.Infrastructure.Http.Clients;
using WebApi.Tests.Infrastructure.Time;

namespace WebApi.Tests.Features.Enrollments;

[TestClass]
public class CreateEnrollmentTests : HostTest
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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .Create();

        // Act
        var response = await member.Enrollments.Enroll(lesson, seats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.ReadBody<CreateEnrollmentResponse>();
        body.ClassId.ShouldBe(lesson.ClassId);
        body.Seats.ShouldBe(seats);
        body.Status.ShouldBe(EnrollmentStatus.Active);
        body.CreatedAt.ShouldNotBe(default);
        var stored = await Enrollments.ActiveBooking(lesson.ClassId, lesson.UserId);
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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var key = Guid.NewGuid().ToString("N");
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .Create();

        // Act
        var first = await member.Enrollments.Enroll(lesson, seats, key);
        var second = await member.Enrollments.Enroll(lesson, seats, key);

        // Assert
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        var firstBody = await first.ReadBody<CreateEnrollmentResponse>();
        var secondBody = await second.ReadBody<CreateEnrollmentResponse>();
        secondBody.Id.ShouldBe(firstBody.Id);
        var stored = await Enrollments.ActiveBooking(lesson.ClassId, lesson.UserId);
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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var key = Guid.NewGuid().ToString("N");
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .Create();
        await member.Enrollments.GetEnrollment(missingEnrollmentId);

        // Act
        var responses = await Task.WhenAll(member.Enrollments.Enroll(lesson, seats, key), member.Enrollments.Enroll(lesson, seats, key));

        // Assert
        responses[0].StatusCode.ShouldBe(HttpStatusCode.Created);
        responses[1].StatusCode.ShouldBe(HttpStatusCode.Created);
        var firstBody = await responses[0].ReadBody<CreateEnrollmentResponse>();
        var secondBody = await responses[1].ReadBody<CreateEnrollmentResponse>();
        secondBody.Id.ShouldBe(firstBody.Id);
        var stored = await Enrollments.ActiveBooking(lesson.ClassId, lesson.UserId);
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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var key = Guid.NewGuid().ToString("N");
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .Create();
        await member.Enrollments.Enroll(lesson, firstSeats, key);

        // Act
        var response = await member.Enrollments.Enroll(lesson, changedSeats, key);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.IdempotencyMismatch);
        var stored = await Enrollments.ActiveBooking(lesson.ClassId, lesson.UserId);
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
        var member = await Users.RegisterMember(visits, Session.Today.AddDays(passValidityDays));

        // Act
        var response = await member.Enrollments.EnrollWithoutKey(classId, seats);

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
        var member = await Users.RegisterMember(visits, Session.Today.AddDays(passValidityDays));

        // Act
        var response = await member.Enrollments.Enroll(classId, seats, blankKey);

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
        var member = await Users.RegisterMember(visits, Session.Today.AddDays(passValidityDays));

        // Act
        var response = await member.Enrollments.Enroll(classId, seats);

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
        var member = await Users.RegisterMember(visits, Session.Today.AddDays(passValidityDays));

        // Act
        var missing = await member.Enrollments.Enroll(missingClassId, seats);
        var zero = await member.Enrollments.Enroll(zeroClassId, seats);

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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now - alreadyStartedAgo)
            .Cancelled()
            .Pass(member.Number, visits, validUntil)
            .Create();

        // Act
        var response = await member.Enrollments.Enroll(lesson, seats);

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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now - alreadyStartedAgo)
            .Pass(member.Number, visits, validUntil)
            .Create();

        // Act
        var response = await member.Enrollments.Enroll(lesson, seats);

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
        var expired = Session.Today.AddDays(-expiredDaysAgo);
        var member = await Users.RegisterMember(visits, expired);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(member.Number, visits, expired)
            .Create();

        // Act
        var response = await member.Enrollments.Enroll(lesson, seats);

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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .Create();

        // Act
        var response = await member.Enrollments.Enroll(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.PassExhausted);
        var stored = await Enrollments.ActiveBooking(lesson.ClassId, lesson.UserId);
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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .Create();

        // Act
        var response = await member.Enrollments.Enroll(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.CapacityExceeded);
        var stored = await Enrollments.ActiveBooking(lesson.ClassId, lesson.UserId);
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
        var response = await Users.Anonymous().Enrollments.Enroll(classId, seats);

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
        var member = await Users.RegisterMember(visits, Session.Today.AddDays(passValidityDays));

        // Act
        var lastStatus = await AuthClient.Hammer(requestNumber => member.Enrollments.Enroll(classId, seats, $"limit-{member.Number}-{requestNumber}"));

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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var holder = await Users.RegisterMember(heldSeats, validUntil);
        var booker = await Users.RegisterMember(visits, validUntil);
        var head = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(booker.Number, visits, validUntil)
            .Create();
        await holder.Enrollments.Book(lesson, heldSeats);
        var headId = await Waitlist.AddWaiting(head.Number, lesson.ClassId, headSeats, Session.Now - queuedAgo);

        // Act
        var response = await booker.Enrollments.Enroll(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.ReadBody<CreateEnrollmentResponse>();
        body.Seats.ShouldBe(requestedSeats);
        body.Status.ShouldBe(EnrollmentStatus.Active);
        var stored = await Enrollments.ActiveBooking(lesson.ClassId, lesson.UserId);
        stored.Count.ShouldBe(1);
        stored.RemainingVisits.ShouldBe(visits - requestedSeats);
        (await Enrollments.SumActiveSeats(lesson.ClassId)).ShouldBe(heldSeats + requestedSeats);
        (await Waitlist.WaitlistStatusOf(headId)).ShouldBe(WaitlistStatus.Waiting);
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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var booker = await Users.RegisterMember(visits, validUntil);
        var waiter = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(booker.Number, visits, validUntil)
            .Create();
        await Waitlist.AddWaiting(waiter.Number, lesson.ClassId, waitingSeats, Session.Now - queuedAgo);

        // Act
        var response = await booker.Enrollments.Enroll(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.CapacityExceeded);
        var stored = await Enrollments.ActiveBooking(lesson.ClassId, lesson.UserId);
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
        var member = await Users.RegisterMember(visits, Session.Today.AddDays(passValidityDays));
        var missingKey = Guid.NewGuid().ToString("N");
        var brokenKey = Guid.NewGuid().ToString("N");
        var missingClassKey = Guid.NewGuid().ToString("N");
        var brokenJson = "{";
        var missingClass = $$"""{"seats":{{seats}}}""";

        // Act
        var missingBody = await member.Enrollments.Post(null, missingKey);
        var brokenBody = await member.Enrollments.Post(brokenJson, brokenKey);
        var missingClassBody = await member.Enrollments.Post(missingClass, missingClassKey);

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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var waiter = await Users.RegisterMember(waiterVisits, validUntil);
        var booker = await Users.RegisterMember(bookerVisits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(booker.Number, bookerVisits, validUntil)
            .Create();
        var headId = await Waitlist.AddWaiting(waiter.Number, lesson.ClassId, requestedSeats, Session.Now - queuedAgo);

        // Act
        var response = await booker.Enrollments.Enroll(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Waitlist.WaitlistStatusOf(headId)).ShouldBe(WaitlistStatus.Skipped);
        (await Enrollments.ActiveSeats(lesson.UserId, lesson.ClassId)).ShouldBe(requestedSeats);
        (await Passes.RemainingVisits(lesson.UserId)).ShouldBe(bookerVisits - requestedSeats);
        (await Enrollments.ActiveCount(await Users.UserId(waiter.Number), lesson.ClassId)).ShouldBe(0);
        (await Passes.RemainingVisits(await Users.UserId(waiter.Number))).ShouldBe(waiterVisits);
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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var waiter = await Users.RegisterMember(visits, validUntil);
        var booker = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(booker.Number, visits, validUntil)
            .Create();
        var headId = await Waitlist.AddWaiting(waiter.Number, lesson.ClassId, requestedSeats, Session.Now - queuedAgo);

        // Act
        var response = await booker.Enrollments.Enroll(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.CapacityExceeded);
        (await Waitlist.WaitlistStatusOf(headId)).ShouldBe(WaitlistStatus.Waiting);
        var waiterUserId = await Users.UserId(waiter.Number);
        (await Enrollments.ActiveCount(waiterUserId, lesson.ClassId)).ShouldBe(0);
        (await Passes.RemainingVisits(waiterUserId)).ShouldBe(visits);
        (await Enrollments.ActiveCount(lesson.UserId, lesson.ClassId)).ShouldBe(0);
        (await Passes.RemainingVisits(lesson.UserId)).ShouldBe(visits);
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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var entryId = await Waitlist.AddWaiting(member.Number, lesson.ClassId, waitingSeats, Session.Now - queuedAgo);

        // Act
        var response = await member.Enrollments.Enroll(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.ReadBody<CreateEnrollmentResponse>();
        body.Seats.ShouldBe(requestedSeats);
        body.Status.ShouldBe(EnrollmentStatus.Active);
        (await Waitlist.WaitlistStatusOf(entryId)).ShouldBe(WaitlistStatus.Waiting);
        (await Enrollments.ActiveSeats(lesson.UserId, lesson.ClassId)).ShouldBe(requestedSeats);
        (await Passes.RemainingVisits(lesson.UserId)).ShouldBe(visits - requestedSeats);
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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var expired = Session.Today.AddDays(-expiredDaysAgo);
        var holder = await Users.RegisterMember(heldSeats, validUntil);
        var head = await Users.RegisterMember(headVisits, expired);
        var next = await Users.RegisterMember(nextVisits, validUntil);
        var booker = await Users.RegisterMember(bookerVisits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(booker.Number, bookerVisits, validUntil)
            .Create();
        await holder.Enrollments.Book(lesson, heldSeats);
        var headId = await Waitlist.AddWaiting(head.Number, lesson.ClassId, headSeats, Session.Now - headQueuedAgo);
        var nextId = await Waitlist.AddWaiting(next.Number, lesson.ClassId, nextSeats, Session.Now - nextQueuedAgo);

        // Act
        var response = await booker.Enrollments.Enroll(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Waitlist.WaitlistStatusOf(headId)).ShouldBe(WaitlistStatus.Skipped);
        (await Waitlist.WaitlistStatusOf(nextId)).ShouldBe(WaitlistStatus.Promoted);
        (await Enrollments.ActiveSeats(await Users.UserId(next.Number), lesson.ClassId)).ShouldBe(nextSeats);
        (await Enrollments.ActiveSeats(lesson.UserId, lesson.ClassId)).ShouldBe(requestedSeats);
        (await Passes.RemainingVisits(lesson.UserId)).ShouldBe(bookerVisits - requestedSeats);
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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var holder = await Users.RegisterMember(visits, validUntil);
        var booker = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(takenSeats)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(booker.Number, visits, validUntil)
            .Create();
        var holderId = await Users.UserId(holder.Number);
        await Enrollments.InsertEnrollmentRow(holderId, lesson.ClassId, takenSeats, storedStatus);

        // Act
        var response = await booker.Enrollments.Enroll(lesson, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.CapacityExceeded);
        (await Enrollments.SumActiveSeats(lesson.ClassId)).ShouldBe(takenSeats);
        (await Passes.RemainingVisits(await Users.UserId(booker.Number))).ShouldBe(visits);
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
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays))
            .Pass(member.Number, visits, validUntil)
            .Create();

        // Act
        var created = await member.Enrollments.Book(lesson, seats);

        // Assert
        (await Enrollments.EnrollmentStatusText(created.Id)).ShouldBe(frameworkStatus);
    }
}
