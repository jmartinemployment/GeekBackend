using System.Data;
using Npgsql;

namespace GeekRepository.Infrastructure;
public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    public NpgsqlConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))

            throw new ArgumentException("Connection string cannot be empty.", nameof(connectionString));

        _connectionString = connectionString;

    }

    public IDbConnection CreateConnection()
        => new NpgsqlConnection(_connectionString);
}
