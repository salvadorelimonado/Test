using DataAccessLayer.Entities;

namespace WebApi.Tests.Infrastructure;

public sealed record ActiveBooking(int Count, int RemainingVisits);

public sealed record EnrollmentRow(int Seats, EnrollmentStatus Status, int RefundedVisits, int RemainingVisits);

public sealed record WaitingSummary(int WaitingCount, long EntryId, int Seats, int RemainingVisits);

public sealed record WaitlistRow(WaitlistStatus Status, DateTime CreatedAt);

public sealed record RoomRef(long Id, string Name);
