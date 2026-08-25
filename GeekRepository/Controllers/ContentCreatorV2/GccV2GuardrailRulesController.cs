using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

[ApiController]
[Route("repo/content-creator-v2/guardrail-rules")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccV2GuardrailRulesController : ControllerBase
{
    private readonly ContentCreatorV2DbContext _db;

    public GccV2GuardrailRulesController(ContentCreatorV2DbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccV2GuardrailRule>>> List(
        [FromQuery] bool? enabled,
        CancellationToken ct)
    {
        var query = _db.GccV2GuardrailRules.AsNoTracking().AsQueryable();
        if (enabled is not null)
            query = query.Where(r => r.Enabled == enabled);
        var results = await query.OrderBy(r => r.Pattern).ToListAsync(ct);
        return Ok(results);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccV2GuardrailRule>> GetById(Guid id, CancellationToken ct)
    {
        var rule = await _db.GccV2GuardrailRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return rule is null ? NotFound() : Ok(rule);
    }

    [HttpPost]
    public async Task<ActionResult<GccV2GuardrailRule>> Create([FromBody] CreateGccV2GuardrailRuleCommand command, CancellationToken ct)
    {
        if (command is null || string.IsNullOrWhiteSpace(command.Pattern))
            return BadRequest("pattern is required");

        var rule = new GccV2GuardrailRule
        {
            Id = Guid.NewGuid(),
            Pattern = command.Pattern.Trim(),
            Action = string.IsNullOrWhiteSpace(command.Action) ? "strip" : command.Action.Trim().ToLowerInvariant(),
            ReplaceWith = command.ReplaceWith,
            Enabled = command.Enabled ?? true,
            Scope = command.Scope,
            ReasonCode = command.ReasonCode,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        _db.GccV2GuardrailRules.Add(rule);
        await _db.SaveChangesAsync(ct);
        return Ok(rule);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<GccV2GuardrailRule>> Patch(Guid id, [FromBody] PatchGccV2GuardrailRuleCommand command, CancellationToken ct)
    {
        var rule = await _db.GccV2GuardrailRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return NotFound();

        if (command.Pattern is not null) rule.Pattern = command.Pattern.Trim();
        if (command.Action is not null) rule.Action = command.Action.Trim().ToLowerInvariant();
        if (command.ReplaceWith is not null) rule.ReplaceWith = command.ReplaceWith;
        if (command.Enabled is not null) rule.Enabled = command.Enabled.Value;
        if (command.Scope is not null) rule.Scope = command.Scope;
        if (command.ReasonCode is not null) rule.ReasonCode = command.ReasonCode;

        await _db.SaveChangesAsync(ct);
        return Ok(rule);
    }

    /// <summary>Seeds v2 with the same eight rules v1's static <c>ContentGuardrail</c> ships — idempotent.</summary>
    [HttpPost("seed-defaults")]
    public async Task<ActionResult<object>> SeedDefaults(CancellationToken ct)
    {
        if (await _db.GccV2GuardrailRules.AnyAsync(ct))
            return Ok(new { seeded = 0, message = "Rules already exist; skipped." });

        var now = DateTimeOffset.UtcNow;
        var defaults = new[]
        {
            ("in today's fast-paced digital world", "strip", null, "AI_FILLER"),
            ("delve deeper", "replace", "examine", "AI_FILLER"),
            ("it is crucial to remember", "strip", null, "AI_FILLER"),
            ("testament to", "restructure", null, "AI_FILLER"),
            ("synergistic approach", "replace", "collaborative strategy", "CORP_JARGON"),
            ("paradigm shift", "replace", "fundamental change", "CORP_JARGON"),
            ("utilize", "replace", "use", "CORP_JARGON"),
            ("moving the needle", "replace", "achieving measurable results", "CORP_JARGON"),
        };

        foreach (var (pattern, action, replace, reason) in defaults)
        {
            _db.GccV2GuardrailRules.Add(new GccV2GuardrailRule
            {
                Id = Guid.NewGuid(),
                Pattern = pattern,
                Action = action,
                ReplaceWith = replace,
                Enabled = true,
                ReasonCode = reason,
                CreatedAtUtc = now,
            });
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { seeded = defaults.Length });
    }
}

public sealed record CreateGccV2GuardrailRuleCommand(
    string Pattern,
    string? Action,
    string? ReplaceWith,
    bool? Enabled,
    string? Scope,
    string? ReasonCode);

public sealed record PatchGccV2GuardrailRuleCommand(
    string? Pattern = null,
    string? Action = null,
    string? ReplaceWith = null,
    bool? Enabled = null,
    string? Scope = null,
    string? ReasonCode = null);
