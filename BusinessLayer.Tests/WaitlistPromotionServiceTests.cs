using BusinessLayer.Features.Enrollments.Waitlist;
using DataAccessLayer;
using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BusinessLayer.Tests;

[TestClass]
public class WaitlistPromotionServiceTests
{
    [TestMethod]
    public void Fill_WhenTheHeadNeedsMoreSeatsThanAreFree_PromotesTheNextWhoFits()
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
        var result = WaitlistPromotionService.Fill(scheduledClass, [head, behind], passes, freeSeats, now);

        // Assert
        result.Changed.ShouldBeTrue();
        result.PromotedUserIds.ShouldBe([behindUserId]);
        head.Status.ShouldBe(WaitlistStatus.Waiting);
        behind.Status.ShouldBe(WaitlistStatus.Promoted);
        passes[behindUserId].RemainingVisits.ShouldBe(visits - behindSeats);
        passes[headUserId].RemainingVisits.ShouldBe(visits);
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
        var result = WaitlistPromotionService.Fill(scheduledClass, [head, next], passes, freeSeats, now);

        // Assert
        result.Changed.ShouldBeTrue();
        result.PromotedUserIds.ShouldBe([nextUserId]);
        head.Status.ShouldBe(WaitlistStatus.Skipped);
        next.Status.ShouldBe(WaitlistStatus.Promoted);
        passes[nextUserId].RemainingVisits.ShouldBe(nextVisits - nextSeats);
    }

    [TestMethod]
    public void Fill_WhenTheHeadCannotPayAndDoesNotFit_SkipsThatRequestAndPromotesTheNext()
    {
        // Arrange
        var classId = 12L;
        var freeSeats = 2;
        var headSeats = 3;
        var nextSeats = 1;
        var headUserId = 32L;
        var nextUserId = 33L;
        var headVisits = 0;
        var nextVisits = 2;
        var now = new DateTime(2032, 6, 5, 9, 0, 0, DateTimeKind.Utc);
        var validUntil = DateOnly.FromDateTime(now).AddDays(-1);
        var nextValidUntil = DateOnly.FromDateTime(now).AddDays(10);
        var scheduledClass = new Class { Id = classId };
        var head = Waiting(headUserId, headSeats, now.AddMinutes(-10));
        var next = Waiting(nextUserId, nextSeats, now.AddMinutes(-5));
        var passes = new Dictionary<long, Pass>
        {
            [headUserId] = new Pass { UserId = headUserId, RemainingVisits = headVisits, ValidUntil = validUntil },
            [nextUserId] = new Pass { UserId = nextUserId, RemainingVisits = nextVisits, ValidUntil = nextValidUntil }
        };

        // Act
        var result = WaitlistPromotionService.Fill(scheduledClass, [head, next], passes, freeSeats, now);

        // Assert
        result.Changed.ShouldBeTrue();
        result.PromotedUserIds.ShouldBe([nextUserId]);
        head.Status.ShouldBe(WaitlistStatus.Skipped);
        next.Status.ShouldBe(WaitlistStatus.Promoted);
    }

    [TestMethod]
    public void MarkOwnRequestCancelled_DropsOnlyThatUsersWaitingEntry()
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
        var cancelled = WaitlistPromotionService.MarkOwnRequestCancelled([own, next], ownerUserId);

        // Assert
        cancelled.ShouldBeTrue();
        own.Status.ShouldBe(WaitlistStatus.Cancelled);
        next.Status.ShouldBe(WaitlistStatus.Waiting);
    }

    [TestMethod]
    public void OmitOwnRequest_LeavesThatEntryWaiting()
    {
        // Arrange
        var ownerUserId = 42L;
        var nextUserId = 43L;
        var ownerSeats = 2;
        var nextSeats = 1;
        var now = new DateTime(2032, 6, 6, 9, 0, 0, DateTimeKind.Utc);
        var own = Waiting(ownerUserId, ownerSeats, now.AddMinutes(-10));
        var next = Waiting(nextUserId, nextSeats, now.AddMinutes(-5));

        // Act
        var others = WaitlistPromotionService.OmitOwnRequest([own, next], ownerUserId);

        // Assert
        own.Status.ShouldBe(WaitlistStatus.Waiting);
        others.ShouldBe([next]);
    }

    [TestMethod]
    public async Task Promote_WithoutATransaction_Throws()
    {
        // Arrange
        var now = new DateTime(2032, 6, 1, 9, 0, 0, DateTimeKind.Utc);
        var scheduledClass = new Class();
        var unusedConnection = "Host=127.0.0.1;Port=1;Database=unused";
        var options = new DbContextOptionsBuilder<ClubDbContext>().UseNpgsql(unusedConnection).Options;
        await using var dbContext = new ClubDbContext(options);
        var promotion = new WaitlistPromotionService(new UnitOfWork(dbContext));

        // Act
        var act = () => promotion.PromoteAsync(scheduledClass, now, null, null, null, CancellationToken.None);

        // Assert
        await Should.ThrowAsync<InvalidOperationException>(act);
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
