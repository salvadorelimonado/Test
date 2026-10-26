using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessLayer.Extensions;
using BusinessLayer.Result;
using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BusinessLayer.Idempotency;

public sealed class IdempotencyService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public IdempotencyService(IUnitOfWork unitOfWork, TimeProvider clock)
    {
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public static string CanonicalRequest(long classId, int seats)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{classId}:{seats}");
    }

    public async Task LockAsync(
        long userId,
        IdempotencyOperation operation,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (_unitOfWork.DbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("An idempotency lock requires a transaction.");
        }

        var keyHash = Hash(idempotencyKey);
        var operationCode = (int)operation;
        await _unitOfWork.DbContext.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({userId}::text || ':' || {operationCode}::text || ':' || encode({keyHash}, 'hex'), 0))",
            cancellationToken);
    }

    public async Task<OperationResult<TResponse>?> FindAsync<TResponse>(
        long userId,
        IdempotencyOperation operation,
        string idempotencyKey,
        string canonicalRequest,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        var keyHash = Hash(idempotencyKey);
        var requestHash = Hash(canonicalRequest);
        var existing = await _unitOfWork.Idempotency.FindAsync(userId, operation, keyHash, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (!existing.RequestHash.AsSpan().SequenceEqual(requestHash))
        {
            return new OperationResult<TResponse>.Conflict(ConflictReason.IdempotencyMismatch);
        }

        var response = JsonSerializer.Deserialize<TResponse>(existing.ResponseBody, JsonSerializerOptions.Web);
        if (response is null)
        {
            throw new InvalidOperationException("Stored idempotency response is empty.");
        }

        return new OperationResult<TResponse>.Ok(response);
    }

    public async Task SaveAsync<TResponse>(
        long userId,
        IdempotencyOperation operation,
        string idempotencyKey,
        string canonicalRequest,
        TResponse response,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        var record = new IdempotencyRecord
        {
            UserId = userId,
            Operation = operation,
            KeyHash = Hash(idempotencyKey),
            RequestHash = Hash(canonicalRequest),
            ResponseBody = JsonSerializer.Serialize(response, JsonSerializerOptions.Web),
            CreatedAt = _clock.TruncateToMicroseconds()
        };

        await _unitOfWork.Idempotency.CreateAsync(record, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static byte[] Hash(string value)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(value));
    }
}
