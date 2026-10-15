using System.Net;
using System.Net.Security;
using Microsoft.Extensions.Options;
using PisoWatcher;
using PisoWatcher.Options;
using PisoWatcher.Services;
using Telegram.Bot;

// Portable: appsettings.json se lee siempre junto al .exe, aunque se lance desde otra carpeta
// (acceso directo, Programador de tareas...).
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

// Permite ejecutarlo como servicio de Windows si se instala con "sc create"; en consola no cambia nada.
builder.Services.AddWindowsService(o => o.ServiceName = "PisoWatcher");

builder.Services.AddOptions<MonitorOptions>()
    .BindConfiguration(MonitorOptions.SectionName)
    .Validate(o => Uri.IsWellFormedUriString(o.PortalUrl, UriKind.Absolute), "Monitor:PortalUrl debe ser una URL absoluta")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ListingXPath), "Monitor:ListingXPath es obligatorio")
    .ValidateOnStart();

var listChats = args.Contains("--chatids", StringComparer.OrdinalIgnoreCase);
// --ficha <url>: muestra en consola el aviso que se enviaría para ese piso (no envía nada).
var previewUrl = args.SkipWhile(a => !a.Equals("--ficha", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
var toolMode = listChats || previewUrl is not null;

builder.Services.AddOptions<TelegramOptions>()
    .BindConfiguration(TelegramOptions.SectionName)
    .Validate(o => !string.IsNullOrWhiteSpace(o.BotToken) && !o.BotToken.StartsWith("PON_AQUI"), "Telegram:BotToken es obligatorio")
    .Validate(o => toolMode || o.ChatIds.Any(id => !string.IsNullOrWhiteSpace(id) && !id.StartsWith("PON_AQUI")),
        "Telegram:ChatIds debe tener al menos un chat (ejecuta con --chatids para verlos)")
    .ValidateOnStart();

builder.Services.AddHttpClient<IHtmlFetcher, HttpHtmlFetcher>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(HttpDefaults.UserAgent);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es-ES,es;q=0.9");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
    })
    .ConfigurePrimaryHttpMessageHandler(sp =>
    {
        var allowExpired = sp.GetRequiredService<IOptions<MonitorOptions>>().Value.AllowExpiredCertificate;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(CertificatePolicy));
        return new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, chain, errors) =>
                    CertificatePolicy.Validate(chain, errors, allowExpired, logger)
            }
        };
    });

builder.Services.AddHttpClient("telegram");
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var token = sp.GetRequiredService<IOptions<TelegramOptions>>().Value.BotToken;
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("telegram");
    return new TelegramBotClient(token, http);
});

builder.Services.AddSingleton<NotifiedStore>();
builder.Services.AddSingleton<ScraperService>();
builder.Services.AddSingleton<TelegramNotifier>();

if (!toolMode)
    builder.Services.AddHostedService<Worker>();

var host = builder.Build();

if (listChats)
{
    await ChatIdLister.PrintAsync(host.Services.GetRequiredService<ITelegramBotClient>());
    return;
}

if (previewUrl is not null)
{
    var scraper = host.Services.GetRequiredService<ScraperService>();
    await scraper.GetAvailableListingsAsync(CancellationToken.None); // abre sesión en la web
    var listing = new PisoWatcher.Models.Listing(previewUrl, previewUrl, previewUrl, null);
    var details = await scraper.GetDetailsAsync(listing, CancellationToken.None);
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.WriteLine(TelegramNotifier.BuildListingMessage(listing, details));
    return;
}

host.Run();
