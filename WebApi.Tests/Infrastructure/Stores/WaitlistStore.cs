using DataAccessLayer;
using DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebApi.Tests.Infrastructure.DTO;

namespace WebApi.Tests.Infrastructure.Stores;

public sealed class WaitlistStore
{
    private readonly TestSession _session;
    private readonly UsersStore _users;

    internal WaitlistStore(TestSession session, UsersStore users)
    {
        _session = session;
        _users = users;
    }

    public async Task SetWaitlistStatus(long entryId, WaitlistStatus status)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var entry = await dbContext.WaitlistEntries.SingleAsync(item => item.Id == entryId);
        entry.Status = status;
        await dbContext.SaveChangesAsync();
    }

    public async Task<long> AddWaiting(int userNumber, long classId, int seats, DateTime createdAt)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var user = await _users.FindAsync(dbContext, userNumber);
        var entry = new WaitlistEntry
        {
            UserId = user.Id,
            ClassId = classId,
            Seats = seats,
            Status = WaitlistStatus.Waiting,
            CreatedAt = createdAt
        };
        dbContext.WaitlistEntries.Add(entry);
        await dbContext.SaveChangesAsync();
        return entry.Id;
    }

    public async Task<WaitlistStatus> WaitlistStatusOf(long entryId)
    {
        var row = await Entry(entryId);
        return row.Status;
    }

    public async Task<WaitlistRow> Entry(long entryId)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var entry = await dbContext.WaitlistEntries.SingleAsync(item => item.Id == entryId);
        return new WaitlistRow(entry.Status, entry.CreatedAt);
    }

    public async Task<int> WaitingCount(long classId)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        return await dbContext.WaitlistEntries.CountAsync(
            entry => entry.ClassId == classId && entry.Status == WaitlistStatus.Waiting);
    }

    public async Task<int> WaitlistRows(long classId, long userId)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        return await dbContext.WaitlistEntries.CountAsync(entry => entry.ClassId == classId && entry.UserId == userId);
    }

    public async Task<WaitingSummary> Waiting(long classId, long userId)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var waiting = await dbContext.WaitlistEntries
            .Where(entry => entry.ClassId == classId && entry.UserId == userId && entry.Status == WaitlistStatus.Waiting)
            .Select(entry => new { entry.Id, entry.Seats })
            .ToListAsync();
        var visits = await dbContext.Passes.Where(pass => pass.UserId == userId).Select(pass => pass.RemainingVisits).SingleAsync();
        var entry = waiting.SingleOrDefault();
        return new WaitingSummary(waiting.Count, entry?.Id ?? 0, entry?.Seats ?? 0, visits);
    }
}
