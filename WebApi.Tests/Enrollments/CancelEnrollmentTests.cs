using System.Net;
using BusinessLayer.Result;
using DataAccessLayer.Entities;
using WebApi.Features.Enrollments.Cancel;
using Shouldly;
using WebApi.Tests.Infrastructure;

namespace WebApi.Tests.Enrollments;

[TestClass]
public class CancelEnrollmentTests : ClubTest
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
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(ClubTime.WholeSecond(Host().Now.AddHours(leadHours)))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Book(lesson, seats);

        // Act
        var response = await member.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadBody<CancelEnrollmentResponse>();
        body.Id.ShouldBe(created.Id);
        body.Status.ShouldBe(EnrollmentStatus.Cancelled);
        body.RefundedVisits.ShouldBe(seats);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
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
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(ClubTime.WholeSecond(Host().Now.AddMinutes(leadMinutes)))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Book(lesson, seats);

        // Act
        var response = await member.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadBody<CancelEnrollmentResponse>();
        body.RefundedVisits.ShouldBe(0);
        body.Status.ShouldBe(EnrollmentStatus.Cancelled);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
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
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var waiter = await Host().RegisterMember(waiterVisits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(ClubTime.WholeSecond(Host().Now.AddHours(leadHours)))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Book(lesson, seats);
        var waiterId = await Host().AddWaiting(waiter.Number, lesson.ClassId, waiterSeats, Host().Now.AddMinutes(-queuedAgo));
        await Host().SetStartsAt(lesson.ClassId, ClubTime.WholeSecond(Host().Now.AddHours(-alreadyStarted)));

        // Act
        var response = await member.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.ClassStarted);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
        stored.Status.ShouldBe(EnrollmentStatus.Active);
        stored.RemainingVisits.ShouldBe(visits - seats);
        (await Host().WaitlistStatusOf(waiterId)).ShouldBe(WaitlistStatus.Waiting);
        (await Host().ActiveCount(await Host().UserId(waiter.Number), lesson.ClassId)).ShouldBe(0);
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
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(ClubTime.WholeSecond(Host().Now.AddHours(-alreadyStarted)))
            .Cancelled()
            .Pass(member.Number, visits, validUntil)
            .Create();
        var enrollmentId = await Host().InsertSpentEnrollment(lesson.UserId, lesson.ClassId, seats, visits);

        // Act
        var response = await member.CancelEnrollment(enrollmentId);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadBody<CancelEnrollmentResponse>();
        body.RefundedVisits.ShouldBe(seats);
        var stored = await Host().Enrollment(enrollmentId, lesson.UserId);
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
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var waiter = await Host().RegisterMember(waiterVisits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(ClubTime.WholeSecond(Host().Now.AddMinutes(leadMinutes)))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Book(lesson, seats);
        var waiterId = await Host().AddWaiting(waiter.Number, lesson.ClassId, waiterSeats, Host().Now.AddMinutes(-queuedAgo));
        await Host().CancelClass(lesson.ClassId);

        // Act
        var response = await member.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadBody<CancelEnrollmentResponse>();
        body.RefundedVisits.ShouldBe(seats);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
        stored.RemainingVisits.ShouldBe(visits);
        (await Host().WaitlistStatusOf(waiterId)).ShouldBe(WaitlistStatus.Waiting);
        var waiterUserId = await Host().UserId(waiter.Number);
        (await Host().ActiveCount(waiterUserId, lesson.ClassId)).ShouldBe(0);
        (await Host().RemainingVisits(waiterUserId)).ShouldBe(waiterVisits);
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
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var firstWaiter = await Host().RegisterMember(waiterVisits, validUntil);
        var laterWaiter = await Host().RegisterMember(waiterVisits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(ClubTime.WholeSecond(Host().Now.AddHours(leadHours)))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Book(lesson, seats);
        var firstWaiterId = await Host().AddWaiting(firstWaiter.Number, lesson.ClassId, waiterSeats, Host().Now.AddMinutes(-firstQueuedAgo));
        var first = await member.CancelEnrollment(created.Id);
        var firstBody = await first.ReadBody<CancelEnrollmentResponse>();
        var secondWaiterId = await Host().AddWaiting(laterWaiter.Number, lesson.ClassId, laterWaiterSeats, Host().Now.AddMinutes(-laterQueuedAgo));
        await Host().SetStartsAt(lesson.ClassId, ClubTime.WholeSecond(Host().Now.AddHours(-alreadyStarted)));

        // Act
        var response = await member.CancelEnrollment(created.Id);

        // Assert
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadBody<CancelEnrollmentResponse>();
        body.RefundedVisits.ShouldBe(firstBody.RefundedVisits);
        body.RefundedVisits.ShouldBe(seats);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
        stored.RemainingVisits.ShouldBe(visits);
        (await Host().WaitlistStatusOf(firstWaiterId)).ShouldBe(WaitlistStatus.Promoted);
        (await Host().WaitlistStatusOf(secondWaiterId)).ShouldBe(WaitlistStatus.Waiting);
        var firstWaiterUserId = await Host().UserId(firstWaiter.Number);
        var secondWaiterUserId = await Host().UserId(laterWaiter.Number);
        (await Host().ActiveCount(firstWaiterUserId, lesson.ClassId)).ShouldBe(1);
        (await Host().ActiveCount(secondWaiterUserId, lesson.ClassId)).ShouldBe(0);
        (await Host().RemainingVisits(firstWaiterUserId)).ShouldBe(waiterVisits - waiterSeats);
        (await Host().RemainingVisits(secondWaiterUserId)).ShouldBe(waiterVisits);
    }

    [TestMethod]
    public async Task Cancel_WhenEnrollmentIsMissing_ReturnsNotFound()
    {
        // Arrange
        var visits = 1;
        var passValidityDays = 30;
        var member = await Host().RegisterMember(visits, ClubTime.Today().AddDays(passValidityDays));

        // Act
        var response = await member.CancelEnrollment(MissingEnrollmentId);

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
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var owner = await Host().RegisterMember(visits, validUntil);
        var other = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(ClubTime.WholeSecond(Host().Now.AddHours(leadHours)))
            .Pass(owner.Number, visits, validUntil)
            .Create();
        var created = await owner.Book(lesson, seats);

        // Act
        var response = await other.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
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
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(ClubTime.WholeSecond(Host().Now.AddHours(leadHours)))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Book(lesson, seats);

        // Act
        var response = await Host().Anonymous().CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
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
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var firstWaiter = await Host().RegisterMember(waiterVisits, validUntil);
        var secondWaiter = await Host().RegisterMember(waiterVisits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(ClubTime.WholeSecond(Host().Now.AddHours(leadHours)))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Book(lesson, seats);
        var firstWaiterId = await Host().AddWaiting(firstWaiter.Number, lesson.ClassId, firstWaiterSeats, Host().Now.AddMinutes(-firstQueuedAgo));
        var secondWaiterId = await Host().AddWaiting(secondWaiter.Number, lesson.ClassId, secondWaiterSeats, Host().Now.AddMinutes(-secondQueuedAgo));

        // Act
        var response = await member.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Host().WaitlistStatusOf(firstWaiterId)).ShouldBe(WaitlistStatus.Promoted);
        (await Host().WaitlistStatusOf(secondWaiterId)).ShouldBe(WaitlistStatus.Promoted);
        var firstWaiterUserId = await Host().UserId(firstWaiter.Number);
        var secondWaiterUserId = await Host().UserId(secondWaiter.Number);
        (await Host().ActiveSeats(firstWaiterUserId, lesson.ClassId)).ShouldBe(firstWaiterSeats);
        (await Host().ActiveSeats(secondWaiterUserId, lesson.ClassId)).ShouldBe(secondWaiterSeats);
        (await Host().RemainingVisits(firstWaiterUserId)).ShouldBe(waiterVisits - firstWaiterSeats);
        (await Host().RemainingVisits(secondWaiterUserId)).ShouldBe(waiterVisits - secondWaiterSeats);
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
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var holder = await Host().RegisterMember(visits, validUntil);
        var head = await Host().RegisterMember(waiterVisits, validUntil);
        var behind = await Host().RegisterMember(waiterVisits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(ClubTime.WholeSecond(Host().Now.AddHours(leadHours)))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Book(lesson, seats);
        await holder.Book(lesson, heldSeats);
        var headId = await Host().AddWaiting(head.Number, lesson.ClassId, headSeats, Host().Now.AddMinutes(-headQueuedAgo));
        var behindId = await Host().AddWaiting(behind.Number, lesson.ClassId, behindSeats, Host().Now.AddMinutes(-behindQueuedAgo));

        // Act
        var response = await member.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Host().WaitlistStatusOf(headId)).ShouldBe(WaitlistStatus.Waiting);
        (await Host().WaitlistStatusOf(behindId)).ShouldBe(WaitlistStatus.Promoted);
        var headUserId = await Host().UserId(head.Number);
        var behindUserId = await Host().UserId(behind.Number);
        (await Host().ActiveCount(headUserId, lesson.ClassId)).ShouldBe(0);
        (await Host().ActiveSeats(behindUserId, lesson.ClassId)).ShouldBe(behindSeats);
        (await Host().RemainingVisits(headUserId)).ShouldBe(waiterVisits);
        (await Host().RemainingVisits(behindUserId)).ShouldBe(waiterVisits - behindSeats);
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
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var head = await Host().RegisterMember(headVisits, validUntil);
        var next = await Host().RegisterMember(nextVisits, validUntil);
        var behind = await Host().RegisterMember(behindVisits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(ClubTime.WholeSecond(Host().Now.AddHours(leadHours)))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Book(lesson, seats);
        var headId = await Host().AddWaiting(head.Number, lesson.ClassId, headSeats, Host().Now.AddMinutes(-headQueuedAgo));
        var nextId = await Host().AddWaiting(next.Number, lesson.ClassId, nextSeats, Host().Now.AddMinutes(-nextQueuedAgo));
        var behindId = await Host().AddWaiting(behind.Number, lesson.ClassId, behindSeats, Host().Now.AddMinutes(-behindQueuedAgo));

        // Act
        var response = await member.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Host().WaitlistStatusOf(headId)).ShouldBe(WaitlistStatus.Skipped);
        (await Host().WaitlistStatusOf(nextId)).ShouldBe(WaitlistStatus.Promoted);
        (await Host().WaitlistStatusOf(behindId)).ShouldBe(WaitlistStatus.Promoted);
        var headUserId = await Host().UserId(head.Number);
        var nextUserId = await Host().UserId(next.Number);
        var behindUserId = await Host().UserId(behind.Number);
        (await Host().ActiveCount(headUserId, lesson.ClassId)).ShouldBe(0);
        (await Host().ActiveSeats(nextUserId, lesson.ClassId)).ShouldBe(nextSeats);
        (await Host().ActiveSeats(behindUserId, lesson.ClassId)).ShouldBe(behindSeats);
        (await Host().RemainingVisits(headUserId)).ShouldBe(headVisits);
        (await Host().RemainingVisits(nextUserId)).ShouldBe(nextVisits - nextSeats);
        (await Host().RemainingVisits(behindUserId)).ShouldBe(behindVisits - behindSeats);
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
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var expiredOn = ClubTime.Today().AddDays(-expiredDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var head = await Host().RegisterMember(waiterVisits, expiredOn);
        var next = await Host().RegisterMember(waiterVisits, validUntil);
        var behind = await Host().RegisterMember(waiterVisits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(ClubTime.WholeSecond(Host().Now.AddHours(leadHours)))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Book(lesson, seats);
        var headId = await Host().AddWaiting(head.Number, lesson.ClassId, headSeats, Host().Now.AddMinutes(-headQueuedAgo));
        var nextId = await Host().AddWaiting(next.Number, lesson.ClassId, nextSeats, Host().Now.AddMinutes(-nextQueuedAgo));
        var behindId = await Host().AddWaiting(behind.Number, lesson.ClassId, behindSeats, Host().Now.AddMinutes(-behindQueuedAgo));

        // Act
        var response = await member.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Host().WaitlistStatusOf(headId)).ShouldBe(WaitlistStatus.Skipped);
        (await Host().WaitlistStatusOf(nextId)).ShouldBe(WaitlistStatus.Promoted);
        (await Host().WaitlistStatusOf(behindId)).ShouldBe(WaitlistStatus.Waiting);
        var nextUserId = await Host().UserId(next.Number);
        var behindUserId = await Host().UserId(behind.Number);
        (await Host().ActiveSeats(nextUserId, lesson.ClassId)).ShouldBe(nextSeats);
        (await Host().ActiveCount(behindUserId, lesson.ClassId)).ShouldBe(0);
        (await Host().RemainingVisits(behindUserId)).ShouldBe(waiterVisits);
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
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var waiter = await Host().RegisterMember(waiterVisits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(ClubTime.WholeSecond(Host().Now.AddMinutes(leadMinutes)))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Book(lesson, seats);
        var waiterId = await Host().AddWaiting(waiter.Number, lesson.ClassId, waiterSeats, Host().Now.AddMinutes(-queuedAgo));

        // Act
        var response = await member.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadBody<CancelEnrollmentResponse>();
        body.RefundedVisits.ShouldBe(0);
        (await Host().WaitlistStatusOf(waiterId)).ShouldBe(WaitlistStatus.Promoted);
        var waiterUserId = await Host().UserId(waiter.Number);
        (await Host().ActiveSeats(waiterUserId, lesson.ClassId)).ShouldBe(waiterSeats);
        (await Host().RemainingVisits(waiterUserId)).ShouldBe(waiterVisits - waiterSeats);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
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
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var firstMember = await Host().RegisterMember(visits, validUntil);
        var secondMember = await Host().RegisterMember(visits, validUntil);
        var waiter = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(ClubTime.WholeSecond(Host().Now.AddHours(leadHours)))
            .Pass(firstMember.Number, visits, validUntil)
            .Create();
        var first = await firstMember.Book(lesson, bookedSeats);
        var second = await secondMember.Book(lesson, bookedSeats);
        var waiterId = await Host().AddWaiting(waiter.Number, lesson.ClassId, requestedSeats, Host().Now.AddMinutes(-queuedAgo));

        // Act
        var firstCancel = await firstMember.CancelEnrollment(first.Id);
        var afterFirst = await Host().WaitlistStatusOf(waiterId);
        var secondCancel = await secondMember.CancelEnrollment(second.Id);

        // Assert
        firstCancel.StatusCode.ShouldBe(HttpStatusCode.OK);
        afterFirst.ShouldBe(WaitlistStatus.Waiting);
        secondCancel.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Host().WaitlistStatusOf(waiterId)).ShouldBe(WaitlistStatus.Promoted);
        var waiterUserId = await Host().UserId(waiter.Number);
        (await Host().ActiveSeats(waiterUserId, lesson.ClassId)).ShouldBe(requestedSeats);
        (await Host().RemainingVisits(waiterUserId)).ShouldBe(visits - requestedSeats);
        (await Host().RemainingVisits(lesson.UserId)).ShouldBe(visits);
        (await Host().RemainingVisits(await Host().UserId(secondMember.Number))).ShouldBe(visits);
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
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var next = await Host().RegisterMember(nextVisits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(ClubTime.WholeSecond(Host().Now.AddHours(leadHours)))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Book(lesson, bookedSeats);
        var ownEntryId = await Host().AddWaiting(member.Number, lesson.ClassId, ownRequestSeats, Host().Now.AddMinutes(-ownQueuedAgo));
        var nextEntryId = await Host().AddWaiting(next.Number, lesson.ClassId, nextRequestSeats, Host().Now.AddMinutes(-nextQueuedAgo));

        // Act
        var response = await member.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadBody<CancelEnrollmentResponse>();
        body.RefundedVisits.ShouldBe(bookedSeats);
        var callerId = await Host().UserId(member.Number);
        (await Host().WaitlistStatusOf(ownEntryId)).ShouldBe(WaitlistStatus.Cancelled);
        (await Host().ActiveCount(callerId, lesson.ClassId)).ShouldBe(0);
        (await Host().RemainingVisits(callerId)).ShouldBe(visits);
        (await Host().WaitlistStatusOf(nextEntryId)).ShouldBe(WaitlistStatus.Promoted);
        var nextUserId = await Host().UserId(next.Number);
        (await Host().ActiveSeats(nextUserId, lesson.ClassId)).ShouldBe(nextRequestSeats);
        (await Host().RemainingVisits(nextUserId)).ShouldBe(nextVisits - nextRequestSeats);
    }

    [TestMethod]
    public async Task Cancel_WhenTheIdIsNotNumeric_ReturnsBadRequest()
    {
        // Arrange
        var visits = 1;
        var passValidityDays = 30;
        var member = await Host().RegisterMember(visits, ClubTime.Today().AddDays(passValidityDays));

        // Act
        var response = await member.CancelEnrollment("abc");

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
        using var clock = Host().Freeze(now);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(now.Add(leadTime))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Book(lesson, seats);

        // Act
        var response = await member.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadJson();
        body.GetProperty("status").GetString().ShouldBe("cancelled");
        body.GetProperty("refundedVisits").GetInt32().ShouldBe(seats);
        (await Host().RemainingVisits(lesson.UserId)).ShouldBe(visits);
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
        using var clock = Host().Freeze(bookedAt);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(startsAt)
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Book(lesson, seats);
        clock.MoveTo(startsAt);

        // Act
        var response = await member.CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReasonText()).ShouldBe("class-started");
        (await Host().RemainingVisits(lesson.UserId)).ShouldBe(visits - seats);
    }
}
