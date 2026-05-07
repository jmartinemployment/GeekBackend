using GeekBackend.Data.Data;
using GeekBackend.Data.Models;
using GeekBackend.Data.Results;

namespace GeekRepository.Repositories;

public class AuditRepository : IAuditRepository
{
    private readonly AppDbContext _context;

    public AuditRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Result<Unit>> CreateLogAsync(CreateAuditLogRequest request)
    {
        try
        {
            var log = new AuditLog
            {
                Action = request.Action,
                AdminId = request.AdminId,
                TargetId = request.TargetId,
                Metadata = request.Metadata != null
                    ? System.Text.Json.JsonSerializer.Serialize(request.Metadata)
                    : null,
                CreatedAt = DateTime.UtcNow
            };

            _context.AuditLogs.Add(log);
            await _context.SaveChangesAsync();

            return Result<Unit>.Success(new Unit());
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(ex.Message);
        }
    }

    public async Task<Result<Unit>> CreateCircuitResetAsync(CreateCircuitResetRequest request)
    {
        try
        {
            var reset = new CircuitReset
            {
                ServiceName = request.ServiceName,
                ActionBySlackId = request.ActionBySlackId,
                ActionByUsername = request.ActionByUsername,
                PreviousState = request.PreviousState,
                CurrentState = request.CurrentState,
                Metadata = request.Metadata != null
                    ? System.Text.Json.JsonSerializer.Serialize(request.Metadata)
                    : null,
                ExecutedAt = DateTime.UtcNow
            };

            _context.CircuitResets.Add(reset);
            await _context.SaveChangesAsync();

            return Result<Unit>.Success(new Unit());
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(ex.Message);
        }
    }
}
