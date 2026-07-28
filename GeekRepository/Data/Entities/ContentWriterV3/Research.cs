namespace GeekRepository.Data.Entities.ContentWriterV3;

public class ResearchRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CampaignId { get; set; }
    public string Keyword { get; set; } = string.Empty;
    public string Status { get; set; } = "queued"; // queued, running, completed, failed, completed-with-partial-coverage
    public int DiscoveredSourceCount { get; set; }
    public decimal SpentBudget { get; set; }
    public decimal MaxBudget { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

public class ResearchSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ResearchRunId { get; set; }
    public string SourceType { get; set; } = "ExistingInternal"; // ExistingInternal, OperatorUploaded, AgentDiscoveredExternal
    public string? Url { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ResearchEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ResearchSourceId { get; set; }
    public string Statement { get; set; } = string.Empty;
    public string SupportLevel { get; set; } = "Unsupported"; // VerifiedClientFact, VerifiedExternalSource, ObservedMarketLanguage, Unsupported
    public bool ApprovedForClaim { get; set; }
    public int Confidence { get; set; } // 0-100
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class PainPoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ReaderSymptom { get; set; } = string.Empty;
    public string CostOfInaction { get; set; } = string.Empty;
    public string OfferTerminology { get; set; } = string.Empty;
    public List<string> Objections { get; set; } = new();
    public int Confidence { get; set; } // 0-100
    public DateTime? StaleeSinceUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class PainPointEvidenceLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PainPointId { get; set; }
    public Guid ResearchEvidenceId { get; set; }
    public string Dimension { get; set; } = "reader"; // reader, symptom, cost, offer, objection
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ReconciliationProposal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ResearchRunId { get; set; }
    public string ProposalType { get; set; } = "new-pain-point"; // new-pain-point, update-pain-point, new-evidence-link
    public Guid? PainPointId { get; set; }
    public Dictionary<string, object> ProposedData { get; set; } = new();
    public string Status { get; set; } = "pending"; // pending, approved, dismissed
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class KeywordCandidate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    public string Keyword { get; set; } = string.Empty;
    public int? SearchVolume { get; set; }
    public int? Difficulty { get; set; }
    public string? Intent { get; set; }
    public string Status { get; set; } = "draft"; // draft, research-queued, researched, briefed, rejected
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
