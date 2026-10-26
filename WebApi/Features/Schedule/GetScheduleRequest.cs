using Microsoft.AspNetCore.Mvc;

namespace WebApi.Features.Schedule;

public sealed class GetScheduleRequest
{
    [ModelBinder(BinderType = typeof(UtcDateTimeModelBinder))]
    public DateTime? From { get; set; }

    [ModelBinder(BinderType = typeof(UtcDateTimeModelBinder))]
    public DateTime? To { get; set; }

    public long? RoomId { get; set; }

    public int? Limit { get; set; }

    public string? ContinuationToken { get; set; }
}
