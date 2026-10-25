using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DataAccessLayer.Database;

public static class UniqueConstraintViolation
{
    public static bool IsRaisedBy(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation;
    }
}
