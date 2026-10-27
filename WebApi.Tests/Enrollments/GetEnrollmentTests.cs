using System.Net;
using DataAccessLayer.Entities;
using WebApi.Features.Enrollments;
using Shouldly;
using WebApi.Tests.Infrastructure;

namespace WebApi.Tests.Enrollments;

[TestClass]
public class GetEnrollmentTests : ClubTest
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
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var startsAt = ClubTime.WholeSecond(Host().Now.AddDays(leadDays));
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(startsAt)
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Book(lesson, seats);

        // Act
        var response = await member.GetEnrollment(created.Id);

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
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(ClubTime.WholeSecond(Host().Now.AddHours(leadHours)))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Book(lesson, seats);
        var cancel = await member.CancelEnrollment(created.Id);
        cancel.EnsureSuccessStatusCode();

        // Act
        var response = await member.GetEnrollment(created.Id);

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
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(ClubTime.WholeSecond(Host().Now.AddDays(leadDays)))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Book(lesson, seats);
        await Host().CancelClass(lesson.ClassId);

        // Act
        var response = await member.GetEnrollment(created.Id);

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
        var member = await Host().RegisterMember(visits, ClubTime.Today().AddDays(passValidityDays));

        // Act
        var response = await member.GetEnrollment(MissingEnrollmentId);

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
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var owner = await Host().RegisterMember(visits, validUntil);
        var other = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(ClubTime.WholeSecond(Host().Now.AddDays(leadDays)))
            .Pass(owner.Number, visits, validUntil)
            .Create();
        var created = await owner.Book(lesson, seats);

        // Act
        var response = await other.GetEnrollment(created.Id);

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
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(ClubTime.WholeSecond(Host().Now.AddDays(leadDays)))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await member.Book(lesson, seats);

        // Act
        var response = await Host().Anonymous().GetEnrollment(created.Id);

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
        var startsAt = ClubTime.WholeSecond(Host().Now.AddHours(leadHours));
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var waiter = await Host().RegisterMember(waiterVisits, validUntil);
        var owner = await Host().RegisterMember(ownerVisits, validUntil);
        var waiterClass = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(startsAt)
            .Pass(waiter.Number, waiterVisits, validUntil)
            .Create();
        var waiterEnrollment = await waiter.Book(waiterClass, waiterBookedSeats);
        var ownerClass = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(startsAt)
            .Pass(owner.Number, ownerVisits, validUntil)
            .Create();
        var ownerEnrollment = await owner.Book(ownerClass, ownerSeats);
        await Host().AddWaiting(waiter.Number, ownerClass.ClassId, waiterRequestedSeats, Host().Now.AddMinutes(-queuedAgo));
        var cached = await waiter.GetEnrollment(waiterEnrollment.Id);
        cached.EnsureSuccessStatusCode();
        var cachedCard = await cached.ReadBody<EnrollmentCard>();
        var cancel = await owner.CancelEnrollment(ownerEnrollment.Id);
        cancel.EnsureSuccessStatusCode();

        // Act
        var response = await waiter.GetEnrollment(waiterEnrollment.Id);

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
        var validUntil = ClubTime.Today().AddDays(passValidityDays);
        var startsAt = ClubTime.WholeSecond(Host().Now.AddHours(leadHours));
        var waiter = await Host().RegisterMember(visits, validUntil);
        var owner = await Host().RegisterMember(visits, validUntil);
        var otherClass = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(startsAt)
            .Pass(waiter.Number, visits, validUntil)
            .Create();
        var otherBooking = await waiter.Book(otherClass, otherSeats);
        var cached = await waiter.GetEnrollment(otherBooking.Id);
        cached.EnsureSuccessStatusCode();
        var target = await Host().Lesson()
            .Capacity(capacity)
            .StartsAt(startsAt.AddHours(laterHours))
            .Pass(owner.Number, visits, validUntil)
            .Create();
        var ownerBooking = await owner.Book(target, bookedSeats);
        await Host().AddWaiting(waiter.Number, target.ClassId, waiterSeats, Host().Now.AddMinutes(-queuedAgo));

        // Act
        var increased = await owner.ChangeSeats(ownerBooking.Id, increasedSeats);
        var response = await waiter.GetEnrollment(otherBooking.Id);

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
        var member = await Host().RegisterMember(visits, ClubTime.Today().AddDays(passValidityDays));

        // Act
        var response = await member.GetEnrollment("abc");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
