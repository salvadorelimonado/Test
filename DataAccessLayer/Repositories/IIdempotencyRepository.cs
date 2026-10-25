using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;

namespace DataAccessLayer.Repositories;

public interface IIdempotencyRepository : IGenericRepository<IdempotencyRecord>
{
    Task<IdempotencyRecord?> FindAsync(
        long userId,
        IdempotencyOperation operation,
        byte[] keyHash,
        CancellationToken ct = default);
}
