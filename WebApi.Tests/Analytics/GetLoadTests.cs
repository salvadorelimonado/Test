using System.Net;
using WebApi.Features.Analytics.GetLoad;
using DataAccessLayer.Entities;
using Shouldly;
using WebApi.Tests.Infrastructure;

namespace WebApi.Tests.Analytics;

[TestClass]
public class GetLoadTests : ClubTest
{
    private const long MissingRoomId = 9_000_000_000;
    private const int YearsUntilIsolatedJune = 67;

    [TestMethod]
    public async Task Get_AggregatesByUtcDateThenRoom()
    {
        // Arrange
        var oneDay = 1;
        var roomCount = 2;
        var morningHour = 9;
        var lateMorningHour = 11;
        var minute = 0;
        var firstCapacity = 10;
        var secondCapacity = 8;
        var otherRoomCapacity = 5;
        var nextDayCapacity = 12;
        var excludedCapacity = 4;
        var firstSeats = 3;
        var secondSeats = 4;
        var nextDaySeats = 2;
        var visits = 0;
        var passValidityDays = 30;
        var firstDay = IsolatedJuneFirst();
        var secondDay = firstDay.AddDays(oneDay);
        var dayBefore = firstDay.AddDays(-oneDay);
        var member = await Host().RegisterMember(visits, Host().Today.AddDays(passValidityDays));
        var rooms = await Host().Rooms(roomCount);
        var firstClassId = await AddClass(member.Number, rooms[0].Id, firstCapacity, At(firstDay, morningHour, minute));
        var secondClassId = await AddClass(member.Number, rooms[0].Id, secondCapacity, At(firstDay, lateMorningHour, minute));
        await AddClass(member.Number, rooms[1].Id, otherRoomCapacity, At(firstDay, morningHour, minute));
        var nextDayClassId = await AddClass(member.Number, rooms[0].Id, nextDayCapacity, At(secondDay, morningHour, minute));
        await AddClass(member.Number, rooms[0].Id, excludedCapacity, At(dayBefore, morningHour, minute));
        await Host().AddEnrollment(member.Number, firstClassId, firstSeats, EnrollmentStatus.Active);
        await Host().AddEnrollment(member.Number, secondClassId, secondSeats, EnrollmentStatus.Active);
        await Host().AddEnrollment(member.Number, nextDayClassId, nextDaySeats, EnrollmentStatus.Active);

        // Act
        var response = await Host().Admin(1).Load(firstDay, secondDay);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var report = await response.ReadBody<LoadResponse>();
        report.Items.Count.ShouldBe(3);
        AssertItem(report.Items[0], firstDay, rooms[0], 2, firstCapacity + secondCapacity, firstSeats + secondSeats);
        AssertItem(report.Items[1], firstDay, rooms[1], 1, otherRoomCapacity, 0);
        AssertItem(report.Items[2], secondDay, rooms[0], 1, nextDayCapacity, nextDaySeats);
    }

    [TestMethod]
    public async Task Get_WhenClassOrEnrollmentIsCancelled_ExcludesThem()
    {
        // Arrange
        var daysAfterJuneFirst = 9;
        var roomCount = 2;
        var morningHour = 9;
        var noonHour = 12;
        var minute = 0;
        var keptCapacity = 10;
        var cancelledCapacity = 20;
        var activeSeats = 2;
        var cancelledSeats = 5;
        var seatsOnCancelledClass = 9;
        var visits = 0;
        var passValidityDays = 30;
        var day = IsolatedJuneFirst().AddDays(daysAfterJuneFirst);
        var member = await Host().RegisterMember(visits, Host().Today.AddDays(passValidityDays));
        var rooms = await Host().Rooms(roomCount);
        var keptClassId = await AddClass(member.Number, rooms[0].Id, keptCapacity, At(day, morningHour, minute));
        var cancelledClassId = await AddClass(member.Number, rooms[0].Id, cancelledCapacity, At(day, noonHour, minute), cancelled: true);
        await AddClass(member.Number, rooms[1].Id, cancelledCapacity, At(day, morningHour, minute), cancelled: true);
        await Host().AddEnrollment(member.Number, keptClassId, activeSeats, EnrollmentStatus.Active);
        await Host().AddEnrollment(member.Number, keptClassId, cancelledSeats, EnrollmentStatus.Cancelled);
        await Host().AddEnrollment(member.Number, cancelledClassId, seatsOnCancelledClass, EnrollmentStatus.Active);

        // Act
        var response = await Host().Admin(1).Load(day, day);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var report = await response.ReadBody<LoadResponse>();
        report.Items.Count.ShouldBe(1);
        AssertItem(report.Items[0], day, rooms[0], 1, keptCapacity, activeSeats);
    }

    [TestMethod]
    public async Task Get_WhenBoundsAreASingleUtcDay_IncludesThatDayOnly()
    {
        // Arrange
        var daysAfterJuneFirst = 19;
        var oneDay = 1;
        var roomCount = 1;
        var eveningHour = 23;
        var eveningMinute = 30;
        var midnightHour = 0;
        var midnightMinute = 0;
        var eveningCapacity = 7;
        var midnightCapacity = 9;
        var eveningSeats = 1;
        var visits = 0;
        var passValidityDays = 30;
        var day = IsolatedJuneFirst().AddDays(daysAfterJuneFirst);
        var nextDay = day.AddDays(oneDay);
        var member = await Host().RegisterMember(visits, Host().Today.AddDays(passValidityDays));
        var rooms = await Host().Rooms(roomCount);
        var eveningClassId = await AddClass(member.Number, rooms[0].Id, eveningCapacity, At(day, eveningHour, eveningMinute));
        await AddClass(member.Number, rooms[0].Id, midnightCapacity, At(nextDay, midnightHour, midnightMinute));
        await Host().AddEnrollment(member.Number, eveningClassId, eveningSeats, EnrollmentStatus.Active);

        // Act
        var evening = await Host().Admin(1).Load(day, day);
        var midnight = await Host().Admin(1).Load(nextDay, nextDay);

        // Assert
        evening.StatusCode.ShouldBe(HttpStatusCode.OK);
        var eveningReport = await evening.ReadBody<LoadResponse>();
        eveningReport.Items.Count.ShouldBe(1);
        AssertItem(eveningReport.Items[0], day, rooms[0], 1, eveningCapacity, eveningSeats);
        midnight.StatusCode.ShouldBe(HttpStatusCode.OK);
        var midnightReport = await midnight.ReadBody<LoadResponse>();
        midnightReport.Items.Count.ShouldBe(1);
        AssertItem(midnightReport.Items[0], nextDay, rooms[0], 1, midnightCapacity, 0);
    }

    [TestMethod]
    public async Task Get_WhenTheQueryIsInvalid_ReturnsBadRequest()
    {
        // Arrange
        var daysAfterJuneFirst = 19;
        var oneDay = 1;
        var timeSuffix = "T23:30:00";
        var from = IsolatedJuneFirst().AddDays(daysAfterJuneFirst);
        var dateText = from.ToString("yyyy-MM-dd");
        var admin = Host().Admin(1);

        // Act
        var missingFrom = await admin.Load(null, dateText);
        var toBeforeFrom = await admin.Load(from, from.AddDays(-oneDay));
        var withTime = await admin.Load(dateText + timeSuffix, dateText);
        var badRoom = await admin.Load(dateText, dateText, "room");

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
        var roomCount = 1;
        var capacity = 4;
        var hour = 23;
        var minute = 30;
        var visits = 0;
        var passValidityDays = 30;
        var member = await Host().RegisterMember(visits, Host().Today.AddDays(passValidityDays));
        var rooms = await Host().Rooms(roomCount);
        await AddClass(member.Number, rooms[0].Id, capacity, At(lastDay, hour, minute));

        // Act
        var response = await Host().Admin(2).Load(lastDay, lastDay);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var report = await response.ReadBody<LoadResponse>();
        report.Items.Count.ShouldBe(1);
        AssertItem(report.Items[0], lastDay, rooms[0], 1, capacity, 0);
    }

    [TestMethod]
    public async Task Get_WhenRoomIsFiltered_ReturnsThatRoomOrAnEmptyList()
    {
        // Arrange
        var daysAfterJuneFirst = 24;
        var roomCount = 2;
        var hour = 9;
        var minute = 0;
        var selectedCapacity = 6;
        var otherCapacity = 8;
        var visits = 0;
        var passValidityDays = 30;
        var day = IsolatedJuneFirst().AddDays(daysAfterJuneFirst);
        var member = await Host().RegisterMember(visits, Host().Today.AddDays(passValidityDays));
        var rooms = await Host().Rooms(roomCount);
        await AddClass(member.Number, rooms[0].Id, selectedCapacity, At(day, hour, minute));
        await AddClass(member.Number, rooms[1].Id, otherCapacity, At(day, hour, minute));
        var admin = Host().Admin(1);

        // Act
        var selected = await admin.Load(day, day, rooms[0].Id);
        var missing = await admin.Load(day, day, MissingRoomId);

        // Assert
        selected.StatusCode.ShouldBe(HttpStatusCode.OK);
        var selectedReport = await selected.ReadBody<LoadResponse>();
        selectedReport.Items.Count.ShouldBe(1);
        AssertItem(selectedReport.Items[0], day, rooms[0], 1, selectedCapacity, 0);
        missing.StatusCode.ShouldBe(HttpStatusCode.OK);
        var missingReport = await missing.ReadBody<LoadResponse>();
        missingReport.Items.Count.ShouldBe(0);
    }

    [TestMethod]
    public async Task Get_WhenTheCallerIsNotAdmin_ReturnsForbidden()
    {
        // Arrange
        var visits = 1;
        var passValidityDays = 30;
        var day = Host().Today;
        var member = await Host().RegisterMember(visits, day.AddDays(passValidityDays));

        // Act
        var response = await member.Load(day, day);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task Get_WhenThereIsNoToken_ReturnsUnauthorized()
    {
        // Arrange
        var day = Host().Today;

        // Act
        var response = await Host().Anonymous().Load(day, day);

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

    private DateOnly IsolatedJuneFirst()
    {
        return Host().Today.AddYears(YearsUntilIsolatedJune);
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

    private Task<long> AddClass(int memberNumber, long roomId, int capacity, DateTime startsAt, bool cancelled = false)
    {
        var visits = 0;
        var draft = Host().Lesson().Capacity(capacity).StartsAt(startsAt).Room(roomId).Pass(memberNumber, visits);
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
