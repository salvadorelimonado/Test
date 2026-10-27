using DataAccessLayer.Entities;

namespace WebApi.Tests.Infrastructure.DTO;

public sealed record WaitlistRow(WaitlistStatus Status, DateTime CreatedAt);
