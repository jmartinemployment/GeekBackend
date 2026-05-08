using System.Data;

namespace GeekRepository.Infrastructure;
/// <summary>
/// Creates database connections from the configured connection string.
/// GeekRepository is the only tier that uses this — GeekAPI and GeekApplication
/// never touch the database directly.
/// </summary>

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}
