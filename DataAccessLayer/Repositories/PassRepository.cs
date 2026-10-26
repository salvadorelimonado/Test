using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataAccessLayer.Repositories;

public class PassRepository : GenericRepository<Pass>, IPassRepository
{
    public PassRepository(DbSet<Pass> dbSet)
        : base(dbSet)
    {
    }

    public async Task<Pass?> GetByUserAsync(long userId, CancellationToken ct = default)
    {
        return await _dbSet.FirstOrDefaultAsync(x => x.UserId == userId, ct);
    }

    public async Task<Pass?> GetByUserForUpdateAsync(long userId, CancellationToken ct = default)
    {
        return await _dbSet
            .FromSql($"SELECT * FROM passes WHERE user_id = {userId} FOR UPDATE")
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyDictionary<long, Pass>> GetByUsersForUpdateAsync(
        IReadOnlyList<long> userIds,
        CancellationToken ct = default)
    {
        var passes = new Dictionary<long, Pass>();
        foreach (var userId in userIds)
        {
            var pass = await GetByUserForUpdateAsync(userId, ct);
            if (pass is null)
            {
                throw new InvalidOperationException("Pass was not found.");
            }

            passes.Add(userId, pass);
        }

        return passes;
    }
}
