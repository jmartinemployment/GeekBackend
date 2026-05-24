using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using GeekApplication.Entities.OpenIddict;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;

namespace GeekAPI.HttpClients.OpenIddict;

public sealed class HttpApplicationStore : IOpenIddictApplicationStore<GeekOpenIddictApplication>
{
    private readonly OpenIddictRepoClient _repo;

    public HttpApplicationStore(IHttpClientFactory factory) => _repo = new OpenIddictRepoClient(factory);

    public async ValueTask<long> CountAsync(CancellationToken cancellationToken) =>
        await _repo.GetCountAsync("repo/openiddict/applications/count", cancellationToken);

    public ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<GeekOpenIddictApplication>, IQueryable<TResult>> query,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Queryable application counts are not supported over HTTP stores.");

    public async ValueTask CreateAsync(GeekOpenIddictApplication application, CancellationToken cancellationToken) =>
        await _repo.PostAsync("repo/openiddict/applications", application, cancellationToken);

    public async ValueTask DeleteAsync(GeekOpenIddictApplication application, CancellationToken cancellationToken) =>
        await _repo.DeleteAsync($"repo/openiddict/applications/{application.Id}", cancellationToken);

    public async ValueTask<GeekOpenIddictApplication?> FindByIdAsync(string identifier, CancellationToken cancellationToken) =>
        await _repo.GetAsync<GeekOpenIddictApplication>($"repo/openiddict/applications/{identifier}", cancellationToken);

    public async ValueTask<GeekOpenIddictApplication?> FindByClientIdAsync(string identifier, CancellationToken cancellationToken) =>
        await _repo.GetAsync<GeekOpenIddictApplication>(
            $"repo/openiddict/applications/by-client-id/{Uri.EscapeDataString(identifier)}", cancellationToken);

    public async IAsyncEnumerable<GeekOpenIddictApplication> FindByPostLogoutRedirectUriAsync(
        string uri,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var items = await _repo.GetListAsync<GeekOpenIddictApplication>(
            $"repo/openiddict/applications/by-post-logout-redirect-uri?uri={Uri.EscapeDataString(uri)}",
            cancellationToken);
        foreach (var item in items)
            yield return item;
    }

    public async IAsyncEnumerable<GeekOpenIddictApplication> FindByRedirectUriAsync(
        string uri,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var items = await _repo.GetListAsync<GeekOpenIddictApplication>(
            $"repo/openiddict/applications/by-redirect-uri?uri={Uri.EscapeDataString(uri)}",
            cancellationToken);
        foreach (var item in items)
            yield return item;
    }

    public ValueTask<string?> GetApplicationTypeAsync(GeekOpenIddictApplication application, CancellationToken cancellationToken) =>
        new(application.ApplicationType);

    public ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<GeekOpenIddictApplication>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Queryable application lookups are not supported over HTTP stores.");

    public ValueTask<string?> GetClientIdAsync(GeekOpenIddictApplication application, CancellationToken cancellationToken) =>
        new(application.ClientId);

    public ValueTask<string?> GetClientSecretAsync(GeekOpenIddictApplication application, CancellationToken cancellationToken) =>
        new(application.ClientSecret);

    public ValueTask<string?> GetClientTypeAsync(GeekOpenIddictApplication application, CancellationToken cancellationToken) =>
        new(application.ClientType);

    public ValueTask<string?> GetConsentTypeAsync(GeekOpenIddictApplication application, CancellationToken cancellationToken) =>
        new(application.ConsentType);

    public ValueTask<string?> GetDisplayNameAsync(GeekOpenIddictApplication application, CancellationToken cancellationToken) =>
        new(application.DisplayName);

    public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(
        GeekOpenIddictApplication application,
        CancellationToken cancellationToken) =>
        new(OpenIddictStoreJson.ReadCultureDictionary(application.DisplayNames));

    public ValueTask<string?> GetIdAsync(GeekOpenIddictApplication application, CancellationToken cancellationToken) =>
        new(application.Id);

    public ValueTask<JsonWebKeySet?> GetJsonWebKeySetAsync(GeekOpenIddictApplication application, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(application.JsonWebKeySet))
            return new((JsonWebKeySet?)null);
        return new(JsonWebKeySet.Create(application.JsonWebKeySet));
    }

    public ValueTask<ImmutableArray<string>> GetPermissionsAsync(GeekOpenIddictApplication application, CancellationToken cancellationToken) =>
        new(OpenIddictStoreJson.ReadStringArray(application.Permissions));

    public ValueTask<ImmutableArray<string>> GetPostLogoutRedirectUrisAsync(GeekOpenIddictApplication application, CancellationToken cancellationToken) =>
        new(OpenIddictStoreJson.ReadStringArray(application.PostLogoutRedirectUris));

    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(
        GeekOpenIddictApplication application,
        CancellationToken cancellationToken) =>
        new(OpenIddictStoreJson.ReadProperties(application.Properties));

    public ValueTask<ImmutableArray<string>> GetRedirectUrisAsync(GeekOpenIddictApplication application, CancellationToken cancellationToken) =>
        new(OpenIddictStoreJson.ReadStringArray(application.RedirectUris));

    public ValueTask<ImmutableArray<string>> GetRequirementsAsync(GeekOpenIddictApplication application, CancellationToken cancellationToken) =>
        new(OpenIddictStoreJson.ReadStringArray(application.Requirements));

    public ValueTask<ImmutableDictionary<string, string>> GetSettingsAsync(GeekOpenIddictApplication application, CancellationToken cancellationToken) =>
        new(OpenIddictStoreJson.ReadDictionary(application.Settings));

    public ValueTask<GeekOpenIddictApplication> InstantiateAsync(CancellationToken cancellationToken) =>
        new(new GeekOpenIddictApplication { Id = Guid.NewGuid().ToString("N") });

    public async IAsyncEnumerable<GeekOpenIddictApplication> ListAsync(
        int? count,
        int? offset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var path = $"repo/openiddict/applications?count={count}&offset={offset ?? 0}";
        var items = await _repo.GetListAsync<GeekOpenIddictApplication>(path, cancellationToken);
        foreach (var item in items)
            yield return item;
    }

    public IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<GeekOpenIddictApplication>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Queryable application lists are not supported over HTTP stores.");

    public ValueTask SetApplicationTypeAsync(GeekOpenIddictApplication application, string? type, CancellationToken cancellationToken)
    {
        application.ApplicationType = type;
        return default;
    }

    public ValueTask SetClientIdAsync(GeekOpenIddictApplication application, string? identifier, CancellationToken cancellationToken)
    {
        application.ClientId = identifier;
        return default;
    }

    public ValueTask SetClientSecretAsync(GeekOpenIddictApplication application, string? secret, CancellationToken cancellationToken)
    {
        application.ClientSecret = secret;
        return default;
    }

    public ValueTask SetClientTypeAsync(GeekOpenIddictApplication application, string? type, CancellationToken cancellationToken)
    {
        application.ClientType = type;
        return default;
    }

    public ValueTask SetConsentTypeAsync(GeekOpenIddictApplication application, string? type, CancellationToken cancellationToken)
    {
        application.ConsentType = type;
        return default;
    }

    public ValueTask SetDisplayNameAsync(GeekOpenIddictApplication application, string? name, CancellationToken cancellationToken)
    {
        application.DisplayName = name;
        return default;
    }

    public ValueTask SetDisplayNamesAsync(
        GeekOpenIddictApplication application,
        ImmutableDictionary<CultureInfo, string> names,
        CancellationToken cancellationToken)
    {
        application.DisplayNames = OpenIddictStoreJson.WriteCultureDictionary(names);
        return default;
    }

    public ValueTask SetJsonWebKeySetAsync(GeekOpenIddictApplication application, JsonWebKeySet? set, CancellationToken cancellationToken)
    {
        application.JsonWebKeySet = set?.ToString();
        return default;
    }

    public ValueTask SetPermissionsAsync(GeekOpenIddictApplication application, ImmutableArray<string> permissions, CancellationToken cancellationToken)
    {
        application.Permissions = OpenIddictStoreJson.WriteStringArray(permissions);
        return default;
    }

    public ValueTask SetPostLogoutRedirectUrisAsync(GeekOpenIddictApplication application, ImmutableArray<string> uris, CancellationToken cancellationToken)
    {
        application.PostLogoutRedirectUris = OpenIddictStoreJson.WriteStringArray(uris);
        return default;
    }

    public ValueTask SetPropertiesAsync(
        GeekOpenIddictApplication application,
        ImmutableDictionary<string, JsonElement> properties,
        CancellationToken cancellationToken)
    {
        application.Properties = OpenIddictStoreJson.WriteProperties(properties);
        return default;
    }

    public ValueTask SetRedirectUrisAsync(GeekOpenIddictApplication application, ImmutableArray<string> uris, CancellationToken cancellationToken)
    {
        application.RedirectUris = OpenIddictStoreJson.WriteStringArray(uris);
        return default;
    }

    public ValueTask SetRequirementsAsync(GeekOpenIddictApplication application, ImmutableArray<string> requirements, CancellationToken cancellationToken)
    {
        application.Requirements = OpenIddictStoreJson.WriteStringArray(requirements);
        return default;
    }

    public ValueTask SetSettingsAsync(
        GeekOpenIddictApplication application,
        ImmutableDictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        application.Settings = OpenIddictStoreJson.WriteDictionary(settings);
        return default;
    }

    public async ValueTask UpdateAsync(GeekOpenIddictApplication application, CancellationToken cancellationToken) =>
        await _repo.PutAsync($"repo/openiddict/applications/{application.Id}", application, cancellationToken);
}
