namespace PisoWatcher.Models;

/// <param name="Id">Clave única para el control de duplicados (URL absoluta del piso).</param>
public sealed record Listing(string Id, string Url, string Title, string? Price);

/// <param name="Total">Inmuebles encontrados en la página (disponibles o no). 0 = la web no se pudo interpretar.</param>
public sealed record ScanResult(int Total, IReadOnlyList<Listing> Available);
