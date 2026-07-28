using GeekApplication.Models.ContentWriterV3;

namespace GeekAPI.HttpClients;

public class HttpContentWriterV3Repository
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpContentWriterV3Repository> _logger;

    public HttpContentWriterV3Repository(HttpClient httpClient, ILogger<HttpContentWriterV3Repository> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    // Campaigns
    public async Task<ContentCampaignDto?> GetCampaignByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/campaigns/{id}", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ContentCampaignDto>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching campaign {CampaignId}", id);
            throw;
        }
    }

    public async Task<IReadOnlyList<ContentCampaignDto>> GetCampaignsByClientIdAsync(Guid clientId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/campaigns?clientId={clientId}", ct);
            if (!response.IsSuccessStatusCode)
                return new List<ContentCampaignDto>().AsReadOnly();

            var content = await response.Content.ReadAsStringAsync(ct);
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<ContentCampaignDto>>(content);
            return (dtos ?? new()).AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching campaigns for client {ClientId}", clientId);
            throw;
        }
    }

    public async Task<ContentCampaignDto> CreateCampaignAsync(CreateContentCampaignCommand command, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("repo/content-writer-v3/campaigns", content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ContentCampaignDto>(responseContent)
                ?? throw new InvalidOperationException("Failed to deserialize campaign response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating campaign");
            throw;
        }
    }

    public async Task<ContentCampaignDto> UpdateCampaignStatusAsync(Guid id, string status, CancellationToken ct = default)
    {
        try
        {
            var command = new { status };
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PatchAsync($"repo/content-writer-v3/campaigns/{id}/status", content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ContentCampaignDto>(responseContent)
                ?? throw new InvalidOperationException("Failed to deserialize campaign response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating campaign {CampaignId} status to {Status}", id, status);
            throw;
        }
    }

    // Jobs
    public async Task<JobDto?> GetJobByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/jobs/{id}", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<JobDto>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching job {JobId}", id);
            throw;
        }
    }

    public async Task<JobDto> CreateJobAsync(CreateJobCommand command, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("repo/content-writer-v3/jobs", content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<JobDto>(responseContent)
                ?? throw new InvalidOperationException("Failed to deserialize job response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating job");
            throw;
        }
    }

    public async Task<JobDto> UpdateJobStatusAsync(UpdateJobStatusCommand command, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PatchAsync($"repo/content-writer-v3/jobs/{command.Id}/status", content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<JobDto>(responseContent)
                ?? throw new InvalidOperationException("Failed to deserialize job response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating job {JobId} status", command.Id);
            throw;
        }
    }

    // Pain Points
    public async Task<PainPointDto?> GetPainPointByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/pain-points/{id}", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<PainPointDto>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching pain point {PainPointId}", id);
            throw;
        }
    }

    public async Task<IReadOnlyList<PainPointDto>> GetPainPointsByClientIdAsync(Guid clientId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/pain-points?clientId={clientId}", ct);
            if (!response.IsSuccessStatusCode)
                return new List<PainPointDto>().AsReadOnly();

            var content = await response.Content.ReadAsStringAsync(ct);
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<PainPointDto>>(content);
            return (dtos ?? new()).AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching pain points for client {ClientId}", clientId);
            throw;
        }
    }

    public async Task<PainPointDto> CreatePainPointAsync(CreatePainPointCommand command, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("repo/content-writer-v3/pain-points", content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<PainPointDto>(responseContent)
                ?? throw new InvalidOperationException("Failed to deserialize pain point response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating pain point");
            throw;
        }
    }

    // Strategy Briefs
    public async Task<StrategyBriefDto?> GetStrategyBriefByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/strategy-briefs/{id}", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<StrategyBriefDto>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching strategy brief {StrategyBriefId}", id);
            throw;
        }
    }

    public async Task<IReadOnlyList<StrategyBriefDto>> GetStrategyBriefsByCampaignIdAsync(Guid campaignId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/strategy-briefs?campaignId={campaignId}", ct);
            if (!response.IsSuccessStatusCode)
                return new List<StrategyBriefDto>().AsReadOnly();

            var content = await response.Content.ReadAsStringAsync(ct);
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<StrategyBriefDto>>(content);
            return (dtos ?? new()).AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching strategy briefs for campaign {CampaignId}", campaignId);
            throw;
        }
    }

    public async Task<StrategyBriefDto> CreateStrategyBriefAsync(CreateStrategyBriefCommand command, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("repo/content-writer-v3/strategy-briefs", content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<StrategyBriefDto>(responseContent)
                ?? throw new InvalidOperationException("Failed to deserialize strategy brief response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating strategy brief");
            throw;
        }
    }

    public async Task<StrategyBriefDto> ApproveStrategyBriefAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PatchAsync($"repo/content-writer-v3/strategy-briefs/{id}/approve", new StringContent(""), ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<StrategyBriefDto>(responseContent)
                ?? throw new InvalidOperationException("Failed to deserialize strategy brief response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving strategy brief {StrategyBriefId}", id);
            throw;
        }
    }

    public async Task<StrategyBriefDto> RejectStrategyBriefAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PatchAsync($"repo/content-writer-v3/strategy-briefs/{id}/reject", new StringContent(""), ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<StrategyBriefDto>(responseContent)
                ?? throw new InvalidOperationException("Failed to deserialize strategy brief response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting strategy brief {StrategyBriefId}", id);
            throw;
        }
    }

    // Assets
    public async Task<ContentAssetDto?> GetAssetByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/assets/{id}", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ContentAssetDto>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching asset {AssetId}", id);
            throw;
        }
    }

    public async Task<IReadOnlyList<ContentAssetDto>> GetAssetsByCampaignIdAsync(Guid campaignId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/assets?campaignId={campaignId}", ct);
            if (!response.IsSuccessStatusCode)
                return new List<ContentAssetDto>().AsReadOnly();

            var content = await response.Content.ReadAsStringAsync(ct);
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<ContentAssetDto>>(content);
            return (dtos ?? new()).AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching assets for campaign {CampaignId}", campaignId);
            throw;
        }
    }

    public async Task<ContentAssetDto> CreateAssetAsync(CreateContentAssetCommand command, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("repo/content-writer-v3/assets", content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ContentAssetDto>(responseContent)
                ?? throw new InvalidOperationException("Failed to deserialize asset response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating asset");
            throw;
        }
    }

    public async Task<bool> DeleteAssetAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"repo/content-writer-v3/assets/{id}", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting asset {AssetId}", id);
            throw;
        }
    }

    // Keywords
    public async Task<KeywordCandidateDto?> GetKeywordByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/keywords/{id}", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<KeywordCandidateDto>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching keyword {KeywordId}", id);
            throw;
        }
    }

    public async Task<IReadOnlyList<KeywordCandidateDto>> GetKeywordsByClientIdAsync(Guid clientId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/keywords?clientId={clientId}", ct);
            if (!response.IsSuccessStatusCode)
                return new List<KeywordCandidateDto>().AsReadOnly();

            var content = await response.Content.ReadAsStringAsync(ct);
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<KeywordCandidateDto>>(content);
            return (dtos ?? new()).AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching keywords for client {ClientId}", clientId);
            throw;
        }
    }

    public async Task<KeywordCandidateDto> CreateKeywordAsync(CreateKeywordCandidateCommand command, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("repo/content-writer-v3/keywords", content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<KeywordCandidateDto>(responseContent)
                ?? throw new InvalidOperationException("Failed to deserialize keyword response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating keyword");
            throw;
        }
    }

    public async Task<KeywordCandidateDto> UpdateKeywordStatusAsync(Guid id, string status, CancellationToken ct = default)
    {
        try
        {
            var command = new { status };
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PatchAsync($"repo/content-writer-v3/keywords/{id}/status", content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<KeywordCandidateDto>(responseContent)
                ?? throw new InvalidOperationException("Failed to deserialize keyword response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating keyword {KeywordId} status", id);
            throw;
        }
    }

    // Reconciliation
    public async Task<ReconciliationProposalDto?> GetReconciliationProposalByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/reconciliation/{id}", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ReconciliationProposalDto>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching reconciliation proposal {ProposalId}", id);
            throw;
        }
    }

    public async Task<IReadOnlyList<ReconciliationProposalDto>> GetReconciliationProposalsByResearchRunIdAsync(Guid researchRunId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/reconciliation?researchRunId={researchRunId}", ct);
            if (!response.IsSuccessStatusCode)
                return new List<ReconciliationProposalDto>().AsReadOnly();

            var content = await response.Content.ReadAsStringAsync(ct);
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<ReconciliationProposalDto>>(content);
            return (dtos ?? new()).AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching reconciliation proposals for research run {ResearchRunId}", researchRunId);
            throw;
        }
    }

    public async Task<ReconciliationProposalDto> CreateReconciliationProposalAsync(CreateReconciliationProposalCommand command, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("repo/content-writer-v3/reconciliation", content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ReconciliationProposalDto>(responseContent)
                ?? throw new InvalidOperationException("Failed to deserialize reconciliation proposal response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating reconciliation proposal");
            throw;
        }
    }

    public async Task<ReconciliationProposalDto> ApproveReconciliationProposalAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        try
        {
            var command = new { userId };
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PatchAsync($"repo/content-writer-v3/reconciliation/{id}/approve", content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ReconciliationProposalDto>(responseContent)
                ?? throw new InvalidOperationException("Failed to deserialize reconciliation proposal response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving reconciliation proposal {ProposalId}", id);
            throw;
        }
    }

    public async Task<ReconciliationProposalDto> DismissReconciliationProposalAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        try
        {
            var command = new { userId };
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PatchAsync($"repo/content-writer-v3/reconciliation/{id}/dismiss", content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ReconciliationProposalDto>(responseContent)
                ?? throw new InvalidOperationException("Failed to deserialize reconciliation proposal response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dismissing reconciliation proposal {ProposalId}", id);
            throw;
        }
    }

    // Publications
    public async Task<PublicationDto?> GetPublicationByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/publications/{id}", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<PublicationDto>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching publication {PublicationId}", id);
            throw;
        }
    }

    public async Task<PublicationDto> CreatePublicationAsync(CreatePublicationCommand command, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("repo/content-writer-v3/publications", content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<PublicationDto>(responseContent)
                ?? throw new InvalidOperationException("Failed to deserialize publication response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating publication");
            throw;
        }
    }

    public async Task<PublicationDto> UpdatePublicationStatusAsync(Guid id, string status, CancellationToken ct = default)
    {
        try
        {
            var command = new { status };
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PatchAsync($"repo/content-writer-v3/publications/{id}/status", content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<PublicationDto>(responseContent)
                ?? throw new InvalidOperationException("Failed to deserialize publication response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating publication {PublicationId} status", id);
            throw;
        }
    }
}
