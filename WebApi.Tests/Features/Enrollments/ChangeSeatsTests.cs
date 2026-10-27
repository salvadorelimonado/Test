using System.Net;
using System.Text.Json;
using DataAccessLayer.Entities;
using BusinessLayer.Result;
using Shouldly;
using WebApi.Features.Enrollments;
using WebApi.Tests.Infrastructure.DTO;
using WebApi.Tests.Infrastructure;
using WebApi.Tests.Infrastructure.Http;
using WebApi.Tests.Infrastructure.Time;

namespace WebApi.Tests.Features.Enrollments;

[TestClass]
public class ChangeSeatsTests : HostTest
{
    private const long MissingEnrollmentId = 9_000_000_000;

    [TestMethod]
    public async Task ChangeSeats_WhenIncreasing_ReturnsCardAndSpendsTheDifference()
    {
        // Arrange
        var bookedSeats = 2;
        var requestedSeats = 4;
        var visits = 10;
        var capacity = 8;
        var leadDays = 2;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var startsAt = Session.Now.AddDays(leadDays).WholeSecond();
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(startsAt)
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, bookedSeats);

        // Act
        var response = await member.Enrollments.ChangeSeats(created.Id, requestedSeats);

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
        var stored = await Enrollments.Enrollment(created.Id, lesson.UserId);
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
        var capacity = 2;
        var leadDays = 2;
        var expiredAgo = 1;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var expiredOn = Session.Today.AddDays(-expiredAgo);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays).WholeSecond())
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, bookedSeats);
        await Passes.SetValidUntil(lesson.UserId, expiredOn);

        // Act
        var response = await member.Enrollments.ChangeSeats(created.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var card = await response.ReadBody<EnrollmentCard>();
        card.Seats.ShouldBe(requestedSeats);
        card.Pass.RemainingVisits.ShouldBe(visits - requestedSeats);
        card.Pass.ValidUntil.ShouldBe(expiredOn);
        var stored = await Enrollments.Enrollment(created.Id, lesson.UserId);
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
        var capacity = 3;
        var leadDays = 2;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var owner = await Users.RegisterMember(ownerVisits, validUntil);
        var other = await Users.RegisterMember(otherVisits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays).WholeSecond())
            .Pass(owner.Number, ownerVisits, validUntil)
            .Create();
        await Passes.SetPass(other.Number, otherVisits, validUntil);
        var ownerEnrollment = await owner.Enrollments.Book(lesson, ownerSeats);
        var otherEnrollment = await other.Enrollments.Book(lesson, otherSeats);

        // Act
        var response = await owner.Enrollments.ChangeSeats(ownerEnrollment.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var card = await response.ReadBody<EnrollmentCard>();
        card.Seats.ShouldBe(requestedSeats);
        card.Pass.RemainingVisits.ShouldBe(ownerVisits - requestedSeats);
        var ownerStored = await Enrollments.Enrollment(ownerEnrollment.Id, lesson.UserId);
        ownerStored.Seats.ShouldBe(requestedSeats);
        ownerStored.RemainingVisits.ShouldBe(ownerVisits - requestedSeats);
        var otherUserId = await Users.UserId(other.Number);
        var otherStored = await Enrollments.Enrollment(otherEnrollment.Id, otherUserId);
        otherStored.Seats.ShouldBe(otherSeats);
        otherStored.RemainingVisits.ShouldBe(otherVisits - otherSeats);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenSeatsStayTheSame_ReturnsCardWithoutChangingVisits()
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
            .StartsAt(Session.Now.AddDays(leadDays).WholeSecond())
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, seats);

        // Act
        var response = await member.Enrollments.ChangeSeats(created.Id, seats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var card = await response.ReadBody<EnrollmentCard>();
        card.Seats.ShouldBe(seats);
        card.Pass.RemainingVisits.ShouldBe(visits - seats);
        var stored = await Enrollments.Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(seats);
        stored.RemainingVisits.ShouldBe(visits - seats);
    }

    [TestMethod]
    public async Task ChangeSeats_WithSeatsBelowOne_ReturnsBadRequest()
    {
        // Arrange
        var seats = 0;
        var visits = 10;
        var passValidityDays = 30;
        var member = await Users.RegisterMember(visits, Session.Today.AddDays(passValidityDays));

        // Act
        var response = await member.Enrollments.ChangeSeats(MissingEnrollmentId, seats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenEnrollmentIsMissing_ReturnsNotFound()
    {
        // Arrange
        var seats = 1;
        var visits = 10;
        var passValidityDays = 30;
        var member = await Users.RegisterMember(visits, Session.Today.AddDays(passValidityDays));

        // Act
        var response = await member.Enrollments.ChangeSeats(MissingEnrollmentId, seats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenEnrollmentBelongsToAnotherUser_ReturnsNotFound()
    {
        // Arrange
        var seats = 2;
        var requestedSeats = 1;
        var visits = 10;
        var capacity = 8;
        var leadDays = 2;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var owner = await Users.RegisterMember(visits, validUntil);
        var other = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays).WholeSecond())
            .Pass(owner.Number, visits, validUntil)
            .Create();
        var created = await owner.Enrollments.Book(lesson, seats);

        // Act
        var response = await other.Enrollments.ChangeSeats(created.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var stored = await Enrollments.Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(seats);
        stored.RemainingVisits.ShouldBe(visits - seats);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenEnrollmentIsCancelled_ReturnsEnrollmentCancelled()
    {
        // Arrange
        var seats = 2;
        var requestedSeats = 1;
        var visits = 10;
        var capacity = 8;
        var leadDays = 2;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays).WholeSecond())
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, seats);
        await Enrollments.SetEnrollmentStatus(created.Id, EnrollmentStatus.Cancelled);

        // Act
        var response = await member.Enrollments.ChangeSeats(created.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.EnrollmentCancelled);
        var stored = await Enrollments.Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(seats);
        stored.Status.ShouldBe(EnrollmentStatus.Cancelled);
        stored.RemainingVisits.ShouldBe(visits - seats);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenClassIsCancelled_ReturnsClassCancelled()
    {
        // Arrange
        var seats = 2;
        var requestedSeats = 1;
        var visits = 10;
        var capacity = 8;
        var leadDays = 2;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays).WholeSecond())
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, seats);
        await Classes.CancelClass(lesson.ClassId);

        // Act
        var response = await member.Enrollments.ChangeSeats(created.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.ClassCancelled);
        var stored = await Enrollments.Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(seats);
        stored.RemainingVisits.ShouldBe(visits - seats);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenClassHasStarted_ReturnsClassStarted()
    {
        // Arrange
        var seats = 2;
        var requestedSeats = 3;
        var visits = 10;
        var capacity = 8;
        var leadDays = 2;
        var startedAgo = 1;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays).WholeSecond())
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, seats);
        await Classes.SetStartsAt(lesson.ClassId, Session.Now.AddHours(-startedAgo).WholeSecond());

        // Act
        var response = await member.Enrollments.ChangeSeats(created.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.ClassStarted);
        var stored = await Enrollments.Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(seats);
        stored.RemainingVisits.ShouldBe(visits - seats);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenSeatsStayTheSameAfterStart_ReturnsClassStarted()
    {
        // Arrange
        var seats = 2;
        var visits = 10;
        var capacity = 8;
        var leadDays = 2;
        var startedAgo = 1;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays).WholeSecond())
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, seats);
        await Classes.SetStartsAt(lesson.ClassId, Session.Now.AddHours(-startedAgo).WholeSecond());

        // Act
        var response = await member.Enrollments.ChangeSeats(created.Id, seats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.ClassStarted);
        var stored = await Enrollments.Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(seats);
        stored.RemainingVisits.ShouldBe(visits - seats);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenIncreaseAndPassExpired_ReturnsPassExpired()
    {
        // Arrange
        var bookedSeats = 2;
        var requestedSeats = 3;
        var visits = 2;
        var capacity = 2;
        var leadDays = 2;
        var expiredAgo = 1;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays).WholeSecond())
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, bookedSeats);
        await Passes.SetValidUntil(lesson.UserId, Session.Today.AddDays(-expiredAgo));

        // Act
        var response = await member.Enrollments.ChangeSeats(created.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.PassExpired);
        var stored = await Enrollments.Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(bookedSeats);
        stored.RemainingVisits.ShouldBe(visits - bookedSeats);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenIncreaseAndVisitsAreShort_ReturnsPassExhausted()
    {
        // Arrange
        var bookedSeats = 2;
        var requestedSeats = 3;
        var visits = 2;
        var capacity = 10;
        var leadDays = 2;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays).WholeSecond())
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, bookedSeats);

        // Act
        var response = await member.Enrollments.ChangeSeats(created.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.PassExhausted);
        var stored = await Enrollments.Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(bookedSeats);
        stored.RemainingVisits.ShouldBe(visits - bookedSeats);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenIncreaseAndClassIsFull_ReturnsCapacityExceeded()
    {
        // Arrange
        var bookedSeats = 2;
        var requestedSeats = 3;
        var visits = 10;
        var capacity = 2;
        var leadDays = 2;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays).WholeSecond())
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, bookedSeats);

        // Act
        var response = await member.Enrollments.ChangeSeats(created.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.CapacityExceeded);
        var stored = await Enrollments.Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(bookedSeats);
        stored.RemainingVisits.ShouldBe(visits - bookedSeats);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenCancelledEnrollmentIsOnCancelledStartedClass_ReturnsEnrollmentCancelled()
    {
        // Arrange
        var seats = 2;
        var requestedSeats = 1;
        var visits = 10;
        var capacity = 8;
        var leadDays = 2;
        var startedAgo = 1;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays).WholeSecond())
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, seats);
        await Enrollments.SetEnrollmentStatus(created.Id, EnrollmentStatus.Cancelled);
        await Classes.CancelClass(lesson.ClassId);
        await Classes.SetStartsAt(lesson.ClassId, Session.Now.AddHours(-startedAgo).WholeSecond());

        // Act
        var response = await member.Enrollments.ChangeSeats(created.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.EnrollmentCancelled);
    }

    [TestMethod]
    public async Task ChangeSeats_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange
        var seats = 2;
        var requestedSeats = 1;
        var visits = 10;
        var capacity = 8;
        var leadDays = 2;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays).WholeSecond())
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, seats);

        // Act
        var response = await Users.Anonymous().Enrollments.ChangeSeats(created.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var stored = await Enrollments.Enrollment(created.Id, lesson.UserId);
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
        var capacity = 3;
        var leadDays = 2;
        var queuedAgo = 5;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var owner = await Users.RegisterMember(ownerVisits, validUntil);
        var waiter = await Users.RegisterMember(waiterVisits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays).WholeSecond())
            .Pass(owner.Number, ownerVisits, validUntil)
            .Create();
        await Passes.SetPass(waiter.Number, waiterVisits, validUntil);
        var created = await owner.Enrollments.Book(lesson, bookedSeats);
        var waiterId = await Waitlist.AddWaiting(waiter.Number, lesson.ClassId, waiterSeats, Session.Now.AddMinutes(-queuedAgo));

        // Act
        var response = await owner.Enrollments.ChangeSeats(created.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var card = await response.ReadBody<EnrollmentCard>();
        card.Seats.ShouldBe(requestedSeats);
        card.Pass.RemainingVisits.ShouldBe(ownerVisits - requestedSeats);
        (await Waitlist.WaitlistStatusOf(waiterId)).ShouldBe(WaitlistStatus.Promoted);
        var waiterUserId = await Users.UserId(waiter.Number);
        (await Enrollments.ActiveSeats(waiterUserId, lesson.ClassId)).ShouldBe(waiterSeats);
        (await Passes.RemainingVisits(waiterUserId)).ShouldBe(waiterVisits - waiterSeats);
        (await Enrollments.SumActiveSeats(lesson.ClassId)).ShouldBe(requestedSeats + waiterSeats);
        var leave = await waiter.Waitlist.LeaveWaitlist(waiterId);
        leave.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await using var stream = await leave.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        document.RootElement.GetProperty("reason").Deserialize<ConflictReason>(HttpResponseMessageExtensions.Json).ShouldBe(ConflictReason.AlreadyPromoted);
        var promotedEnrollmentId = document.RootElement.GetProperty("promotedEnrollmentId").GetInt64();
        var waiterCard = await waiter.Enrollments.GetEnrollment(promotedEnrollmentId);
        waiterCard.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await waiterCard.ReadBody<EnrollmentCard>()).Id.ShouldBe(promotedEnrollmentId);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenTheIdIsNotNumeric_ReturnsBadRequest()
    {
        // Arrange
        var seats = 1;
        var visits = 10;
        var passValidityDays = 30;
        var member = await Users.RegisterMember(visits, Session.Today.AddDays(passValidityDays));

        // Act
        var response = await member.Enrollments.ChangeSeats("abc", seats);

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
        var capacity = 4;
        var leadDays = 2;
        var queuedAgo = 5;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var owner = await Users.RegisterMember(visits, validUntil);
        var head = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays).WholeSecond())
            .Pass(owner.Number, visits, validUntil)
            .Create();
        var created = await owner.Enrollments.Book(lesson, bookedSeats);
        await Passes.SetPass(head.Number, visits, validUntil);
        var headId = await Waitlist.AddWaiting(head.Number, lesson.ClassId, headSeats, Session.Now.AddMinutes(-queuedAgo));

        // Act
        var response = await owner.Enrollments.ChangeSeats(created.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stored = await Enrollments.Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(requestedSeats);
        stored.RemainingVisits.ShouldBe(visits - requestedSeats);
        (await Enrollments.SumActiveSeats(lesson.ClassId)).ShouldBe(requestedSeats);
        (await Waitlist.WaitlistStatusOf(headId)).ShouldBe(WaitlistStatus.Waiting);
    }

    [TestMethod]
    public async Task ChangeSeats_WhenIncreasingPastCapacityAndTheQueueIsNotEmpty_ReturnsCapacityExceeded()
    {
        // Arrange
        var bookedSeats = 2;
        var requestedSeats = 3;
        var waiterSeats = 1;
        var visits = 6;
        var capacity = 2;
        var leadDays = 2;
        var queuedAgo = 5;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var owner = await Users.RegisterMember(visits, validUntil);
        var waiter = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays).WholeSecond())
            .Pass(owner.Number, visits, validUntil)
            .Create();
        var created = await owner.Enrollments.Book(lesson, bookedSeats);
        await Waitlist.AddWaiting(waiter.Number, lesson.ClassId, waiterSeats, Session.Now.AddMinutes(-queuedAgo));

        // Act
        var response = await owner.Enrollments.ChangeSeats(created.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.CapacityExceeded);
        var stored = await Enrollments.Enrollment(created.Id, lesson.UserId);
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
        var leadDays = 3;
        var queuedAgo = 5;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var owner = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddDays(leadDays).WholeSecond())
            .Pass(owner.Number, visits, validUntil)
            .Create();
        var created = await owner.Enrollments.Book(lesson, bookedSeats);
        var ownEntryId = await Waitlist.AddWaiting(
            owner.Number,
            lesson.ClassId,
            ownRequestSeats,
            Session.Now.AddMinutes(-queuedAgo));

        // Act
        var response = await owner.Enrollments.ChangeSeats(created.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stored = await Enrollments.Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(requestedSeats);
        stored.RemainingVisits.ShouldBe(visits - requestedSeats);
        (await Waitlist.WaitlistStatusOf(ownEntryId)).ShouldBe(WaitlistStatus.Waiting);
        (await Enrollments.SumActiveSeats(lesson.ClassId)).ShouldBe(requestedSeats);
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
        var activeEnrollments = 1;
        var leadDays = 2;
        var ownQueuedAgo = 10;
        var nextQueuedAgo = 5;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var owner = await Users.RegisterMember(ownerVisits, validUntil);
        var next = await Users.RegisterMember(nextVisits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(bookedSeats)
            .StartsAt(Session.Now.AddDays(leadDays).WholeSecond())
            .Pass(owner.Number, ownerVisits, validUntil)
            .Create();
        var created = await owner.Enrollments.Book(lesson, bookedSeats);
        var ownEntryId = await Waitlist.AddWaiting(owner.Number, lesson.ClassId, ownRequestSeats, Session.Now.AddMinutes(-ownQueuedAgo));
        var nextEntryId = await Waitlist.AddWaiting(next.Number, lesson.ClassId, nextRequestSeats, Session.Now.AddMinutes(-nextQueuedAgo));

        // Act
        var response = await owner.Enrollments.ChangeSeats(created.Id, requestedSeats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var card = await response.ReadBody<EnrollmentCard>();
        card.Seats.ShouldBe(requestedSeats);
        card.Pass.RemainingVisits.ShouldBe(ownerVisits - requestedSeats);
        var ownerId = await Users.UserId(owner.Number);
        (await Waitlist.WaitlistStatusOf(ownEntryId)).ShouldBe(WaitlistStatus.Cancelled);
        (await Enrollments.ActiveCount(ownerId, lesson.ClassId)).ShouldBe(activeEnrollments);
        (await Enrollments.ActiveSeats(ownerId, lesson.ClassId)).ShouldBe(requestedSeats);
        (await Passes.RemainingVisits(ownerId)).ShouldBe(ownerVisits - requestedSeats);
        (await Waitlist.WaitlistStatusOf(nextEntryId)).ShouldBe(WaitlistStatus.Promoted);
        var nextUserId = await Users.UserId(next.Number);
        (await Enrollments.ActiveSeats(nextUserId, lesson.ClassId)).ShouldBe(nextRequestSeats);
        (await Passes.RemainingVisits(nextUserId)).ShouldBe(nextVisits - nextRequestSeats);
    }
}
