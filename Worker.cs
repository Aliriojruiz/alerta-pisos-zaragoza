using System.Text;
using Microsoft.Extensions.Options;
using PisoWatcher.Models;
using PisoWatcher.Options;
using PisoWatcher.Services;

namespace PisoWatcher;

public sealed class Worker(
    ScraperService scraper,
    TelegramNotifier notifier,
    NotifiedStore notified,
    IOptions<MonitorOptions> monitorOptions,
    IOptions<TelegramOptions> telegramOptions,
    IHostApplicationLifetime lifetime,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly MonitorOptions _monitor = monitorOptions.Value;
    private readonly TimeZoneInfo _timeZone = TimeZoneInfo.FindSystemTimeZoneById(monitorOptions.Value.TimeZone);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_monitor.RunOnce)
        {
            await RunCycleAsync(stoppingToken);
            lifetime.StopApplication();
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _monitor.IntervalMinutes));
        logger.LogInformation("Vigilando {Url} cada {Minutes} min para {Count} chat(s)",
            _monitor.PortalUrl, interval.TotalMinutes, notifier.ChatIds.Count);

        try
        {
            if (telegramOptions.Value.NotifyOnStartup)
                await notifier.BroadcastTextAsync(
                    $"✅ Vigilancia de pisos iniciada. Revisaré {_monitor.PortalUrl} cada {interval.TotalMinutes} min.",
                    stoppingToken);

            using var timer = new PeriodicTimer(interval);
            do
            {
                await RunCycleAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Apagado normal del servicio.
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        ScanResult? result = null;
        string? error = null;

        try
        {
            result = await scraper.GetAvailableListingsAsync(ct);
            await NotifyNewListingsAsync(result.Available, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Un fallo puntual (red, cambio en el HTML...) no debe tumbar el Worker.
            logger.LogError(ex, "Error al comprobar el portal");
            error = ex.Message;
        }

        try
        {
            await SendStatusReportIfDueAsync(result, error, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error enviando el informe periódico");
        }
    }

    private async Task NotifyNewListingsAsync(IReadOnlyList<Listing> listings, CancellationToken ct)
    {
        foreach (var listing in listings)
        foreach (var chatId in notifier.ChatIds)
        {
            // Se controla por chat para que, si el envío falla en uno, solo se reintente en ese.
            if (notified.Contains(chatId, listing.Id))
                continue;

            try
            {
                await notifier.SendListingAsync(chatId, listing, ct);
                // Solo se marca si el envío tuvo éxito; si falla, se reintenta en la siguiente vuelta.
                notified.Add(chatId, listing.Id);
                // Telegram limita la frecuencia de envío; un pequeño respiro evita errores 429.
                await Task.Delay(TimeSpan.FromSeconds(1.1), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error enviando a Telegram ({ChatId}) el piso {Url}", chatId, listing.Url);
            }
        }
    }

    /// <summary>
    /// Envía el informe "sigo funcionando" la primera vez que se pasa por cada hora de StatusReportHours.
    /// </summary>
    private async Task SendStatusReportIfDueAsync(ScanResult? result, string? error, CancellationToken ct)
    {
        var slot = LatestReportSlot();
        if (slot is null)
            return;

        if (notified.LastReportSlot is null)
        {
            // Primera ejecución: no se envía un informe atrasado, se espera al siguiente horario.
            notified.MarkReportSent(slot.Value);
            return;
        }

        if (slot <= notified.LastReportSlot)
            return;

        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _timeZone);
        var text = new StringBuilder();

        if (error is not null)
        {
            text.AppendLine($"⚠️ Informe de las {slot.Value:HH:mm}: el servicio funciona, pero no se pudo revisar la web.");
            text.AppendLine($"Error: {error}");
        }
        else if (result!.Total == 0)
        {
            text.AppendLine($"⚠️ Informe de las {slot.Value:HH:mm}: el servicio funciona, pero no encontré ningún piso en la web.");
            text.AppendLine("Puede que la página haya cambiado de diseño.");
        }
        else
        {
            text.AppendLine($"🟢 Informe de las {slot.Value:HH:mm}: servicio funcionando");
            text.AppendLine($"Revisado a las {nowLocal:HH:mm} ({result.Total} pisos en la web).");
            text.AppendLine();
            text.AppendLine($"🏠 Disponibles ahora: {result.Available.Count}");
            text.AppendLine($"🔔 Pisos nuevos avisados desde el último informe: {notified.NewSinceLastReport}");
            foreach (var listing in result.Available)
                text.AppendLine($"• {listing.Title} {listing.Price} {listing.Url}");
            if (result.Available.Count == 0)
                text.AppendLine("Sin pisos libres por ahora.");
        }

        await notifier.BroadcastTextAsync(text.ToString().TrimEnd(), ct);
        notified.MarkReportSent(slot.Value);
        logger.LogInformation("Informe de las {Slot:HH:mm} enviado", slot.Value);
    }

    /// <summary>Último horario de informe ya alcanzado (hoy o ayer), en la zona horaria configurada.</summary>
    private DateTimeOffset? LatestReportSlot()
    {
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _timeZone);
        DateTimeOffset? latest = null;

        foreach (var daysAgo in new[] { 0, 1 })
        foreach (var hour in _monitor.StatusReportHours.Where(h => h is >= 0 and <= 23))
        {
            var local = now.Date.AddDays(-daysAgo).AddHours(hour);
            var slot = new DateTimeOffset(local, _timeZone.GetUtcOffset(local));
            if (slot <= now && (latest is null || slot > latest))
                latest = slot;
        }

        return latest;
    }
}
