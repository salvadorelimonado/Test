using System.Net;
using Shouldly;
using WebApi.Tests.Infrastructure;

namespace WebApi.Tests.Enrollments;

[TestClass]
public class ResponseContractTests : ClubTest
{
    [TestMethod]
    public async Task Create_ReturnsTheClosedEnrollmentBody()
    {
        // Arrange
        var seats = 2;
        var visits = 6;
        var validUntil = ClubTime.Today().AddDays(40);
        var startsAt = new DateTime(2035, 2, 2, 11, 0, 0, DateTimeKind.Utc);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson().Capacity(6).StartsAt(startsAt).Pass(member.Number, visits, validUntil).Create();

        // Act
        var response = await Host().User(member.Number).Enroll(lesson, seats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.ReadJson();
        body.ShouldHaveProperties("classId", "createdAt", "id", "seats", "status");
        body.GetProperty("classId").GetInt64().ShouldBe(lesson.ClassId);
        body.GetProperty("seats").GetInt32().ShouldBe(seats);
        body.GetProperty("status").GetString().ShouldBe("active");
        body.GetProperty("id").GetInt64().ShouldBeGreaterThan(0);
    }

    [TestMethod]
    public async Task Get_ReturnsTheClosedCard()
    {
        // Arrange
        var seats = 1;
        var visits = 5;
        var validUntil = new DateOnly(2035, 3, 1);
        var startsAt = new DateTime(2035, 2, 3, 11, 0, 0, DateTimeKind.Utc);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson().Capacity(6).StartsAt(startsAt).Pass(member.Number, visits, validUntil).Create();
        var created = await Host().User(member.Number).Book(lesson, seats);

        // Act
        var response = await Host().User(member.Number).GetEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadJson();
        body.ShouldHaveProperties("class", "createdAt", "id", "pass", "seats", "status");
        body.GetProperty("status").GetString().ShouldBe("active");
        body.GetProperty("seats").GetInt32().ShouldBe(seats);
        var scheduledClass = body.GetProperty("class");
        scheduledClass.ShouldHaveProperties("durationMinutes", "id", "isCancelled", "room", "startsAt", "title");
        scheduledClass.GetProperty("room").ShouldHaveProperties("id", "name");
        body.GetProperty("pass").ShouldHaveProperties("remainingVisits", "validUntil");
        body.GetProperty("pass").GetProperty("remainingVisits").GetInt32().ShouldBe(visits - seats);
    }

    [TestMethod]
    public async Task Cancel_ReturnsTheClosedCancellationBody()
    {
        // Arrange
        var seats = 1;
        var visits = 4;
        var validUntil = ClubTime.Today().AddDays(20);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(4)
            .StartsAt(DateTime.UtcNow.AddDays(12))
            .Pass(member.Number, visits, validUntil)
            .Create();
        var created = await Host().User(member.Number).Book(lesson, seats);

        // Act
        var response = await Host().User(member.Number).CancelEnrollment(created.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadJson();
        body.ShouldHaveProperties("id", "refundedVisits", "status");
        body.GetProperty("id").GetInt64().ShouldBe(created.Id);
        body.GetProperty("status").GetString().ShouldBe("cancelled");
        body.GetProperty("refundedVisits").GetInt32().ShouldBe(seats);
    }

    [TestMethod]
    public async Task Create_WhenTheClassIsFull_ReturnsCapacityExceededLiteral()
    {
        // Arrange
        var seats = 1;
        var visits = 4;
        var validUntil = ClubTime.Today().AddDays(20);
        var holder = await Host().RegisterMember(visits, validUntil);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(1)
            .StartsAt(DateTime.UtcNow.AddDays(13))
            .Pass(holder.Number, visits, validUntil)
            .Create();
        await Host().User(holder.Number).Book(lesson, seats);

        // Act
        var response = await Host().User(member.Number).Enroll(lesson, seats);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadReasonText()).ShouldBe("capacity-exceeded");
    }

    [TestMethod]
    public async Task JoinAndLeave_ReturnTheClosedWaitlistBodies()
    {
        // Arrange
        var seats = 1;
        var visits = 4;
        var validUntil = ClubTime.Today().AddDays(20);
        var holder = await Host().RegisterMember(visits, validUntil);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(1)
            .StartsAt(DateTime.UtcNow.AddDays(14))
            .Pass(holder.Number, visits, validUntil)
            .TakenBy(holder.Number, seats)
            .Create();

        // Act
        var joined = await Host().User(member.Number).JoinWaitlist(lesson, seats);
        var joinedBody = await joined.ReadJson();
        var entryId = joinedBody.GetProperty("id").GetInt64();
        var left = await Host().User(member.Number).LeaveWaitlist(entryId);

        // Assert
        joined.StatusCode.ShouldBe(HttpStatusCode.Created);
        joinedBody.ShouldHaveProperties("classId", "createdAt", "id", "position", "seats");
        joinedBody.GetProperty("classId").GetInt64().ShouldBe(lesson.ClassId);
        joinedBody.GetProperty("seats").GetInt32().ShouldBe(seats);
        left.StatusCode.ShouldBe(HttpStatusCode.OK);
        var leftBody = await left.ReadJson();
        leftBody.ShouldHaveProperties("id", "status");
        leftBody.GetProperty("status").GetString().ShouldBe("cancelled");
    }

    [TestMethod]
    public async Task ScheduleAndLoad_ReturnTheClosedListBodies()
    {
        // Arrange
        var startsAt = new DateTime(2035, 8, 8, 9, 0, 0, DateTimeKind.Utc);
        var day = DateOnly.FromDateTime(startsAt);
        var visits = 4;
        var validUntil = new DateOnly(2035, 9, 1);
        var member = await Host().RegisterMember(visits, validUntil);
        var lesson = await Host().Lesson()
            .Capacity(6)
            .StartsAt(startsAt)
            .Pass(member.Number, visits, validUntil)
            .Create();

        // Act
        var schedule = await Host().User(member.Number).Schedule(startsAt.AddHours(-1), startsAt.AddHours(2), roomId: lesson.RoomId);
        var load = await Host().Admin(1).Load(day, day, lesson.RoomId);

        // Assert
        schedule.StatusCode.ShouldBe(HttpStatusCode.OK);
        var scheduleBody = await schedule.ReadJson();
        scheduleBody.ShouldHaveProperties("continuationToken", "items");
        var item = scheduleBody.GetProperty("items").EnumerateArray().Single();
        item.ShouldHaveProperties("capacity", "classId", "durationMinutes", "freeSeats", "mySeats", "room", "startsAt", "title");
        item.GetProperty("room").ShouldHaveProperties("id", "name");
        item.GetProperty("classId").GetInt64().ShouldBe(lesson.ClassId);
        load.StatusCode.ShouldBe(HttpStatusCode.OK);
        var loadBody = await load.ReadJson();
        loadBody.ShouldHaveProperties("items");
        var loadItem = loadBody.GetProperty("items").EnumerateArray().Single();
        loadItem.ShouldHaveProperties("bookedSeats", "classesCount", "date", "room", "totalCapacity");
        loadItem.GetProperty("room").ShouldHaveProperties("id", "name");
    }
}
