using BusinessLayer.Features.Schedule;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Features.Schedule;

public sealed class GetScheduleRequest
{
    private string? _continuationToken;

    [ModelBinder(BinderType = typeof(UtcDateTimeModelBinder))]
    public DateTime? From { get; set; }

    [ModelBinder(BinderType = typeof(UtcDateTimeModelBinder))]
    public DateTime? To { get; set; }

    public long? RoomId { get; set; }

    public int? Limit { get; set; }

    public string? ContinuationToken
    {
        get => _continuationToken;
        set
        {
            _continuationToken = value;
            if (string.IsNullOrWhiteSpace(value))
            {
                Cursor = null;
                return;
            }

            if (!ScheduleContinuation.TryRead(value, out var startsAt, out var classId))
            {
                Cursor = null;
                return;
            }

            Cursor = new ScheduleCursor(startsAt, classId);
        }
    }

    public ScheduleCursor? Cursor { get; private set; }
}
