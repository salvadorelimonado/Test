namespace BusinessLayer.Result;

public abstract record OperationResult<T>
    where T : class
{
    private OperationResult()
    {
    }

    public sealed record Ok : OperationResult<T>
    {
        public Ok(T value)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public T Value { get; }
    }

    public sealed record NotFound : OperationResult<T>;

    public sealed record Conflict : OperationResult<T>
    {
        public Conflict(string reason)
        {
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }

        public string Reason { get; }
    }

    public OperationResult<TResult> Select<TResult>(Func<T, TResult> map)
        where TResult : class
    {
        var result = this;
        switch (result)
        {
            case Ok ok:
                return new OperationResult<TResult>.Ok(map(ok.Value));
            case NotFound:
                return new OperationResult<TResult>.NotFound();
            case Conflict conflict:
                return new OperationResult<TResult>.Conflict(conflict.Reason);
            default:
                throw new ArgumentOutOfRangeException(nameof(result), result, null);
        }
    }
}
