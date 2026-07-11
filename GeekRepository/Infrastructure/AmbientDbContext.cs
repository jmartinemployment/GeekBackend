using System.Data;

namespace GeekRepository.Infrastructure;

public sealed class AmbientDbContext : IAmbientDbContext
{
    private IDbConnection? _connection;
    private IDbTransaction? _transaction;

    public IDbConnection Connection =>
        _connection ?? throw new InvalidOperationException(
            "No ambient connection is active. Execute work inside IUnitOfWork.ExecuteInResilientTransactionAsync.");

    public IDbTransaction? Transaction => _transaction;

    public bool HasActiveTransaction => _transaction is not null;

    public void Attach(IDbConnection connection, IDbTransaction? transaction = null)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public void Detach()
    {
        _transaction = null;
        _connection = null;
    }
}
