using DataAccessLayer;
using DataAccessLayer.Entities;
using DataAccessLayer.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebApi.Tests.Infrastructure.Http.Clients;

namespace WebApi.Tests.Infrastructure.Stores;

public sealed class UsersStore
{
    private static readonly string PasswordHash = new PasswordHasher<User>().HashPassword(new User(), DatabaseSeeder.DefaultPassword);
    private static int _nextRegisteredNumber = 1_000;

    private readonly TestSession _session;
    private readonly Dictionary<int, string> _registeredEmails = [];

    internal UsersStore(TestSession session)
    {
        _session = session;
    }

    public async Task<ClubUser> RegisterMember(int visits, DateOnly validUntil)
    {
        var number = Interlocked.Increment(ref _nextRegisteredNumber);
        var email = $"member{number}@club.test";
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var user = new User
        {
            Email = email,
            PasswordHash = PasswordHash,
            Role = "user",
            CreatedAt = _session.Now
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
        return new ClubUser(number, email);
    }

    public ClubUser User(int userNumber)
    {
        return new ClubUser(userNumber, EmailFor(userNumber));
    }

    public ClubUser Admin(int adminNumber)
    {
        return new ClubUser(adminNumber, AdminEmail(adminNumber));
    }

    public ClubUser Anonymous()
    {
        return new ClubUser(0, null);
    }

    public async Task<long> UserId(int userNumber)
    {
        await using var scope = _session.OpenScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var user = await FindAsync(dbContext, userNumber);
        return user.Id;
    }

    internal async Task<User> FindAsync(ClubDbContext dbContext, int userNumber)
    {
        var email = EmailFor(userNumber);
        return await dbContext.Users.SingleAsync(user => user.Email == email);
    }

    internal string EmailFor(int userNumber)
    {
        if (_registeredEmails.TryGetValue(userNumber, out var email))
        {
            return email;
        }

        return SeedEmail(userNumber);
    }

    private static string SeedEmail(int userNumber)
    {
        return $"user{userNumber:D3}@club.test";
    }

    private static string AdminEmail(int adminNumber)
    {
        return $"admin{adminNumber:D3}@club.test";
    }
}
