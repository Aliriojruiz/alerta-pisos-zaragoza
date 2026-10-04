namespace PisoWatcher.Options;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    public string BotToken { get; set; } = string.Empty;

    /// <summary>Chats que reciben los avisos. Cada persona debe haber pulsado "Iniciar" en el bot antes.</summary>
    public string[] ChatIds { get; set; } = [];

    /// <summary>Envía un mensaje al arrancar para confirmar que el aviso llega a todos.</summary>
    public bool NotifyOnStartup { get; set; } = true;
}
