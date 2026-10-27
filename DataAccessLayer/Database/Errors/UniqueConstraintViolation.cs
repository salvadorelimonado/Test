using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DataAccessLayer.Database.Errors;

public static class UniqueConstraintViolation
{
    public static bool IsRaisedBy(DbUpdateException exception, string constraintName)
    {
        return exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
            && postgresException.ConstraintName == constraintName;
    }
}
