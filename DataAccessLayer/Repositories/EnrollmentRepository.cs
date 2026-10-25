using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataAccessLayer.Repositories;

public class EnrollmentRepository : GenericRepository<Enrollment>, IEnrollmentRepository
{
    public EnrollmentRepository(DbSet<Enrollment> dbSet)
        : base(dbSet)
    {
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
