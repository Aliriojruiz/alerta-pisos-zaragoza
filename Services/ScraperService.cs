using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;
using PisoWatcher.Models;
using PisoWatcher.Options;

namespace PisoWatcher.Services;

public sealed partial class ScraperService(IHtmlFetcher fetcher, IOptions<MonitorOptions> options, ILogger<ScraperService> logger)
{
    private readonly MonitorOptions _options = options.Value;

    /// <summary>Devuelve los inmuebles del portal que NO están marcados como reservados/alquilados.</summary>
    public async Task<ScanResult> GetAvailableListingsAsync(CancellationToken ct)
    {
        var page = await fetcher.GetPageAsync(_options.PortalUrl, ct);

        var doc = new HtmlDocument();
        doc.LoadHtml(page.Html);

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

    /// <summary>
    /// Lee la ficha del piso (dirección, características, contacto). La web solo enseña fichas con la
    /// cookie de sesión del catálogo; si redirige al catálogo, se recarga este y se reintenta una vez.
    /// </summary>
    public async Task<ListingDetails?> GetDetailsAsync(Listing listing, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var page = await fetcher.GetPageAsync(listing.Url, ct);
            if (page.Url.AbsolutePath.TrimEnd('/') == new Uri(listing.Url).AbsolutePath.TrimEnd('/'))
                return ParseDetails(page.Html, page.Url);

            logger.LogDebug("La ficha {Url} redirigió a {Final}; se renueva la sesión", listing.Url, page.Url);
            await fetcher.GetPageAsync(_options.PortalUrl, ct);
        }

        logger.LogWarning("No se pudo abrir la ficha {Url} (la web redirige al catálogo)", listing.Url);
        return null;
    }

    private static ListingDetails ParseDetails(string html, Uri pageUrl)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var root = doc.DocumentNode;

        // Cabecera: 1º calle y planta, 2º precio; aparte, una con los ingresos mínimos.
        var headers = root.SelectNodes("//h4[contains(@class,'sub_benef')]")?.ToList() ?? [];
        var incomeHeader = headers.FirstOrDefault(h => h.InnerText.Contains("Ingresos", StringComparison.OrdinalIgnoreCase));
        if (incomeHeader is not null)
            headers.Remove(incomeHeader);
        var address = CleanText(headers.ElementAtOrDefault(0)?.InnerText);
        var price = CleanText(headers.ElementAtOrDefault(1)?.InnerText);
        var minIncome = incomeHeader?.SelectNodes("./a") is { Count: >= 2 } incomeParts
            ? CleanText(string.Join(' ', incomeParts.Skip(1).Select(a => a.InnerText)))
            : null;

        // Características: cada bloque tiene <p> etiqueta + <p> valor.
        var features = new List<KeyValuePair<string, string>>();
        foreach (var block in root.SelectNodes("//div[contains(@class,'col-10')][count(p)>=2]") ?? Enumerable.Empty<HtmlNode>())
        {
            var label = CleanText(block.SelectSingleNode("./p[1]")?.InnerText);
            var value = CleanText(block.SelectSingleNode("./p[2]")?.InnerText);
            if (label is not null && value is not null && features.All(f => f.Key != label))
                features.Add(new(label, SquareMeters().Replace(value, "m²")));
        }

        // Contacto del pie de página (Zaragoza Vivienda; la web no da un teléfono por piso).
        var footerText = CleanText(root.InnerText) ?? "";
        var phone = PhoneAfterLabel().Match(footerText) is { Success: true } m ? m.Groups[1].Value.Trim() : null;
        var email = root.SelectSingleNode("//a[starts-with(@href,'mailto:')]")?.GetAttributeValue("href", "")["mailto:".Length..];

        var planUrl = root.SelectSingleNode("//*[contains(@onclick,'descarga-plano')]") is not null
            ? new Uri(pageUrl, pageUrl.AbsolutePath.TrimEnd('/') + "/descarga-plano").ToString()
            : null;

        return new ListingDetails(address, price, minIncome, features, phone, email, planUrl);
    }

    [GeneratedRegex(@"\bm\s?2\b")]
    private static partial Regex SquareMeters();

    [GeneratedRegex(@"Teléfono\s*([+\d][\d\s]{7,15}\d)")]
    private static partial Regex PhoneAfterLabel();

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
