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
        var lesson = await Host().Lesson().Capacity(8).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddHours(3))).Pass(40, visits).Create();
        var created = await Host().User(40).Book(lesson, seats);

        // Act
        var response = await Host().User(40).CancelEnrollment(created.Id);

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
        var lesson = await Host().Lesson().Capacity(8).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddMinutes(30))).Pass(41, visits).Create();
        var created = await Host().User(41).Book(lesson, seats);

        // Act
        var response = await Host().User(41).CancelEnrollment(created.Id);

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
        var visits = 10;
        var waiterVisits = 4;
        var validUntil = ClubTime.Today().AddDays(30);
        var lesson = await Host().Lesson().Capacity(8).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddHours(3))).Pass(42, visits, validUntil).Create();
        var created = await Host().User(42).Book(lesson, seats);
        await Host().SetPass(43, waiterVisits, validUntil);
        var waiterId = await Host().AddWaiting(43, lesson.ClassId, 1, DateTime.UtcNow.AddMinutes(-5));
        await Host().SetStartsAt(lesson.ClassId, ClubTime.WholeSecond(DateTime.UtcNow.AddHours(-1)));

        // Act
        var response = await Host().User(42).CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.ClassStarted);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
        stored.Status.ShouldBe(EnrollmentStatus.Active);
        stored.RemainingVisits.ShouldBe(visits - seats);
        (await Host().WaitlistStatusOf(waiterId)).ShouldBe(WaitlistStatus.Waiting);
        (await Host().ActiveCount(await Host().UserId(43), lesson.ClassId)).ShouldBe(0);
    }

    [TestMethod]
    public async Task Cancel_WhenClubCancelledAfterStart_RefundsAllVisits()
    {
        // Arrange
        var seats = 2;
        var visits = 10;
        var lesson = await Host().Lesson()
            .Capacity(8)
            .StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddHours(-1)))
            .Cancelled()
            .Pass(44, visits)
            .Create();
        var enrollmentId = await Host().InsertSpentEnrollment(lesson.UserId, lesson.ClassId, seats, visits);

        // Act
        var response = await Host().User(44).CancelEnrollment(enrollmentId);

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
        var visits = 10;
        var waiterVisits = 4;
        var validUntil = ClubTime.Today().AddDays(30);
        var lesson = await Host().Lesson().Capacity(8).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddMinutes(30))).Pass(45, visits, validUntil).Create();
        var created = await Host().User(45).Book(lesson, seats);
        await Host().SetPass(46, waiterVisits, validUntil);
        var waiterId = await Host().AddWaiting(46, lesson.ClassId, 1, DateTime.UtcNow.AddMinutes(-5));
        await Host().CancelClass(lesson.ClassId);

        // Act
        var response = await Host().User(45).CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadBody<CancelEnrollmentResponse>();
        body.RefundedVisits.ShouldBe(seats);
        var stored = await Host().Enrollment(created.Id, lesson.UserId);
        stored.RemainingVisits.ShouldBe(visits);
        (await Host().WaitlistStatusOf(waiterId)).ShouldBe(WaitlistStatus.Waiting);
        var waiterUserId = await Host().UserId(46);
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
        var validUntil = ClubTime.Today().AddDays(30);
        var lesson = await Host().Lesson().Capacity(8).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddHours(3))).Pass(47, visits, validUntil).Create();
        var created = await Host().User(47).Book(lesson, seats);
        await Host().SetPass(48, waiterVisits, validUntil);
        await Host().SetPass(49, waiterVisits, validUntil);
        var firstWaiterId = await Host().AddWaiting(48, lesson.ClassId, waiterSeats, DateTime.UtcNow.AddMinutes(-10));
        var first = await Host().User(47).CancelEnrollment(created.Id);
        var firstBody = await first.ReadBody<CancelEnrollmentResponse>();
        var secondWaiterId = await Host().AddWaiting(49, lesson.ClassId, laterWaiterSeats, DateTime.UtcNow.AddMinutes(-1));
        await Host().SetStartsAt(lesson.ClassId, ClubTime.WholeSecond(DateTime.UtcNow.AddHours(-1)));

        // Act
        var response = await Host().User(47).CancelEnrollment(created.Id);

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
        var firstWaiterUserId = await Host().UserId(48);
        var secondWaiterUserId = await Host().UserId(49);
        (await Host().ActiveCount(firstWaiterUserId, lesson.ClassId)).ShouldBe(1);
        (await Host().ActiveCount(secondWaiterUserId, lesson.ClassId)).ShouldBe(0);
        (await Host().RemainingVisits(firstWaiterUserId)).ShouldBe(waiterVisits - waiterSeats);
        (await Host().RemainingVisits(secondWaiterUserId)).ShouldBe(waiterVisits);
    }

    [TestMethod]
    public async Task Cancel_WhenEnrollmentIsMissing_ReturnsNotFound()
    {
        // Act
        var response = await Host().User(50).CancelEnrollment(MissingEnrollmentId);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task Cancel_WhenEnrollmentBelongsToAnotherUser_ReturnsNotFound()
    {
        // Arrange
        var seats = 2;
        var visits = 10;
        var lesson = await Host().Lesson().Capacity(8).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddHours(3))).Pass(51, visits).Create();
        var created = await Host().User(51).Book(lesson, seats);

        // Act
        var response = await Host().User(52).CancelEnrollment(created.Id);

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
        var lesson = await Host().Lesson().Capacity(8).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddHours(3))).Pass(53, visits).Create();
        var created = await Host().User(53).Book(lesson, seats);

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
        var validUntil = ClubTime.Today().AddDays(30);
        var lesson = await Host().Lesson().Capacity(8).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddHours(3))).Pass(54, visits, validUntil).Create();
        var created = await Host().User(54).Book(lesson, seats);
        await Host().SetPass(55, waiterVisits, validUntil);
        await Host().SetPass(56, waiterVisits, validUntil);
        var firstWaiterId = await Host().AddWaiting(55, lesson.ClassId, firstWaiterSeats, DateTime.UtcNow.AddMinutes(-10));
        var secondWaiterId = await Host().AddWaiting(56, lesson.ClassId, secondWaiterSeats, DateTime.UtcNow.AddMinutes(-5));

        // Act
        var response = await Host().User(54).CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Host().WaitlistStatusOf(firstWaiterId)).ShouldBe(WaitlistStatus.Promoted);
        (await Host().WaitlistStatusOf(secondWaiterId)).ShouldBe(WaitlistStatus.Promoted);
        var firstWaiterUserId = await Host().UserId(55);
        var secondWaiterUserId = await Host().UserId(56);
        (await Host().ActiveSeats(firstWaiterUserId, lesson.ClassId)).ShouldBe(firstWaiterSeats);
        (await Host().ActiveSeats(secondWaiterUserId, lesson.ClassId)).ShouldBe(secondWaiterSeats);
        (await Host().RemainingVisits(firstWaiterUserId)).ShouldBe(waiterVisits - firstWaiterSeats);
        (await Host().RemainingVisits(secondWaiterUserId)).ShouldBe(waiterVisits - secondWaiterSeats);
    }

    [TestMethod]
    public async Task Cancel_WhenHeadNeedsMoreSeatsThanAreFree_PromotesNobody()
    {
        // Arrange
        var seats = 1;
        var heldSeats = 1;
        var headSeats = 3;
        var behindSeats = 1;
        var visits = 10;
        var waiterVisits = 6;
        var validUntil = ClubTime.Today().AddDays(30);
        var lesson = await Host().Lesson().Capacity(3).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddHours(3))).Pass(57, visits, validUntil).Create();
        await Host().SetPass(74, visits, validUntil);
        var created = await Host().User(57).Book(lesson, seats);
        await Host().User(74).Book(lesson, heldSeats);
        await Host().SetPass(58, waiterVisits, validUntil);
        await Host().SetPass(59, waiterVisits, validUntil);
        var headId = await Host().AddWaiting(58, lesson.ClassId, headSeats, DateTime.UtcNow.AddMinutes(-10));
        var behindId = await Host().AddWaiting(59, lesson.ClassId, behindSeats, DateTime.UtcNow.AddMinutes(-5));

        // Act
        var response = await Host().User(57).CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Host().WaitlistStatusOf(headId)).ShouldBe(WaitlistStatus.Waiting);
        (await Host().WaitlistStatusOf(behindId)).ShouldBe(WaitlistStatus.Waiting);
        var headUserId = await Host().UserId(58);
        var behindUserId = await Host().UserId(59);
        (await Host().ActiveCount(headUserId, lesson.ClassId)).ShouldBe(0);
        (await Host().ActiveCount(behindUserId, lesson.ClassId)).ShouldBe(0);
        (await Host().RemainingVisits(headUserId)).ShouldBe(waiterVisits);
        (await Host().RemainingVisits(behindUserId)).ShouldBe(waiterVisits);
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
        var validUntil = ClubTime.Today().AddDays(30);
        var lesson = await Host().Lesson().Capacity(8).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddHours(3))).Pass(60, visits, validUntil).Create();
        var created = await Host().User(60).Book(lesson, seats);
        await Host().SetPass(61, headVisits, validUntil);
        await Host().SetPass(62, nextVisits, validUntil);
        await Host().SetPass(63, behindVisits, validUntil);
        var headId = await Host().AddWaiting(61, lesson.ClassId, headSeats, DateTime.UtcNow.AddMinutes(-15));
        var nextId = await Host().AddWaiting(62, lesson.ClassId, nextSeats, DateTime.UtcNow.AddMinutes(-10));
        var behindId = await Host().AddWaiting(63, lesson.ClassId, behindSeats, DateTime.UtcNow.AddMinutes(-5));

        // Act
        var response = await Host().User(60).CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Host().WaitlistStatusOf(headId)).ShouldBe(WaitlistStatus.Waiting);
        (await Host().WaitlistStatusOf(nextId)).ShouldBe(WaitlistStatus.Promoted);
        (await Host().WaitlistStatusOf(behindId)).ShouldBe(WaitlistStatus.Promoted);
        var headUserId = await Host().UserId(61);
        var nextUserId = await Host().UserId(62);
        var behindUserId = await Host().UserId(63);
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
        var waiterVisits = 5;
        var validUntil = ClubTime.Today().AddDays(30);
        var lesson = await Host().Lesson().Capacity(3).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddHours(3))).Pass(64, 10, validUntil).Create();
        var created = await Host().User(64).Book(lesson, seats);
        await Host().SetPass(65, waiterVisits, ClubTime.Today().AddDays(-1));
        await Host().SetPass(66, waiterVisits, validUntil);
        await Host().SetPass(67, waiterVisits, validUntil);
        var headId = await Host().AddWaiting(65, lesson.ClassId, headSeats, DateTime.UtcNow.AddMinutes(-15));
        var nextId = await Host().AddWaiting(66, lesson.ClassId, nextSeats, DateTime.UtcNow.AddMinutes(-10));
        var behindId = await Host().AddWaiting(67, lesson.ClassId, behindSeats, DateTime.UtcNow.AddMinutes(-5));

        // Act
        var response = await Host().User(64).CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Host().WaitlistStatusOf(headId)).ShouldBe(WaitlistStatus.Waiting);
        (await Host().WaitlistStatusOf(nextId)).ShouldBe(WaitlistStatus.Promoted);
        (await Host().WaitlistStatusOf(behindId)).ShouldBe(WaitlistStatus.Waiting);
        var nextUserId = await Host().UserId(66);
        var behindUserId = await Host().UserId(67);
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
        var validUntil = ClubTime.Today().AddDays(30);
        var lesson = await Host().Lesson().Capacity(8).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddMinutes(30))).Pass(68, visits, validUntil).Create();
        var created = await Host().User(68).Book(lesson, seats);
        await Host().SetPass(69, waiterVisits, validUntil);
        var waiterId = await Host().AddWaiting(69, lesson.ClassId, waiterSeats, DateTime.UtcNow.AddMinutes(-5));

        // Act
        var response = await Host().User(68).CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadBody<CancelEnrollmentResponse>();
        body.RefundedVisits.ShouldBe(0);
        (await Host().WaitlistStatusOf(waiterId)).ShouldBe(WaitlistStatus.Promoted);
        var waiterUserId = await Host().UserId(69);
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
        var validUntil = ClubTime.Today().AddDays(30);
        var lesson = await Host().Lesson().Capacity(2).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddHours(3))).Pass(71, visits, validUntil).Create();
        await Host().SetPass(72, visits, validUntil);
        await Host().SetPass(73, visits, validUntil);
        var first = await Host().User(71).Book(lesson, bookedSeats);
        var second = await Host().User(72).Book(lesson, bookedSeats);
        var waiterId = await Host().AddWaiting(73, lesson.ClassId, requestedSeats, DateTime.UtcNow.AddMinutes(-5));

        // Act
        var firstCancel = await Host().User(71).CancelEnrollment(first.Id);
        var afterFirst = await Host().WaitlistStatusOf(waiterId);
        var secondCancel = await Host().User(72).CancelEnrollment(second.Id);

        // Assert
        firstCancel.StatusCode.ShouldBe(HttpStatusCode.OK);
        afterFirst.ShouldBe(WaitlistStatus.Waiting);
        secondCancel.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Host().WaitlistStatusOf(waiterId)).ShouldBe(WaitlistStatus.Promoted);
        var waiterUserId = await Host().UserId(73);
        (await Host().ActiveSeats(waiterUserId, lesson.ClassId)).ShouldBe(requestedSeats);
        (await Host().RemainingVisits(waiterUserId)).ShouldBe(visits - requestedSeats);
        (await Host().RemainingVisits(lesson.UserId)).ShouldBe(visits);
        (await Host().RemainingVisits(await Host().UserId(72))).ShouldBe(visits);
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
        var validUntil = ClubTime.Today().AddDays(30);
        var lesson = await Host().Lesson()
            .Capacity(2)
            .StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddHours(3)))
            .Pass(75, visits, validUntil)
            .Create();
        var created = await Host().User(75).Book(lesson, bookedSeats);
        var ownEntryId = await Host().AddWaiting(75, lesson.ClassId, ownRequestSeats, DateTime.UtcNow.AddMinutes(-10));
        await Host().SetPass(76, nextVisits, validUntil);
        var nextEntryId = await Host().AddWaiting(76, lesson.ClassId, nextRequestSeats, DateTime.UtcNow.AddMinutes(-5));

        // Act
        var response = await Host().User(75).CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadBody<CancelEnrollmentResponse>();
        body.RefundedVisits.ShouldBe(bookedSeats);
        var callerId = await Host().UserId(75);
        (await Host().WaitlistStatusOf(ownEntryId)).ShouldBe(WaitlistStatus.Cancelled);
        (await Host().ActiveCount(callerId, lesson.ClassId)).ShouldBe(0);
        (await Host().RemainingVisits(callerId)).ShouldBe(visits);
        (await Host().WaitlistStatusOf(nextEntryId)).ShouldBe(WaitlistStatus.Promoted);
        var nextUserId = await Host().UserId(76);
        (await Host().ActiveSeats(nextUserId, lesson.ClassId)).ShouldBe(nextRequestSeats);
        (await Host().RemainingVisits(nextUserId)).ShouldBe(nextVisits - nextRequestSeats);
    }

    [TestMethod]
    public async Task Cancel_WhenTheIdIsNotNumeric_ReturnsBadRequest()
    {
        // Arrange
        var user = Host().User(70);

        // Act
        var response = await user.CancelEnrollment("abc");

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
        var leadTime = TimeSpan.FromHours(2);
        var validUntil = new DateOnly(2032, 4, 1);
        using var clock = Host().Freeze(now);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(4)
            .StartsAt(now.Add(leadTime))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await Host().User(member.Number).Book(lesson, seats);

        // Act
        var response = await Host().User(member.Number).CancelEnrollment(created.Id);

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
        var startsAt = bookedAt.AddHours(4);
        var seats = 1;
        var visits = 4;
        var validUntil = new DateOnly(2032, 7, 1);
        using var clock = Host().Freeze(bookedAt);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(4)
            .StartsAt(startsAt)
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await Host().User(member.Number).Book(lesson, seats);
        clock.MoveTo(startsAt);

        // Act
        var response = await Host().User(member.Number).CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReasonText()).ShouldBe("class-started");
        (await Host().RemainingVisits(lesson.UserId)).ShouldBe(visits - seats);
    }
}
