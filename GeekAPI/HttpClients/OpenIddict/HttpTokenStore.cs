using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using GeekApplication.Entities.OpenIddict;
using OpenIddict.Abstractions;

namespace GeekAPI.HttpClients.OpenIddict;

public sealed class HttpTokenStore : IOpenIddictTokenStore<GeekOpenIddictToken>
{
    private const string BasePath = "repo/openiddict/tokens";
    private readonly OpenIddictRepoClient _repo;

    public HttpTokenStore(IHttpClientFactory factory) => _repo = new OpenIddictRepoClient(factory);

    public async ValueTask<long> CountAsync(CancellationToken cancellationToken) =>
        await _repo.GetCountAsync($"{BasePath}/count", cancellationToken);

    public ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<GeekOpenIddictToken>, IQueryable<TResult>> query,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Queryable token counts are not supported over HTTP stores.");

    public async ValueTask CreateAsync(GeekOpenIddictToken token, CancellationToken cancellationToken) =>
        await _repo.PostAsync(BasePath, token, cancellationToken);

    public async ValueTask DeleteAsync(GeekOpenIddictToken token, CancellationToken cancellationToken) =>
        await _repo.DeleteAsync($"{BasePath}/{token.Id}", cancellationToken);

    public async IAsyncEnumerable<GeekOpenIddictToken> FindAsync(
        string? subject,
        string? client,
        string? status,
        string? type,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var items = await _repo.GetListAsync<GeekOpenIddictToken>($"{BasePath}?offset=0", cancellationToken);
        foreach (var item in items)
        {
            if (Matches(item, subject, client, status, type))
                yield return item;
        }
    }

    public async IAsyncEnumerable<GeekOpenIddictToken> FindByApplicationIdAsync(
        string identifier,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var items = await _repo.GetListAsync<GeekOpenIddictToken>($"{BasePath}?offset=0", cancellationToken);
        foreach (var item in items)
        {
            if (string.Equals(item.ApplicationId, identifier, StringComparison.Ordinal))
                yield return item;
        }
    }

    public async IAsyncEnumerable<GeekOpenIddictToken> FindByAuthorizationIdAsync(
        string identifier,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var items = await _repo.GetListAsync<GeekOpenIddictToken>($"{BasePath}?offset=0", cancellationToken);
        foreach (var item in items)
        {
            if (string.Equals(item.AuthorizationId, identifier, StringComparison.Ordinal))
                yield return item;
        }
    }

    public async ValueTask<GeekOpenIddictToken?> FindByIdAsync(string identifier, CancellationToken cancellationToken) =>
        await _repo.GetAsync<GeekOpenIddictToken>($"{BasePath}/{identifier}", cancellationToken);

    public async ValueTask<GeekOpenIddictToken?> FindByReferenceIdAsync(string identifier, CancellationToken cancellationToken) =>
        await _repo.GetAsync<GeekOpenIddictToken>($"{BasePath}/by-reference-id/{Uri.EscapeDataString(identifier)}", cancellationToken);

    public async IAsyncEnumerable<GeekOpenIddictToken> FindBySubjectAsync(
        string subject,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var items = await _repo.GetListAsync<GeekOpenIddictToken>($"{BasePath}?offset=0", cancellationToken);
        foreach (var item in items)
        {
            if (string.Equals(item.Subject, subject, StringComparison.Ordinal))
                yield return item;
        }
    }

    public ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<GeekOpenIddictToken>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Queryable token lookups are not supported over HTTP stores.");

    public ValueTask<string?> GetApplicationIdAsync(GeekOpenIddictToken token, CancellationToken cancellationToken) =>
        new(token.ApplicationId);

    public ValueTask<string?> GetAuthorizationIdAsync(GeekOpenIddictToken token, CancellationToken cancellationToken) =>
        new(token.AuthorizationId);

    public ValueTask<DateTimeOffset?> GetCreationDateAsync(GeekOpenIddictToken token, CancellationToken cancellationToken) =>
        new(token.CreationDate);

    public ValueTask<DateTimeOffset?> GetExpirationDateAsync(GeekOpenIddictToken token, CancellationToken cancellationToken) =>
        new(token.ExpirationDate);

    public ValueTask<string?> GetIdAsync(GeekOpenIddictToken token, CancellationToken cancellationToken) =>
        new(token.Id);

    public ValueTask<string?> GetPayloadAsync(GeekOpenIddictToken token, CancellationToken cancellationToken) =>
        new(token.Payload);

    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(
        GeekOpenIddictToken token,
        CancellationToken cancellationToken) =>
        new(OpenIddictStoreJson.ReadProperties(token.Properties));

    public ValueTask<DateTimeOffset?> GetRedemptionDateAsync(GeekOpenIddictToken token, CancellationToken cancellationToken) =>
        new(token.RedemptionDate);

    public ValueTask<string?> GetReferenceIdAsync(GeekOpenIddictToken token, CancellationToken cancellationToken) =>
        new(token.ReferenceId);

    public ValueTask<string?> GetStatusAsync(GeekOpenIddictToken token, CancellationToken cancellationToken) =>
        new(token.Status);

    public ValueTask<string?> GetSubjectAsync(GeekOpenIddictToken token, CancellationToken cancellationToken) =>
        new(token.Subject);

    public ValueTask<string?> GetTypeAsync(GeekOpenIddictToken token, CancellationToken cancellationToken) =>
        new(token.Type);

    public ValueTask<GeekOpenIddictToken> InstantiateAsync(CancellationToken cancellationToken) =>
        new(new GeekOpenIddictToken { Id = Guid.NewGuid().ToString("N") });

    public async IAsyncEnumerable<GeekOpenIddictToken> ListAsync(
        int? count,
        int? offset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var path = $"{BasePath}?count={count}&offset={offset ?? 0}";
        var items = await _repo.GetListAsync<GeekOpenIddictToken>(path, cancellationToken);
        foreach (var item in items)
            yield return item;
    }

    public IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<GeekOpenIddictToken>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Queryable token lists are not supported over HTTP stores.");

    public async ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken)
    {
        var authorizations = await _repo.GetListAsync<GeekOpenIddictAuthorization>(
            "repo/openiddict/authorizations?offset=0",
            cancellationToken);
        var authorizationStatuses = authorizations.ToDictionary(static a => a.Id, static a => a.Status, StringComparer.Ordinal);

        var tokens = await _repo.GetListAsync<GeekOpenIddictToken>($"{BasePath}?offset=0", cancellationToken);
        var now = DateTimeOffset.UtcNow;
        long removed = 0;
        foreach (var token in tokens)
        {
            if (token.CreationDate is not null && token.CreationDate >= threshold)
                continue;

            var hasInvalidStatus = !string.Equals(token.Status, OpenIddictConstants.Statuses.Inactive, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(token.Status, OpenIddictConstants.Statuses.Valid, StringComparison.OrdinalIgnoreCase);
            var hasInvalidAuthorization = token.AuthorizationId is not null
                && authorizationStatuses.TryGetValue(token.AuthorizationId, out var authorizationStatus)
                && !string.Equals(authorizationStatus, OpenIddictConstants.Statuses.Valid, StringComparison.OrdinalIgnoreCase);
            var isExpired = token.ExpirationDate is not null && token.ExpirationDate < now;
            if (!hasInvalidStatus && !hasInvalidAuthorization && !isExpired)
                continue;

            await _repo.DeleteAsync($"{BasePath}/{token.Id}", cancellationToken);
            removed++;
        }

        return removed;
    }

    public async ValueTask<long> RevokeAsync(
        string? subject,
        string? client,
        string? status,
        string? type,
        CancellationToken cancellationToken) =>
        await RevokeMatchesAsync(subject, client, status, type, cancellationToken);

    public async ValueTask<long> RevokeByApplicationIdAsync(string identifier, CancellationToken cancellationToken = default) =>
        await RevokeMatchesAsync(subject: null, client: identifier, status: null, type: null, cancellationToken);

    public async ValueTask<long> RevokeByAuthorizationIdAsync(string identifier, CancellationToken cancellationToken) =>
        await RevokeByAuthorizationIdInternalAsync(identifier, cancellationToken);

    public async ValueTask<long> RevokeBySubjectAsync(string subject, CancellationToken cancellationToken = default) =>
        await _repo.PostCountActionAsync($"{BasePath}/revoke-by-subject/{Uri.EscapeDataString(subject)}", cancellationToken);

    public ValueTask SetApplicationIdAsync(GeekOpenIddictToken token, string? identifier, CancellationToken cancellationToken)
    {
        token.ApplicationId = identifier;
        return default;
    }

    public ValueTask SetAuthorizationIdAsync(GeekOpenIddictToken token, string? identifier, CancellationToken cancellationToken)
    {
        token.AuthorizationId = identifier;
        return default;
    }

    public ValueTask SetCreationDateAsync(GeekOpenIddictToken token, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        token.CreationDate = date;
        return default;
    }

    public ValueTask SetExpirationDateAsync(GeekOpenIddictToken token, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        token.ExpirationDate = date;
        return default;
    }

    public ValueTask SetPayloadAsync(GeekOpenIddictToken token, string? payload, CancellationToken cancellationToken)
    {
        token.Payload = payload;
        return default;
    }

    public ValueTask SetPropertiesAsync(
        GeekOpenIddictToken token,
        ImmutableDictionary<string, JsonElement> properties,
        CancellationToken cancellationToken)
    {
        token.Properties = OpenIddictStoreJson.WriteProperties(properties);
        return default;
    }

    public ValueTask SetRedemptionDateAsync(GeekOpenIddictToken token, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        token.RedemptionDate = date;
        return default;
    }

    public ValueTask SetReferenceIdAsync(GeekOpenIddictToken token, string? identifier, CancellationToken cancellationToken)
    {
        token.ReferenceId = identifier;
        return default;
    }

    public ValueTask SetStatusAsync(GeekOpenIddictToken token, string? status, CancellationToken cancellationToken)
    {
        token.Status = status;
        return default;
    }

    public ValueTask SetSubjectAsync(GeekOpenIddictToken token, string? subject, CancellationToken cancellationToken)
    {
        token.Subject = subject;
        return default;
    }

    public ValueTask SetTypeAsync(GeekOpenIddictToken token, string? type, CancellationToken cancellationToken)
    {
        token.Type = type;
        return default;
    }

    public async ValueTask UpdateAsync(GeekOpenIddictToken token, CancellationToken cancellationToken) =>
        await _repo.PutAsync($"{BasePath}/{token.Id}", token, cancellationToken);

    private async ValueTask<long> RevokeMatchesAsync(
        string? subject,
        string? client,
        string? status,
        string? type,
        CancellationToken cancellationToken)
    {
        var items = await _repo.GetListAsync<GeekOpenIddictToken>($"{BasePath}?offset=0", cancellationToken);
        long revoked = 0;
        foreach (var item in items)
        {
            if (!Matches(item, subject, client, status, type))
                continue;
            if (string.Equals(item.Status, OpenIddictConstants.Statuses.Revoked, StringComparison.OrdinalIgnoreCase))
                continue;

            item.Status = OpenIddictConstants.Statuses.Revoked;
            await _repo.PutAsync($"{BasePath}/{item.Id}", item, cancellationToken);
            revoked++;
        }

        return revoked;
    }

    private async ValueTask<long> RevokeByAuthorizationIdInternalAsync(string identifier, CancellationToken cancellationToken)
    {
        var items = await _repo.GetListAsync<GeekOpenIddictToken>($"{BasePath}?offset=0", cancellationToken);
        long revoked = 0;
        foreach (var item in items)
        {
            if (!string.Equals(item.AuthorizationId, identifier, StringComparison.Ordinal))
                continue;
            if (string.Equals(item.Status, OpenIddictConstants.Statuses.Revoked, StringComparison.OrdinalIgnoreCase))
                continue;

            item.Status = OpenIddictConstants.Statuses.Revoked;
            await _repo.PutAsync($"{BasePath}/{item.Id}", item, cancellationToken);
            revoked++;
        }

        return revoked;
    }

    private static bool Matches(
        GeekOpenIddictToken token,
        string? subject,
        string? client,
        string? status,
        string? type)
    {
        if (subject is not null && !string.Equals(token.Subject, subject, StringComparison.Ordinal))
            return false;
        if (client is not null && !string.Equals(token.ApplicationId, client, StringComparison.Ordinal))
            return false;
        if (status is not null && !string.Equals(token.Status, status, StringComparison.OrdinalIgnoreCase))
            return false;
        if (type is not null && !string.Equals(token.Type, type, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }
}
