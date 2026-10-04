namespace PisoWatcher.Services;

public interface IHtmlFetcher
{
    Task<string> GetHtmlAsync(string url, CancellationToken ct);
}

internal static class HttpDefaults
{
    public const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";
}
