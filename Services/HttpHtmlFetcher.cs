namespace PisoWatcher.Services;

/// <summary>Descarga el HTML estático con HttpClient (rápido, sin ejecutar JavaScript).</summary>
public sealed class HttpHtmlFetcher(HttpClient http) : IHtmlFetcher
{
    public async Task<string> GetHtmlAsync(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
