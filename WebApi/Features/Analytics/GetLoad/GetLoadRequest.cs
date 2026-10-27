using Microsoft.AspNetCore.Mvc;

namespace WebApi.Features.Analytics.GetLoad;

public sealed class GetLoadRequest
{
    [ModelBinder(BinderType = typeof(UtcDateModelBinder))]
    public DateOnly? From { get; set; }

    [ModelBinder(BinderType = typeof(UtcDateModelBinder))]
    public DateOnly? To { get; set; }

    public long? RoomId { get; set; }
}
