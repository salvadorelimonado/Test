using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace WebApi.Features.Analytics.GetLoad;

public sealed class UtcDateModelBinder : IModelBinder
{
    private const string DateFormat = "yyyy-MM-dd";

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (value == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, value);
        if (!TryParseDate(value.FirstValue, out var date))
        {
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "The value is not a date.");
            return Task.CompletedTask;
        }

        bindingContext.Result = ModelBindingResult.Success(date);
        return Task.CompletedTask;
    }

    private static bool TryParseDate(string? text, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text) || text.Contains(':') || text.Contains('T') || text.Contains('t'))
        {
            return false;
        }

        return DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
