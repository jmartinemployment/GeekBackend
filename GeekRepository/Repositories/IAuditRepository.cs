using GeekBackend.Data.Models;
using GeekBackend.Data.Results;

namespace GeekRepository.Repositories;

public interface IAuditRepository
{
    Task<Result<Unit>> CreateLogAsync(CreateAuditLogRequest request);
    Task<Result<Unit>> CreateCircuitResetAsync(CreateCircuitResetRequest request);
}

public class CreateAuditLogRequest
{
    public string Action { get; set; } = null!;
    public string? AdminId { get; set; }
    public string? TargetId { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

public class CreateCircuitResetRequest
{
    public string ServiceName { get; set; } = null!;
    public string ActionBySlackId { get; set; } = null!;
    public string? ActionByUsername { get; set; }
    public string PreviousState { get; set; } = null!;
    public string CurrentState { get; set; } = null!;
    public Dictionary<string, object>? Metadata { get; set; }
}
