using BusinessLayer.Features.Schedule;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace WebApi.Features.Schedule;

public sealed class ScheduleContinuationBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (value == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, value);
        var text = value.FirstValue;
        if (string.IsNullOrWhiteSpace(text))
        {
            bindingContext.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask;
        }

        if (!ScheduleContinuation.TryRead(text, out var startsAt, out var classId))
        {
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "The continuation token is not valid.");
            return Task.CompletedTask;
        }

        bindingContext.Result = ModelBindingResult.Success(new ScheduleCursor(startsAt, classId));
        return Task.CompletedTask;
    }
}
