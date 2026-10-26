using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;

namespace DataAccessLayer.Repositories;

public interface IClassRepository : IGenericRepository<Class>
{
    Task<Class?> GetWithEnrollmentsAsync(long id, CancellationToken ct = default);

    Task<Class?> GetByIdForUpdateAsync(long id, CancellationToken ct = default);

    Task<List<ClassScheduleRow>> ListScheduleAsync(
        DateTime from,
        DateTime to,
        long? roomId,
        long userId,
        DateTime? cursorStartsAt,
        long? cursorId,
        int take,
        CancellationToken ct = default);

    Task<List<ClassLoadRow>> ListLoadAsync(
        DateOnly from,
        DateOnly to,
        long? roomId,
        CancellationToken ct = default);
}
