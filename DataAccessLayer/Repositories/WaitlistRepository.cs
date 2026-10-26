using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;
using EFCore.PostgresExtensions.Extensions;
using Microsoft.EntityFrameworkCore;

namespace DataAccessLayer.Repositories;

public class WaitlistRepository : GenericRepository<WaitlistEntry>, IWaitlistRepository
{
    public WaitlistRepository(DbSet<WaitlistEntry> dbSet)
        : base(dbSet)
    {
    }

    public async Task<WaitlistOwner?> FindOwnerAsync(long id, CancellationToken ct = default)
    {
        return await _dbSet
            .AsNoTracking()
            .Where(entry => entry.Id == id)
            .Select(entry => new WaitlistOwner(entry.UserId, entry.ClassId))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<WaitlistEntry?> GetByIdForUpdateAsync(long id, CancellationToken ct = default)
    {
        return await _dbSet
            .Where(entry => entry.Id == id)
            .ForUpdate()
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<WaitlistEntry>> ListWaitingForUpdateAsync(long classId, CancellationToken ct = default)
    {
        return await _dbSet
            .Where(x => x.ClassId == classId && x.Status == WaitlistStatus.Waiting)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ForUpdate()
            .ToListAsync(ct);
    }

    public async Task<WaitlistEntry?> FindWaitingAsync(long userId, long classId, CancellationToken ct = default)
    {
        return await _dbSet
            .AsNoTracking()
            .FirstOrDefaultAsync(
                entry => entry.UserId == userId && entry.ClassId == classId && entry.Status == WaitlistStatus.Waiting,
                ct);
    }

    public async Task<int> CountWaitingPositionAsync(
        long classId,
        DateTime createdAt,
        long entryId,
        CancellationToken ct = default)
    {
        return await _dbSet.CountAsync(
            entry => entry.ClassId == classId
                && entry.Status == WaitlistStatus.Waiting
                && (entry.CreatedAt < createdAt || (entry.CreatedAt == createdAt && entry.Id <= entryId)),
            ct);
    }

    public Task<int> CountWaitingAsync(long classId, CancellationToken ct = default)
    {
        return _dbSet.CountAsync(
            entry => entry.ClassId == classId && entry.Status == WaitlistStatus.Waiting,
            ct);
    }
}
