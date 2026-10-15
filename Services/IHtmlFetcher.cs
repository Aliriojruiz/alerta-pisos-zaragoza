namespace PisoWatcher.Services;

public interface IHtmlFetcher
{
    /// <summary>Descarga una página. <see cref="FetchedPage.Url"/> es la URL final, tras redirecciones.</summary>
    Task<FetchedPage> GetPageAsync(string url, CancellationToken ct);
}

public sealed record FetchedPage(string Html, Uri Url);

internal static class HttpDefaults
{
    public const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";
}
