using System.Net;
using WebApi.Features.Analytics;
using DataAccessLayer.Entities;
using Shouldly;
using WebApi.Tests.Infrastructure;

namespace WebApi.Tests.Analytics;

[TestClass]
public class GetLoadTests : ClubTest
{
    private const long MissingRoomId = 9_000_000_000;

    [TestMethod]
    public async Task Get_AggregatesByUtcDateThenRoom()
    {
        // Arrange
        var firstDay = new DateOnly(2099, 6, 1);
        var secondDay = new DateOnly(2099, 6, 2);
        var rooms = await Host().Rooms(2);
        var firstClassId = await AddClass(rooms[0].Id, 10, At(firstDay, 9, 0));
        var secondClassId = await AddClass(rooms[0].Id, 8, At(firstDay, 11, 0));
        await AddClass(rooms[1].Id, 5, At(firstDay, 9, 0));
        var nextDayClassId = await AddClass(rooms[0].Id, 12, At(secondDay, 9, 0));
        await AddClass(rooms[0].Id, 4, At(new DateOnly(2099, 5, 31), 9, 0));
        await Host().AddEnrollment(151, firstClassId, 3, EnrollmentStatus.Active);
        await Host().AddEnrollment(151, secondClassId, 4, EnrollmentStatus.Active);
        await Host().AddEnrollment(151, nextDayClassId, 2, EnrollmentStatus.Active);

        // Act
        var response = await Host().Admin(1).Load(firstDay, secondDay);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var report = await response.ReadBody<LoadResponse>();
        report.Items.Count.ShouldBe(3);
        AssertItem(report.Items[0], firstDay, rooms[0], 2, 18, 7);
        AssertItem(report.Items[1], firstDay, rooms[1], 1, 5, 0);
        AssertItem(report.Items[2], secondDay, rooms[0], 1, 12, 2);
    }

    [TestMethod]
    public async Task Get_WhenClassOrEnrollmentIsCancelled_ExcludesThem()
    {
        // Arrange
        var day = new DateOnly(2099, 6, 10);
        var rooms = await Host().Rooms(2);
        var keptClassId = await AddClass(rooms[0].Id, 10, At(day, 9, 0));
        var cancelledClassId = await AddClass(rooms[0].Id, 20, At(day, 12, 0), cancelled: true);
        await AddClass(rooms[1].Id, 20, At(day, 9, 0), cancelled: true);
        await Host().AddEnrollment(151, keptClassId, 2, EnrollmentStatus.Active);
        await Host().AddEnrollment(151, keptClassId, 5, EnrollmentStatus.Cancelled);
        await Host().AddEnrollment(151, cancelledClassId, 9, EnrollmentStatus.Active);

        // Act
        var response = await Host().Admin(1).Load(day, day);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var report = await response.ReadBody<LoadResponse>();
        report.Items.Count.ShouldBe(1);
        AssertItem(report.Items[0], day, rooms[0], 1, 10, 2);
    }

    [TestMethod]
    public async Task Get_WhenBoundsAreASingleUtcDay_IncludesThatDayOnly()
    {
        // Arrange
        var day = new DateOnly(2099, 6, 20);
        var nextDay = day.AddDays(1);
        var rooms = await Host().Rooms(1);
        var eveningClassId = await AddClass(rooms[0].Id, 7, At(day, 23, 30));
        await AddClass(rooms[0].Id, 9, At(nextDay, 0, 0));
        await Host().AddEnrollment(151, eveningClassId, 1, EnrollmentStatus.Active);

        // Act
        var evening = await Host().Admin(1).Load(day, day);
        var midnight = await Host().Admin(1).Load(nextDay, nextDay);

        // Assert
        evening.StatusCode.ShouldBe(HttpStatusCode.OK);
        var eveningReport = await evening.ReadBody<LoadResponse>();
        eveningReport.Items.Count.ShouldBe(1);
        AssertItem(eveningReport.Items[0], day, rooms[0], 1, 7, 1);
        midnight.StatusCode.ShouldBe(HttpStatusCode.OK);
        var midnightReport = await midnight.ReadBody<LoadResponse>();
        midnightReport.Items.Count.ShouldBe(1);
        AssertItem(midnightReport.Items[0], nextDay, rooms[0], 1, 9, 0);
    }

    [TestMethod]
    public async Task Get_WhenTheQueryIsInvalid_ReturnsBadRequest()
    {
        // Arrange
        var from = new DateOnly(2099, 6, 20);
        var admin = Host().Admin(1);

        // Act
        var missingFrom = await admin.Load(null, from.ToString("yyyy-MM-dd"));
        var toBeforeFrom = await admin.Load(from, from.AddDays(-1));
        var withTime = await admin.Load("2099-06-20T23:30:00", from.ToString("yyyy-MM-dd"));
        var badRoom = await admin.Load(from.ToString("yyyy-MM-dd"), from.ToString("yyyy-MM-dd"), "room");

        // Assert
        missingFrom.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        toBeforeFrom.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        withTime.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        badRoom.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task Get_WhenTheEndIsTheLastRepresentableDate_IncludesThatDay()
    {
        // Arrange
        var lastDay = DateOnly.MaxValue;
        var rooms = await Host().Rooms(1);
        await AddClass(rooms[0].Id, 4, At(lastDay, 23, 30));

        // Act
        var response = await Host().Admin(2).Load(lastDay, lastDay);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var report = await response.ReadBody<LoadResponse>();
        report.Items.Count.ShouldBe(1);
        AssertItem(report.Items[0], lastDay, rooms[0], 1, 4, 0);
    }

    [TestMethod]
    public async Task Get_WhenRoomIsFiltered_ReturnsThatRoomOrAnEmptyList()
    {
        // Arrange
        var day = new DateOnly(2099, 6, 25);
        var rooms = await Host().Rooms(2);
        await AddClass(rooms[0].Id, 6, At(day, 9, 0));
        await AddClass(rooms[1].Id, 8, At(day, 9, 0));
        var admin = Host().Admin(1);

        // Act
        var selected = await admin.Load(day, day, rooms[0].Id);
        var missing = await admin.Load(day, day, MissingRoomId);

        // Assert
        selected.StatusCode.ShouldBe(HttpStatusCode.OK);
        var selectedReport = await selected.ReadBody<LoadResponse>();
        selectedReport.Items.Count.ShouldBe(1);
        AssertItem(selectedReport.Items[0], day, rooms[0], 1, 6, 0);
        missing.StatusCode.ShouldBe(HttpStatusCode.OK);
        var missingReport = await missing.ReadBody<LoadResponse>();
        missingReport.Items.Count.ShouldBe(0);
    }

    [TestMethod]
    public async Task Get_WhenTheCallerIsNotAdmin_ReturnsForbidden()
    {
        // Act
        var response = await Host().User(152).Load(new DateOnly(2099, 6, 1), new DateOnly(2099, 6, 1));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task Get_WhenThereIsNoToken_ReturnsUnauthorized()
    {
        // Act
        var response = await Host().Anonymous().Load(new DateOnly(2099, 6, 1), new DateOnly(2099, 6, 1));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task Get_WhenTheLimitIsExceeded_ReturnsTooManyRequests()
    {
        // Arrange
        var admin = Host().Admin(5);

        // Act
        var lastStatus = await admin.Hammer(_ => admin.Load());

        // Assert
        lastStatus.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    private static void AssertItem(LoadResponseItem item, DateOnly date, RoomRef room, int classesCount, int totalCapacity, int bookedSeats)
    {
        item.Date.ShouldBe(date);
        item.Room.Id.ShouldBe(room.Id);
        item.Room.Name.ShouldBe(room.Name);
        item.ClassesCount.ShouldBe(classesCount);
        item.TotalCapacity.ShouldBe(totalCapacity);
        item.BookedSeats.ShouldBe(bookedSeats);
    }

    private Task<long> AddClass(long roomId, int capacity, DateTime startsAt, bool cancelled = false)
    {
        var draft = Host().Lesson().Capacity(capacity).StartsAt(startsAt).Room(roomId).Pass(151, 0);
        if (cancelled)
        {
            draft.Cancelled();
        }

        return CreateClassId(draft);
    }

    private static async Task<long> CreateClassId(LessonDraft draft)
    {
        var lesson = await draft.Create();
        return lesson.ClassId;
    }

    private static DateTime At(DateOnly day, int hour, int minute)
    {
        return new DateTime(day.Year, day.Month, day.Day, hour, minute, 0, DateTimeKind.Utc);
    }
}
