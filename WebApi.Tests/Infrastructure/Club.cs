using System.Net.Http.Json;
using BusinessLayer.Features.Enrollments.Cancel;
using BusinessLayer.Result;
using DataAccessLayer;
using DataAccessLayer.Entities;
using DataAccessLayer.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using WebApi.Auth;

namespace WebApi.Tests.Infrastructure;

public sealed class Club : IDisposable
{
    private static readonly string PasswordHash = new PasswordHasher<User>().HashPassword(new User(), DatabaseSeeder.DefaultPassword);
    private static ApiApplicationFactory? _sharedFactory;
    private static int _nextRegisteredNumber = 1_000;

    private readonly ApiApplicationFactory _factory;
    private readonly Dictionary<string, string> _tokens = [];
    private readonly Dictionary<int, string> _registeredEmails = [];

    private Club(ApiApplicationFactory factory)
    {
        _factory = factory;
        Http = factory.CreateClient();
    }

    internal HttpClient Http { get; }

    public static Club Open()
    {
        _sharedFactory ??= new ApiApplicationFactory();
        return new Club(_sharedFactory);
    }

    public static void Shutdown()
    {
        _sharedFactory?.Dispose();
        _sharedFactory = null;
    }

    public LessonDraft Lesson()
    {
        return new LessonDraft(this);
    }

    public FrozenClock Freeze(DateTime instant)
    {
        if (instant.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The instant must be UTC.", nameof(instant));
        }

        return new FrozenClock(_factory.Clock, new DateTimeOffset(instant));
    }

    public async Task<Member> RegisterMember(int visits, DateOnly validUntil)
    {
        var number = Interlocked.Increment(ref _nextRegisteredNumber);
        var email = $"member{number}@club.test";
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var user = new User
        {
            Email = email,
            PasswordHash = PasswordHash,
            Role = "user",
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        dbContext.Passes.Add(new Pass
        {
            UserId = user.Id,
            RemainingVisits = visits,
            ValidUntil = validUntil
        });
        await dbContext.SaveChangesAsync();
        _registeredEmails[number] = email;
        return new Member(this, number, email);
    }

    public Member User(int userNumber)
    {
        return new Member(this, userNumber, EmailFor(userNumber));
    }

    public Member Admin(int adminNumber)
    {
        return new Member(this, adminNumber, AdminEmail(adminNumber));
    }

    public Member Anonymous()
    {
        return new Member(this, 0, null);
    }

    public void Dispose()
    {
        Http.Dispose();
    }

    internal async Task<string> Token(string email)
    {
        if (_tokens.TryGetValue(email, out var cached))
        {
            return cached;
        }

        var response = await Http.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest { Email = email, Password = DatabaseSeeder.DefaultPassword });
        response.EnsureSuccessStatusCode();
        var body = await response.ReadBody<LoginResponse>();
        if (string.IsNullOrWhiteSpace(body.AccessToken))
        {
            throw new InvalidOperationException("Login did not return an access token.");
        }

        _tokens[email] = body.AccessToken;
        return body.AccessToken;
    }

    internal async Task<Lesson> CreateLesson(
        int capacity,
        DateTime startsAt,
        long? roomId,
        bool cancelled,
        IReadOnlyList<LessonDraft.PassSetup> passes,
        IReadOnlyList<LessonDraft.SeatSetup> taken)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var room = roomId is long selectedRoomId
            ? await dbContext.Rooms.SingleAsync(room => room.Id == selectedRoomId)
            : await dbContext.Rooms.OrderBy(room => room.Id).FirstAsync();
        var owner = passes[0];
        var ownerUser = await FindUser(dbContext, owner.UserNumber);
        foreach (var pass in passes)
        {
            var user = pass.UserNumber == owner.UserNumber
                ? ownerUser
                : await FindUser(dbContext, pass.UserNumber);
            var membership = await dbContext.Passes.SingleAsync(item => item.UserId == user.Id);
            membership.RemainingVisits = pass.Visits;
            membership.ValidUntil = pass.ValidUntil;
        }

        var scheduledClass = new Class
        {
            RoomId = room.Id,
            Title = WebApi.Tests.Infrastructure.Lesson.DefaultTitle,
            StartsAt = startsAt,
            DurationMinutes = WebApi.Tests.Infrastructure.Lesson.DefaultDurationMinutes,
            Capacity = capacity,
            IsCancelled = cancelled
        };
        dbContext.Classes.Add(scheduledClass);
        await dbContext.SaveChangesAsync();
        foreach (var seat in taken)
        {
            var holder = await FindUser(dbContext, seat.UserNumber);
            dbContext.Enrollments.Add(new Enrollment
            {
                UserId = holder.Id,
                ClassId = scheduledClass.Id,
                Seats = seat.Seats,
                Status = EnrollmentStatus.Active,
                CreatedAt = DateTime.UtcNow
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

    public async Task<IReadOnlyList<RoomRef>> Rooms(int count)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        return await dbContext.Rooms
            .OrderBy(room => room.Id)
            .Take(count)
            .Select(room => new RoomRef(room.Id, room.Name))
            .ToListAsync();
    }

    public async Task<long> UserId(int userNumber)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var user = await FindUser(dbContext, userNumber);
        return user.Id;
    }

    public async Task SetPass(int userNumber, int visits, DateOnly validUntil)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var user = await FindUser(dbContext, userNumber);
        var pass = await dbContext.Passes.SingleAsync(item => item.UserId == user.Id);
        pass.RemainingVisits = visits;
        pass.ValidUntil = validUntil;
        await dbContext.SaveChangesAsync();
    }

    public async Task SetVisits(long userId, int visits)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var pass = await dbContext.Passes.SingleAsync(item => item.UserId == userId);
        pass.RemainingVisits = visits;
        await dbContext.SaveChangesAsync();
    }

    public async Task SetValidUntil(long userId, DateOnly validUntil)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var pass = await dbContext.Passes.SingleAsync(item => item.UserId == userId);
        pass.ValidUntil = validUntil;
        await dbContext.SaveChangesAsync();
    }

    public async Task SetStartsAt(long classId, DateTime startsAt)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var scheduledClass = await dbContext.Classes.IgnoreQueryFilters().SingleAsync(item => item.Id == classId);
        scheduledClass.StartsAt = startsAt;
        await dbContext.SaveChangesAsync();
    }

    public async Task CancelClass(long classId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var scheduledClass = await dbContext.Classes.IgnoreQueryFilters().SingleAsync(item => item.Id == classId);
        scheduledClass.IsCancelled = true;
        await dbContext.SaveChangesAsync();
    }

    public async Task SetEnrollmentStatus(long enrollmentId, EnrollmentStatus status)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var enrollment = await dbContext.Enrollments.SingleAsync(item => item.Id == enrollmentId);
        enrollment.Status = status;
        await dbContext.SaveChangesAsync();
    }

    public async Task SetWaitlistStatus(long entryId, WaitlistStatus status)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var entry = await dbContext.WaitlistEntries.SingleAsync(item => item.Id == entryId);
        entry.Status = status;
        await dbContext.SaveChangesAsync();
    }

    public async Task<long> AddWaiting(int userNumber, long classId, int seats, DateTime createdAt)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var user = await FindUser(dbContext, userNumber);
        var entry = new WaitlistEntry
        {
            UserId = user.Id,
            ClassId = classId,
            Seats = seats,
            Status = WaitlistStatus.Waiting,
            CreatedAt = createdAt
        };
        dbContext.WaitlistEntries.Add(entry);
        await dbContext.SaveChangesAsync();
        return entry.Id;
    }

    public async Task<long> InsertSpentEnrollment(long userId, long classId, int seats, int visitsBeforeSpend)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var pass = await dbContext.Passes.SingleAsync(item => item.UserId == userId);
        pass.RemainingVisits = visitsBeforeSpend - seats;
        var enrollment = new Enrollment
        {
            UserId = userId,
            ClassId = classId,
            Seats = seats,
            Status = EnrollmentStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Enrollments.Add(enrollment);
        await dbContext.SaveChangesAsync();
        return enrollment.Id;
    }

    public async Task<ActiveBooking> ActiveBooking(long classId, long userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var count = await dbContext.Enrollments.CountAsync(
            enrollment => enrollment.ClassId == classId && enrollment.UserId == userId && enrollment.Status == EnrollmentStatus.Active);
        var visits = await dbContext.Passes.Where(pass => pass.UserId == userId).Select(pass => pass.RemainingVisits).SingleAsync();
        return new ActiveBooking(count, visits);
    }

    public async Task<EnrollmentRow> Enrollment(long enrollmentId, long userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var enrollment = await dbContext.Enrollments.SingleAsync(item => item.Id == enrollmentId);
        var visits = await dbContext.Passes.Where(pass => pass.UserId == userId).Select(pass => pass.RemainingVisits).SingleAsync();
        return new EnrollmentRow(enrollment.Seats, enrollment.Status, enrollment.RefundedVisits, visits);
    }

    public async Task<long> ActiveEnrollmentId(long userId, long classId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        return await dbContext.Enrollments
            .Where(enrollment => enrollment.UserId == userId && enrollment.ClassId == classId && enrollment.Status == EnrollmentStatus.Active)
            .Select(enrollment => enrollment.Id)
            .SingleAsync();
    }

    public async Task<int> ActiveCount(long userId, long classId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        return await dbContext.Enrollments.CountAsync(
            enrollment => enrollment.UserId == userId && enrollment.ClassId == classId && enrollment.Status == EnrollmentStatus.Active);
    }

    public async Task<int> ActiveSeats(long userId, long classId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        return await dbContext.Enrollments
            .Where(enrollment => enrollment.UserId == userId && enrollment.ClassId == classId && enrollment.Status == EnrollmentStatus.Active)
            .SumAsync(enrollment => (int?)enrollment.Seats) ?? 0;
    }

    public async Task<int> RemainingVisits(long userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        return await dbContext.Passes.Where(pass => pass.UserId == userId).Select(pass => pass.RemainingVisits).SingleAsync();
    }

    public async Task<WaitlistStatus> WaitlistStatusOf(long entryId)
    {
        var row = await Waitlist(entryId);
        return row.Status;
    }

    public async Task<WaitlistRow> Waitlist(long entryId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var entry = await dbContext.WaitlistEntries.SingleAsync(item => item.Id == entryId);
        return new WaitlistRow(entry.Status, entry.CreatedAt);
    }

    public async Task<int> WaitingCount(long classId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        return await dbContext.WaitlistEntries.CountAsync(
            entry => entry.ClassId == classId && entry.Status == WaitlistStatus.Waiting);
    }

    public async Task<int> WaitlistRows(long classId, long userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        return await dbContext.WaitlistEntries.CountAsync(entry => entry.ClassId == classId && entry.UserId == userId);
    }

    public async Task<WaitingSummary> Waiting(long classId, long userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var waiting = await dbContext.WaitlistEntries
            .Where(entry => entry.ClassId == classId && entry.UserId == userId && entry.Status == WaitlistStatus.Waiting)
            .Select(entry => new { entry.Id, entry.Seats })
            .ToListAsync();
        var visits = await dbContext.Passes.Where(pass => pass.UserId == userId).Select(pass => pass.RemainingVisits).SingleAsync();
        var entry = waiting.SingleOrDefault();
        return new WaitingSummary(waiting.Count, entry?.Id ?? 0, entry?.Seats ?? 0, visits);
    }

    public async Task<int> SumActiveSeats(long classId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        return await dbContext.Enrollments
            .Where(enrollment => enrollment.ClassId == classId && enrollment.Status == EnrollmentStatus.Active)
            .SumAsync(enrollment => (int?)enrollment.Seats) ?? 0;
    }

    public async Task<long> AddEnrollment(int userNumber, long classId, int seats, EnrollmentStatus status)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var user = await FindUser(dbContext, userNumber);
        var enrollment = new Enrollment
        {
            UserId = user.Id,
            ClassId = classId,
            Seats = seats,
            Status = status,
            CreatedAt = DateTime.UtcNow,
            CancelledAt = status == EnrollmentStatus.Cancelled ? DateTime.UtcNow : null
        };
        dbContext.Enrollments.Add(enrollment);
        await dbContext.SaveChangesAsync();
        return enrollment.Id;
    }

    public async Task InsertEnrollmentRow(long userId, long classId, int seats, string status)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var createdAt = DateTime.UtcNow;
        var refundedVisits = 0;
        await dbContext.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO enrollments (user_id, class_id, seats, status, created_at, refunded_visits)
            VALUES ({userId}, {classId}, {seats}, {status}, {createdAt}, {refundedVisits})
            """);
    }

    public async Task<string> EnrollmentStatusText(long enrollmentId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        return await dbContext.Database
            .SqlQuery<string>($"SELECT status AS \"Value\" FROM enrollments WHERE id = {enrollmentId}")
            .SingleAsync();
    }

    public async Task InScope(Func<IServiceProvider, Task> work)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await work(scope.ServiceProvider);
    }

    public async Task<OperationResult<CancelledEnrollment>> CancelThroughTheService(long userId, long enrollmentId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<CancelEnrollmentService>();
        return await service.CancelAsync(
            new CancelEnrollmentCommand
            {
                UserId = userId,
                EnrollmentId = enrollmentId
            },
            CancellationToken.None);
    }

    public async Task WaitUntilRedisResponds()
    {
        var multiplexer = _factory.Services.GetRequiredService<IConnectionMultiplexer>();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await multiplexer.GetDatabase().PingAsync();
                return;
            }
            catch (RedisException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }

        throw new InvalidOperationException("Redis did not respond.");
    }

    private async Task<User> FindUser(ClubDbContext dbContext, int userNumber)
    {
        var email = EmailFor(userNumber);
        return await dbContext.Users.SingleAsync(user => user.Email == email);
    }

    private string EmailFor(int userNumber)
    {
        if (_registeredEmails.TryGetValue(userNumber, out var email))
        {
            return email;
        }

        return Email(userNumber);
    }

    private static string Email(int userNumber)
    {
        return $"user{userNumber:D3}@club.test";
    }

    private static string AdminEmail(int adminNumber)
    {
        return $"admin{adminNumber:D3}@club.test";
    }
}
