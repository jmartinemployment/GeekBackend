using System.Data;
using Npgsql;
using Polly;
using Polly.Retry;

namespace GeekRepository.Infrastructure;

public sealed class SqlUnitOfWork : IUnitOfWork
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ResiliencePipeline _retryPipeline;

    public SqlUnitOfWork(IDbConnectionFactory connectionFactory, AmbientDbContext context)
    {
        _connectionFactory = connectionFactory;
        Context = context;

        _retryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 5,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(50),
                ShouldHandle = new PredicateBuilder().Handle<PostgresException>(IsTransientPostgresFailure)
            })
            .Build();
    }

    public IAmbientDbContext Context { get; }

    public async Task ExecuteInResilientTransactionAsync(
        Func<Task> businessLogic,
        CancellationToken cancellationToken = default)
    {
        await _retryPipeline.ExecuteAsync(async token =>
        {
            var connection = _connectionFactory.CreateConnection();
            var npgsql = (NpgsqlConnection)connection;

            await npgsql.OpenAsync(token);
            Context.Attach(npgsql);

            await using var transaction = await npgsql.BeginTransactionAsync(token);
            Context.Attach(npgsql, transaction);

            try
            {
                await businessLogic();
                await transaction.CommitAsync(token);
            }
            catch
            {
                await transaction.RollbackAsync(token);
                throw;
            }
            finally
            {
                Context.Detach();
                await npgsql.DisposeAsync();
            }
        }, cancellationToken);
    }

    private static bool IsTransientPostgresFailure(PostgresException ex) =>
        ex.SqlState is "40001" or "40P01";
}
