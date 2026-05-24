using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using GeekApplication.Entities.OpenIddict;
using OpenIddict.Abstractions;

namespace GeekAPI.HttpClients.OpenIddict;

public sealed class HttpAuthorizationStore : IOpenIddictAuthorizationStore<GeekOpenIddictAuthorization>
{
    private const string BasePath = "repo/openiddict/authorizations";
    private readonly OpenIddictRepoClient _repo;

    public HttpAuthorizationStore(IHttpClientFactory factory) => _repo = new OpenIddictRepoClient(factory);

    public async ValueTask<long> CountAsync(CancellationToken cancellationToken) =>
        await _repo.GetCountAsync($"{BasePath}/count", cancellationToken);

    public ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<GeekOpenIddictAuthorization>, IQueryable<TResult>> query,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Queryable authorization counts are not supported over HTTP stores.");

    public async ValueTask CreateAsync(GeekOpenIddictAuthorization authorization, CancellationToken cancellationToken) =>
        await _repo.PostAsync(BasePath, authorization, cancellationToken);

    public async ValueTask DeleteAsync(GeekOpenIddictAuthorization authorization, CancellationToken cancellationToken) =>
        await _repo.DeleteAsync($"{BasePath}/{authorization.Id}", cancellationToken);

    public async IAsyncEnumerable<GeekOpenIddictAuthorization> FindAsync(
        string? subject,
        string? client,
        string? status,
        string? type,
        ImmutableArray<string>? scopes,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var items = await _repo.GetListAsync<GeekOpenIddictAuthorization>($"{BasePath}?offset=0", cancellationToken);
        foreach (var item in items)
        {
            if (!Matches(item, subject, client, status, type, scopes))
                continue;
            yield return item;
        }
    }

    public async IAsyncEnumerable<GeekOpenIddictAuthorization> FindByApplicationIdAsync(
        string identifier,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var items = await _repo.GetListAsync<GeekOpenIddictAuthorization>($"{BasePath}?offset=0", cancellationToken);
        foreach (var item in items)
        {
            if (string.Equals(item.ApplicationId, identifier, StringComparison.Ordinal))
                yield return item;
        }
    }

    public async ValueTask<GeekOpenIddictAuthorization?> FindByIdAsync(string identifier, CancellationToken cancellationToken) =>
        await _repo.GetAsync<GeekOpenIddictAuthorization>($"{BasePath}/{identifier}", cancellationToken);

    public async IAsyncEnumerable<GeekOpenIddictAuthorization> FindBySubjectAsync(
        string subject,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var items = await _repo.GetListAsync<GeekOpenIddictAuthorization>($"{BasePath}?offset=0", cancellationToken);
        foreach (var item in items)
        {
            if (string.Equals(item.Subject, subject, StringComparison.Ordinal))
                yield return item;
        }
    }

    public ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<GeekOpenIddictAuthorization>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Queryable authorization lookups are not supported over HTTP stores.");

    public ValueTask<string?> GetApplicationIdAsync(GeekOpenIddictAuthorization authorization, CancellationToken cancellationToken) =>
        new(authorization.ApplicationId);

    public ValueTask<DateTimeOffset?> GetCreationDateAsync(GeekOpenIddictAuthorization authorization, CancellationToken cancellationToken) =>
        new(authorization.CreationDate);

    public ValueTask<string?> GetIdAsync(GeekOpenIddictAuthorization authorization, CancellationToken cancellationToken) =>
        new(authorization.Id);

    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(
        GeekOpenIddictAuthorization authorization,
        CancellationToken cancellationToken) =>
        new(OpenIddictStoreJson.ReadProperties(authorization.Properties));

    public ValueTask<ImmutableArray<string>> GetScopesAsync(GeekOpenIddictAuthorization authorization, CancellationToken cancellationToken) =>
        new(OpenIddictStoreJson.ReadStringArray(authorization.Scopes));

    public ValueTask<string?> GetStatusAsync(GeekOpenIddictAuthorization authorization, CancellationToken cancellationToken) =>
        new(authorization.Status);

    public ValueTask<string?> GetSubjectAsync(GeekOpenIddictAuthorization authorization, CancellationToken cancellationToken) =>
        new(authorization.Subject);

    public ValueTask<string?> GetTypeAsync(GeekOpenIddictAuthorization authorization, CancellationToken cancellationToken) =>
        new(authorization.Type);

    public ValueTask<GeekOpenIddictAuthorization> InstantiateAsync(CancellationToken cancellationToken) =>
        new(new GeekOpenIddictAuthorization { Id = Guid.NewGuid().ToString("N") });

    public async IAsyncEnumerable<GeekOpenIddictAuthorization> ListAsync(
        int? count,
        int? offset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var path = $"{BasePath}?count={count}&offset={offset ?? 0}";
        var items = await _repo.GetListAsync<GeekOpenIddictAuthorization>(path, cancellationToken);
        foreach (var item in items)
            yield return item;
    }

    public IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<GeekOpenIddictAuthorization>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Queryable authorization lists are not supported over HTTP stores.");

    public async ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken)
    {
        var tokens = await _repo.GetListAsync<GeekOpenIddictToken>("repo/openiddict/tokens?offset=0", cancellationToken);
        var tokenAuthorizationIds = tokens
            .Select(static t => t.AuthorizationId)
            .Where(static id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.Ordinal);

        var authorizations = await _repo.GetListAsync<GeekOpenIddictAuthorization>($"{BasePath}?offset=0", cancellationToken);
        long removed = 0;
        foreach (var authorization in authorizations)
        {
            var isNonValid = !string.Equals(authorization.Status, OpenIddictConstants.Statuses.Valid, StringComparison.OrdinalIgnoreCase);
            var isAdHoc = string.Equals(authorization.Type, OpenIddictConstants.AuthorizationTypes.AdHoc, StringComparison.OrdinalIgnoreCase);
            if (!isNonValid && !isAdHoc)
                continue;
            if (authorization.CreationDate is not null && authorization.CreationDate >= threshold)
                continue;
            if (tokenAuthorizationIds.Contains(authorization.Id))
                continue;

            await _repo.DeleteAsync($"{BasePath}/{authorization.Id}", cancellationToken);
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

    public async ValueTask<long> RevokeByApplicationIdAsync(string identifier, CancellationToken cancellationToken) =>
        await RevokeMatchesAsync(subject: null, client: identifier, status: null, type: null, cancellationToken);

    public async ValueTask<long> RevokeBySubjectAsync(string subject, CancellationToken cancellationToken) =>
        await RevokeMatchesAsync(subject, client: null, status: null, type: null, cancellationToken);

    public ValueTask SetApplicationIdAsync(GeekOpenIddictAuthorization authorization, string? identifier, CancellationToken cancellationToken)
    {
        authorization.ApplicationId = identifier;
        return default;
    }

    public ValueTask SetCreationDateAsync(GeekOpenIddictAuthorization authorization, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        authorization.CreationDate = date;
        return default;
    }

    public ValueTask SetPropertiesAsync(
        GeekOpenIddictAuthorization authorization,
        ImmutableDictionary<string, JsonElement> properties,
        CancellationToken cancellationToken)
    {
        authorization.Properties = OpenIddictStoreJson.WriteProperties(properties);
        return default;
    }

    public ValueTask SetScopesAsync(GeekOpenIddictAuthorization authorization, ImmutableArray<string> scopes, CancellationToken cancellationToken)
    {
        authorization.Scopes = OpenIddictStoreJson.WriteStringArray(scopes);
        return default;
    }

    public ValueTask SetStatusAsync(GeekOpenIddictAuthorization authorization, string? status, CancellationToken cancellationToken)
    {
        authorization.Status = status;
        return default;
    }

    public ValueTask SetSubjectAsync(GeekOpenIddictAuthorization authorization, string? subject, CancellationToken cancellationToken)
    {
        authorization.Subject = subject;
        return default;
    }

    public ValueTask SetTypeAsync(GeekOpenIddictAuthorization authorization, string? type, CancellationToken cancellationToken)
    {
        authorization.Type = type;
        return default;
    }

    public async ValueTask UpdateAsync(GeekOpenIddictAuthorization authorization, CancellationToken cancellationToken) =>
        await _repo.PutAsync($"{BasePath}/{authorization.Id}", authorization, cancellationToken);

    private async ValueTask<long> RevokeMatchesAsync(
        string? subject,
        string? client,
        string? status,
        string? type,
        CancellationToken cancellationToken)
    {
        var items = await _repo.GetListAsync<GeekOpenIddictAuthorization>($"{BasePath}?offset=0", cancellationToken);
        long revoked = 0;
        foreach (var item in items)
        {
            if (!Matches(item, subject, client, status, type, scopes: null))
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
        GeekOpenIddictAuthorization authorization,
        string? subject,
        string? client,
        string? status,
        string? type,
        ImmutableArray<string>? scopes)
    {
        if (subject is not null && !string.Equals(authorization.Subject, subject, StringComparison.Ordinal))
            return false;
        if (client is not null && !string.Equals(authorization.ApplicationId, client, StringComparison.Ordinal))
            return false;
        if (status is not null && !string.Equals(authorization.Status, status, StringComparison.OrdinalIgnoreCase))
            return false;
        if (type is not null && !string.Equals(authorization.Type, type, StringComparison.OrdinalIgnoreCase))
            return false;
        if (scopes is { IsDefault: false })
        {
            var authorizationScopes = OpenIddictStoreJson.ReadStringArray(authorization.Scopes);
            foreach (var scope in scopes.Value)
            {
                if (!authorizationScopes.Contains(scope, StringComparer.Ordinal))
                    return false;
            }
        }

        return true;
    }
}
