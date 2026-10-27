using BusinessLayer.Features.Schedule;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Features.Schedule.GetSchedule;

public sealed class GetScheduleRequest
{
    [ModelBinder(BinderType = typeof(UtcDateTimeModelBinder))]
    public DateTime? From { get; set; }

    [ModelBinder(BinderType = typeof(UtcDateTimeModelBinder))]
    public DateTime? To { get; set; }

    public long? RoomId { get; set; }

    public int? Limit { get; set; }

    [ModelBinder(BinderType = typeof(ScheduleContinuationBinder))]
    public ScheduleCursor? ContinuationToken { get; set; }
}
