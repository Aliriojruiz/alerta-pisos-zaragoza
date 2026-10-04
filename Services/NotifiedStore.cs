using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PisoWatcher.Options;

namespace PisoWatcher.Services;

/// <summary>
/// Estado del vigilante: pares "chat|URL" ya notificados y datos del informe periódico.
/// En memoria por defecto; si Monitor:StateFile está configurado, se carga y guarda en ese JSON
/// (necesario en GitHub Actions, donde cada ejecución arranca de cero).
/// </summary>
public sealed class NotifiedStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly State _state;
    private readonly HashSet<string> _keys;
    private readonly string? _path;

    public NotifiedStore(IOptions<MonitorOptions> options, ILogger<NotifiedStore> logger)
    {
        _path = string.IsNullOrWhiteSpace(options.Value.StateFile) ? null : Path.GetFullPath(options.Value.StateFile);

        _state = _path is not null && File.Exists(_path)
            ? JsonSerializer.Deserialize<State>(File.ReadAllText(_path)) ?? new State()
            : new State();
        _keys = new HashSet<string>(_state.Notified, StringComparer.OrdinalIgnoreCase);

        if (_path is not null)
            logger.LogInformation("Estado cargado de {Path}: {Count} avisos previos", _path, _keys.Count);
    }

    /// <summary>Último informe periódico enviado (instante programado, en UTC).</summary>
    public DateTimeOffset? LastReportSlot => _state.LastReportSlot;

    /// <summary>Pisos distintos avisados desde el último informe.</summary>
    public int NewSinceLastReport => _state.NewSinceLastReport.Count;

    public bool Contains(string chatId, string listingId) => _keys.Contains(Key(chatId, listingId));

    public void Add(string chatId, string listingId)
    {
        if (!_keys.Add(Key(chatId, listingId)))
            return;

        if (!_state.NewSinceLastReport.Contains(listingId, StringComparer.OrdinalIgnoreCase))
            _state.NewSinceLastReport.Add(listingId);
        Save();
    }

    public void MarkReportSent(DateTimeOffset slot)
    {
        _state.LastReportSlot = slot;
        _state.NewSinceLastReport.Clear();
        Save();
    }

    private void Save()
    {
        if (_path is null)
            return;

        _state.Notified = _keys.Order(StringComparer.OrdinalIgnoreCase).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_state, JsonOptions));
    }

    // El chat va cifrado (hash) para que el archivo de estado no exponga los Chat IDs si el repositorio es público.
    private static string Key(string chatId, string listingId) =>
        $"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(chatId)))[..12]}|{listingId}";

    private sealed class State
    {
        public DateTimeOffset? LastReportSlot { get; set; }
        public List<string> NewSinceLastReport { get; set; } = [];
        public List<string> Notified { get; set; } = [];
    }
}
