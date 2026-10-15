using System.Text;
using Microsoft.Extensions.Options;
using PisoWatcher.Models;
using PisoWatcher.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace PisoWatcher.Services;

public sealed class TelegramNotifier(ITelegramBotClient bot, IOptions<TelegramOptions> options, ILogger<TelegramNotifier> logger)
{
    // Admite también varios IDs separados por comas en una sola entrada (cómodo como secreto de GitHub).
    public IReadOnlyList<string> ChatIds { get; } = options.Value.ChatIds
        .SelectMany(id => id.Split(',', StringSplitOptions.RemoveEmptyEntries))
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Select(id => id.Trim())
        .Distinct()
        .ToArray();

    // Ya se muestran en la cabecera del mensaje; no se repiten en la lista de detalles.
    private static readonly string[] HeaderFeatures = ["Distrito", "Precio de la comunidad"];

    public async Task SendListingAsync(string chatId, Listing listing, ListingDetails? details, CancellationToken ct)
    {
        var text = BuildListingMessage(listing, details);
        await bot.SendMessage(chatId, text, parseMode: ParseMode.Html,
            linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true }, cancellationToken: ct);
        logger.LogInformation("Notificado a {ChatId}: {Url}", chatId, listing.Url);
    }

    public static string BuildListingMessage(Listing listing, ListingDetails? details)
    {
        // Telegram solo necesita escapar estos caracteres; HtmlEncode convertiría tildes en entidades.
        static string E(string? s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        var sb = new StringBuilder("🏠 <b>¡Piso disponible!</b>\n\n");

        if (details is null)
        {
            // Sin ficha: aviso básico con lo que se ve en el catálogo.
            sb.Append(E(listing.Title)).Append('\n');
            if (listing.Price is not null)
                sb.Append("💶 ").Append(E(listing.Price)).Append('\n');
            sb.Append($"\n🔗 <a href=\"{E(listing.Url)}\">Ver anuncio</a>");
            return sb.ToString();
        }

        var address = details.Address ?? listing.Title;
        var district = details.Feature("Distrito");
        sb.Append("📍 <b>").Append(E(address)).Append("</b>");
        if (district is not null)
            sb.Append(" (").Append(E(district)).Append(')');
        sb.Append('\n');

        // Para el mapa sobra la planta: "SAN RAFAEL, planta 3" -> "SAN RAFAEL, Zaragoza".
        var street = address.Split(", planta", StringSplitOptions.TrimEntries)[0];
        var mapUrl = "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString($"{street}, Zaragoza");
        sb.Append($"🗺 <a href=\"{E(mapUrl)}\">Ver en el mapa</a>\n\n");

        var price = details.Price ?? listing.Price ?? "precio no indicado";
        if (details.Feature("Precio de la comunidad") is { } community)
            price = price.Replace("+ comunidad", "", StringComparison.OrdinalIgnoreCase).Trim() + $" + {community} de comunidad";
        sb.Append("💶 ").Append(E(price));
        sb.Append('\n');
        if (details.MinIncome is not null)
            sb.Append("💰 Ingresos anuales mínimos: ").Append(E(details.MinIncome)).Append('\n');

        var rest = details.Features.Where(f => !HeaderFeatures.Contains(f.Key, StringComparer.OrdinalIgnoreCase)).ToList();
        if (rest.Count > 0)
        {
            sb.Append("\n📋 <b>Detalles</b>\n");
            foreach (var (label, value) in rest)
                sb.Append("• ").Append(E(label)).Append(": ").Append(E(value)).Append('\n');
        }

        if (details.Phone is not null || details.Email is not null)
        {
            sb.Append("\n☎️ <b>Contacto</b> (Zaragoza Vivienda)\n");
            if (details.Phone is not null)
                sb.Append("📞 ").Append(E(details.Phone)).Append('\n');
            if (details.Email is not null)
                sb.Append("✉️ ").Append(E(details.Email)).Append('\n');
        }

        sb.Append($"\n🔗 <a href=\"{E(listing.Url)}\">Ver anuncio</a>");
        if (details.PlanUrl is not null)
            sb.Append($" · 📐 <a href=\"{E(details.PlanUrl)}\">Planos</a>");
        return sb.ToString();
    }

    /// <summary>Envía un texto a todos los chats; un chat que falle no impide enviar al resto.</summary>
    public async Task BroadcastTextAsync(string text, CancellationToken ct)
    {
        foreach (var chatId in ChatIds)
        {
            try
            {
                await bot.SendMessage(chatId, text, cancellationToken: ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "No se pudo enviar el mensaje al chat {ChatId}", chatId);
            }
        }
    }
}
