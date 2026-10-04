namespace PisoWatcher.Options;

public sealed class MonitorOptions
{
    public const string SectionName = "Monitor";

    /// <summary>URL del listado de pisos a vigilar.</summary>
    public string PortalUrl { get; set; } = string.Empty;

    /// <summary>Minutos entre cada comprobación.</summary>
    public int IntervalMinutes { get; set; } = 3;

    /// <summary>true = una sola comprobación y termina (para ejecutarlo programado, p. ej. GitHub Actions).</summary>
    public bool RunOnce { get; set; }

    /// <summary>JSON donde se guardan los avisos ya enviados. Vacío = solo en memoria.</summary>
    public string? StateFile { get; set; }

    /// <summary>Horas (0-23) a las que se envía el informe "sigo funcionando". Vacío = sin informes.</summary>
    public int[] StatusReportHours { get; set; } = [0, 12, 18];

    /// <summary>Zona horaria de StatusReportHours (IANA).</summary>
    public string TimeZone { get; set; } = "Europe/Madrid";

    /// <summary>XPath que selecciona cada tarjeta de inmueble.</summary>
    public string ListingXPath { get; set; } = "//article";

    /// <summary>XPath relativo a la tarjeta para el enlace al piso.</summary>
    public string LinkXPath { get; set; } = ".//a[@href]";

    /// <summary>XPath relativo a la tarjeta para el título (opcional).</summary>
    public string? TitleXPath { get; set; }

    /// <summary>XPath relativo a la tarjeta para el precio (opcional).</summary>
    public string? PriceXPath { get; set; }

    /// <summary>Textos que marcan un inmueble como no disponible (sin distinguir mayúsculas ni tildes).</summary>
    public string[] ExcludedKeywords { get; set; } = ["Reservado", "Reservada", "Alquilado", "Alquilada"];
}
