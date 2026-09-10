using GeekAPI.Services.ContentCreatorV2.Context;
using System.Text.Json;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2ProductClaimPolicyTests
{
    [Fact]
    public void Authoring_requires_approved_claims_and_mandatory_disclaimers()
    {
        Assert.Equal(
            "Add at least one approved Product claim.",
            GccV2ProductClaimPolicy.ValidateAuthoring([], [], ["Disclaimer"]));
        Assert.Equal(
            "Add at least one mandatory Product disclaimer.",
            GccV2ProductClaimPolicy.ValidateAuthoring(["Approved claim"], [], []));
        Assert.Null(GccV2ProductClaimPolicy.ValidateAuthoring(
            ["Approved claim"], ["Forbidden claim"], ["Results vary."]));
    }

    [Fact]
    public void Authoring_rejects_claims_that_are_both_approved_and_prohibited()
    {
        Assert.Equal(
            "A Product claim cannot be both approved and prohibited.",
            GccV2ProductClaimPolicy.ValidateAuthoring(
                ["SOC2 ready"], ["soc2 ready"], ["Results vary."]));
    }

    [Fact]
    public void Resolve_blocks_missing_or_conflicting_claim_policy()
    {
        var versionId = Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");
        Assert.Equal(
            [
                $"product:{versionId}:approved_claims_required",
                $"product:{versionId}:mandatory_disclaimers_required",
            ],
            GccV2ProductClaimPolicy.ResolveBlockers(versionId, "[]", "[]", "[]"));

        var conflict = GccV2ProductClaimPolicy.ResolveBlockers(
            versionId,
            JsonSerializer.Serialize(new[] { "SOC2 ready" }),
            JsonSerializer.Serialize(new[] { "soc2 ready" }),
            JsonSerializer.Serialize(new[] { "Results vary." }));
        Assert.Contains($"product:{versionId}:claim_policy_conflict", conflict);

        Assert.Empty(GccV2ProductClaimPolicy.ResolveBlockers(
            versionId,
            JsonSerializer.Serialize(new[] { "Evidence Engine cites every claim" }),
            JsonSerializer.Serialize(new[] { "guarantees perfect accuracy" }),
            JsonSerializer.Serialize(new[] { "Results depend on source coverage." })));
    }
}
