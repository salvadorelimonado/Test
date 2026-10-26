using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;

namespace DataAccessLayer.Repositories;

public interface IWaitlistRepository : IGenericRepository<WaitlistEntry>
{
    Task<List<WaitlistEntry>> ListWaitingForUpdateAsync(long classId, CancellationToken ct = default);
}
