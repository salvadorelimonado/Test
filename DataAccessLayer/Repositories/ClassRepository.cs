using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;
using EFCore.PostgresExtensions.Extensions;
using Microsoft.EntityFrameworkCore;

namespace DataAccessLayer.Repositories;

public class ClassRepository : GenericRepository<Class>, IClassRepository
{
    public ClassRepository(DbSet<Class> dbSet)
        : base(dbSet)
    {
    }

    public async Task<Class?> GetWithEnrollmentsAsync(long id, CancellationToken ct = default)
    {
        return await _dbSet
            .Include(x => x.Enrollments)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<Class?> GetByIdForUpdateAsync(long id, CancellationToken ct = default)
    {
        return await _dbSet
            .Where(x => x.Id == id)
            .ForUpdate()
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<ClassScheduleRow>> ListScheduleAsync(
        DateTime from,
        DateTime to,
        long? roomId,
        long userId,
        DateTime? cursorStartsAt,
        long? cursorId,
        int take,
        CancellationToken ct = default)
    {
        var query = _dbSet
            .AsNoTracking()
            .Where(scheduledClass => !scheduledClass.IsCancelled
                && scheduledClass.StartsAt >= from
                && scheduledClass.StartsAt < to);

        if (roomId is long selectedRoomId)
        {
            query = query.Where(scheduledClass => scheduledClass.RoomId == selectedRoomId);
        }

        if (cursorStartsAt is DateTime cursorStarts && cursorId is long cursorClassId)
        {
            query = query.Where(scheduledClass =>
                scheduledClass.StartsAt > cursorStarts
                || (scheduledClass.StartsAt == cursorStarts && scheduledClass.Id > cursorClassId));
        }

        return await query
            .OrderBy(scheduledClass => scheduledClass.StartsAt)
            .ThenBy(scheduledClass => scheduledClass.Id)
            .Select(scheduledClass => new ClassScheduleRow
            {
                ClassId = scheduledClass.Id,
                Title = scheduledClass.Title,
                StartsAt = scheduledClass.StartsAt,
                DurationMinutes = scheduledClass.DurationMinutes,
                Capacity = scheduledClass.Capacity,
                RoomId = scheduledClass.RoomId,
                RoomName = scheduledClass.Room.Name,
                TakenSeats = scheduledClass.Enrollments
                    .Where(enrollment => enrollment.Status == EnrollmentStatus.Active)
                    .Sum(enrollment => (int?)enrollment.Seats) ?? 0,
                MySeats = scheduledClass.Enrollments
                    .Where(enrollment => enrollment.Status == EnrollmentStatus.Active && enrollment.UserId == userId)
                    .Sum(enrollment => (int?)enrollment.Seats) ?? 0
            })
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<List<ClassLoadRow>> ListLoadAsync(
        DateOnly from,
        DateOnly to,
        long? roomId,
        CancellationToken ct = default)
    {
        var fromInstant = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toInstant = to.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var query = _dbSet
            .AsNoTracking()
            .Where(scheduledClass => !scheduledClass.IsCancelled
                && scheduledClass.StartsAt >= fromInstant
                && scheduledClass.StartsAt.Date <= toInstant);

        if (roomId is long selectedRoomId)
        {
            query = query.Where(scheduledClass => scheduledClass.RoomId == selectedRoomId);
        }

        var groups = await query
            .GroupBy(scheduledClass => new
            {
                Day = scheduledClass.StartsAt.Date,
                scheduledClass.RoomId,
                RoomName = scheduledClass.Room.Name
            })
            .Select(group => new ClassLoadRow
            {
                Day = group.Key.Day,
                RoomId = group.Key.RoomId,
                RoomName = group.Key.RoomName,
                ClassesCount = group.Count(),
                TotalCapacity = group.Sum(scheduledClass => scheduledClass.Capacity)
            })
            .ToListAsync(ct);

        var bookedSeats = await query
            .SelectMany(scheduledClass => scheduledClass.Enrollments)
            .Where(enrollment => enrollment.Status == EnrollmentStatus.Active)
            .GroupBy(enrollment => new
            {
                Day = enrollment.Class.StartsAt.Date,
                enrollment.Class.RoomId
            })
            .Select(group => new
            {
                group.Key.Day,
                group.Key.RoomId,
                Seats = group.Sum(enrollment => enrollment.Seats)
            })
            .ToListAsync(ct);

        var seatsByDayAndRoom = bookedSeats.ToDictionary(
            row => (DateOnly.FromDateTime(row.Day), row.RoomId),
            row => row.Seats);

        foreach (var group in groups)
        {
            if (seatsByDayAndRoom.TryGetValue((DateOnly.FromDateTime(group.Day), group.RoomId), out var seats))
            {
                group.BookedSeats = seats;
            }
        }

        return groups
            .OrderBy(group => group.Day)
            .ThenBy(group => group.RoomId)
            .ToList();
    }
}
