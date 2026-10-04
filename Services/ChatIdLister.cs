using Telegram.Bot;

namespace PisoWatcher.Services;

/// <summary>
/// Modo "PisoWatcher.exe --chatids": muestra el Chat ID de cada persona que ha escrito al bot,
/// para copiarlo a appsettings.json sin tener que tocar la API a mano.
/// </summary>
public static class ChatIdLister
{
    public static async Task PrintAsync(ITelegramBotClient bot)
    {
        var updates = await bot.GetUpdates();
        var chats = updates
            .Select(u => u.Message?.Chat ?? u.MyChatMember?.Chat)
            .Where(c => c is not null)
            .DistinctBy(c => c!.Id)
            .ToList();

        if (chats.Count == 0)
        {
            Console.WriteLine("Nadie ha escrito al bot todavía (o pasaron más de 24 h).");
            Console.WriteLine("Pide a cada persona que abra el bot, pulse Iniciar y escriba 'hola'. Luego repite este comando.");
            return;
        }

        Console.WriteLine("Chats que han escrito al bot (copia los ChatId en appsettings.json):");
        Console.WriteLine();
        foreach (var chat in chats)
        {
            var name = chat!.Title ?? $"{chat.FirstName} {chat.LastName}".Trim();
            var user = chat.Username is null ? "" : $" (@{chat.Username})";
            Console.WriteLine($"  ChatId: {chat.Id,-15} {name}{user}");
        }
    }
}
