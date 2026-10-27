using DataAccessLayer.Entities;

namespace WebApi.Tests.Infrastructure.DTO;

public sealed record EnrollmentRow(int Seats, EnrollmentStatus Status, int RefundedVisits, int RemainingVisits);
