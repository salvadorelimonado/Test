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
        if (userIds.Count == 0)
        {
            return new Dictionary<long, Pass>();
        }

        var ids = userIds.Distinct().ToArray();
        var passes = await _dbSet
            .FromSql($"SELECT * FROM passes WHERE user_id = ANY({ids}) ORDER BY user_id FOR UPDATE")
            .ToListAsync(ct);
        if (passes.Count != ids.Length)
        {
            throw new InvalidOperationException("Pass was not found.");
        }

        return passes.ToDictionary(pass => pass.UserId);
    }
}
