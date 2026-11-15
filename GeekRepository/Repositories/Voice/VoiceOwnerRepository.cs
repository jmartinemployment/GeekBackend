using Dapper;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using GeekRepository.Infrastructure;
using Npgsql;

namespace GeekRepository.Repositories.Voice;

/// <summary>
/// geek_voice.owner_logins and owner_refresh_tokens over Dapper. A request that cannot be right is refused before a connection is opened;
/// every call runs once, and a database fault that is not a refusal below is raised, not retried, not defaulted. Time is the database's
/// own (NOW()), so a refresh token's life does not depend on GeekAPI's clock. Logs carry ids and codes, never an email, a hash or a token.
/// </summary>
public sealed class VoiceOwnerRepository(IDbConnectionFactory db, ILogger<VoiceOwnerRepository> logger) : IVoiceOwnerStore
{
    private const string LoginColumns = "id AS Id, tenant_id AS TenantId, email AS Email, password_hash AS PasswordHash, display_name AS DisplayName";

    public async Task<Result<VoiceOwnerLogin>> CreateOwnerLoginAsync(CreateVoiceOwnerLoginCommand command, CancellationToken ct = default)
    {
        if (command.TenantId == Guid.Empty) return Invalid<VoiceOwnerLogin>("A tenant is required.");
        var email = NormalizeEmail(command.Email);
        if (email is null) return Invalid<VoiceOwnerLogin>("A valid email is required.");
        if (string.IsNullOrWhiteSpace(command.PasswordHash)) return Invalid<VoiceOwnerLogin>("A password hash is required.");

        try
        {
            await using var conn = await OpenAsync(ct);
            var row = await conn.QuerySingleAsync<VoiceOwnerLogin>(new CommandDefinition($"""
                INSERT INTO geek_voice.owner_logins (tenant_id, email, password_hash, display_name)
                VALUES (@TenantId, @Email, @PasswordHash, @DisplayName)
                RETURNING {LoginColumns};
                """,
                new { command.TenantId, Email = email, command.PasswordHash, DisplayName = string.IsNullOrWhiteSpace(command.DisplayName) ? null : command.DisplayName.Trim() },
                cancellationToken: ct));
            return Result<VoiceOwnerLogin>.Success(row);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            logger.LogWarning("An owner login was refused as a duplicate: constraint {Constraint}.", ex.ConstraintName);
            return Result<VoiceOwnerLogin>.Failure("That tenant already has a login, or that email is already in use.", VoiceFailureCodes.Conflict);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            logger.LogWarning("An owner login was refused: the tenant does not exist.");
            return Result<VoiceOwnerLogin>.Failure("The tenant was not found.", VoiceFailureCodes.NotFound);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.CheckViolation)
        {
            logger.LogWarning("An owner login was refused by a check: constraint {Constraint}.", ex.ConstraintName);
            return Invalid<VoiceOwnerLogin>("The login was refused by the database.");
        }
    }

    public async Task<Result<VoiceOwnerLogin>> FindOwnerLoginByEmailAsync(string email, CancellationToken ct = default)
    {
        var normalized = NormalizeEmail(email);
        if (normalized is null) return Invalid<VoiceOwnerLogin>("A valid email is required.");

        await using var conn = await OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<VoiceOwnerLogin>(new CommandDefinition(
            $"SELECT {LoginColumns} FROM geek_voice.owner_logins WHERE email = @Email;",
            new { Email = normalized },
            cancellationToken: ct));
        return row is null ? NotFound<VoiceOwnerLogin>("No login has that email.") : Result<VoiceOwnerLogin>.Success(row);
    }

    public async Task<Result<bool>> StoreRefreshTokenAsync(StoreVoiceRefreshTokenCommand command, CancellationToken ct = default)
    {
        if (command.OwnerLoginId == Guid.Empty || command.FamilyId == Guid.Empty) return Invalid<bool>("A login and a family are required.");
        if (string.IsNullOrWhiteSpace(command.TokenHash)) return Invalid<bool>("A token hash is required.");

        try
        {
            await using var conn = await OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO geek_voice.owner_refresh_tokens (owner_login_id, family_id, token_hash, expires_at)
                VALUES (@OwnerLoginId, @FamilyId, @TokenHash, @ExpiresAt);
                """,
                new { command.OwnerLoginId, command.FamilyId, command.TokenHash, ExpiresAt = command.ExpiresAt.UtcDateTime },
                cancellationToken: ct));
            return Result<bool>.Success(true);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            logger.LogWarning("A refresh token was refused as a duplicate.");
            return Result<bool>.Failure("That refresh token already exists.", VoiceFailureCodes.Conflict);
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.CheckViolation)
        {
            logger.LogWarning("A refresh token was refused: sqlstate {SqlState}.", ex.SqlState);
            return Invalid<bool>("The refresh token was refused by the database.");
        }
    }

    public async Task<Result<ConsumedVoiceRefreshToken>> ConsumeRefreshTokenAsync(string tokenHash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tokenHash)) return Invalid<ConsumedVoiceRefreshToken>("A token hash is required.");

        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Spent in one statement: two requests presenting the same token cannot both succeed, because only one UPDATE finds used_at null.
        var spent = await conn.QuerySingleOrDefaultAsync<SpentRow>(new CommandDefinition("""
            UPDATE geek_voice.owner_refresh_tokens
               SET used_at = NOW()
             WHERE token_hash = @TokenHash AND used_at IS NULL AND revoked_at IS NULL AND expires_at > NOW()
            RETURNING owner_login_id AS OwnerLoginId, family_id AS FamilyId;
            """,
            new { TokenHash = tokenHash }, tx, cancellationToken: ct));

        if (spent is not null)
        {
            var login = await conn.QuerySingleAsync<LoginRow>(new CommandDefinition(
                "SELECT tenant_id AS TenantId, email AS Email FROM geek_voice.owner_logins WHERE id = @Id;",
                new { Id = spent.OwnerLoginId }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            return Result<ConsumedVoiceRefreshToken>.Success(new ConsumedVoiceRefreshToken(spent.OwnerLoginId, login.TenantId, spent.FamilyId, login.Email));
        }

        // Not spendable. A token that exists and was already spent or revoked is a replay: the sign-in it belonged to is ended.
        var known = await conn.QuerySingleOrDefaultAsync<KnownRow>(new CommandDefinition("""
            SELECT family_id AS FamilyId, (used_at IS NOT NULL OR revoked_at IS NOT NULL) AS WasSpent
              FROM geek_voice.owner_refresh_tokens WHERE token_hash = @TokenHash;
            """,
            new { TokenHash = tokenHash }, tx, cancellationToken: ct));

        if (known is not null && known.WasSpent)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE geek_voice.owner_refresh_tokens SET revoked_at = NOW() WHERE family_id = @FamilyId AND revoked_at IS NULL;",
                new { known.FamilyId }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            logger.LogWarning("A spent refresh token was presented again; family {FamilyId} was revoked.", known.FamilyId);
            return Result<ConsumedVoiceRefreshToken>.Failure("That refresh token was already used.", VoiceOwnerFailureCodes.RefreshTokenReused);
        }

        await tx.CommitAsync(ct);
        return NotFound<ConsumedVoiceRefreshToken>("That refresh token is unknown or has expired.");
    }

    public async Task<Result<bool>> RevokeRefreshTokenFamilyAsync(string tokenHash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tokenHash)) return Invalid<bool>("A token hash is required.");

        await using var conn = await OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE geek_voice.owner_refresh_tokens SET revoked_at = NOW()
             WHERE revoked_at IS NULL
               AND family_id = (SELECT family_id FROM geek_voice.owner_refresh_tokens WHERE token_hash = @TokenHash);
            """,
            new { TokenHash = tokenHash },
            cancellationToken: ct));
        return Result<bool>.Success(true);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = (NpgsqlConnection)db.CreateConnection();
        try
        {
            await conn.OpenAsync(ct);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    /// <summary>Trimmed and lower-cased, the one form a login is stored and looked up in. Null when it cannot be an email.</summary>
    public static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var normalized = email.Trim().ToLowerInvariant();
        if (normalized.Length is < 3 or > 254) return null;
        var at = normalized.IndexOf('@', StringComparison.Ordinal);
        if (at < 1 || at != normalized.LastIndexOf('@') || at == normalized.Length - 1 || normalized.Any(char.IsWhiteSpace)) return null;
        return normalized;
    }

    private static Result<T> Invalid<T>(string message) => Result<T>.Failure(message, VoiceFailureCodes.InvalidRequest);

    private static Result<T> NotFound<T>(string message) => Result<T>.Failure(message, VoiceFailureCodes.NotFound);

    private sealed record SpentRow(Guid OwnerLoginId, Guid FamilyId);

    private sealed record LoginRow(Guid TenantId, string Email);

    private sealed record KnownRow(Guid FamilyId, bool WasSpent);
}
