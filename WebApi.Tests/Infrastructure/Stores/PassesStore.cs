using DataAccessLayer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace WebApi.Tests.Infrastructure.Stores;

public sealed class PassesStore
{
    private readonly TestSession _session;
    private readonly UsersStore _users;

    internal PassesStore(TestSession session, UsersStore users)
    {
        _session = session;
        _users = users;
    }

    public async Task SetPass(int userNumber, int visits, DateOnly validUntil)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var user = await _users.FindAsync(dbContext, userNumber);
        var pass = await dbContext.Passes.SingleAsync(item => item.UserId == user.Id);
        pass.RemainingVisits = visits;
        pass.ValidUntil = validUntil;
        await dbContext.SaveChangesAsync();
    }

    public async Task SetVisits(long userId, int visits)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var pass = await dbContext.Passes.SingleAsync(item => item.UserId == userId);
        pass.RemainingVisits = visits;
        await dbContext.SaveChangesAsync();
    }

    public async Task SetValidUntil(long userId, DateOnly validUntil)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var pass = await dbContext.Passes.SingleAsync(item => item.UserId == userId);
        pass.ValidUntil = validUntil;
        await dbContext.SaveChangesAsync();
    }

    public async Task<int> RemainingVisits(long userId)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        return await dbContext.Passes.Where(pass => pass.UserId == userId).Select(pass => pass.RemainingVisits).SingleAsync();
    }
}
