using BusinessLayer.Features.Enrollments.Waitlist;
using DataAccessLayer.Entities;
using Shouldly;

namespace WebApi.Tests.Enrollments;

[TestClass]
public class WaitlistFillResultTests
{
    [TestMethod]
    public void Fill_WhenTheHeadNeedsMoreSeatsThanAreFree_PromotesNobody()
    {
        // Arrange
        var classId = 10L;
        var freeSeats = 2;
        var headSeats = 3;
        var behindSeats = 1;
        var headUserId = 20L;
        var behindUserId = 21L;
        var visits = 4;
        var now = new DateTime(2032, 6, 1, 9, 0, 0, DateTimeKind.Utc);
        var validUntil = DateOnly.FromDateTime(now).AddDays(10);
        var scheduledClass = new Class { Id = classId };
        var head = Waiting(headUserId, headSeats, now.AddMinutes(-10));
        var behind = Waiting(behindUserId, behindSeats, now.AddMinutes(-5));
        var passes = Passes(validUntil, (headUserId, visits), (behindUserId, visits));

        // Act
        var result = WaitlistFillResult.Fill(scheduledClass, [head, behind], passes, freeSeats, now);

        // Assert
        result.Changed.ShouldBeFalse();
        result.PromotedUserIds.ShouldBeEmpty();
        head.Status.ShouldBe(WaitlistStatus.Waiting);
        behind.Status.ShouldBe(WaitlistStatus.Waiting);
    }

    [TestMethod]
    public void Fill_WhenTheHeadCannotPay_SkipsThatRequestAndPromotesTheNext()
    {
        // Arrange
        var classId = 11L;
        var freeSeats = 2;
        var headSeats = 1;
        var nextSeats = 1;
        var headUserId = 30L;
        var nextUserId = 31L;
        var headVisits = 0;
        var nextVisits = 2;
        var now = new DateTime(2032, 6, 2, 9, 0, 0, DateTimeKind.Utc);
        var validUntil = DateOnly.FromDateTime(now).AddDays(10);
        var scheduledClass = new Class { Id = classId };
        var head = Waiting(headUserId, headSeats, now.AddMinutes(-10));
        var next = Waiting(nextUserId, nextSeats, now.AddMinutes(-5));
        var passes = Passes(validUntil, (headUserId, headVisits), (nextUserId, nextVisits));

        // Act
        var result = WaitlistFillResult.Fill(scheduledClass, [head, next], passes, freeSeats, now);

        // Assert
        result.Changed.ShouldBeTrue();
        result.PromotedUserIds.ShouldBe([nextUserId]);
        head.Status.ShouldBe(WaitlistStatus.Skipped);
        next.Status.ShouldBe(WaitlistStatus.Promoted);
        passes[nextUserId].RemainingVisits.ShouldBe(nextVisits - nextSeats);
    }

    [TestMethod]
    public void CancelOwnRequest_DropsOnlyThatUsersWaitingEntry()
    {
        // Arrange
        var ownerUserId = 40L;
        var nextUserId = 41L;
        var ownerSeats = 2;
        var nextSeats = 1;
        var now = new DateTime(2032, 6, 3, 9, 0, 0, DateTimeKind.Utc);
        var own = Waiting(ownerUserId, ownerSeats, now.AddMinutes(-10));
        var next = Waiting(nextUserId, nextSeats, now.AddMinutes(-5));

        // Act
        var stillWaiting = WaitlistFillResult.CancelOwnRequest([own, next], ownerUserId);

        // Assert
        own.Status.ShouldBe(WaitlistStatus.Cancelled);
        next.Status.ShouldBe(WaitlistStatus.Waiting);
        stillWaiting.ShouldBe([next]);
    }

    [TestMethod]
    public void PassIdsToLock_SortsTheOwnerTogetherWithTheQueue()
    {
        // Arrange
        var ownerUserId = 50L;
        var earlierUserId = 15L;
        var laterUserId = 70L;
        var freeSeats = 3;
        var requestedSeats = 1;
        var now = new DateTime(2032, 6, 4, 9, 0, 0, DateTimeKind.Utc);
        var earlier = Waiting(earlierUserId, requestedSeats, now.AddMinutes(-10));
        var later = Waiting(laterUserId, requestedSeats, now.AddMinutes(-5));

        // Act
        var lockIds = WaitlistFillResult.PassIdsToLock([earlier, later], freeSeats, ownerUserId);

        // Assert
        lockIds.ShouldBe([earlierUserId, ownerUserId, laterUserId]);
    }

    private static WaitlistEntry Waiting(long userId, int seats, DateTime createdAt)
    {
        return new WaitlistEntry
        {
            UserId = userId,
            Seats = seats,
            Status = WaitlistStatus.Waiting,
            CreatedAt = createdAt
        };
    }

    private static Dictionary<long, Pass> Passes(DateOnly validUntil, params (long UserId, int Visits)[] holders)
    {
        var passes = new Dictionary<long, Pass>();
        foreach (var holder in holders)
        {
            passes.Add(holder.UserId, new Pass
            {
                UserId = holder.UserId,
                RemainingVisits = holder.Visits,
                ValidUntil = validUntil
            });
        }

        return passes;
    }
}
