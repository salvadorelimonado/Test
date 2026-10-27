using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;
using DataAccessLayer.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DataAccessLayer.Repositories.Implementations;

public class IdempotencyRepository : GenericRepository<IdempotencyRecord>, IIdempotencyRepository
{
    public IdempotencyRepository(DbSet<IdempotencyRecord> dbSet)
        : base(dbSet)
    {
    }

    public async Task<IdempotencyRecord?> FindAsync(
        long userId,
        IdempotencyOperation operation,
        byte[] keyHash,
        CancellationToken ct = default)
    {
        return await _dbSet.FirstOrDefaultAsync(
            x => x.UserId == userId && x.Operation == operation && x.KeyHash == keyHash,
            ct);
    }
}
