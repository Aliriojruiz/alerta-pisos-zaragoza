using System.Net;
using Microsoft.Extensions.Options;
using PisoWatcher.Models;
using PisoWatcher.Options;
using Telegram.Bot;
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

    public async Task SendListingAsync(string chatId, Listing listing, CancellationToken ct)
    {
        var text =
            "🏠 <b>¡Piso disponible!</b>\n\n" +
            $"{WebUtility.HtmlEncode(listing.Title)}\n" +
            (listing.Price is null ? "" : $"💶 {WebUtility.HtmlEncode(listing.Price)}\n") +
            $"\n🔗 <a href=\"{WebUtility.HtmlEncode(listing.Url)}\">Ver anuncio</a>";

        await bot.SendMessage(chatId, text, parseMode: ParseMode.Html, cancellationToken: ct);
        logger.LogInformation("Notificado a {ChatId}: {Url}", chatId, listing.Url);
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
