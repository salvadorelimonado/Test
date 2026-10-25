using FluentValidation;
using Microsoft.AspNetCore.Http;

namespace WebApi.Idempotency;

public sealed class IdempotencyKeyReader
{
    private readonly IValidator<IdempotencyKey> _validator;

    public IdempotencyKeyReader(IValidator<IdempotencyKey> validator)
    {
        _validator = validator;
    }

    public string? Read(HttpRequest request)
    {
        var value = request.Headers.TryGetValue(IdempotencyHeader.Name, out var headerValues)
            ? headerValues.ToString()
            : string.Empty;
        var validation = _validator.Validate(new IdempotencyKey(value));
        if (!validation.IsValid)
        {
            return null;
        }

        return value;
    }
}
