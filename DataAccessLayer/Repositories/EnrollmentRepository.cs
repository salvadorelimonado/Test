using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;
using EFCore.PostgresExtensions.Extensions;
using Microsoft.EntityFrameworkCore;

namespace DataAccessLayer.Repositories;

public class EnrollmentRepository : GenericRepository<Enrollment>, IEnrollmentRepository
{
    public EnrollmentRepository(DbSet<Enrollment> dbSet)
        : base(dbSet)
    {
    }

    public async Task<EnrollmentOwner?> FindOwnerAsync(long id, CancellationToken ct = default)
    {
        return await _dbSet
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new EnrollmentOwner(x.UserId, x.ClassId))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Enrollment?> GetByIdForUpdateAsync(long id, CancellationToken ct = default)
    {
        return await _dbSet
            .Where(x => x.Id == id)
            .ForUpdate()
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<Enrollment>> GetByUserAsync(long userId, CancellationToken ct = default)
    {
        return await _dbSet
            .Where(x => x.UserId == userId)
            .ToListAsync(ct);
    }

    public async Task<int> SumActiveSeatsAsync(long classId, CancellationToken ct = default)
    {
        return await _dbSet
            .Where(x => x.ClassId == classId && x.Status == EnrollmentStatus.Active)
            .SumAsync(x => x.Seats, ct);
    }
}
