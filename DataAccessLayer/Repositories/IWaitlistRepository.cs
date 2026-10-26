using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;

namespace DataAccessLayer.Repositories;

public interface IWaitlistRepository : IGenericRepository<WaitlistEntry>
{
    Task<WaitlistOwner?> FindOwnerAsync(long id, CancellationToken ct = default);

    Task<WaitlistEntry?> GetByIdForUpdateAsync(long id, CancellationToken ct = default);

    Task<List<WaitlistEntry>> ListWaitingForUpdateAsync(long classId, CancellationToken ct = default);

    Task<WaitlistEntry?> FindWaitingAsync(long userId, long classId, CancellationToken ct = default);

    Task<int> CountWaitingPositionAsync(long classId, DateTime createdAt, long entryId, CancellationToken ct = default);

    Task<int> CountWaitingAsync(long classId, CancellationToken ct = default);
}
