using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.AgentTests;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.Rag;
using GeekRepository.Controllers.ContentCreatorV2;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using GeekAPI.Controllers.ContentCreatorV2.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2SpecialistAgentTests
{
    [Fact]
    public async Task AgentVersion_PinsPublishedSkill_AndPublishedVersionIsImmutable()
    {
        await using var db = Db();
        var skillPackage = new GccV2SkillPackage
        {
            Slug = "citation-discipline", DisplayName = "Citation Discipline",
            Description = "Citations", SourceRepository = "GeekBackend", SourcePath = "skills/citations",
            Publisher = "test", LifecycleState = "published",
        };
        var skill = new GccV2SkillVersion
        {
            Package = skillPackage, SemanticVersion = "1.0.0", ImmutableGitRef = "first-party",
            PackageSha256 = new string('a', 64), ManifestDigest = new string('b', 64),
            License = "MIT", Compatibility = "gcc-v2", State = "published",
            Applicability = new[]
            {
                "researchPlanning", "outline", "section", "repair",
                "validation", "finalSynthesis", "complete",
            }.Select(stage => new GccV2SkillApplicability
                { Stage = stage, ContentType = "blog", RequiredToolsJson = "[]", ConflictsJson = "[]" }).ToList(),
        };
        db.Add(skill);
        await db.SaveChangesAsync();
        var controller = new GccV2AgentsController(db);

        var created = await controller.Create(new(
            "writing", "Writing", "Producer", true, "admin", null, "r1"), default);
        var agent = Assert.IsType<GccV2Agent>(Assert.IsType<CreatedAtActionResult>(created.Result).Value);
        var versionResult = await controller.CreateVersion(agent.Id, new(
            "1.0.0", "Produce canonical content.", ["blog"], [],
            [ContentModelPolicy.O3], [skill.Id],
            new[] { "researchPlanning", "outline", "section", "repair", "validation", "finalSynthesis", "complete" }
                .Select((stage, order) => new GccV2AgentsController.StageParticipationCommand(
                    stage, "producer", 100 + order)).ToList(),
            "admin", null, "r2"), default);
        var version = Assert.IsType<GccV2AgentVersion>(
            Assert.IsType<CreatedAtActionResult>(versionResult.Result).Value);

        Assert.Contains(version.Skills, x => x.SkillVersionId == skill.Id);
        Assert.IsType<OkObjectResult>((await controller.Review(version.Id,
            new(true, "approved", "admin", null, "r3"), default)).Result);
        var queued = Assert.IsType<GccV2AgentTestRun>(
            Assert.IsType<AcceptedAtActionResult>((await controller.QueueTest(version.Id,
                new("contract", "{}", "admin", null, "r4"), default)).Result).Value);
        Assert.IsType<OkObjectResult>((await controller.PatchTestRun(queued.Id,
            new("passed", 100, "passed", """{"passed":true}""", null, null, null,
                "worker", null, "r4-complete"), default)).Result);
        Assert.IsType<OkObjectResult>((await controller.Publish(version.Id,
            new("admin", null, null, "r5"), default)).Result);
        Assert.IsType<ConflictObjectResult>((await controller.Patch(agent.Id,
            new("Changed", null, "admin", null, "r6"), default)).Result);
        Assert.IsType<OkObjectResult>((await controller.Revoke(version.Id,
            new("admin", "compromised", null, "r7"), default)).Result);
        Assert.Equal(7, await db.GccV2AgentAuditEvents.CountAsync());
    }

    [Fact]
    public void TeamContract_RequiresExactlyOneProducer_AndExplicitSkills()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory,
            "ContentCreatorV2", "Fixtures", "specialist-agent-team.json");
        var snapshot = JsonSerializer.Deserialize<GccV2AgentTeamSnapshot>(
            File.ReadAllText(fixturePath), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(snapshot);
        GccV2AgentTeamResolver.Validate(snapshot.Agents, "blog");

        var invalid = snapshot with
        {
            Agents = snapshot.Agents.Select(x => x with
            {
                Participation = x.Participation.Select(p => p with
                    { Role = p.Role == "producer" ? "contributor" : p.Role }).ToList(),
            }).ToList(),
        };
        var error = Assert.Throws<InvalidOperationException>(
            () => GccV2AgentTeamResolver.Validate(invalid.Agents, "blog"));
        Assert.Contains("exactly one producer", error.Message);
    }

    [Fact]
    public void CoordinatorOrderingAndReturnedDigest_AreFailClosed()
    {
        var snapshot = JsonSerializer.Deserialize<GccV2AgentTeamSnapshot>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ContentCreatorV2",
                "Fixtures", "specialist-agent-team.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var order = GccV2SpecialistCoordinator.DeterministicParticipantOrder(
            snapshot, "outline", "contributor");
        Assert.Equal(order.Order(StringComparer.Ordinal), order);
        var payload = new RagSpecialistContributionDto(
            "contributorOutput.v1", "outline", "summary", null, [], null, null, []);
        var digest = GccV2AgentExecutionFactory.CanonicalDigest(payload);
        Assert.Equal(digest,
            GccV2SpecialistCoordinator.VerifyReturnedDigest("seo", payload, digest));
        Assert.Throws<InvalidOperationException>(() =>
            GccV2SpecialistCoordinator.VerifyReturnedDigest("seo", payload, new string('0', 64)));
    }

    [Fact]
    public void AgentTeamSigner_DetectsSnapshotTampering()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GccV2Agents:SnapshotSigningKey"] = new string('t', 40),
        }).Build();
        var signer = new GccV2AgentTeamSigner(config);
        var signature = signer.Sign(new string('a', 64));
        Assert.True(signer.Verify(new string('a', 64), signature));
        Assert.False(signer.Verify(new string('b', 64), signature));
    }

    [Fact]
    public void AgentExecutionCanonicalization_MatchesPythonGolden()
    {
        const string selectedDigest = "084b62d7ec10b389794ecf32d5946d06f9d482d37b3b6f52cfa00827c374d2c5";
        // String artifacts are hashed as their raw UTF-8 value, not as a JSON string.
        var instructionsDigest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("Write grounded content."))).ToLowerInvariant();
        var policyDigest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("explicit"))).ToLowerInvariant();
        var selected = new RagSelectedAgentDto(
            "writing", "1.1.0", new string('0', 64), "Writing", "producer",
            "Write grounded content.", instructionsDigest, "explicit", policyDigest,
            ["outline"],
            ["activate_skill", "get_brief_context", "get_specialist_artifacts",
                "load_evidence_page", "read_skill_resource", "search_corpus", "submit_outline"],
            ["o3"]);
        Assert.Equal(selectedDigest,
            GccV2AgentExecutionFactory.CanonicalDigest(selected, "digest"));
        selected = selected with { Digest = selectedDigest };
        var execution = new RagAgentExecutionRequestDto(
            "specialist-team-execution.v1", new string('0', 64), new string('0', 64), "test-key",
            "11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222",
            "33333333-3333-3333-3333-333333333333",
            "44444444-4444-4444-4444-444444444444",
            new string('a', 64), DateTimeOffset.Parse("2026-09-08T12:00:00Z"),
            DateTimeOffset.Parse("2026-09-08T12:10:00Z"), 1, null, "outline", selected,
            "producerOutput.v1", [], [], new RagAgentBudgetDto(), false, 0);
        var digest = GccV2AgentExecutionFactory.CanonicalDigest(
            execution, "snapshotDigest", "signature", "cancelled");
        Assert.Equal("aaeb57b0d4d9822306c2d4e24ea8e942f54ec29bea0436320c46b14b4cd0fc19", digest);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GccV2Agents:SnapshotSigningKey"] = "0123456789abcdef0123456789abcdef",
        }).Build();
        Assert.Equal("9c6b32e1eabe82747fe09228dc74011c0905b14bf2f96de66d821dbbc65be4cf",
            new GccV2AgentTeamSigner(config).Sign(digest));
    }

    [Fact]
    public void SerializedAgentExecution_MatchesPythonWireGolden()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "ContentCreatorV2", "Fixtures",
            "python-v3-agent-execution.json")));
        var execution = fixture.RootElement.GetProperty("execution")
            .Deserialize<RagAgentExecutionRequestDto>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var digest = GccV2AgentExecutionFactory.CanonicalDigest(
            execution, "snapshotDigest", "signature", "cancelled");
        Assert.Equal(fixture.RootElement.GetProperty("expectedDigest").GetString(), digest);
        using var wireDocument = JsonDocument.Parse(
            GccV2AgentExecutionFactory.SerializeWireExecution(execution));
        var wire = wireDocument.RootElement;
        Assert.True(JsonElement.DeepEquals(
            fixture.RootElement.GetProperty("execution"), wire));
    }

    [Fact]
    public async Task TestRuns_AreDurableClaimedOnce_AndLatestRunGatesPublish()
    {
        await using var db = Db();
        var (controller, version) = await ApprovedVersionAsync(db);
        var first = Assert.IsType<GccV2AgentTestRun>(
            Assert.IsType<AcceptedAtActionResult>((await controller.QueueTest(version.Id,
                new("contract", "{}", "admin", null, "q1"), default)).Result).Value);
        var claimed = Assert.IsType<GccV2AgentTestRun>(
            Assert.IsType<OkObjectResult>((await controller.ClaimTestRun(
                first.Id, "worker-a", 120, default)).Result).Value);
        Assert.Equal("running", claimed.Status);
        Assert.Equal(1, claimed.AttemptCount);
        Assert.IsType<ConflictObjectResult>((await controller.ClaimTestRun(
            first.Id, "worker-b", 120, default)).Result);
        Assert.IsType<OkObjectResult>((await controller.PatchTestRun(first.Id,
            new("passed", 100, "passed", "{}", null, null, null,
                "worker-a", null, "done"), default)).Result);

        var latest = Assert.IsType<GccV2AgentTestRun>(
            Assert.IsType<AcceptedAtActionResult>((await controller.QueueTest(version.Id,
                new("contract", "{}", "admin", null, "q2"), default)).Result).Value);
        Assert.IsType<ConflictObjectResult>((await controller.Publish(version.Id,
            new("admin", null, null, "publish-denied"), default)).Result);
        Assert.IsType<OkObjectResult>((await controller.ClaimTestRun(
            latest.Id, "worker-b", 120, default)).Result);
        Assert.IsType<OkObjectResult>((await controller.PatchTestRun(latest.Id,
            new("failed", 100, "failed", "{}", "failure", null, null,
                "worker-b", null, "failed"), default)).Result);
        Assert.IsType<ConflictObjectResult>((await controller.Publish(version.Id,
            new("admin", null, null, "publish-denied-2"), default)).Result);

        var recovery = Assert.IsType<GccV2AgentTestRun>(
            Assert.IsType<AcceptedAtActionResult>((await controller.QueueTest(version.Id,
                new("contract", "{}", "admin", null, "q3"), default)).Result).Value);
        Assert.IsType<OkObjectResult>((await controller.ClaimTestRun(
            recovery.Id, "crashed-worker", 120, default)).Result);
        var stale = await db.GccV2AgentTestRuns.SingleAsync(x => x.Id == recovery.Id);
        stale.LeaseUntilUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        var reclaimed = Assert.IsType<GccV2AgentTestRun>(
            Assert.IsType<OkObjectResult>((await controller.ClaimTestRun(
                recovery.Id, "recovery-worker", 120, default)).Result).Value);
        Assert.Equal(2, reclaimed.AttemptCount);
        Assert.Equal(1, reclaimed.RecoveryCount);
    }

    [Fact]
    public async Task PublishRejectsPassingRunBoundToDifferentDigest()
    {
        await using var db = Db();
        var (controller, version) = await ApprovedVersionAsync(db);
        var run = Assert.IsType<GccV2AgentTestRun>(
            Assert.IsType<AcceptedAtActionResult>((await controller.QueueTest(version.Id,
                new("contract", "{}", "admin", null, "q1"), default)).Result).Value);
        Assert.IsType<OkObjectResult>((await controller.PatchTestRun(run.Id,
            new("passed", 100, "passed", "{}", null, null, null,
                "worker", null, "done"), default)).Result);
        var stored = await db.GccV2AgentTestRuns.SingleAsync(x => x.Id == run.Id);
        stored.VersionDigest = new string('f', 64);
        await db.SaveChangesAsync();
        Assert.IsType<ConflictObjectResult>((await controller.Publish(version.Id,
            new("admin", null, null, "publish-denied"), default)).Result);
    }

    [Fact]
    public async Task AgentTestPolicy_FailsClosedOnToolAndModelPolicy()
    {
        await using var db = Db();
        var (_, version) = await ApprovedVersionAsync(db);
        Assert.Empty(GccV2AgentTestPolicy.Failures(
            ToDto(version, """["o3"]""", version.AllowedToolsJson)));
        var failures = GccV2AgentTestPolicy.Failures(
            ToDto(version, """["unapproved-model"]""", """["shell"]"""));
        Assert.Contains(failures, x => x.Contains("prohibited tool"));
        Assert.Contains(failures, x => x.Contains("model intersection"));
    }

    [Fact]
    public async Task ObjectiveFindingsAndPublishedSuccessor_AreVersionBound()
    {
        await using var db = Db();
        var (controller, version) = await ApprovedVersionAsync(db);
        var originalDigest = version.VersionDigest;
        version.State = "draft";
        var finding = new GccV2AgentReviewFinding
        {
            AgentVersionId = version.Id, Severity = "high", Rule = "objective-review",
            Message = "Objective requires explicit review.",
        };
        db.Add(finding);
        await db.SaveChangesAsync();
        Assert.IsType<ConflictObjectResult>((await controller.Review(version.Id,
            new(true, null, "admin", null, "blocked"), default)).Result);
        Assert.IsType<OkObjectResult>((await controller.PatchFinding(version.Id, finding.Id,
            new("accepted", "Reviewed.", "admin", null, "finding"), default)).Result);
        Assert.IsType<OkObjectResult>((await controller.Review(version.Id,
            new(true, null, "admin", null, "approved"), default)).Result);
        var run = Assert.IsType<GccV2AgentTestRun>(Assert.IsType<AcceptedAtActionResult>(
            (await controller.QueueTest(version.Id, new("contract", "{}", "admin", null, "test"), default)).Result).Value);
        await controller.PatchTestRun(run.Id, new("passed", 100, "passed", "{}", null, null, null,
            "worker", null, "done"), default);
        await controller.Publish(version.Id, new("admin", null, null, "publish"), default);
        var successor = Assert.IsType<GccV2AgentVersion>(Assert.IsType<CreatedAtActionResult>(
            (await controller.CreateSuccessor(version.Id, new(
                "2.0.0", "A materially different objective.", null, null, null, null, null, null,
                ContentModelPolicy.CurrentVersion, "admin", null, "successor"), default)).Result).Value);
        Assert.Equal("draft", successor.State);
        Assert.NotEqual(originalDigest, successor.VersionDigest);
        Assert.Equal("A materially different objective.", successor.Objective);
        Assert.Equal(ContentModelPolicy.CurrentVersion, successor.ModelPolicyVersion);
    }

    [Fact]
    public void AgentTestEvent_UsesStableRealtimeContract()
    {
        var now = DateTimeOffset.UtcNow;
        var run = new GccV2AgentTestRunDto(
            Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), "contract", "{}",
            "running", 55, "rag-smoke", null, null, "admin", now, now, null, now,
            "worker", now, now.AddMinutes(2), 1, 0, null, null);
        var evt = GccV2AgentTestProgressNotifier.ToEvent(run, "progress");
        Assert.Equal("gcc-agent-test-event.v1", evt.ContractVersion);
        Assert.Equal(run.Id, evt.TestRunId);
        Assert.Equal("AgentTestEvent", nameof(GccV2AgentTestEvent).Replace("GccV2", ""));
        Assert.Equal("progress", evt.Message);
    }

    [Fact]
    public async Task AgentTestNotifier_DeliversEventToRunGroup()
    {
        var now = DateTimeOffset.UtcNow;
        var run = new GccV2AgentTestRunDto(
            Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), "contract", "{}",
            "running", 55, "rag-smoke", null, null, "admin", now, now, null, now,
            "worker", now, now.AddMinutes(2), 1, 0, null, null);
        var context = new RecordingHubContext();
        await new GccV2AgentTestProgressNotifier(context).PushAsync(run, "progress");
        Assert.Equal($"agent-test:{run.Id:D}", context.ClientsImpl.LastGroup);
        Assert.Equal("AgentTestEvent", context.ClientsImpl.Proxy.Method);
        var delivered = Assert.IsType<GccV2AgentTestEvent>(
            Assert.Single(context.ClientsImpl.Proxy.Args!));
        Assert.Equal(run.Id, delivered.TestRunId);
    }

    private static GccV2AgentVersionDto ToDto(
        GccV2AgentVersion version, string models, string tools) => new(
        version.Id, version.AgentId, version.SemanticVersion, version.Instructions,
        version.ContentTypesJson, tools, models, version.VersionDigest, version.State,
        version.CreatedAtUtc, version.ReviewedAtUtc, version.TestedAtUtc, version.PublishedAtUtc,
        version.DeprecatedAtUtc, version.RevokedAtUtc, version.Reviewer, version.ReviewNotes,
        version.TestResultJson,
        version.Skills.Select(x => new GccV2AgentSkillVersionDto(
            version.Id, x.SkillVersionId, x.Order, new(
                x.SkillVersion.Id, x.SkillVersion.SemanticVersion, x.SkillVersion.PackageSha256,
                x.SkillVersion.State, new(x.SkillVersion.Package.Id, x.SkillVersion.Package.Slug,
                    x.SkillVersion.Package.DisplayName),
                x.SkillVersion.Applicability.Select(a => new GccV2SkillApplicabilityDto(
                    a.Id, a.VersionId, a.Stage, a.ContentType, a.Order, a.ConflictsJson,
                    a.RequiredToolsJson, a.ActivationMode)).ToList()))).ToList(),
        version.StageParticipation.Select(x => new GccV2AgentStageParticipationDto(
            x.Id, x.AgentVersionId, x.Stage, x.Role, x.Order)).ToList(),
        version.Objective, version.ModelPolicyVersion,
        version.Findings.Select(x => new GccV2AgentReviewFindingDto(
            x.Id, x.AgentVersionId, x.Severity, x.Rule, x.Message, x.Disposition,
            x.ReviewerRationale, x.CreatedAtUtc, x.DisposedAtUtc)).ToList());

    private static async Task<(GccV2AgentsController Controller, GccV2AgentVersion Version)>
        ApprovedVersionAsync(ContentCreatorV2DbContext db)
    {
        var package = new GccV2SkillPackage
        {
            Slug = $"skill-{Guid.NewGuid():N}", DisplayName = "Skill", Description = "Safe",
            SourceRepository = "test", SourcePath = "skill", Publisher = "test",
            LifecycleState = "published",
        };
        var stages = new[] { "researchPlanning", "outline", "section", "repair", "validation", "finalSynthesis", "complete" };
        var skill = new GccV2SkillVersion
        {
            Package = package, SemanticVersion = "1.0.0", ImmutableGitRef = "first-party",
            PackageSha256 = new string('a', 64), ManifestDigest = new string('b', 64),
            License = "MIT", Compatibility = "gcc-v2", State = "published",
            Applicability = stages.Select(stage => new GccV2SkillApplicability
                { Stage = stage, ContentType = "blog", RequiredToolsJson = "[]", ConflictsJson = "[]" }).ToList(),
        };
        db.Add(skill);
        await db.SaveChangesAsync();
        var controller = new GccV2AgentsController(db);
        var agent = Assert.IsType<GccV2Agent>(Assert.IsType<CreatedAtActionResult>(
            (await controller.Create(new($"agent-{Guid.NewGuid():N}", "Agent", "Test", false,
                "admin", null, "create"), default)).Result).Value);
        var version = Assert.IsType<GccV2AgentVersion>(Assert.IsType<CreatedAtActionResult>(
            (await controller.CreateVersion(agent.Id, new(
                "1.0.0", "Test instructions.", ["blog"],
                ["search_corpus", "load_evidence_page", "get_brief_context", "get_outline_context",
                    "get_completed_section_summaries", "get_specialist_artifacts", "activate_skill",
                    "read_skill_resource", "submit_research_plan", "submit_outline", "submit_section",
                    "submit_repair", "submit_validation", "submit_final_synthesis"],
                [ContentModelPolicy.O3], [skill.Id],
                stages.Select((stage, order) => new GccV2AgentsController.StageParticipationCommand(
                    stage, "producer", order)).ToList(), "admin", null, "version"), default)).Result).Value);
        Assert.IsType<OkObjectResult>((await controller.Review(version.Id,
            new(true, null, "admin", null, "review"), default)).Result);
        return (controller, version);
    }

    private static ContentCreatorV2DbContext Db() => new(
        new DbContextOptionsBuilder<ContentCreatorV2DbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed class RecordingHubContext : IHubContext<GccV2RealtimeHub>
    {
        public RecordingHubClients ClientsImpl { get; } = new();
        public IHubClients Clients => ClientsImpl;
        public IGroupManager Groups { get; } = new NoopGroups();
    }

    private sealed class RecordingHubClients : IHubClients
    {
        public RecordingClientProxy Proxy { get; } = new();
        public string? LastGroup { get; private set; }
        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) { LastGroup = groupName; return Proxy; }
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }

    private sealed class RecordingClientProxy : IClientProxy
    {
        public string? Method { get; private set; }
        public object?[]? Args { get; private set; }
        public Task SendCoreAsync(
            string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Method = method;
            Args = args;
            return Task.CompletedTask;
        }
    }

    private sealed class NoopGroups : IGroupManager
    {
        public Task AddToGroupAsync(
            string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task RemoveFromGroupAsync(
            string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
