using System.Net.Http.Json;

namespace GeekAPI.HttpClients.OpenIddict;

internal sealed class OpenIddictRepoClient
{
    private readonly HttpClient _http;

    public OpenIddictRepoClient(IHttpClientFactory factory) =>
        _http = factory.CreateClient("GeekRepository");

    public async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        var response = await _http.GetAsync(path, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return default;
        response.EnsureSuccessStatusCode();
        var wrapper = await response.Content.ReadFromJsonAsync<ApiWrapper<T>>(cancellationToken);
        return wrapper?.Data;
    }

    public async Task<IReadOnlyList<T>> GetListAsync<T>(string path, CancellationToken cancellationToken)
    {
        var response = await _http.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        var wrapper = await response.Content.ReadFromJsonAsync<ApiWrapper<IReadOnlyList<T>>>(cancellationToken);
        return wrapper?.Data ?? [];
    }

    public async Task<T> PostAsync<T>(string path, T body, CancellationToken cancellationToken)
    {
        var response = await _http.PostAsJsonAsync(path, body, cancellationToken);
        response.EnsureSuccessStatusCode();
        var wrapper = await response.Content.ReadFromJsonAsync<ApiWrapper<T>>(cancellationToken)
            ?? throw new InvalidOperationException("Empty repository response.");
        return wrapper.Data!;
    }

    public async Task<T> PutAsync<T>(string path, T body, CancellationToken cancellationToken)
    {
        var response = await _http.PutAsJsonAsync(path, body, cancellationToken);
        response.EnsureSuccessStatusCode();
        var wrapper = await response.Content.ReadFromJsonAsync<ApiWrapper<T>>(cancellationToken)
            ?? throw new InvalidOperationException("Empty repository response.");
        return wrapper.Data!;
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        var response = await _http.DeleteAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<long> GetCountAsync(string path, CancellationToken cancellationToken)
    {
        var response = await _http.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        var wrapper = await response.Content.ReadFromJsonAsync<ApiWrapper<int>>(cancellationToken);
        return wrapper?.Data ?? 0;
    }

    public async Task<long> PostCountActionAsync(string path, CancellationToken cancellationToken)
    {
        var response = await _http.PostAsync(path, null, cancellationToken);
        response.EnsureSuccessStatusCode();
        var wrapper = await response.Content.ReadFromJsonAsync<ApiWrapper<int>>(cancellationToken);
        return wrapper?.Data ?? 0;
    }

    private sealed class ApiWrapper<T>
    {
        public bool Success { get; init; }
        public T? Data { get; init; }
    }
}
