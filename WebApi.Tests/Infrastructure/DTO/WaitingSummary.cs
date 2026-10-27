namespace WebApi.Tests.Infrastructure.DTO;

public sealed record WaitingSummary(int WaitingCount, long EntryId, int Seats, int RemainingVisits);
