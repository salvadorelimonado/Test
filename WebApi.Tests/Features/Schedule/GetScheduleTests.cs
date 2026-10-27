using System.Net;
using Shouldly;
using WebApi.Features.Schedule.GetSchedule;
using WebApi.Tests.Infrastructure.DTO;
using WebApi.Tests.Infrastructure;
using WebApi.Tests.Infrastructure.Http;
using WebApi.Tests.Infrastructure.Http.Clients;
using WebApi.Tests.Infrastructure.Time;

namespace WebApi.Tests.Features.Schedule;

[TestClass]
public class GetScheduleTests : HostTest
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
        var passValidityDays = 30;
        var roomCount = 2;
        var hoursUntilSecondClass = 2;
        var windowHours = 3;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var other = await Users.RegisterMember(visits, validUntil);
        var rooms = await Classes.Rooms(roomCount);
        var first = await Classes.Lesson().Capacity(capacity).StartsAt(Anchor).Room(rooms[0].Id).Pass(member.Number, visits, validUntil).Create();
        var second = await Classes.Lesson().Capacity(capacity).StartsAt(Anchor.AddHours(hoursUntilSecondClass)).Room(rooms[0].Id).Pass(member.Number, visits, validUntil).Create();
        await member.Enrollments.Book(first, firstSeats);
        await member.Enrollments.Book(first, secondSeats);
        await other.Enrollments.Book(first, otherSeats);

        // Act
        var response = await member.Schedule.Get(Anchor, Anchor.AddHours(windowHours));

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
        var daysUntilClass = 1;
        var windowHours = 1;
        var capacity = 8;
        var visits = 4;
        var passValidityDays = 30;
        var startsAt = Anchor.AddDays(daysUntilClass);
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson().Capacity(capacity).StartsAt(startsAt).Pass(member.Number, visits, validUntil).Create();

        // Act
        var response = await member.Schedule.Get(
            startsAt.ToString("yyyy-MM-ddTHH:mm:ss"),
            startsAt.AddHours(windowHours).ToString("yyyy-MM-ddTHH:mm:ss"));

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
        var daysUntilWindow = 2;
        var windowHours = 1;
        var visits = 4;
        var passValidityDays = 30;
        var from = Anchor.AddDays(daysUntilWindow);
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);

        // Act
        var missingFrom = await member.Schedule.Get(null, from.ToString("yyyy-MM-ddTHH:mm:ss"));
        var equalBounds = await member.Schedule.Get(from, from);
        var unitedStatesDate = await member.Schedule.Get("10/22/2026 10:00", from.AddHours(windowHours).ToString("yyyy-MM-ddTHH:mm:ss"));

        // Assert
        missingFrom.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        equalBounds.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        unitedStatesDate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task Get_WhenLimitOrTokenIsInvalid_ReturnsBadRequest()
    {
        // Arrange
        var daysUntilWindow = 3;
        var windowHours = 1;
        var visits = 4;
        var passValidityDays = 30;
        var belowMinimumPageSize = SchedulePaginationLimits.MinPageSize - 1;
        var aboveMaximumPageSize = SchedulePaginationLimits.MaxPageSize + 1;
        var from = Anchor.AddDays(daysUntilWindow);
        var to = from.AddHours(windowHours);
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);

        // Act
        var smallLimit = await member.Schedule.Get(from, to, belowMinimumPageSize);
        var largeLimit = await member.Schedule.Get(from, to, aboveMaximumPageSize);
        var badToken = await member.Schedule.Get(from, to, SchedulePaginationLimits.DefaultPageSize, continuation: "not-a-token");

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
        var eveningHour = 23;
        var eveningMinute = 30;
        var nextDay = 1;
        var capacity = 8;
        var visits = 4;
        var passValidityDays = 30;
        var nextMidnight = day.AddDays(nextDay);
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var inside = await Classes.Lesson().Capacity(capacity).StartsAt(day.AddHours(eveningHour).AddMinutes(eveningMinute)).Pass(member.Number, visits, validUntil).Create();
        await Classes.Lesson().Capacity(capacity).StartsAt(nextMidnight).Pass(member.Number, visits, validUntil).Create();

        // Act
        var response = await member.Schedule.Get(day.ToString("yyyy-MM-dd"), nextMidnight.ToString("yyyy-MM-dd"));

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
        var capacity = 8;
        var visits = 4;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson().Capacity(capacity).StartsAt(startsAt).Pass(member.Number, visits, validUntil).Create();

        // Act
        var response = await member.Schedule.Get("2099-03-13T12:30:00+03:00", "2099-03-13T14:00:00+03:00");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.ReadBody<SchedulePage>();
        page.Items.Count.ShouldBe(1);
        page.Items[0].ClassId.ShouldBe(lesson.ClassId);
        page.Items[0].StartsAt.ShouldBe(startsAt);
    }

    [TestMethod]
    public async Task Get_WhenTheBoundHasFractionalSecondsOrNoSeconds_ParsesItAsUtc()
    {
        // Arrange
        var startsAt = new DateTime(2099, 4, 8, 10, 0, 0, DateTimeKind.Utc);
        var capacity = 8;
        var visits = 4;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson().Capacity(capacity).StartsAt(startsAt).Pass(member.Number, visits, validUntil).Create();
        var atTheStart = "2099-04-08T10:00:00.000Z";
        var fraction = "2099-04-08T10:00:00.1234567Z";
        var oneSecondLater = "2099-04-08T10:00:01Z";
        var minute = "2099-04-08T10:00Z";
        var nextMinute = "2099-04-08T10:01Z";

        // Act
        var throughTheFraction = await member.Schedule.Get(atTheStart, fraction);
        var afterTheFraction = await member.Schedule.Get(fraction, oneSecondLater);
        var wholeMinute = await member.Schedule.Get(minute, nextMinute);

        // Assert
        throughTheFraction.StatusCode.ShouldBe(HttpStatusCode.OK);
        var included = await throughTheFraction.ReadBody<SchedulePage>();
        included.Items.Count.ShouldBe(1);
        included.Items[0].ClassId.ShouldBe(lesson.ClassId);
        afterTheFraction.StatusCode.ShouldBe(HttpStatusCode.OK);
        var excluded = await afterTheFraction.ReadBody<SchedulePage>();
        excluded.Items.Count.ShouldBe(0);
        wholeMinute.StatusCode.ShouldBe(HttpStatusCode.OK);
        var byMinute = await wholeMinute.ReadBody<SchedulePage>();
        byMinute.Items.Count.ShouldBe(1);
        byMinute.Items[0].ClassId.ShouldBe(lesson.ClassId);
    }

    [TestMethod]
    public async Task Get_PagesByStartThenIdAndUsesTheDefaultLimit()
    {
        // Arrange
        var daysUntilClasses = 4;
        var hoursUntilLaterClass = 2;
        var windowHours = 1;
        var roomCount = 1;
        var capacity = 6;
        var visits = 4;
        var passValidityDays = 30;
        var sharedStartsAt = Anchor.AddDays(daysUntilClasses);
        var laterStartsAt = sharedStartsAt.AddHours(hoursUntilLaterClass);
        var to = laterStartsAt.AddHours(windowHours);
        var validUntil = Session.Today.AddDays(passValidityDays);
        var rooms = await Classes.Rooms(roomCount);
        var member = await Users.RegisterMember(visits, validUntil);
        var first = await Classes.Lesson().Capacity(capacity).StartsAt(sharedStartsAt).Room(rooms[0].Id).Pass(member.Number, visits, validUntil).Create();
        var second = await Classes.Lesson().Capacity(capacity).StartsAt(sharedStartsAt).Room(rooms[0].Id).Pass(member.Number, visits, validUntil).Create();
        var third = await Classes.Lesson().Capacity(capacity).StartsAt(laterStartsAt).Room(rooms[0].Id).Pass(member.Number, visits, validUntil).Create();

        // Act
        var firstPage = await member.Schedule.Get(sharedStartsAt, to, SchedulePaginationLimits.MinPageSize);
        var firstBody = await firstPage.ReadBody<SchedulePage>();
        var secondPage = await member.Schedule.Get(sharedStartsAt, to, SchedulePaginationLimits.MinPageSize, continuation: firstBody.ContinuationToken);
        var secondBody = await secondPage.ReadBody<SchedulePage>();
        var thirdPage = await member.Schedule.Get(sharedStartsAt, to, SchedulePaginationLimits.MinPageSize, continuation: secondBody.ContinuationToken);

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
        var daysUntilClasses = 5;
        var nextDay = 1;
        var roomCount = 1;
        var capacity = 6;
        var visits = 4;
        var passValidityDays = 30;
        var startsAt = Anchor.AddDays(daysUntilClasses);
        var validUntil = Session.Today.AddDays(passValidityDays);
        var rooms = await Classes.Rooms(roomCount);
        var member = await Users.RegisterMember(visits, validUntil);
        for (var index = 0; index < classCount; index++)
        {
            await Classes.Lesson().Capacity(capacity).StartsAt(startsAt.AddMinutes(index)).Room(rooms[0].Id).Pass(member.Number, visits, validUntil).Create();
        }

        // Act
        var response = await member.Schedule.Get(startsAt, startsAt.AddDays(nextDay));

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
        var daysUntilWindow = 6;
        var oneHour = 1;
        var twoHours = 2;
        var capacity = 8;
        var visits = 4;
        var passValidityDays = 30;
        var from = Anchor.AddDays(daysUntilWindow);
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var included = await Classes.Lesson().Capacity(capacity).StartsAt(from).Pass(member.Number, visits, validUntil).Create();
        await Classes.Lesson().Capacity(capacity).StartsAt(from.AddHours(oneHour)).Cancelled().Pass(member.Number, visits, validUntil).Create();
        await Classes.Lesson().Capacity(capacity).StartsAt(from.AddHours(twoHours)).Pass(member.Number, visits, validUntil).Create();

        // Act
        var response = await member.Schedule.Get(from, from.AddHours(twoHours));

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
        var daysUntilClasses = 7;
        var oneHour = 1;
        var windowHours = 2;
        var roomCount = 2;
        var capacity = 8;
        var visits = 4;
        var passValidityDays = 30;
        var startsAt = Anchor.AddDays(daysUntilClasses);
        var validUntil = Session.Today.AddDays(passValidityDays);
        var rooms = await Classes.Rooms(roomCount);
        var member = await Users.RegisterMember(visits, validUntil);
        var first = await Classes.Lesson().Capacity(capacity).StartsAt(startsAt).Room(rooms[0].Id).Pass(member.Number, visits, validUntil).Create();
        await Classes.Lesson().Capacity(capacity).StartsAt(startsAt.AddHours(oneHour)).Room(rooms[1].Id).Pass(member.Number, visits, validUntil).Create();

        // Act
        var filtered = await member.Schedule.Get(startsAt, startsAt.AddHours(windowHours), roomId: rooms[0].Id);
        var unknown = await member.Schedule.Get(startsAt, startsAt.AddHours(windowHours), roomId: MissingRoomId);

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
        var visits = 8;
        var daysUntilClass = 8;
        var windowHours = 1;
        var passValidityDays = 30;
        var startsAt = Anchor.AddDays(daysUntilClass);
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);
        var lesson = await Classes.Lesson().Capacity(capacity).StartsAt(startsAt).Pass(member.Number, visits, validUntil).Create();
        await member.Enrollments.Book(lesson, seats);
        var cancel = await member.Enrollments.CancelEnrollment(await Enrollments.ActiveEnrollmentId(lesson.UserId, lesson.ClassId));
        cancel.EnsureSuccessStatusCode();

        // Act
        var response = await member.Schedule.Get(startsAt, startsAt.AddHours(windowHours));

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
        var daysUntilWindow = 9;
        var windowHours = 1;
        var from = Anchor.AddDays(daysUntilWindow);

        // Act
        var response = await Users.Anonymous().Schedule.Get(from, from.AddHours(windowHours));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task Get_OnThe61stRequest_ReturnsTooManyRequests()
    {
        // Arrange
        var visits = 4;
        var passValidityDays = 30;
        var validUntil = Session.Today.AddDays(passValidityDays);
        var member = await Users.RegisterMember(visits, validUntil);

        // Act
        var lastStatus = await AuthClient.Hammer(_ => member.Schedule.Get());

        // Assert
        lastStatus.ShouldBe(HttpStatusCode.TooManyRequests);
    }
}
