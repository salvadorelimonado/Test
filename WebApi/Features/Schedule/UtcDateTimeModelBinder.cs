using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace WebApi.Features.Schedule;

public sealed class UtcDateTimeModelBinder : IModelBinder
{
    private static readonly string[] Formats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
        "yyyy-MM-dd'T'HH:mmK",
        "yyyy-MM-dd"
    ];

    private const DateTimeStyles Styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

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

        // решение: дата без времени — полночь UTC, граница расписания совпадает с началом суток.
        if (!DateTimeOffset.TryParseExact(text, Formats, CultureInfo.InvariantCulture, Styles, out var parsed))
        {
            return false;
        }

        utc = parsed.UtcDateTime;
        return true;
    }
}
