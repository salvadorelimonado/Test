using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;
using DataAccessLayer.Repositories.Projections;

namespace DataAccessLayer.Repositories.Interfaces;

public interface IEnrollmentRepository : IGenericRepository<Enrollment>
{
    Task<EnrollmentOwner?> FindOwnerAsync(long id, CancellationToken ct = default);

    Task<Enrollment?> GetByIdForUpdateAsync(long id, CancellationToken ct = default);

    Task<List<Enrollment>> GetByUserAsync(long userId, CancellationToken ct = default);

    Task<int> SumActiveSeatsAsync(long classId, CancellationToken ct = default);

    Task<EnrollmentCardRow?> FindCardAsync(long enrollmentId, long userId, CancellationToken ct = default);
}
