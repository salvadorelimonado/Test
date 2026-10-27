using DataAccessLayer;
using DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebApi.Tests.Infrastructure.DTO;

namespace WebApi.Tests.Infrastructure.Stores;

public sealed class EnrollmentStore
{
    private readonly TestSession _session;
    private readonly UsersStore _users;

    internal EnrollmentStore(TestSession session, UsersStore users)
    {
        _session = session;
        _users = users;
    }

    public async Task SetEnrollmentStatus(long enrollmentId, EnrollmentStatus status)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var enrollment = await dbContext.Enrollments.SingleAsync(item => item.Id == enrollmentId);
        enrollment.Status = status;
        await dbContext.SaveChangesAsync();
    }

    public async Task<ActiveBooking> ActiveBooking(long classId, long userId)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var count = await dbContext.Enrollments.CountAsync(
            enrollment => enrollment.ClassId == classId && enrollment.UserId == userId && enrollment.Status == EnrollmentStatus.Active);
        var visits = await dbContext.Passes.Where(pass => pass.UserId == userId).Select(pass => pass.RemainingVisits).SingleAsync();
        return new ActiveBooking(count, visits);
    }

    public async Task<EnrollmentRow> Enrollment(long enrollmentId, long userId)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var enrollment = await dbContext.Enrollments.SingleAsync(item => item.Id == enrollmentId);
        var visits = await dbContext.Passes.Where(pass => pass.UserId == userId).Select(pass => pass.RemainingVisits).SingleAsync();
        return new EnrollmentRow(enrollment.Seats, enrollment.Status, enrollment.RefundedVisits, visits);
    }

    public async Task<long> ActiveEnrollmentId(long userId, long classId)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        return await dbContext.Enrollments
            .Where(enrollment => enrollment.UserId == userId && enrollment.ClassId == classId && enrollment.Status == EnrollmentStatus.Active)
            .Select(enrollment => enrollment.Id)
            .SingleAsync();
    }

    public async Task<int> ActiveCount(long userId, long classId)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        return await dbContext.Enrollments.CountAsync(
            enrollment => enrollment.UserId == userId && enrollment.ClassId == classId && enrollment.Status == EnrollmentStatus.Active);
    }

    public async Task<int> ActiveSeats(long userId, long classId)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        return await dbContext.Enrollments
            .Where(enrollment => enrollment.UserId == userId && enrollment.ClassId == classId && enrollment.Status == EnrollmentStatus.Active)
            .SumAsync(enrollment => (int?)enrollment.Seats) ?? 0;
    }

    public async Task<int> SumActiveSeats(long classId)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        return await dbContext.Enrollments
            .Where(enrollment => enrollment.ClassId == classId && enrollment.Status == EnrollmentStatus.Active)
            .SumAsync(enrollment => (int?)enrollment.Seats) ?? 0;
    }

    public async Task<long> AddEnrollment(int userNumber, long classId, int seats, EnrollmentStatus status)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var user = await _users.FindAsync(dbContext, userNumber);
        var enrollment = new Enrollment
        {
            UserId = user.Id,
            ClassId = classId,
            Seats = seats,
            Status = status,
            CreatedAt = _session.Now,
            CancelledAt = status == EnrollmentStatus.Cancelled ? _session.Now : null
        };
        dbContext.Enrollments.Add(enrollment);
        await dbContext.SaveChangesAsync();
        return enrollment.Id;
    }

    public async Task<long> InsertSpentEnrollment(long userId, long classId, int seats, int visitsBeforeSpend)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var pass = await dbContext.Passes.SingleAsync(item => item.UserId == userId);
        pass.RemainingVisits = visitsBeforeSpend - seats;
        var enrollment = new Enrollment
        {
            UserId = userId,
            ClassId = classId,
            Seats = seats,
            Status = EnrollmentStatus.Active,
            CreatedAt = _session.Now
        };
        dbContext.Enrollments.Add(enrollment);
        await dbContext.SaveChangesAsync();
        return enrollment.Id;
    }

    public async Task InsertEnrollmentRow(long userId, long classId, int seats, string status)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var createdAt = _session.Now;
        var refundedVisits = 0;
        await dbContext.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO enrollments (user_id, class_id, seats, status, created_at, refunded_visits)
            VALUES ({userId}, {classId}, {seats}, {status}, {createdAt}, {refundedVisits})
            """);
    }

    public async Task<string> EnrollmentStatusText(long enrollmentId)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        return await dbContext.Database
            .SqlQuery<string>($"SELECT status AS \"Value\" FROM enrollments WHERE id = {enrollmentId}")
            .SingleAsync();
    }
}
