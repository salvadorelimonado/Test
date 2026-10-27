using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;
using DataAccessLayer.Repositories.Interfaces;
using DataAccessLayer.Repositories.Projections;
using Microsoft.EntityFrameworkCore;

namespace DataAccessLayer.Repositories.Implementations;

public class EnrollmentRepository : GenericRepository<Enrollment>, IEnrollmentRepository
{
    private readonly DbSet<Pass> _passes;

    public EnrollmentRepository(DbSet<Enrollment> dbSet, DbSet<Pass> passes)
        : base(dbSet)
    {
        _passes = passes;
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
            .FromSql($"SELECT * FROM enrollments WHERE id = {id} FOR UPDATE")
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

    public async Task<EnrollmentCardRow?> FindCardAsync(long enrollmentId, long userId, CancellationToken ct = default)
    {
        return await _dbSet
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(enrollment => enrollment.Id == enrollmentId && enrollment.UserId == userId)
            .Select(enrollment => new EnrollmentCardRow
            {
                Id = enrollment.Id,
                Status = enrollment.Status,
                Seats = enrollment.Seats,
                CreatedAt = enrollment.CreatedAt,
                ClassId = enrollment.Class.Id,
                Title = enrollment.Class.Title,
                StartsAt = enrollment.Class.StartsAt,
                DurationMinutes = enrollment.Class.DurationMinutes,
                IsCancelled = enrollment.Class.IsCancelled,
                RoomId = enrollment.Class.Room.Id,
                RoomName = enrollment.Class.Room.Name,
                Pass = _passes
                    .Where(pass => pass.UserId == enrollment.UserId)
                    .Select(pass => new EnrollmentCardPassRow
                    {
                        RemainingVisits = pass.RemainingVisits,
                        ValidUntil = pass.ValidUntil
                    })
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);
    }
}
