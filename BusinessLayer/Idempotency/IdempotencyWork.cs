namespace BusinessLayer.Idempotency;

public sealed class IdempotencyWork<TResponse, TResult>
    where TResponse : class
    where TResult : class
{
    private IdempotencyWork(TResponse? response, TResult? failure)
    {
        Response = response;
        Failure = failure;
    }

    public TResponse? Response { get; }

    public TResult? Failure { get; }

    public static IdempotencyWork<TResponse, TResult> Done(TResponse response)
    {
        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        return new IdempotencyWork<TResponse, TResult>(response, null);
    }

    public static IdempotencyWork<TResponse, TResult> Reject(TResult failure)
    {
        if (failure is null)
        {
            throw new ArgumentNullException(nameof(failure));
        }

        return new IdempotencyWork<TResponse, TResult>(null, failure);
    }
}
