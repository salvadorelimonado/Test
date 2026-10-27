using DataAccessLayer;
using DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebApi.Tests.Infrastructure.DTO;

namespace WebApi.Tests.Infrastructure.Stores;

public sealed class ClassesStore
{
    private readonly TestSession _session;
    private readonly UsersStore _users;

    internal ClassesStore(TestSession session, UsersStore users)
    {
        _session = session;
        _users = users;
    }

    internal DateTime Now => _session.Now;

    internal DateOnly Today => _session.Today;

    public LessonBuilder Lesson()
    {
        return new LessonBuilder(this);
    }

    public async Task<IReadOnlyList<RoomRef>> Rooms(int count)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        return await dbContext.Rooms
            .OrderBy(room => room.Id)
            .Take(count)
            .Select(room => new RoomRef(room.Id, room.Name))
            .ToListAsync();
    }

    public async Task SetStartsAt(long classId, DateTime startsAt)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var scheduledClass = await dbContext.Classes.IgnoreQueryFilters().SingleAsync(item => item.Id == classId);
        scheduledClass.StartsAt = startsAt;
        await dbContext.SaveChangesAsync();
    }

    public async Task CancelClass(long classId)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var scheduledClass = await dbContext.Classes.IgnoreQueryFilters().SingleAsync(item => item.Id == classId);
        scheduledClass.IsCancelled = true;
        await dbContext.SaveChangesAsync();
    }

    internal async Task<Lesson> CreateLesson(
        int capacity,
        DateTime startsAt,
        long? roomId,
        bool cancelled,
        IReadOnlyList<LessonBuilder.PassSetup> passes,
        IReadOnlyList<LessonBuilder.SeatSetup> taken)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var room = roomId is long selectedRoomId
            ? await dbContext.Rooms.SingleAsync(room => room.Id == selectedRoomId)
            : await dbContext.Rooms.OrderBy(room => room.Id).FirstAsync();
        var owner = passes[0];
        var ownerUser = await _users.FindAsync(dbContext, owner.UserNumber);
        foreach (var pass in passes)
        {
            var user = pass.UserNumber == owner.UserNumber
                ? ownerUser
                : await _users.FindAsync(dbContext, pass.UserNumber);
            var membership = await dbContext.Passes.SingleAsync(item => item.UserId == user.Id);
            membership.RemainingVisits = pass.Visits;
            membership.ValidUntil = pass.ValidUntil;
        }

        var scheduledClass = new Class
        {
            RoomId = room.Id,
            Title = DTO.Lesson.DefaultTitle,
            StartsAt = startsAt,
            DurationMinutes = DTO.Lesson.DefaultDurationMinutes,
            Capacity = capacity,
            IsCancelled = cancelled
        };
        dbContext.Classes.Add(scheduledClass);
        await dbContext.SaveChangesAsync();
        foreach (var seat in taken)
        {
            var holder = await _users.FindAsync(dbContext, seat.UserNumber);
            dbContext.Enrollments.Add(new Enrollment
            {
                UserId = holder.Id,
                ClassId = scheduledClass.Id,
                Seats = seat.Seats,
                Status = EnrollmentStatus.Active,
                CreatedAt = _session.Now
            });
        }

        await dbContext.SaveChangesAsync();
        return new Lesson
        {
            ClassId = scheduledClass.Id,
            UserId = ownerUser.Id,
            UserNumber = owner.UserNumber,
            RoomId = room.Id,
            RoomName = room.Name,
            StartsAt = startsAt,
            ValidUntil = owner.ValidUntil
        };
    }
}
