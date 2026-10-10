using GeekAPI.Services.GeekCrawler;

namespace GeekAPI.Services.ContentCreatorV2.Generation;

/// <summary>Typed outcomes for RAG capability negotiation (no silent null→v2).</summary>
public enum GccV2CapabilitiesNegotiationReason
{
    SignerUnconfigured,
    CompleteStageV2Only,
    CapabilitiesV2Only,
    AgentV3,
}

public sealed record GccV2CapabilitiesNegotiation(
    string ExecutionVersion,
    GccV2SignedSkillExecutionEnvelopeV2? Envelope,
    GccV2CapabilitiesNegotiationReason Reason)
{
    public string NegotiationReasonCode => Reason switch
    {
        GccV2CapabilitiesNegotiationReason.SignerUnconfigured => "signer_unconfigured",
        GccV2CapabilitiesNegotiationReason.CompleteStageV2Only => "complete_stage_v2_only",
        GccV2CapabilitiesNegotiationReason.CapabilitiesV2Only => "capabilities_v2_only",
        GccV2CapabilitiesNegotiationReason.AgentV3 => "agent_v3",
        _ => "unknown",
    };
}

/// <summary>Transient RAG capabilities transport failure — retryable by the job worker.</summary>
public sealed class CapabilitiesTransportError(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>Terminal: RAG capabilities endpoint returned 4xx or a malformed body.</summary>
public sealed class CapabilitiesUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
