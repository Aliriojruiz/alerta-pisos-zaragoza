using System.Globalization;
using System.Net;
using System.Text;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;
using PisoWatcher.Models;
using PisoWatcher.Options;

namespace PisoWatcher.Services;

public sealed class ScraperService(IHtmlFetcher fetcher, IOptions<MonitorOptions> options, ILogger<ScraperService> logger)
{
    private readonly MonitorOptions _options = options.Value;

    /// <summary>Devuelve los inmuebles del portal que NO están marcados como reservados/alquilados.</summary>
    public async Task<ScanResult> GetAvailableListingsAsync(CancellationToken ct)
    {
        var html = await fetcher.GetHtmlAsync(_options.PortalUrl, ct);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var cards = doc.DocumentNode.SelectNodes(_options.ListingXPath);
        if (cards is null || cards.Count == 0)
        {
            logger.LogWarning("No se encontró ningún inmueble con el XPath '{XPath}'. ¿Ha cambiado la web?", _options.ListingXPath);
            return new ScanResult(0, []);
        }

        var baseUri = new Uri(_options.PortalUrl);
        var excluded = _options.ExcludedKeywords.Select(Normalize).ToArray();
        var available = new List<Listing>();
        var discarded = 0;

        foreach (var card in cards)
        {
            if (IsUnavailable(card, excluded))
            {
                discarded++;
                continue;
            }

            var listing = ParseListing(card, baseUri);
            if (listing is not null)
            {
                available.Add(listing);
                logger.LogInformation("Disponible: {Title} | {Price} | {Url}", listing.Title, listing.Price, listing.Url);
            }
        }

        logger.LogInformation("Encontrados {Total} inmuebles: {Available} disponibles, {Discarded} reservados/alquilados",
            cards.Count, available.Count, discarded);

        // Una tarjeta puede repetir enlaces al mismo piso: deduplicamos por Id.
        return new ScanResult(cards.Count, available.DistinctBy(l => l.Id).ToList());
    }

    private Listing? ParseListing(HtmlNode card, Uri baseUri)
    {
        var href = card.SelectSingleNode(_options.LinkXPath)?.GetAttributeValue("href", null);
        if (string.IsNullOrWhiteSpace(href) || href.StartsWith('#') || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug("Tarjeta sin enlace válido, se ignora");
            return null;
        }

        var url = new Uri(baseUri, WebUtility.HtmlDecode(href)).GetLeftPart(UriPartial.Query);
        var title = CleanText(SelectText(card, _options.TitleXPath)) ?? url;
        var price = CleanText(SelectText(card, _options.PriceXPath));

        return new Listing(url, url, title, price);
    }

    /// <summary>
    /// Un inmueble se descarta si alguna palabra prohibida aparece en su texto visible
    /// o en atributos típicos de etiquetas/badges (class, alt, title, aria-label, data-*).
    /// </summary>
    private static bool IsUnavailable(HtmlNode card, string[] excludedKeywords)
    {
        var haystack = new StringBuilder(WebUtility.HtmlDecode(card.InnerText));

        foreach (var node in card.DescendantsAndSelf())
        {
            foreach (var attr in node.Attributes)
            {
                if (attr.Name is "class" or "alt" or "title" or "aria-label" || attr.Name.StartsWith("data-"))
                    haystack.Append(' ').Append(attr.Value);
            }
        }

        var text = Normalize(haystack.ToString());
        return excludedKeywords.Any(k => text.Contains(k, StringComparison.Ordinal));
    }

    private static string? SelectText(HtmlNode card, string? xpath) =>
        string.IsNullOrWhiteSpace(xpath) ? null : card.SelectSingleNode(xpath)?.InnerText;

    private static string? CleanText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var decoded = WebUtility.HtmlDecode(text);
        return string.Join(' ', decoded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Minúsculas y sin tildes, para que "RESERVADO" o "Reservádo" coincidan igual.</summary>
    private static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
