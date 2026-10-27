using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Shouldly;
using WebApi.Features.Schedule.GetSchedule;

namespace WebApi.Tests.Schedule;

[TestClass]
public class ScheduleContinuationTests
{
    [TestMethod]
    public void TryRead_RoundTripsTheUtcInstantAndClassId()
    {
        // Arrange
        var startsAt = new DateTime(2031, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        var classId = 42L;

        // Act
        var token = ScheduleContinuation.Write(startsAt, classId);
        var read = ScheduleContinuation.TryRead(token, out var actualStartsAt, out var actualClassId);

        // Assert
        read.ShouldBeTrue();
        actualStartsAt.ShouldBe(startsAt);
        actualClassId.ShouldBe(classId);
    }

    [TestMethod]
    public void TryRead_RejectsATokenThatDoesNotNameAClass()
    {
        // Arrange
        var startsAt = new DateTime(2031, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        var classId = 0L;
        var payload = string.Create(CultureInfo.InvariantCulture, $"{startsAt.Ticks}:{classId}");
        var token = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));

        // Act
        var blank = ScheduleContinuation.TryRead("   ", out _, out _);
        var broken = ScheduleContinuation.TryRead("not-a-token", out _, out _);
        var zeroClass = ScheduleContinuation.TryRead(token, out _, out _);

        // Assert
        blank.ShouldBeFalse();
        broken.ShouldBeFalse();
        zeroClass.ShouldBeFalse();
    }
}
