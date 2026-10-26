using System.Text;
using Common.Caching;
using DataAccessLayer;
using DataAccessLayer.Entities;
using DataAccessLayer.Seeding;
using DataAccessLayer.UnitOfWork;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using Microsoft.IdentityModel.Tokens;
using SharpGrip.FluentValidation.AutoValidation.Mvc.Extensions;
using StackExchange.Redis;
using BusinessLayer.Features.Enrollments.ChangeSeats;
using BusinessLayer.Features.Enrollments.Create;
using BusinessLayer.Idempotency;
using WebApi.Auth;
using WebApi.Features.Enrollments.Create;
using WebApi.Idempotency;
using WebApi.Middleware;
using WebApi.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

builder.Services.AddControllers();
builder.Services.AddValidatorsFromAssemblyContaining<CreateEnrollmentRequestValidator>();
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

builder.Services.AddDbContext<ClubDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IdempotencyExecution>();
builder.Services.AddScoped<CreateEnrollmentService>();
builder.Services.AddScoped<ChangeSeatsService>();
builder.Services.AddScoped<IdempotencyKeyReader>();

EnrollmentRateLimit.Add(builder.Services);

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));

builder.Services.AddSingleton<ICacheService>(provider =>
    new RedisCacheService(provider.GetRequiredService<IConnectionMultiplexer>(), TimeSpan.FromMinutes(5)));

builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()!;

builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<AccessTokenService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health");

app.Services.GetRequiredService<IConnectionMultiplexer>();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();

    await dbContext.Database.MigrateAsync();
    await DatabaseSeeder.SeedAsync(dbContext);
}

app.Run();

public partial class Program;
