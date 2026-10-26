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
        // Act
        var response = await Host().User(7).Enroll(long.MaxValue, 1);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
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
    public async Task Create_WhenTheQueueIsNotEmpty_ReturnsWaitlistNotEmpty()
    {
        // Arrange
        var visits = 6;
        var lesson = await Host().Lesson().Capacity(4).StartsAt(DateTime.UtcNow.AddDays(2)).Pass(14, visits).Create();
        await Host().AddWaiting(15, lesson.ClassId, 2, DateTime.UtcNow.AddMinutes(-5));

        // Act
        var response = await Host().User(14).Enroll(lesson, 1);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReason()).ShouldBe(ConflictReason.WaitlistNotEmpty);
        var stored = await Host().ActiveBooking(lesson.ClassId, lesson.UserId);
        stored.Count.ShouldBe(0);
        stored.RemainingVisits.ShouldBe(visits);
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
    public async Task Create_WhenClassIdIsZeroOrTheBodyIsUnreadable_ReturnsBadRequest()
    {
        // Arrange
        var member = await Host().RegisterMember(4, ClubTime.Today().AddDays(30));
        var user = Host().User(member.Number);
        var missingKey = Guid.NewGuid().ToString("N");
        var brokenKey = Guid.NewGuid().ToString("N");
        var zeroKey = Guid.NewGuid().ToString("N");
        var zeroClass = """{"classId":0,"seats":1}""";
        var brokenJson = "{";

        // Act
        var missingBody = await user.Post("/enrollments", null, missingKey);
        var brokenBody = await user.Post("/enrollments", brokenJson, brokenKey);
        var zeroClassBody = await user.Post("/enrollments", zeroClass, zeroKey);

        // Assert
        missingBody.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        brokenBody.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        zeroClassBody.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
