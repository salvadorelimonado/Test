using System.Net;
using BusinessLayer.Result;
using DataAccessLayer.Entities;
using WebApi.Features.Enrollments.Cancel;
using Shouldly;
using WebApi.Tests.Infrastructure.DTO;
using WebApi.Tests.Infrastructure;
using WebApi.Tests.Infrastructure.Http;
using WebApi.Tests.Infrastructure.Time;

namespace WebApi.Tests.Features.Enrollments;

[TestClass]
public class CancelEnrollmentTests : HostTest
{
    private const long MissingEnrollmentId = 9_000_000_000;

    [TestMethod]
    public async Task Cancel_MoreThanTwoHoursBeforeStart_RefundsAllVisits()
    {
        // Arrange
        var seats = 2;
        var visits = 10;
        var capacity = 8;
        var leadHours = 3;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddHours(leadHours).WholeSecond())
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, seats);

        // Act
        var response = await member.Enrollments.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadBody<CancelEnrollmentResponse>();
        body.Id.ShouldBe(created.Id);
        body.Status.ShouldBe(EnrollmentStatus.Cancelled);
        body.RefundedVisits.ShouldBe(seats);
        var stored = await Enrollments.Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(seats);
        stored.Status.ShouldBe(EnrollmentStatus.Cancelled);
        stored.RefundedVisits.ShouldBe(seats);
        stored.RemainingVisits.ShouldBe(visits);
    }

    [TestMethod]
    public async Task Cancel_LessThanTwoHoursBeforeStart_FreesSeatsWithoutRefund()
    {
        // Arrange
        var seats = 2;
        var visits = 10;
        var capacity = 8;
        var leadMinutes = 30;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddMinutes(leadMinutes).WholeSecond())
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, seats);

        // Act
        var response = await member.Enrollments.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadBody<CancelEnrollmentResponse>();
        body.RefundedVisits.ShouldBe(0);
        body.Status.ShouldBe(EnrollmentStatus.Cancelled);
        var stored = await Enrollments.Enrollment(created.Id, lesson.UserId);
        stored.Seats.ShouldBe(seats);
        stored.Status.ShouldBe(EnrollmentStatus.Cancelled);
        stored.RefundedVisits.ShouldBe(0);
        stored.RemainingVisits.ShouldBe(visits - seats);
    }

    [TestMethod]
    public async Task Cancel_WhenClassHasStarted_ReturnsClassStarted()
    {
        // Arrange
        var seats = 2;
        var waiterSeats = 1;
        var visits = 10;
        var waiterVisits = 4;
        var capacity = 8;
        var leadHours = 3;
        var queuedAgo = 5;
        var alreadyStarted = 1;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var waiter = await Users.RegisterMember(waiterVisits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddHours(leadHours).WholeSecond())
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, seats);
        var waiterId = await Waitlist.AddWaiting(waiter.Number, lesson.ClassId, waiterSeats, Session.Now.AddMinutes(-queuedAgo));
        await Classes.SetStartsAt(lesson.ClassId, Session.Now.AddHours(-alreadyStarted).WholeSecond());

        // Act
        var response = await member.Enrollments.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.ClassStarted);
        var stored = await Enrollments.Enrollment(created.Id, lesson.UserId);
        stored.Status.ShouldBe(EnrollmentStatus.Active);
        stored.RemainingVisits.ShouldBe(visits - seats);
        (await Waitlist.WaitlistStatusOf(waiterId)).ShouldBe(WaitlistStatus.Waiting);
        (await Enrollments.ActiveCount(await Users.UserId(waiter.Number), lesson.ClassId)).ShouldBe(0);
    }

    [TestMethod]
    public async Task Cancel_WhenClubCancelledAfterStart_RefundsAllVisits()
    {
        // Arrange
        var seats = 2;
        var visits = 10;
        var capacity = 8;
        var alreadyStarted = 1;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddHours(-alreadyStarted).WholeSecond())
            .Cancelled()
            .Pass(member.Number, visits, validUntil)
            .Create();
        var enrollmentId = await Enrollments.InsertSpentEnrollment(lesson.UserId, lesson.ClassId, seats, visits);

        // Act
        var response = await member.Enrollments.CancelEnrollment(enrollmentId);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadBody<CancelEnrollmentResponse>();
        body.RefundedVisits.ShouldBe(seats);
        var stored = await Enrollments.Enrollment(enrollmentId, lesson.UserId);
        stored.Status.ShouldBe(EnrollmentStatus.Cancelled);
        stored.RefundedVisits.ShouldBe(seats);
        stored.RemainingVisits.ShouldBe(visits);
    }

    [TestMethod]
    public async Task Cancel_WhenClubCancelledInsideTwoHours_RefundsVisitsAndDoesNotPromote()
    {
        // Arrange
        var seats = 2;
        var waiterSeats = 1;
        var visits = 10;
        var waiterVisits = 4;
        var capacity = 8;
        var leadMinutes = 30;
        var queuedAgo = 5;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var waiter = await Users.RegisterMember(waiterVisits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddMinutes(leadMinutes).WholeSecond())
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, seats);
        var waiterId = await Waitlist.AddWaiting(waiter.Number, lesson.ClassId, waiterSeats, Session.Now.AddMinutes(-queuedAgo));
        await Classes.CancelClass(lesson.ClassId);

        // Act
        var response = await member.Enrollments.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadBody<CancelEnrollmentResponse>();
        body.RefundedVisits.ShouldBe(seats);
        var stored = await Enrollments.Enrollment(created.Id, lesson.UserId);
        stored.RemainingVisits.ShouldBe(visits);
        (await Waitlist.WaitlistStatusOf(waiterId)).ShouldBe(WaitlistStatus.Waiting);
        var waiterUserId = await Users.UserId(waiter.Number);
        (await Enrollments.ActiveCount(waiterUserId, lesson.ClassId)).ShouldBe(0);
        (await Passes.RemainingVisits(waiterUserId)).ShouldBe(waiterVisits);
    }

    [TestMethod]
    public async Task Cancel_WhenRepeated_ReturnsTheSameRefundAndDoesNotPromoteAgain()
    {
        // Arrange
        var seats = 2;
        var waiterSeats = 2;
        var laterWaiterSeats = 1;
        var visits = 10;
        var waiterVisits = 6;
        var capacity = 8;
        var leadHours = 3;
        var firstQueuedAgo = 10;
        var laterQueuedAgo = 1;
        var alreadyStarted = 1;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var firstWaiter = await Users.RegisterMember(waiterVisits, validUntil);
        var laterWaiter = await Users.RegisterMember(waiterVisits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddHours(leadHours).WholeSecond())
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, seats);
        var firstWaiterId = await Waitlist.AddWaiting(firstWaiter.Number, lesson.ClassId, waiterSeats, Session.Now.AddMinutes(-firstQueuedAgo));
        var first = await member.Enrollments.CancelEnrollment(created.Id);
        var firstBody = await first.ReadBody<CancelEnrollmentResponse>();
        var secondWaiterId = await Waitlist.AddWaiting(laterWaiter.Number, lesson.ClassId, laterWaiterSeats, Session.Now.AddMinutes(-laterQueuedAgo));
        await Classes.SetStartsAt(lesson.ClassId, Session.Now.AddHours(-alreadyStarted).WholeSecond());

        // Act
        var response = await member.Enrollments.CancelEnrollment(created.Id);

        // Assert
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadBody<CancelEnrollmentResponse>();
        body.RefundedVisits.ShouldBe(firstBody.RefundedVisits);
        body.RefundedVisits.ShouldBe(seats);
        var stored = await Enrollments.Enrollment(created.Id, lesson.UserId);
        stored.RemainingVisits.ShouldBe(visits);
        (await Waitlist.WaitlistStatusOf(firstWaiterId)).ShouldBe(WaitlistStatus.Promoted);
        (await Waitlist.WaitlistStatusOf(secondWaiterId)).ShouldBe(WaitlistStatus.Waiting);
        var firstWaiterUserId = await Users.UserId(firstWaiter.Number);
        var secondWaiterUserId = await Users.UserId(laterWaiter.Number);
        (await Enrollments.ActiveCount(firstWaiterUserId, lesson.ClassId)).ShouldBe(1);
        (await Enrollments.ActiveCount(secondWaiterUserId, lesson.ClassId)).ShouldBe(0);
        (await Passes.RemainingVisits(firstWaiterUserId)).ShouldBe(waiterVisits - waiterSeats);
        (await Passes.RemainingVisits(secondWaiterUserId)).ShouldBe(waiterVisits);
    }

    [TestMethod]
    public async Task Cancel_WhenEnrollmentIsMissing_ReturnsNotFound()
    {
        // Arrange
        var visits = 1;
        var passValidityDays = 30;
        var member = await Users.RegisterMember(visits, Session.Today.AddDays(passValidityDays));

        // Act
        var response = await member.Enrollments.CancelEnrollment(MissingEnrollmentId);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task Cancel_WhenEnrollmentBelongsToAnotherUser_ReturnsNotFound()
    {
        // Arrange
        var seats = 2;
        var visits = 10;
        var capacity = 8;
        var leadHours = 3;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var owner = await Users.RegisterMember(visits, validUntil);
        var other = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddHours(leadHours).WholeSecond())
            .Pass(owner.Number, visits, validUntil)
            .Create();
        var created = await owner.Enrollments.Book(lesson, seats);

        // Act
        var response = await other.Enrollments.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var stored = await Enrollments.Enrollment(created.Id, lesson.UserId);
        stored.Status.ShouldBe(EnrollmentStatus.Active);
        stored.RemainingVisits.ShouldBe(visits - seats);
    }

    [TestMethod]
    public async Task Cancel_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange
        var seats = 2;
        var visits = 10;
        var capacity = 8;
        var leadHours = 3;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddHours(leadHours).WholeSecond())
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, seats);

        // Act
        var response = await Users.Anonymous().Enrollments.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var stored = await Enrollments.Enrollment(created.Id, lesson.UserId);
        stored.Status.ShouldBe(EnrollmentStatus.Active);
        stored.RemainingVisits.ShouldBe(visits - seats);
    }

    [TestMethod]
    public async Task Cancel_WhenWaitersFit_PromotesInOrder()
    {
        // Arrange
        var seats = 3;
        var firstWaiterSeats = 1;
        var secondWaiterSeats = 2;
        var visits = 10;
        var waiterVisits = 6;
        var capacity = 8;
        var leadHours = 3;
        var firstQueuedAgo = 10;
        var secondQueuedAgo = 5;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var firstWaiter = await Users.RegisterMember(waiterVisits, validUntil);
        var secondWaiter = await Users.RegisterMember(waiterVisits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddHours(leadHours).WholeSecond())
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, seats);
        var firstWaiterId = await Waitlist.AddWaiting(firstWaiter.Number, lesson.ClassId, firstWaiterSeats, Session.Now.AddMinutes(-firstQueuedAgo));
        var secondWaiterId = await Waitlist.AddWaiting(secondWaiter.Number, lesson.ClassId, secondWaiterSeats, Session.Now.AddMinutes(-secondQueuedAgo));

        // Act
        var response = await member.Enrollments.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Waitlist.WaitlistStatusOf(firstWaiterId)).ShouldBe(WaitlistStatus.Promoted);
        (await Waitlist.WaitlistStatusOf(secondWaiterId)).ShouldBe(WaitlistStatus.Promoted);
        var firstWaiterUserId = await Users.UserId(firstWaiter.Number);
        var secondWaiterUserId = await Users.UserId(secondWaiter.Number);
        (await Enrollments.ActiveSeats(firstWaiterUserId, lesson.ClassId)).ShouldBe(firstWaiterSeats);
        (await Enrollments.ActiveSeats(secondWaiterUserId, lesson.ClassId)).ShouldBe(secondWaiterSeats);
        (await Passes.RemainingVisits(firstWaiterUserId)).ShouldBe(waiterVisits - firstWaiterSeats);
        (await Passes.RemainingVisits(secondWaiterUserId)).ShouldBe(waiterVisits - secondWaiterSeats);
    }

    [TestMethod]
    public async Task Cancel_WhenHeadNeedsMoreSeatsThanAreFree_PromotesTheNextWhoFits()
    {
        // Arrange
        var seats = 1;
        var heldSeats = 1;
        var headSeats = 3;
        var behindSeats = 1;
        var visits = 10;
        var waiterVisits = 6;
        var capacity = 3;
        var leadHours = 3;
        var headQueuedAgo = 10;
        var behindQueuedAgo = 5;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var holder = await Users.RegisterMember(visits, validUntil);
        var head = await Users.RegisterMember(waiterVisits, validUntil);
        var behind = await Users.RegisterMember(waiterVisits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddHours(leadHours).WholeSecond())
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, seats);
        await holder.Enrollments.Book(lesson, heldSeats);
        var headId = await Waitlist.AddWaiting(head.Number, lesson.ClassId, headSeats, Session.Now.AddMinutes(-headQueuedAgo));
        var behindId = await Waitlist.AddWaiting(behind.Number, lesson.ClassId, behindSeats, Session.Now.AddMinutes(-behindQueuedAgo));

        // Act
        var response = await member.Enrollments.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Waitlist.WaitlistStatusOf(headId)).ShouldBe(WaitlistStatus.Waiting);
        (await Waitlist.WaitlistStatusOf(behindId)).ShouldBe(WaitlistStatus.Promoted);
        var headUserId = await Users.UserId(head.Number);
        var behindUserId = await Users.UserId(behind.Number);
        (await Enrollments.ActiveCount(headUserId, lesson.ClassId)).ShouldBe(0);
        (await Enrollments.ActiveSeats(behindUserId, lesson.ClassId)).ShouldBe(behindSeats);
        (await Passes.RemainingVisits(headUserId)).ShouldBe(waiterVisits);
        (await Passes.RemainingVisits(behindUserId)).ShouldBe(waiterVisits - behindSeats);
    }

    [TestMethod]
    public async Task Cancel_WhenHeadCannotPay_PromotesTheFollowingWaiters()
    {
        // Arrange
        var seats = 2;
        var headSeats = 1;
        var nextSeats = 1;
        var behindSeats = 1;
        var visits = 10;
        var headVisits = 0;
        var nextVisits = 4;
        var behindVisits = 4;
        var capacity = 8;
        var leadHours = 3;
        var headQueuedAgo = 15;
        var nextQueuedAgo = 10;
        var behindQueuedAgo = 5;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var head = await Users.RegisterMember(headVisits, validUntil);
        var next = await Users.RegisterMember(nextVisits, validUntil);
        var behind = await Users.RegisterMember(behindVisits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddHours(leadHours).WholeSecond())
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, seats);
        var headId = await Waitlist.AddWaiting(head.Number, lesson.ClassId, headSeats, Session.Now.AddMinutes(-headQueuedAgo));
        var nextId = await Waitlist.AddWaiting(next.Number, lesson.ClassId, nextSeats, Session.Now.AddMinutes(-nextQueuedAgo));
        var behindId = await Waitlist.AddWaiting(behind.Number, lesson.ClassId, behindSeats, Session.Now.AddMinutes(-behindQueuedAgo));

        // Act
        var response = await member.Enrollments.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Waitlist.WaitlistStatusOf(headId)).ShouldBe(WaitlistStatus.Skipped);
        (await Waitlist.WaitlistStatusOf(nextId)).ShouldBe(WaitlistStatus.Promoted);
        (await Waitlist.WaitlistStatusOf(behindId)).ShouldBe(WaitlistStatus.Promoted);
        var headUserId = await Users.UserId(head.Number);
        var nextUserId = await Users.UserId(next.Number);
        var behindUserId = await Users.UserId(behind.Number);
        (await Enrollments.ActiveCount(headUserId, lesson.ClassId)).ShouldBe(0);
        (await Enrollments.ActiveSeats(nextUserId, lesson.ClassId)).ShouldBe(nextSeats);
        (await Enrollments.ActiveSeats(behindUserId, lesson.ClassId)).ShouldBe(behindSeats);
        (await Passes.RemainingVisits(headUserId)).ShouldBe(headVisits);
        (await Passes.RemainingVisits(nextUserId)).ShouldBe(nextVisits - nextSeats);
        (await Passes.RemainingVisits(behindUserId)).ShouldBe(behindVisits - behindSeats);
    }

    [TestMethod]
    public async Task Cancel_WhenHeadPassIsExpired_SkipsHeadAndDoesNotPassALargerRequest()
    {
        // Arrange
        var seats = 2;
        var headSeats = 1;
        var nextSeats = 1;
        var behindSeats = 3;
        var visits = 10;
        var waiterVisits = 5;
        var capacity = 3;
        var leadHours = 3;
        var headQueuedAgo = 15;
        var nextQueuedAgo = 10;
        var behindQueuedAgo = 5;
        var passValidityDays = 30;
        var expiredDays = 1;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var expiredOn = Session.Today.AddDays(-expiredDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var head = await Users.RegisterMember(waiterVisits, expiredOn);
        var next = await Users.RegisterMember(waiterVisits, validUntil);
        var behind = await Users.RegisterMember(waiterVisits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddHours(leadHours).WholeSecond())
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, seats);
        var headId = await Waitlist.AddWaiting(head.Number, lesson.ClassId, headSeats, Session.Now.AddMinutes(-headQueuedAgo));
        var nextId = await Waitlist.AddWaiting(next.Number, lesson.ClassId, nextSeats, Session.Now.AddMinutes(-nextQueuedAgo));
        var behindId = await Waitlist.AddWaiting(behind.Number, lesson.ClassId, behindSeats, Session.Now.AddMinutes(-behindQueuedAgo));

        // Act
        var response = await member.Enrollments.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Waitlist.WaitlistStatusOf(headId)).ShouldBe(WaitlistStatus.Skipped);
        (await Waitlist.WaitlistStatusOf(nextId)).ShouldBe(WaitlistStatus.Promoted);
        (await Waitlist.WaitlistStatusOf(behindId)).ShouldBe(WaitlistStatus.Waiting);
        var nextUserId = await Users.UserId(next.Number);
        var behindUserId = await Users.UserId(behind.Number);
        (await Enrollments.ActiveSeats(nextUserId, lesson.ClassId)).ShouldBe(nextSeats);
        (await Enrollments.ActiveCount(behindUserId, lesson.ClassId)).ShouldBe(0);
        (await Passes.RemainingVisits(behindUserId)).ShouldBe(waiterVisits);
    }

    [TestMethod]
    public async Task Cancel_LessThanTwoHoursBeforeStart_StillPromotes()
    {
        // Arrange
        var seats = 1;
        var waiterSeats = 1;
        var visits = 10;
        var waiterVisits = 4;
        var capacity = 8;
        var leadMinutes = 30;
        var queuedAgo = 5;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var waiter = await Users.RegisterMember(waiterVisits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddMinutes(leadMinutes).WholeSecond())
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, seats);
        var waiterId = await Waitlist.AddWaiting(waiter.Number, lesson.ClassId, waiterSeats, Session.Now.AddMinutes(-queuedAgo));

        // Act
        var response = await member.Enrollments.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadBody<CancelEnrollmentResponse>();
        body.RefundedVisits.ShouldBe(0);
        (await Waitlist.WaitlistStatusOf(waiterId)).ShouldBe(WaitlistStatus.Promoted);
        var waiterUserId = await Users.UserId(waiter.Number);
        (await Enrollments.ActiveSeats(waiterUserId, lesson.ClassId)).ShouldBe(waiterSeats);
        (await Passes.RemainingVisits(waiterUserId)).ShouldBe(waiterVisits - waiterSeats);
        var stored = await Enrollments.Enrollment(created.Id, lesson.UserId);
        stored.RemainingVisits.ShouldBe(visits - seats);
    }

    [TestMethod]
    public async Task Cancel_WhenFreeSeatsAccumulate_PromotesTheWaitingHead()
    {
        // Arrange
        var bookedSeats = 1;
        var requestedSeats = 2;
        var visits = 4;
        var capacity = 2;
        var leadHours = 3;
        var queuedAgo = 5;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var firstMember = await Users.RegisterMember(visits, validUntil);
        var secondMember = await Users.RegisterMember(visits, validUntil);
        var waiter = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddHours(leadHours).WholeSecond())
            .Pass(firstMember.Number, visits, validUntil)
            .Create();
        var first = await firstMember.Enrollments.Book(lesson, bookedSeats);
        var second = await secondMember.Enrollments.Book(lesson, bookedSeats);
        var waiterId = await Waitlist.AddWaiting(waiter.Number, lesson.ClassId, requestedSeats, Session.Now.AddMinutes(-queuedAgo));

        // Act
        var firstCancel = await firstMember.Enrollments.CancelEnrollment(first.Id);
        var afterFirst = await Waitlist.WaitlistStatusOf(waiterId);
        var secondCancel = await secondMember.Enrollments.CancelEnrollment(second.Id);

        // Assert
        firstCancel.StatusCode.ShouldBe(HttpStatusCode.OK);
        afterFirst.ShouldBe(WaitlistStatus.Waiting);
        secondCancel.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Waitlist.WaitlistStatusOf(waiterId)).ShouldBe(WaitlistStatus.Promoted);
        var waiterUserId = await Users.UserId(waiter.Number);
        (await Enrollments.ActiveSeats(waiterUserId, lesson.ClassId)).ShouldBe(requestedSeats);
        (await Passes.RemainingVisits(waiterUserId)).ShouldBe(visits - requestedSeats);
        (await Passes.RemainingVisits(lesson.UserId)).ShouldBe(visits);
        (await Passes.RemainingVisits(await Users.UserId(secondMember.Number))).ShouldBe(visits);
    }

    [TestMethod]
    public async Task Cancel_WhenTheCallerIsAlsoWaiting_DropsThatRequestAndPromotesTheNext()
    {
        // Arrange
        var bookedSeats = 2;
        var ownRequestSeats = 1;
        var nextRequestSeats = 1;
        var visits = 10;
        var nextVisits = 4;
        var capacity = 2;
        var leadHours = 3;
        var ownQueuedAgo = 10;
        var nextQueuedAgo = 5;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var next = await Users.RegisterMember(nextVisits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(Session.Now.AddHours(leadHours).WholeSecond())
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, bookedSeats);
        var ownEntryId = await Waitlist.AddWaiting(member.Number, lesson.ClassId, ownRequestSeats, Session.Now.AddMinutes(-ownQueuedAgo));
        var nextEntryId = await Waitlist.AddWaiting(next.Number, lesson.ClassId, nextRequestSeats, Session.Now.AddMinutes(-nextQueuedAgo));

        // Act
        var response = await member.Enrollments.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadBody<CancelEnrollmentResponse>();
        body.RefundedVisits.ShouldBe(bookedSeats);
        var callerId = await Users.UserId(member.Number);
        (await Waitlist.WaitlistStatusOf(ownEntryId)).ShouldBe(WaitlistStatus.Cancelled);
        (await Enrollments.ActiveCount(callerId, lesson.ClassId)).ShouldBe(0);
        (await Passes.RemainingVisits(callerId)).ShouldBe(visits);
        (await Waitlist.WaitlistStatusOf(nextEntryId)).ShouldBe(WaitlistStatus.Promoted);
        var nextUserId = await Users.UserId(next.Number);
        (await Enrollments.ActiveSeats(nextUserId, lesson.ClassId)).ShouldBe(nextRequestSeats);
        (await Passes.RemainingVisits(nextUserId)).ShouldBe(nextVisits - nextRequestSeats);
    }

    [TestMethod]
    public async Task Cancel_WhenTheIdIsNotNumeric_ReturnsBadRequest()
    {
        // Arrange
        var visits = 1;
        var passValidityDays = 30;
        var member = await Users.RegisterMember(visits, Session.Today.AddDays(passValidityDays));

        // Act
        var response = await member.Enrollments.CancelEnrollment("abc");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task Cancel_ExactlyTwoHoursBeforeStart_RefundsEveryVisit()
    {
        // Arrange
        var now = new DateTime(2032, 3, 4, 10, 0, 0, DateTimeKind.Utc);
        var seats = 2;
        var visits = 6;
        var capacity = 4;
        var leadTime = TimeSpan.FromHours(2);
        var validUntil = new DateOnly(2032, 4, 1);
        using var clock = Session.Freeze(now);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(now.Add(leadTime))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, seats);

        // Act
        var response = await member.Enrollments.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadJson();
        body.GetProperty("status").GetString().ShouldBe("cancelled");
        body.GetProperty("refundedVisits").GetInt32().ShouldBe(seats);
        (await Passes.RemainingVisits(lesson.UserId)).ShouldBe(visits);
    }

    [TestMethod]
    public async Task Cancel_ExactlyWhenTheClassStarts_ReturnsClassStarted()
    {
        // Arrange
        var bookedAt = new DateTime(2032, 6, 1, 8, 0, 0, DateTimeKind.Utc);
        var leadHours = 4;
        var startsAt = bookedAt.AddHours(leadHours);
        var seats = 1;
        var visits = 4;
        var capacity = 4;
        var validUntil = new DateOnly(2032, 7, 1);
        using var clock = Session.Freeze(bookedAt);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(startsAt)
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Enrollments.Book(lesson, seats);
        clock.MoveTo(startsAt);

        // Act
        var response = await member.Enrollments.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReasonText()).ShouldBe("class-started");
        (await Passes.RemainingVisits(lesson.UserId)).ShouldBe(visits - seats);
    }
}
