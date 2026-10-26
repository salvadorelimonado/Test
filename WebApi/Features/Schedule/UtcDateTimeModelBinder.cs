using Microsoft.AspNetCore.Mvc.ModelBinding;
using NodaTime;
using NodaTime.Text;

namespace WebApi.Features.Schedule;

public sealed class UtcDateTimeModelBinder : IModelBinder
{
    private static readonly OffsetDateTimePattern OffsetDateTime = OffsetDateTimePattern.ExtendedIso;
    private static readonly LocalDateTimePattern LocalDateTime = LocalDateTimePattern.ExtendedIso;
    private static readonly OffsetDateTimePattern OffsetMinutes =
        OffsetDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd'T'HH':'mmo<G>");
    private static readonly LocalDateTimePattern LocalMinutes =
        LocalDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd'T'HH':'mm");
    private static readonly LocalDatePattern Date = LocalDatePattern.Iso;

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (value == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, value);
        if (!TryParseUtc(value.FirstValue, out var utc))
        {
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "The value is not a date and time.");
            return Task.CompletedTask;
        }

        bindingContext.Result = ModelBindingResult.Success(utc);
        return Task.CompletedTask;
    }

    private static bool TryParseUtc(string? text, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var offset = OffsetDateTime.Parse(text);
        if (offset.Success)
        {
            return TrySet(offset.Value.ToInstant(), out utc);
        }

        var local = LocalDateTime.Parse(text);
        if (local.Success)
        {
            return TrySet(local.Value.WithOffset(Offset.Zero).ToInstant(), out utc);
        }

        var offsetMinutes = OffsetMinutes.Parse(text);
        if (offsetMinutes.Success)
        {
            return TrySet(offsetMinutes.Value.ToInstant(), out utc);
        }

        var localMinutes = LocalMinutes.Parse(text);
        if (localMinutes.Success)
        {
            return TrySet(localMinutes.Value.WithOffset(Offset.Zero).ToInstant(), out utc);
        }

        var date = Date.Parse(text);
        if (date.Success)
        {
            return TrySet(date.Value.AtMidnight().WithOffset(Offset.Zero).ToInstant(), out utc);
        }

        return false;
    }

    private static bool TrySet(Instant instant, out DateTime utc)
    {
        try
        {
            utc = instant.ToDateTimeUtc();
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            utc = default;
            return false;
        }
    }
}
