using GeekApplication.Models.ContentWriterV3;

namespace GeekAPI.HttpClients;

public class HttpContentWriterV3Repository
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

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
            return System.Text.Json.JsonSerializer.Deserialize<ContentCampaignDto>(content, JsonOpts);
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
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<ContentCampaignDto>>(content, JsonOpts);
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
            return System.Text.Json.JsonSerializer.Deserialize<ContentCampaignDto>(responseContent, JsonOpts)
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
            return System.Text.Json.JsonSerializer.Deserialize<ContentCampaignDto>(responseContent, JsonOpts)
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
            return System.Text.Json.JsonSerializer.Deserialize<JobDto>(content, JsonOpts);
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
            return System.Text.Json.JsonSerializer.Deserialize<JobDto>(responseContent, JsonOpts)
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
            return System.Text.Json.JsonSerializer.Deserialize<JobDto>(responseContent, JsonOpts)
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
            return System.Text.Json.JsonSerializer.Deserialize<PainPointDto>(content, JsonOpts);
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
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<PainPointDto>>(content, JsonOpts);
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
            return System.Text.Json.JsonSerializer.Deserialize<PainPointDto>(responseContent, JsonOpts)
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
            return System.Text.Json.JsonSerializer.Deserialize<StrategyBriefDto>(content, JsonOpts);
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
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<StrategyBriefDto>>(content, JsonOpts);
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
            return System.Text.Json.JsonSerializer.Deserialize<StrategyBriefDto>(responseContent, JsonOpts)
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
            return System.Text.Json.JsonSerializer.Deserialize<StrategyBriefDto>(responseContent, JsonOpts)
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
            return System.Text.Json.JsonSerializer.Deserialize<StrategyBriefDto>(responseContent, JsonOpts)
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
            return System.Text.Json.JsonSerializer.Deserialize<ContentAssetDto>(content, JsonOpts);
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
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<ContentAssetDto>>(content, JsonOpts);
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
            return System.Text.Json.JsonSerializer.Deserialize<ContentAssetDto>(responseContent, JsonOpts)
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
            return System.Text.Json.JsonSerializer.Deserialize<KeywordCandidateDto>(content, JsonOpts);
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
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<KeywordCandidateDto>>(content, JsonOpts);
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
            return System.Text.Json.JsonSerializer.Deserialize<KeywordCandidateDto>(responseContent, JsonOpts)
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
            return System.Text.Json.JsonSerializer.Deserialize<KeywordCandidateDto>(responseContent, JsonOpts)
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
            return System.Text.Json.JsonSerializer.Deserialize<ReconciliationProposalDto>(content, JsonOpts);
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
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<ReconciliationProposalDto>>(content, JsonOpts);
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
            return System.Text.Json.JsonSerializer.Deserialize<ReconciliationProposalDto>(responseContent, JsonOpts)
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
            return System.Text.Json.JsonSerializer.Deserialize<ReconciliationProposalDto>(responseContent, JsonOpts)
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
            return System.Text.Json.JsonSerializer.Deserialize<ReconciliationProposalDto>(responseContent, JsonOpts)
                ?? throw new InvalidOperationException("Failed to deserialize reconciliation proposal response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dismissing reconciliation proposal {ProposalId}", id);
            throw;
        }
    }

    // Research Runs
    public async Task<ResearchRunDto?> GetResearchRunByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/research-runs/{id}", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ResearchRunDto>(content, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching research run {ResearchRunId}", id);
            throw;
        }
    }

    public async Task<IReadOnlyList<ResearchRunDto>> GetResearchRunsByCampaignIdAsync(Guid campaignId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/research-runs?campaignId={campaignId}", ct);
            if (!response.IsSuccessStatusCode)
                return new List<ResearchRunDto>().AsReadOnly();

            var content = await response.Content.ReadAsStringAsync(ct);
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<ResearchRunDto>>(content, JsonOpts);
            return (dtos ?? new()).AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching research runs for campaign {CampaignId}", campaignId);
            throw;
        }
    }

    public async Task<ResearchRunDto> CreateResearchRunAsync(CreateResearchRunCommand command, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("repo/content-writer-v3/research-runs", content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ResearchRunDto>(responseContent, JsonOpts)
                ?? throw new InvalidOperationException("Failed to deserialize research run response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating research run");
            throw;
        }
    }

    public async Task<ResearchRunDto> UpdateResearchRunStatusAsync(Guid id, string status, int discoveredSourceCount, decimal spentBudget, string? errorMessage, CancellationToken ct = default)
    {
        try
        {
            var command = new { status, discoveredSourceCount, spentBudget, errorMessage };
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PatchAsync($"repo/content-writer-v3/research-runs/{id}/status", content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ResearchRunDto>(responseContent, JsonOpts)
                ?? throw new InvalidOperationException("Failed to deserialize research run response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating research run {ResearchRunId} status", id);
            throw;
        }
    }

    // Research Sources
    public async Task<ResearchSourceDto?> GetResearchSourceByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/research-sources/{id}", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ResearchSourceDto>(content, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching research source {SourceId}", id);
            throw;
        }
    }

    public async Task<IReadOnlyList<ResearchSourceDto>> GetResearchSourcesByRunIdAsync(Guid researchRunId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/research-sources?researchRunId={researchRunId}", ct);
            if (!response.IsSuccessStatusCode)
                return new List<ResearchSourceDto>().AsReadOnly();

            var content = await response.Content.ReadAsStringAsync(ct);
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<ResearchSourceDto>>(content, JsonOpts);
            return (dtos ?? new()).AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching research sources for run {ResearchRunId}", researchRunId);
            throw;
        }
    }

    public async Task<ResearchSourceDto> CreateResearchSourceAsync(CreateResearchSourceCommand command, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("repo/content-writer-v3/research-sources", content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ResearchSourceDto>(responseContent, JsonOpts)
                ?? throw new InvalidOperationException("Failed to deserialize research source response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating research source");
            throw;
        }
    }

    // Research Evidence
    public async Task<ResearchEvidenceDto?> GetResearchEvidenceByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/research-evidence/{id}", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ResearchEvidenceDto>(content, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching research evidence {EvidenceId}", id);
            throw;
        }
    }

    public async Task<IReadOnlyList<ResearchEvidenceDto>> GetResearchEvidenceBySourceIdAsync(Guid sourceId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/research-evidence?sourceId={sourceId}", ct);
            if (!response.IsSuccessStatusCode)
                return new List<ResearchEvidenceDto>().AsReadOnly();

            var content = await response.Content.ReadAsStringAsync(ct);
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<ResearchEvidenceDto>>(content, JsonOpts);
            return (dtos ?? new()).AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching research evidence for source {SourceId}", sourceId);
            throw;
        }
    }

    public async Task<ResearchEvidenceDto> CreateResearchEvidenceAsync(CreateResearchEvidenceCommand command, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("repo/content-writer-v3/research-evidence", content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ResearchEvidenceDto>(responseContent, JsonOpts)
                ?? throw new InvalidOperationException("Failed to deserialize research evidence response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating research evidence");
            throw;
        }
    }

    public async Task<ResearchEvidenceDto> ApproveResearchEvidenceAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PatchAsync($"repo/content-writer-v3/research-evidence/{id}/approve", new StringContent(""), ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ResearchEvidenceDto>(responseContent, JsonOpts)
                ?? throw new InvalidOperationException("Failed to deserialize research evidence response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving research evidence {EvidenceId}", id);
            throw;
        }
    }

    // Content Asset Versions (Drafts)
    public async Task<ContentAssetVersionDto?> GetAssetVersionByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/asset-versions/{id}", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ContentAssetVersionDto>(content, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching asset version {VersionId}", id);
            throw;
        }
    }

    public async Task<IReadOnlyList<ContentAssetVersionDto>> GetAssetVersionsByAssetIdAsync(Guid assetId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/asset-versions?assetId={assetId}", ct);
            if (!response.IsSuccessStatusCode)
                return new List<ContentAssetVersionDto>().AsReadOnly();

            var content = await response.Content.ReadAsStringAsync(ct);
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<ContentAssetVersionDto>>(content, JsonOpts);
            return (dtos ?? new()).AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching asset versions for asset {AssetId}", assetId);
            throw;
        }
    }

    public async Task<ContentAssetVersionDto> CreateAssetVersionAsync(CreateContentAssetVersionCommand command, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("repo/content-writer-v3/asset-versions", content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ContentAssetVersionDto>(responseContent, JsonOpts)
                ?? throw new InvalidOperationException("Failed to deserialize asset version response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating asset version");
            throw;
        }
    }

    public async Task<ContentAssetVersionDto> UpdateAssetVersionAsync(UpdateContentAssetVersionCommand command, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PatchAsync($"repo/content-writer-v3/asset-versions/{command.Id}", content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ContentAssetVersionDto>(responseContent, JsonOpts)
                ?? throw new InvalidOperationException("Failed to deserialize asset version response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating asset version {VersionId}", command.Id);
            throw;
        }
    }

    // Workspaces
    public async Task<WorkspaceDto?> GetWorkspaceByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/workspaces/{id}", ct);
            if (!response.IsSuccessStatusCode) return null;
            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<WorkspaceDto>(content, JsonOpts);
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching workspace {WorkspaceId}", id); throw; }
    }

    public async Task<WorkspaceDto> CreateWorkspaceAsync(CreateWorkspaceCommand command, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var response = await _httpClient.PostAsync("repo/content-writer-v3/workspaces", new StringContent(json, System.Text.Encoding.UTF8, "application/json"), ct);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<WorkspaceDto>(content, JsonOpts) ?? throw new InvalidOperationException("Failed to deserialize workspace response");
        }
        catch (Exception ex) { _logger.LogError(ex, "Error creating workspace"); throw; }
    }

    // Clients
    public async Task<ClientDto?> GetClientByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/clients/{id}", ct);
            if (!response.IsSuccessStatusCode) return null;
            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ClientDto>(content, JsonOpts);
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching client {ClientId}", id); throw; }
    }

    public async Task<IReadOnlyList<ClientDto>> GetClientsByWorkspaceIdAsync(Guid workspaceId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/clients?workspaceId={workspaceId}", ct);
            if (!response.IsSuccessStatusCode) return new List<ClientDto>().AsReadOnly();
            var content = await response.Content.ReadAsStringAsync(ct);
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<ClientDto>>(content, JsonOpts);
            return (dtos ?? new()).AsReadOnly();
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching clients for workspace {WorkspaceId}", workspaceId); throw; }
    }

    public async Task<ClientDto> CreateClientAsync(CreateClientCommand command, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var response = await _httpClient.PostAsync("repo/content-writer-v3/clients", new StringContent(json, System.Text.Encoding.UTF8, "application/json"), ct);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ClientDto>(content, JsonOpts) ?? throw new InvalidOperationException("Failed to deserialize client response");
        }
        catch (Exception ex) { _logger.LogError(ex, "Error creating client"); throw; }
    }

    // ReviewComments
    public async Task<ReviewCommentDto?> GetReviewCommentByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/review-comments/{id}", ct);
            if (!response.IsSuccessStatusCode) return null;
            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ReviewCommentDto>(content, JsonOpts);
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching review comment {CommentId}", id); throw; }
    }

    public async Task<IReadOnlyList<ReviewCommentDto>> GetReviewCommentsByAssetVersionIdAsync(Guid assetVersionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/review-comments?assetVersionId={assetVersionId}", ct);
            if (!response.IsSuccessStatusCode) return new List<ReviewCommentDto>().AsReadOnly();
            var content = await response.Content.ReadAsStringAsync(ct);
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<ReviewCommentDto>>(content, JsonOpts);
            return (dtos ?? new()).AsReadOnly();
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching review comments for asset version {AssetVersionId}", assetVersionId); throw; }
    }

    public async Task<ReviewCommentDto> CreateReviewCommentAsync(CreateReviewCommentCommand command, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var response = await _httpClient.PostAsync("repo/content-writer-v3/review-comments", new StringContent(json, System.Text.Encoding.UTF8, "application/json"), ct);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ReviewCommentDto>(content, JsonOpts) ?? throw new InvalidOperationException("Failed to deserialize review comment response");
        }
        catch (Exception ex) { _logger.LogError(ex, "Error creating review comment"); throw; }
    }

    // ApprovalEvents
    public async Task<ApprovalEventDto?> GetApprovalEventByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/approval-events/{id}", ct);
            if (!response.IsSuccessStatusCode) return null;
            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ApprovalEventDto>(content, JsonOpts);
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching approval event {EventId}", id); throw; }
    }

    public async Task<IReadOnlyList<ApprovalEventDto>> GetApprovalEventsByAssetVersionIdAsync(Guid assetVersionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/approval-events?assetVersionId={assetVersionId}", ct);
            if (!response.IsSuccessStatusCode) return new List<ApprovalEventDto>().AsReadOnly();
            var content = await response.Content.ReadAsStringAsync(ct);
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<ApprovalEventDto>>(content, JsonOpts);
            return (dtos ?? new()).AsReadOnly();
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching approval events for asset version {AssetVersionId}", assetVersionId); throw; }
    }

    public async Task<ApprovalEventDto> CreateApprovalEventAsync(CreateApprovalEventCommand command, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var response = await _httpClient.PostAsync("repo/content-writer-v3/approval-events", new StringContent(json, System.Text.Encoding.UTF8, "application/json"), ct);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ApprovalEventDto>(content, JsonOpts) ?? throw new InvalidOperationException("Failed to deserialize approval event response");
        }
        catch (Exception ex) { _logger.LogError(ex, "Error creating approval event"); throw; }
    }

    // PublicationEvents
    public async Task<PublicationEventDto?> GetPublicationEventByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/publication-events/{id}", ct);
            if (!response.IsSuccessStatusCode) return null;
            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<PublicationEventDto>(content, JsonOpts);
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching publication event {EventId}", id); throw; }
    }

    public async Task<IReadOnlyList<PublicationEventDto>> GetPublicationEventsByPublicationIdAsync(Guid publicationId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/publication-events?publicationId={publicationId}", ct);
            if (!response.IsSuccessStatusCode) return new List<PublicationEventDto>().AsReadOnly();
            var content = await response.Content.ReadAsStringAsync(ct);
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<PublicationEventDto>>(content, JsonOpts);
            return (dtos ?? new()).AsReadOnly();
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching publication events for publication {PublicationId}", publicationId); throw; }
    }

    public async Task<PublicationEventDto> CreatePublicationEventAsync(CreatePublicationEventCommand command, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var response = await _httpClient.PostAsync("repo/content-writer-v3/publication-events", new StringContent(json, System.Text.Encoding.UTF8, "application/json"), ct);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<PublicationEventDto>(content, JsonOpts) ?? throw new InvalidOperationException("Failed to deserialize publication event response");
        }
        catch (Exception ex) { _logger.LogError(ex, "Error creating publication event"); throw; }
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
            return System.Text.Json.JsonSerializer.Deserialize<PublicationDto>(content, JsonOpts);
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
            return System.Text.Json.JsonSerializer.Deserialize<PublicationDto>(responseContent, JsonOpts)
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
            return System.Text.Json.JsonSerializer.Deserialize<PublicationDto>(responseContent, JsonOpts)
                ?? throw new InvalidOperationException("Failed to deserialize publication response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating publication {PublicationId} status", id);
            throw;
        }
    }

    // Jobs (extended methods for HTTP layer)
    public async Task<IReadOnlyList<JobDto>> GetJobsByStatusAsync(string status, int limit = 100, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/jobs/by-status/{status}?limit={limit}", ct);
            if (!response.IsSuccessStatusCode) return new List<JobDto>().AsReadOnly();
            var content = await response.Content.ReadAsStringAsync(ct);
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<JobDto>>(content, JsonOpts);
            return (dtos ?? new()).AsReadOnly();
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching jobs by status {Status}", status); throw; }
    }

    public async Task<JobDto> LeaseJobAsync(Guid jobId, string leaseOwner, TimeSpan duration, CancellationToken ct = default)
    {
        try
        {
            var command = new { leaseOwner, duration };
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var response = await _httpClient.PostAsync($"repo/content-writer-v3/jobs/{jobId}/lease", new StringContent(json, System.Text.Encoding.UTF8, "application/json"), ct);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<JobDto>(content, JsonOpts) ?? throw new InvalidOperationException("Failed to deserialize job response");
        }
        catch (Exception ex) { _logger.LogError(ex, "Error leasing job {JobId}", jobId); throw; }
    }

    public async Task<JobDto> ReleaseJobLeaseAsync(Guid jobId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsync($"repo/content-writer-v3/jobs/{jobId}/release-lease", null, ct);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<JobDto>(content, JsonOpts) ?? throw new InvalidOperationException("Failed to deserialize job response");
        }
        catch (Exception ex) { _logger.LogError(ex, "Error releasing job lease {JobId}", jobId); throw; }
    }

    // PainPointEvidenceLinks
    public async Task<PainPointEvidenceLinkDto?> GetPainPointEvidenceLinkByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/pain-point-evidence-links/{id}", ct);
            if (!response.IsSuccessStatusCode) return null;
            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<PainPointEvidenceLinkDto>(content, JsonOpts);
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching pain point evidence link {LinkId}", id); throw; }
    }

    public async Task<IReadOnlyList<PainPointEvidenceLinkDto>> GetPainPointEvidenceLinksByPainPointIdAsync(Guid painPointId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/pain-point-evidence-links?painPointId={painPointId}", ct);
            if (!response.IsSuccessStatusCode) return new List<PainPointEvidenceLinkDto>().AsReadOnly();
            var content = await response.Content.ReadAsStringAsync(ct);
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<PainPointEvidenceLinkDto>>(content, JsonOpts);
            return (dtos ?? new()).AsReadOnly();
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching pain point evidence links for pain point {PainPointId}", painPointId); throw; }
    }

    public async Task<PainPointEvidenceLinkDto> CreatePainPointEvidenceLinkAsync(CreatePainPointEvidenceLinkCommand command, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var response = await _httpClient.PostAsync("repo/content-writer-v3/pain-point-evidence-links", new StringContent(json, System.Text.Encoding.UTF8, "application/json"), ct);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<PainPointEvidenceLinkDto>(content, JsonOpts) ?? throw new InvalidOperationException("Failed to deserialize pain point evidence link response");
        }
        catch (Exception ex) { _logger.LogError(ex, "Error creating pain point evidence link"); throw; }
    }

    // ClientProfiles
    public async Task<ClientProfileDto?> GetClientProfileByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/client-profiles/{id}", ct);
            if (!response.IsSuccessStatusCode) return null;
            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ClientProfileDto>(content, JsonOpts);
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching client profile {ProfileId}", id); throw; }
    }

    public async Task<ClientProfileDto?> GetClientProfileByClientIdAsync(Guid clientId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/client-profiles/by-client/{clientId}", ct);
            if (!response.IsSuccessStatusCode) return null;
            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ClientProfileDto>(content, JsonOpts);
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching client profile for client {ClientId}", clientId); throw; }
    }

    public async Task<ClientProfileDto> CreateClientProfileAsync(CreateClientProfileCommand command, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var response = await _httpClient.PostAsync("repo/content-writer-v3/client-profiles", new StringContent(json, System.Text.Encoding.UTF8, "application/json"), ct);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ClientProfileDto>(content, JsonOpts) ?? throw new InvalidOperationException("Failed to deserialize client profile response");
        }
        catch (Exception ex) { _logger.LogError(ex, "Error creating client profile"); throw; }
    }

    // ClientProfileVersions
    public async Task<ClientProfileVersionDto?> GetClientProfileVersionByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/client-profile-versions/{id}", ct);
            if (!response.IsSuccessStatusCode) return null;
            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ClientProfileVersionDto>(content, JsonOpts);
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching client profile version {VersionId}", id); throw; }
    }

    public async Task<IReadOnlyList<ClientProfileVersionDto>> GetClientProfileVersionsByProfileIdAsync(Guid profileId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/client-profile-versions?profileId={profileId}", ct);
            if (!response.IsSuccessStatusCode) return new List<ClientProfileVersionDto>().AsReadOnly();
            var content = await response.Content.ReadAsStringAsync(ct);
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<ClientProfileVersionDto>>(content, JsonOpts);
            return (dtos ?? new()).AsReadOnly();
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching client profile versions for profile {ProfileId}", profileId); throw; }
    }

    public async Task<ClientProfileVersionDto> CreateClientProfileVersionAsync(CreateClientProfileVersionCommand command, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var response = await _httpClient.PostAsync("repo/content-writer-v3/client-profile-versions", new StringContent(json, System.Text.Encoding.UTF8, "application/json"), ct);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ClientProfileVersionDto>(content, JsonOpts) ?? throw new InvalidOperationException("Failed to deserialize client profile version response");
        }
        catch (Exception ex) { _logger.LogError(ex, "Error creating client profile version"); throw; }
    }

    // ClientBrandVoiceLinks
    public async Task<ClientBrandVoiceLinkDto?> GetClientBrandVoiceLinkByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/client-brand-voice-links/{id}", ct);
            if (!response.IsSuccessStatusCode) return null;
            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ClientBrandVoiceLinkDto>(content, JsonOpts);
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching client brand voice link {LinkId}", id); throw; }
    }

    public async Task<IReadOnlyList<ClientBrandVoiceLinkDto>> GetClientBrandVoiceLinksByProfileVersionIdAsync(Guid profileVersionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"repo/content-writer-v3/client-brand-voice-links?profileVersionId={profileVersionId}", ct);
            if (!response.IsSuccessStatusCode) return new List<ClientBrandVoiceLinkDto>().AsReadOnly();
            var content = await response.Content.ReadAsStringAsync(ct);
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<ClientBrandVoiceLinkDto>>(content, JsonOpts);
            return (dtos ?? new()).AsReadOnly();
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching client brand voice links for profile version {ProfileVersionId}", profileVersionId); throw; }
    }

    public async Task<ClientBrandVoiceLinkDto> CreateClientBrandVoiceLinkAsync(CreateClientBrandVoiceLinkCommand command, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            var response = await _httpClient.PostAsync("repo/content-writer-v3/client-brand-voice-links", new StringContent(json, System.Text.Encoding.UTF8, "application/json"), ct);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(ct);
            return System.Text.Json.JsonSerializer.Deserialize<ClientBrandVoiceLinkDto>(content, JsonOpts) ?? throw new InvalidOperationException("Failed to deserialize client brand voice link response");
        }
        catch (Exception ex) { _logger.LogError(ex, "Error creating client brand voice link"); throw; }
    }
}
