namespace PisoWatcher.Services;

/// <summary>Descarga el HTML estático con HttpClient (rápido, sin ejecutar JavaScript).</summary>
public sealed class HttpHtmlFetcher(HttpClient http) : IHtmlFetcher
{
    public async Task<FetchedPage> GetPageAsync(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(ct);
        return new FetchedPage(html, response.RequestMessage?.RequestUri ?? new Uri(url));
    }
}
