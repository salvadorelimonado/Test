using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DataAccessLayer.Database;
using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BusinessLayer.Idempotency;

public sealed class IdempotencyExecution
{
    private readonly IUnitOfWork _unitOfWork;

    public IdempotencyExecution(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<TResult> RunAsync<TResponse, TResult>(
        long userId,
        IdempotencyOperation operation,
        string idempotencyKey,
        string canonicalRequest,
        Func<TResponse, TResult> success,
        Func<TResult> mismatch,
        Func<CancellationToken, Task<IdempotencyWork<TResponse, TResult>>> work,
        CancellationToken cancellationToken)
        where TResponse : class
        where TResult : class
    {
        var keyHash = Hash(idempotencyKey);
        var requestHash = Hash(canonicalRequest);
        var strategy = _unitOfWork.DbContext.Database.CreateExecutionStrategy();

        try
        {
            return await strategy.ExecuteAsync(() =>
                RunInTransactionAsync(
                    userId,
                    operation,
                    keyHash,
                    requestHash,
                    success,
                    mismatch,
                    work,
                    cancellationToken));
        }
        catch (DbUpdateException exception) when (UniqueConstraintViolation.IsRaisedBy(exception))
        {
            _unitOfWork.ClearChangeTracker();
            return await ReplayAsync(userId, operation, keyHash, requestHash, success, mismatch, cancellationToken);
        }
    }

    private async Task<TResult> RunInTransactionAsync<TResponse, TResult>(
        long userId,
        IdempotencyOperation operation,
        byte[] keyHash,
        byte[] requestHash,
        Func<TResponse, TResult> success,
        Func<TResult> mismatch,
        Func<CancellationToken, Task<IdempotencyWork<TResponse, TResult>>> work,
        CancellationToken cancellationToken)
        where TResponse : class
        where TResult : class
    {
        await using var transaction = await _unitOfWork.DbContext.Database.BeginTransactionAsync(cancellationToken);
        var existing = await _unitOfWork.Idempotency.FindAsync(userId, operation, keyHash, cancellationToken);
        if (existing is not null)
        {
            return Resolve(existing, requestHash, success, mismatch);
        }

        var step = await work(cancellationToken);
        if (step.Response is null)
        {
            if (step.Failure is null)
            {
                throw new ArgumentNullException(nameof(step.Failure));
            }

            return step.Failure;
        }

        var record = new IdempotencyRecord
        {
            UserId = userId,
            Operation = operation,
            KeyHash = keyHash,
            RequestHash = requestHash,
            ResponseBody = JsonSerializer.Serialize(step.Response, JsonSerializerOptions.Web)
        };

        await _unitOfWork.Idempotency.CreateAsync(record, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return success(step.Response);
    }

    private async Task<TResult> ReplayAsync<TResponse, TResult>(
        long userId,
        IdempotencyOperation operation,
        byte[] keyHash,
        byte[] requestHash,
        Func<TResponse, TResult> success,
        Func<TResult> mismatch,
        CancellationToken cancellationToken)
        where TResponse : class
        where TResult : class
    {
        var existing = await _unitOfWork.Idempotency.FindAsync(userId, operation, keyHash, cancellationToken);
        if (existing is null)
        {
            throw new InvalidOperationException("Idempotency record was not found after a unique constraint violation.");
        }

        return Resolve(existing, requestHash, success, mismatch);
    }

    private static TResult Resolve<TResponse, TResult>(
        IdempotencyRecord existing,
        byte[] requestHash,
        Func<TResponse, TResult> success,
        Func<TResult> mismatch)
        where TResponse : class
        where TResult : class
    {
        if (!existing.RequestHash.AsSpan().SequenceEqual(requestHash))
        {
            return mismatch();
        }

        var response = JsonSerializer.Deserialize<TResponse>(existing.ResponseBody, JsonSerializerOptions.Web);
        if (response is null)
        {
            throw new InvalidOperationException("Stored idempotency response is empty.");
        }

        return success(response);
    }

    private static byte[] Hash(string value)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(value));
    }
}
