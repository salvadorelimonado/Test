using System.Net;
using Shouldly;
using WebApi.Features.Schedule;
using WebApi.Tests.Infrastructure;

namespace WebApi.Tests.Schedule;

[TestClass]
public class GetScheduleTests : ClubTest
{
    private const long MissingRoomId = 9_000_000_000;
    private static readonly DateTime Anchor = new(2099, 3, 1, 8, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task Get_ReturnsClassesInOrderWithFreeAndMySeats()
    {
        // Arrange
        var firstSeats = 2;
        var secondSeats = 1;
        var otherSeats = 3;
        var capacity = 10;
        var visits = 10;
        var validUntil = ClubTime.Today().AddDays(30);
        var rooms = await Host().Rooms(2);
        var first = await Host().Lesson().Capacity(capacity).StartsAt(Anchor).Room(rooms[0].Id).Pass(100, visits, validUntil).Create();
        await Host().SetPass(101, visits, validUntil);
        var second = await Host().Lesson().Capacity(capacity).StartsAt(Anchor.AddHours(2)).Room(rooms[0].Id).Pass(100, visits, validUntil).Create();
        await Host().User(100).Book(first, firstSeats);
        await Host().User(100).Book(first, secondSeats);
        await Host().User(101).Book(first, otherSeats);

        // Act
        var response = await Host().User(100).Schedule(Anchor, Anchor.AddHours(3));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.ReadBody<SchedulePage>();
        page.ContinuationToken.ShouldBeNull();
        page.Items.Count.ShouldBe(2);
        page.Items[0].ClassId.ShouldBe(first.ClassId);
        page.Items[0].Title.ShouldBe(Lesson.DefaultTitle);
        page.Items[0].StartsAt.ShouldBe(Anchor);
        page.Items[0].DurationMinutes.ShouldBe(Lesson.DefaultDurationMinutes);
        page.Items[0].Room.Id.ShouldBe(rooms[0].Id);
        page.Items[0].Room.Name.ShouldBe(rooms[0].Name);
        page.Items[0].Capacity.ShouldBe(capacity);
        page.Items[0].FreeSeats.ShouldBe(capacity - (firstSeats + secondSeats + otherSeats));
        page.Items[0].MySeats.ShouldBe(firstSeats + secondSeats);
        page.Items[1].ClassId.ShouldBe(second.ClassId);
        page.Items[1].FreeSeats.ShouldBe(capacity);
        page.Items[1].MySeats.ShouldBe(0);
    }

    [TestMethod]
    public async Task Get_WhenFromHasNoOffset_TreatsItAsUtc()
    {
        // Arrange
        var startsAt = Anchor.AddDays(1);
        var lesson = await Host().Lesson().Capacity(8).StartsAt(startsAt).Pass(102, 4).Create();

        // Act
        var response = await Host().User(102).Schedule(
            startsAt.ToString("yyyy-MM-ddTHH:mm:ss"),
            startsAt.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ss"));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.ReadBody<SchedulePage>();
        page.Items.Count.ShouldBe(1);
        page.Items[0].ClassId.ShouldBe(lesson.ClassId);
    }

    [TestMethod]
    public async Task Get_WhenBoundsAreInvalid_ReturnsBadRequest()
    {
        // Arrange
        var from = Anchor.AddDays(2);
        var user = Host().User(103);

        // Act
        var missingFrom = await user.Schedule(null, from.ToString("yyyy-MM-ddTHH:mm:ss"));
        var equalBounds = await user.Schedule(from, from);
        var unitedStatesDate = await user.Schedule("10/22/2026 10:00", from.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ss"));

        // Assert
        missingFrom.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        equalBounds.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        unitedStatesDate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task Get_WhenLimitOrTokenIsInvalid_ReturnsBadRequest()
    {
        // Arrange
        var from = Anchor.AddDays(3);
        var to = from.AddHours(1);
        var user = Host().User(104);

        // Act
        var smallLimit = await user.Schedule(from, to, SchedulePaginationLimits.MinPageSize - 1);
        var largeLimit = await user.Schedule(from, to, SchedulePaginationLimits.MaxPageSize + 1);
        var badToken = await user.Schedule(from, to, SchedulePaginationLimits.DefaultPageSize, continuation: "not-a-token");

        // Assert
        smallLimit.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        largeLimit.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        badToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task Get_WhenBoundsHaveNoTime_UsesTheStartOfThatUtcDay()
    {
        // Arrange
        var day = new DateTime(2099, 3, 11, 0, 0, 0, DateTimeKind.Utc);
        var inside = await Host().Lesson().Capacity(8).StartsAt(day.AddHours(23).AddMinutes(30)).Pass(111, 4).Create();
        await Host().Lesson().Capacity(8).StartsAt(day.AddDays(1)).Pass(111, 4).Create();

        // Act
        var response = await Host().User(111).Schedule("2099-03-11", "2099-03-12");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.ReadBody<SchedulePage>();
        page.Items.Count.ShouldBe(1);
        page.Items[0].ClassId.ShouldBe(inside.ClassId);
    }

    [TestMethod]
    public async Task Get_WhenTheBoundHasAnOffset_ConvertsItToUtc()
    {
        // Arrange
        var startsAt = new DateTime(2099, 3, 13, 10, 0, 0, DateTimeKind.Utc);
        var lesson = await Host().Lesson().Capacity(8).StartsAt(startsAt).Pass(112, 4).Create();

        // Act
        var response = await Host().User(112).Schedule("2099-03-13T12:30:00+03:00", "2099-03-13T14:00:00+03:00");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.ReadBody<SchedulePage>();
        page.Items.Count.ShouldBe(1);
        page.Items[0].ClassId.ShouldBe(lesson.ClassId);
        page.Items[0].StartsAt.ShouldBe(startsAt);
    }

    [TestMethod]
    public async Task Get_PagesByStartThenIdAndUsesTheDefaultLimit()
    {
        // Arrange
        var sharedStartsAt = Anchor.AddDays(4);
        var laterStartsAt = sharedStartsAt.AddHours(2);
        var rooms = await Host().Rooms(1);
        var first = await Host().Lesson().Capacity(6).StartsAt(sharedStartsAt).Room(rooms[0].Id).Pass(105, 4).Create();
        var second = await Host().Lesson().Capacity(6).StartsAt(sharedStartsAt).Room(rooms[0].Id).Pass(105, 4).Create();
        var third = await Host().Lesson().Capacity(6).StartsAt(laterStartsAt).Room(rooms[0].Id).Pass(105, 4).Create();
        var user = Host().User(105);

        // Act
        var firstPage = await user.Schedule(sharedStartsAt, laterStartsAt.AddHours(1), SchedulePaginationLimits.MinPageSize);
        var firstBody = await firstPage.ReadBody<SchedulePage>();
        var secondPage = await user.Schedule(sharedStartsAt, laterStartsAt.AddHours(1), SchedulePaginationLimits.MinPageSize, continuation: firstBody.ContinuationToken);
        var secondBody = await secondPage.ReadBody<SchedulePage>();
        var thirdPage = await user.Schedule(sharedStartsAt, laterStartsAt.AddHours(1), SchedulePaginationLimits.MinPageSize, continuation: secondBody.ContinuationToken);

        // Assert
        firstPage.StatusCode.ShouldBe(HttpStatusCode.OK);
        firstBody.Items.Count.ShouldBe(1);
        firstBody.Items[0].ClassId.ShouldBe(first.ClassId);
        firstBody.ContinuationToken.ShouldNotBeNull();
        secondPage.StatusCode.ShouldBe(HttpStatusCode.OK);
        secondBody.Items[0].ClassId.ShouldBe(second.ClassId);
        secondBody.ContinuationToken.ShouldNotBeNull();
        thirdPage.StatusCode.ShouldBe(HttpStatusCode.OK);
        var thirdBody = await thirdPage.ReadBody<SchedulePage>();
        thirdBody.Items[0].ClassId.ShouldBe(third.ClassId);
        thirdBody.ContinuationToken.ShouldBeNull();
    }

    [TestMethod]
    public async Task Get_WithoutLimit_ReturnsTheDefaultPage()
    {
        // Arrange
        var classCount = SchedulePaginationLimits.DefaultPageSize + 1;
        var startsAt = Anchor.AddDays(5);
        var rooms = await Host().Rooms(1);
        for (var index = 0; index < classCount; index++)
        {
            await Host().Lesson().Capacity(6).StartsAt(startsAt.AddMinutes(index)).Room(rooms[0].Id).Pass(106, 4).Create();
        }

        // Act
        var response = await Host().User(106).Schedule(startsAt, startsAt.AddDays(1));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.ReadBody<SchedulePage>();
        page.Items.Count.ShouldBe(SchedulePaginationLimits.DefaultPageSize);
        page.ContinuationToken.ShouldNotBeNull();
    }

    [TestMethod]
    public async Task Get_ExcludesCancelledClassesAndTheExclusiveEnd()
    {
        // Arrange
        var from = Anchor.AddDays(6);
        var included = await Host().Lesson().Capacity(8).StartsAt(from).Pass(107, 4).Create();
        await Host().Lesson().Capacity(8).StartsAt(from.AddHours(1)).Cancelled().Pass(107, 4).Create();
        await Host().Lesson().Capacity(8).StartsAt(from.AddHours(2)).Pass(107, 4).Create();

        // Act
        var response = await Host().User(107).Schedule(from, from.AddHours(2));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.ReadBody<SchedulePage>();
        page.Items.Count.ShouldBe(1);
        page.Items[0].ClassId.ShouldBe(included.ClassId);
    }

    [TestMethod]
    public async Task Get_FiltersByRoomAndReturnsEmptyForAnUnknownRoom()
    {
        // Arrange
        var startsAt = Anchor.AddDays(7);
        var rooms = await Host().Rooms(2);
        var first = await Host().Lesson().Capacity(8).StartsAt(startsAt).Room(rooms[0].Id).Pass(108, 4).Create();
        await Host().Lesson().Capacity(8).StartsAt(startsAt.AddHours(1)).Room(rooms[1].Id).Pass(108, 4).Create();
        var user = Host().User(108);

        // Act
        var filtered = await user.Schedule(startsAt, startsAt.AddHours(2), roomId: rooms[0].Id);
        var unknown = await user.Schedule(startsAt, startsAt.AddHours(2), roomId: MissingRoomId);

        // Assert
        filtered.StatusCode.ShouldBe(HttpStatusCode.OK);
        var filteredPage = await filtered.ReadBody<SchedulePage>();
        filteredPage.Items.Count.ShouldBe(1);
        filteredPage.Items[0].ClassId.ShouldBe(first.ClassId);
        filteredPage.Items[0].Room.Id.ShouldBe(rooms[0].Id);
        unknown.StatusCode.ShouldBe(HttpStatusCode.OK);
        var unknownPage = await unknown.ReadBody<SchedulePage>();
        unknownPage.Items.Count.ShouldBe(0);
        unknownPage.ContinuationToken.ShouldBeNull();
    }

    [TestMethod]
    public async Task Get_WhenEnrollmentIsCancelled_DoesNotCountItsSeats()
    {
        // Arrange
        var seats = 4;
        var capacity = 10;
        var startsAt = Anchor.AddDays(8);
        var lesson = await Host().Lesson().Capacity(capacity).StartsAt(startsAt).Pass(109, 8).Create();
        await Host().User(109).Book(lesson, seats);
        var cancel = await Host().User(109).CancelEnrollment(await Host().ActiveEnrollmentId(lesson.UserId, lesson.ClassId));
        cancel.EnsureSuccessStatusCode();

        // Act
        var response = await Host().User(109).Schedule(startsAt, startsAt.AddHours(1));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.ReadBody<SchedulePage>();
        page.Items.Count.ShouldBe(1);
        page.Items[0].FreeSeats.ShouldBe(capacity);
        page.Items[0].MySeats.ShouldBe(0);
    }

    [TestMethod]
    public async Task Get_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange
        var from = Anchor.AddDays(9);

        // Act
        var response = await Host().Anonymous().Schedule(from, from.AddHours(1));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task Get_OnThe61stRequest_ReturnsTooManyRequests()
    {
        // Arrange
        var user = Host().User(110);

        // Act
        var lastStatus = await user.Hammer(_ => user.Schedule());

        // Assert
        lastStatus.ShouldBe(HttpStatusCode.TooManyRequests);
    }
}
