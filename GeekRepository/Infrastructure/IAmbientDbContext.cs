using System.Data;

namespace GeekRepository.Infrastructure;

/// <summary>
/// Ambient connection and transaction shared across repositories within a Unit of Work scope.
/// </summary>
public interface IAmbientDbContext
{
    IDbConnection Connection { get; }
    IDbTransaction? Transaction { get; }
    bool HasActiveTransaction { get; }

    void Attach(IDbConnection connection, IDbTransaction? transaction = null);
    void Detach();
}
