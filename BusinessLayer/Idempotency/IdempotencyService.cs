using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessLayer.Result;
using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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

    public async Task<OperationResult<TResponse>?> FindAsync<TResponse>(
        long userId,
        IdempotencyOperation operation,
        string idempotencyKey,
        string canonicalRequest,
        string mismatchReason,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        var keyHash = Hash(idempotencyKey);
        var requestHash = Hash(canonicalRequest);
        await LockSameKeyAsync(userId, operation, keyHash, cancellationToken);

        var existing = await _unitOfWork.Idempotency.FindAsync(userId, operation, keyHash, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (!existing.RequestHash.AsSpan().SequenceEqual(requestHash))
        {
            return new OperationResult<TResponse>.Conflict(mismatchReason);
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

        var now = _clock.GetUtcNow().UtcDateTime;
        var record = new IdempotencyRecord
        {
            UserId = userId,
            Operation = operation,
            KeyHash = Hash(idempotencyKey),
            RequestHash = Hash(canonicalRequest),
            ResponseBody = JsonSerializer.Serialize(response, JsonSerializerOptions.Web),
            CreatedAt = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc)
        };

        await _unitOfWork.Idempotency.CreateAsync(record, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task LockSameKeyAsync(
        long userId,
        IdempotencyOperation operation,
        byte[] keyHash,
        CancellationToken cancellationToken)
    {
        var transaction = _unitOfWork.DbContext.Database.CurrentTransaction;
        if (transaction is null)
        {
            return;
        }

        var connection = _unitOfWork.DbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            "SELECT pg_advisory_xact_lock(hashtextextended(@userId::text || ':' || @operation::text || ':' || encode(@key, 'hex'), 0))";
        AddParameter(command, "userId", DbType.Int64, userId);
        AddParameter(command, "operation", DbType.Int32, (int)operation);
        AddParameter(command, "key", DbType.Binary, keyHash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    }

    private static void AddParameter(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static byte[] Hash(string value)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(value));
    }
}
