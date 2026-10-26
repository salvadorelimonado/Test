using Common.Database.Interfaces;

namespace DataAccessLayer.Entities;

public class IdempotencyRecord : IEntity
{
    public const int HashLength = 32;

    public const string KeyIndexName = "ix_idempotency_records_user_id_operation_key_hash";

    public long Id { get; set; }

    public long UserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public IdempotencyOperation Operation { get; set; }

    public byte[] KeyHash { get; set; } = [];

    public byte[] RequestHash { get; set; } = [];

    public string ResponseBody { get; set; } = string.Empty;
}
