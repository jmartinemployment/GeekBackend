using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using GeekApplication.Entities.OpenIddict;
using OpenIddict.Abstractions;

namespace GeekAPI.HttpClients.OpenIddict;

public sealed class HttpScopeStore : IOpenIddictScopeStore<GeekOpenIddictScope>
{
    private const string BasePath = "repo/openiddict/scopes";
    private readonly OpenIddictRepoClient _repo;

    public HttpScopeStore(IHttpClientFactory factory) => _repo = new OpenIddictRepoClient(factory);

    public async ValueTask<long> CountAsync(CancellationToken cancellationToken) =>
        await _repo.GetCountAsync($"{BasePath}/count", cancellationToken);

    public ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<GeekOpenIddictScope>, IQueryable<TResult>> query,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Queryable scope counts are not supported over HTTP stores.");

    public async ValueTask CreateAsync(GeekOpenIddictScope scope, CancellationToken cancellationToken) =>
        await _repo.PostAsync(BasePath, scope, cancellationToken);

    public async ValueTask DeleteAsync(GeekOpenIddictScope scope, CancellationToken cancellationToken) =>
        await _repo.DeleteAsync($"{BasePath}/{scope.Id}", cancellationToken);

    public async ValueTask<GeekOpenIddictScope?> FindByIdAsync(string identifier, CancellationToken cancellationToken) =>
        await _repo.GetAsync<GeekOpenIddictScope>($"{BasePath}/{identifier}", cancellationToken);

    public async ValueTask<GeekOpenIddictScope?> FindByNameAsync(string name, CancellationToken cancellationToken) =>
        await _repo.GetAsync<GeekOpenIddictScope>($"{BasePath}/by-name/{Uri.EscapeDataString(name)}", cancellationToken);

    public async IAsyncEnumerable<GeekOpenIddictScope> FindByNamesAsync(
        ImmutableArray<string> names,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var name in names)
        {
            var scope = await FindByNameAsync(name, cancellationToken);
            if (scope is not null)
                yield return scope;
        }
    }

    public async IAsyncEnumerable<GeekOpenIddictScope> FindByResourceAsync(
        string resource,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var items = await _repo.GetListAsync<GeekOpenIddictScope>($"{BasePath}?offset=0", cancellationToken);
        foreach (var item in items)
        {
            var resources = OpenIddictStoreJson.ReadStringArray(item.Resources);
            if (resources.Contains(resource, StringComparer.Ordinal))
                yield return item;
        }
    }

    public ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<GeekOpenIddictScope>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Queryable scope lookups are not supported over HTTP stores.");

    public ValueTask<string?> GetDescriptionAsync(GeekOpenIddictScope scope, CancellationToken cancellationToken) =>
        new(scope.Description);

    public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDescriptionsAsync(
        GeekOpenIddictScope scope,
        CancellationToken cancellationToken) =>
        new(OpenIddictStoreJson.ReadCultureDictionary(scope.Descriptions));

    public ValueTask<string?> GetDisplayNameAsync(GeekOpenIddictScope scope, CancellationToken cancellationToken) =>
        new(scope.DisplayName);

    public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(
        GeekOpenIddictScope scope,
        CancellationToken cancellationToken) =>
        new(OpenIddictStoreJson.ReadCultureDictionary(scope.DisplayNames));

    public ValueTask<string?> GetIdAsync(GeekOpenIddictScope scope, CancellationToken cancellationToken) =>
        new(scope.Id);

    public ValueTask<string?> GetNameAsync(GeekOpenIddictScope scope, CancellationToken cancellationToken) =>
        new(scope.Name);

    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(
        GeekOpenIddictScope scope,
        CancellationToken cancellationToken) =>
        new(OpenIddictStoreJson.ReadProperties(scope.Properties));

    public ValueTask<ImmutableArray<string>> GetResourcesAsync(GeekOpenIddictScope scope, CancellationToken cancellationToken) =>
        new(OpenIddictStoreJson.ReadStringArray(scope.Resources));

    public ValueTask<GeekOpenIddictScope> InstantiateAsync(CancellationToken cancellationToken) =>
        new(new GeekOpenIddictScope { Id = Guid.NewGuid().ToString("N") });

    public async IAsyncEnumerable<GeekOpenIddictScope> ListAsync(
        int? count,
        int? offset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var path = $"{BasePath}?count={count}&offset={offset ?? 0}";
        var items = await _repo.GetListAsync<GeekOpenIddictScope>(path, cancellationToken);
        foreach (var item in items)
            yield return item;
    }

    public IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<GeekOpenIddictScope>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Queryable scope lists are not supported over HTTP stores.");

    public ValueTask SetDescriptionAsync(GeekOpenIddictScope scope, string? description, CancellationToken cancellationToken)
    {
        scope.Description = description;
        return default;
    }

    public ValueTask SetDescriptionsAsync(
        GeekOpenIddictScope scope,
        ImmutableDictionary<CultureInfo, string> descriptions,
        CancellationToken cancellationToken)
    {
        scope.Descriptions = OpenIddictStoreJson.WriteCultureDictionary(descriptions);
        return default;
    }

    public ValueTask SetDisplayNameAsync(GeekOpenIddictScope scope, string? name, CancellationToken cancellationToken)
    {
        scope.DisplayName = name;
        return default;
    }

    public ValueTask SetDisplayNamesAsync(
        GeekOpenIddictScope scope,
        ImmutableDictionary<CultureInfo, string> names,
        CancellationToken cancellationToken)
    {
        scope.DisplayNames = OpenIddictStoreJson.WriteCultureDictionary(names);
        return default;
    }

    public ValueTask SetNameAsync(GeekOpenIddictScope scope, string? name, CancellationToken cancellationToken)
    {
        scope.Name = name;
        return default;
    }

    public ValueTask SetPropertiesAsync(
        GeekOpenIddictScope scope,
        ImmutableDictionary<string, JsonElement> properties,
        CancellationToken cancellationToken)
    {
        scope.Properties = OpenIddictStoreJson.WriteProperties(properties);
        return default;
    }

    public ValueTask SetResourcesAsync(GeekOpenIddictScope scope, ImmutableArray<string> resources, CancellationToken cancellationToken)
    {
        scope.Resources = OpenIddictStoreJson.WriteStringArray(resources);
        return default;
    }

    public async ValueTask UpdateAsync(GeekOpenIddictScope scope, CancellationToken cancellationToken) =>
        await _repo.PutAsync($"{BasePath}/{scope.Id}", scope, cancellationToken);
}
