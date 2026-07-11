namespace GeekRepository.Infrastructure;

public interface IUnitOfWork
{
    IAmbientDbContext Context { get; }

    Task ExecuteInResilientTransactionAsync(
        Func<Task> businessLogic,
        CancellationToken cancellationToken = default);
}
