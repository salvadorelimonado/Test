using Common.Database.UnitOfWork;
using DataAccessLayer.Entities;
using DataAccessLayer.Repositories.Implementations;
using DataAccessLayer.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DataAccessLayer.UnitOfWork;

public interface IUnitOfWork : IUnitOfWorkBase
{
    IRoomRepository Room { get; }

    IClassRepository Class { get; }

    IEnrollmentRepository Enrollment { get; }

    IPassRepository Pass { get; }

    IIdempotencyRepository Idempotency { get; }

    IWaitlistRepository Waitlist { get; }

    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        Func<T, bool> shouldCommit,
        CancellationToken ct);
}

public class UnitOfWork : UnitOfWorkBase, IUnitOfWork
{
    public UnitOfWork(ClubDbContext dbContext)
        : base(dbContext)
    {
    }

    public IRoomRepository Room => GetRepository<Room, IRoomRepository>(set => new RoomRepository(set));

    public IClassRepository Class => GetRepository<Class, IClassRepository>(set => new ClassRepository(set));

    public IEnrollmentRepository Enrollment =>
        GetRepository<Enrollment, IEnrollmentRepository>(_ =>
            new EnrollmentRepository(DbContext.Set<Enrollment>(), DbContext.Set<Pass>()));

    public IPassRepository Pass => GetRepository<Pass, IPassRepository>(set => new PassRepository(set));

    public IIdempotencyRepository Idempotency =>
        GetRepository<IdempotencyRecord, IIdempotencyRepository>(set => new IdempotencyRepository(set));

    public IWaitlistRepository Waitlist =>
        GetRepository<WaitlistEntry, IWaitlistRepository>(set => new WaitlistRepository(set));

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        Func<T, bool> shouldCommit,
        CancellationToken ct)
    {
        ClearChangeTracker();
        await using var transaction = await DbContext.Database.BeginTransactionAsync(ct);
        var result = await work(ct);
        if (shouldCommit(result))
        {
            await transaction.CommitAsync(ct);
        }

        return result;
    }
}
