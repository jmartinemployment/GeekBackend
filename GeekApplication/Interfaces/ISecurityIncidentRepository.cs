namespace GeekApplication.Interfaces;

public interface ISecurityIncidentRepository
{
    Task<Result<SecurityIncident>> CreateAsync(Guid userId, string incidentType, string description, string? metadata);
    Task<Result<SecurityIncident>> FindByIdAsync(Guid incidentId);
    Task<Result<List<SecurityIncident>>> GetByUserAsync(Guid userId);
    Task<Result<List<SecurityIncident>>> GetByTypeAsync(string incidentType);
    Task<Result<List<SecurityIncident>>> GetUnresolvedAsync();
    Task<Result<bool>> MarkResolvedAsync(Guid incidentId, string resolution);
    Task<Result<bool>> DeleteAsync(Guid incidentId);
}
