using System.Globalization;
using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;
using DataAccessLayer.Repositories.Interfaces;
using Medallion.Threading.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DataAccessLayer.Repositories.Implementations;

public class IdempotencyRepository : GenericRepository<IdempotencyRecord>, IIdempotencyRepository
{
    private readonly DbContext _dbContext;

    public IdempotencyRepository(DbContext dbContext)
        : base(dbContext.Set<IdempotencyRecord>())
    {
        _dbContext = dbContext;
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

    public async Task LockAsync(
        long userId,
        IdempotencyOperation operation,
        byte[] keyHash,
        CancellationToken ct = default)
    {
        if (keyHash is null)
        {
            throw new ArgumentNullException(nameof(keyHash));
        }

        var transaction = _dbContext.Database.CurrentTransaction;
        if (transaction is null)
        {
            throw new InvalidOperationException("An idempotency lock requires a transaction.");
        }

        var key = new PostgresAdvisoryLockKey(LockName(userId, operation, keyHash), allowHashing: true);
        await PostgresDistributedLock.AcquireWithTransactionAsync(key, transaction.GetDbTransaction(), cancellationToken: ct);
    }

    private static string LockName(long userId, IdempotencyOperation operation, byte[] keyHash)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{userId}:{(int)operation}:{Convert.ToHexString(keyHash)}");
    }
}
