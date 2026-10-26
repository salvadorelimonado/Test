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
        var startsAt = ClubTime.WholeSecond(DateTime.UtcNow.AddDays(2));
        var lesson = await Host().Lesson().Capacity(8).StartsAt(startsAt).Pass(80, visits).Create();
        var created = await Host().User(80).Book(lesson, seats);

        // Act
        var response = await Host().User(80).GetEnrollment(created.Id);

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
        var lesson = await Host().Lesson().Capacity(8).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddHours(3))).Pass(81, visits).Create();
        var created = await Host().User(81).Book(lesson, seats);
        var cancel = await Host().User(81).CancelEnrollment(created.Id);
        cancel.EnsureSuccessStatusCode();

        // Act
        var response = await Host().User(81).GetEnrollment(created.Id);

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
        var lesson = await Host().Lesson().Capacity(8).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddDays(2))).Pass(82, 10).Create();
        var created = await Host().User(82).Book(lesson, seats);
        await Host().CancelClass(lesson.ClassId);

        // Act
        var response = await Host().User(82).GetEnrollment(created.Id);

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
        // Act
        var response = await Host().User(83).GetEnrollment(MissingEnrollmentId);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task Get_WhenEnrollmentBelongsToAnotherUser_ReturnsNotFound()
    {
        // Arrange
        var lesson = await Host().Lesson().Capacity(8).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddDays(2))).Pass(84, 10).Create();
        var created = await Host().User(84).Book(lesson, 1);

        // Act
        var response = await Host().User(85).GetEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task Get_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange
        var lesson = await Host().Lesson().Capacity(8).StartsAt(ClubTime.WholeSecond(DateTime.UtcNow.AddDays(2))).Pass(86, 10).Create();
        var created = await Host().User(86).Book(lesson, 1);

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
        var startsAt = ClubTime.WholeSecond(DateTime.UtcNow.AddHours(3));
        var validUntil = ClubTime.Today().AddDays(30);
        var waiterClass = await Host().Lesson().Capacity(8).StartsAt(startsAt).Pass(89, waiterVisits, validUntil).Create();
        var waiterEnrollment = await Host().User(89).Book(waiterClass, waiterBookedSeats);
        var ownerClass = await Host().Lesson().Capacity(8).StartsAt(startsAt).Pass(88, 6, validUntil).Create();
        var ownerEnrollment = await Host().User(88).Book(ownerClass, ownerSeats);
        await Host().AddWaiting(89, ownerClass.ClassId, waiterRequestedSeats, DateTime.UtcNow.AddMinutes(-5));
        var cached = await Host().User(89).GetEnrollment(waiterEnrollment.Id);
        cached.EnsureSuccessStatusCode();
        var cachedCard = await cached.ReadBody<EnrollmentCard>();
        var cancel = await Host().User(88).CancelEnrollment(ownerEnrollment.Id);
        cancel.EnsureSuccessStatusCode();

        // Act
        var response = await Host().User(89).GetEnrollment(waiterEnrollment.Id);

        // Assert
        cachedCard.Pass.RemainingVisits.ShouldBe(waiterVisits - waiterBookedSeats);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var card = await response.ReadBody<EnrollmentCard>();
        card.Seats.ShouldBe(waiterBookedSeats);
        card.Pass.RemainingVisits.ShouldBe(waiterVisits - waiterBookedSeats - waiterRequestedSeats);
    }

    [TestMethod]
    public async Task Get_WhenTheIdIsNotNumeric_ReturnsBadRequest()
    {
        // Arrange
        var user = Host().User(90);

        // Act
        var response = await user.GetEnrollment("abc");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
