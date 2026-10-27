using System.Net;
using DataAccessLayer.Entities;
using WebApi.Features.Enrollments;
using Shouldly;
using WebApi.Tests.Infrastructure.DTO;
using WebApi.Tests.Infrastructure;
using WebApi.Tests.Infrastructure.Http;
using WebApi.Tests.Infrastructure.Time;

namespace WebApi.Tests.Features.Enrollments;

[TestClass]
public class GetEnrollmentTests : HostTest
{
    private const long MissingEnrollmentId = 9_000_000_000;

    [TestMethod]
    public async Task Get_ReturnsTheEnrollmentCard()
    {
        // Arrange
        var seats = 2;
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
        var created = await member.Enrollments.Book(lesson, seats);

        // Act
        var response = await member.Enrollments.GetEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var card = await response.ReadBody<EnrollmentCard>();
        card.Id.ShouldBe(created.Id);
        card.Status.ShouldBe(EnrollmentStatus.Active);
        card.Seats.ShouldBe(seats);
        card.CreatedAt.ShouldBe(created.CreatedAt);
        card.Class.Id.ShouldBe(lesson.ClassId);
        card.Class.Title.ShouldBe(Lesson.DefaultTitle);
        card.Class.StartsAt.ShouldBe(startsAt);
        card.Class.DurationMinutes.ShouldBe(Lesson.DefaultDurationMinutes);
        card.Class.IsCancelled.ShouldBeFalse();
        card.Class.Room.Id.ShouldBe(lesson.RoomId);
        card.Class.Room.Name.ShouldBe(lesson.RoomName);
        card.Pass.RemainingVisits.ShouldBe(visits - seats);
        card.Pass.ValidUntil.ShouldBe(lesson.ValidUntil);
    }

    [TestMethod]
    public async Task Get_WhenEnrollmentIsCancelled_ReturnsCancelledCard()
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
        var cancel = await member.Enrollments.CancelEnrollment(created.Id);
        cancel.EnsureSuccessStatusCode();

        // Act
        var response = await member.Enrollments.GetEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var card = await response.ReadBody<EnrollmentCard>();
        card.Status.ShouldBe(EnrollmentStatus.Cancelled);
        card.Seats.ShouldBe(seats);
        card.Pass.RemainingVisits.ShouldBe(visits);
        card.Class.IsCancelled.ShouldBeFalse();
    }

    [TestMethod]
    public async Task Get_WhenClassIsCancelled_ReturnsTheCard()
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
        await Classes.CancelClass(lesson.ClassId);

        // Act
        var response = await member.Enrollments.GetEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var card = await response.ReadBody<EnrollmentCard>();
        card.Class.IsCancelled.ShouldBeTrue();
        card.Status.ShouldBe(EnrollmentStatus.Active);
        card.Seats.ShouldBe(seats);
    }

    [TestMethod]
    public async Task Get_WhenEnrollmentIsMissing_ReturnsNotFound()
    {
        // Arrange
        var visits = 10;
        var passValidityDays = 30;
        var member = await Users.RegisterMember(visits, Session.Today.AddDays(passValidityDays));

        // Act
        var response = await member.Enrollments.GetEnrollment(MissingEnrollmentId);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task Get_WhenEnrollmentBelongsToAnotherUser_ReturnsNotFound()
    {
        // Arrange
        var seats = 1;
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
        var response = await other.Enrollments.GetEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task Get_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange
        var seats = 1;
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
        var response = await Users.Anonymous().Enrollments.GetEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task Get_WhenAnotherUserIsPromoted_RefreshesThatUsersCachedCard()
    {
        // Arrange
        var ownerSeats = 1;
        var waiterBookedSeats = 2;
        var waiterRequestedSeats = 1;
        var waiterVisits = 10;
        var ownerVisits = 6;
        var capacity = 8;
        var leadHours = 3;
        var queuedAgo = 5;
        var passValidityDays = 30;
        var startsAt = Session.Now.AddHours(leadHours).WholeSecond();
        var validUntil = Session.Today.AddDays(passValidityDays);
        var waiter = await Users.RegisterMember(waiterVisits, validUntil);
        var owner = await Users.RegisterMember(ownerVisits, validUntil);
        var waiterClass = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(startsAt)
            .Pass(waiter.Number, waiterVisits, validUntil)
            .Create();
        var waiterEnrollment = await waiter.Enrollments.Book(waiterClass, waiterBookedSeats);
        var ownerClass = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(startsAt)
            .Pass(owner.Number, ownerVisits, validUntil)
            .Create();
        var ownerEnrollment = await owner.Enrollments.Book(ownerClass, ownerSeats);
        await Waitlist.AddWaiting(waiter.Number, ownerClass.ClassId, waiterRequestedSeats, Session.Now.AddMinutes(-queuedAgo));
        var cached = await waiter.Enrollments.GetEnrollment(waiterEnrollment.Id);
        cached.EnsureSuccessStatusCode();
        var cachedCard = await cached.ReadBody<EnrollmentCard>();
        var cancel = await owner.Enrollments.CancelEnrollment(ownerEnrollment.Id);
        cancel.EnsureSuccessStatusCode();

        // Act
        var response = await waiter.Enrollments.GetEnrollment(waiterEnrollment.Id);

        // Assert
        cachedCard.Pass.RemainingVisits.ShouldBe(waiterVisits - waiterBookedSeats);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var card = await response.ReadBody<EnrollmentCard>();
        card.Seats.ShouldBe(waiterBookedSeats);
        card.Pass.RemainingVisits.ShouldBe(waiterVisits - waiterBookedSeats - waiterRequestedSeats);
    }

    [TestMethod]
    public async Task Get_WhenAnIncreasePromotesAnotherClient_RefreshesThatClientsCachedCard()
    {
        // Arrange
        var bookedSeats = 1;
        var increasedSeats = 2;
        var waiterSeats = 1;
        var otherSeats = 1;
        var visits = 6;
        var capacity = 4;
        var leadHours = 5;
        var laterHours = 1;
        var queuedAgo = 5;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var startsAt = Session.Now.AddHours(leadHours).WholeSecond();
        var waiter = await Users.RegisterMember(visits, validUntil);
        var owner = await Users.RegisterMember(visits, validUntil);
        var otherClass = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(startsAt)
            .Pass(waiter.Number, visits, validUntil)
            .Create();
        var otherBooking = await waiter.Enrollments.Book(otherClass, otherSeats);
        var cached = await waiter.Enrollments.GetEnrollment(otherBooking.Id);
        cached.EnsureSuccessStatusCode();
        var target = await Classes.Lesson()
            .Capacity(capacity)
            .StartsAt(startsAt.AddHours(laterHours))
            .Pass(owner.Number, visits, validUntil)
            .Create();
        var ownerBooking = await owner.Enrollments.Book(target, bookedSeats);
        await Waitlist.AddWaiting(waiter.Number, target.ClassId, waiterSeats, Session.Now.AddMinutes(-queuedAgo));

        // Act
        var increased = await owner.Enrollments.ChangeSeats(ownerBooking.Id, increasedSeats);
        var response = await waiter.Enrollments.GetEnrollment(otherBooking.Id);

        // Assert
        increased.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var card = await response.ReadBody<EnrollmentCard>();
        card.Pass.RemainingVisits.ShouldBe(visits - otherSeats - waiterSeats);
    }

    [TestMethod]
    public async Task Get_WhenTheIdIsNotNumeric_ReturnsBadRequest()
    {
        // Arrange
        var visits = 10;
        var passValidityDays = 30;
        var member = await Users.RegisterMember(visits, Session.Today.AddDays(passValidityDays));

        // Act
        var response = await member.Enrollments.GetEnrollment("abc");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
