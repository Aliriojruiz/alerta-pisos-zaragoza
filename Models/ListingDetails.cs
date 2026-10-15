namespace PisoWatcher.Models;

/// <summary>Datos de la ficha de un piso. Cualquier campo puede faltar si la web cambia.</summary>
/// <param name="Address">Calle y planta, p. ej. "SAN RAFAEL, planta 3".</param>
/// <param name="Features">Características tal como aparecen en la ficha (Distrito, Superficie, Nº de habitaciones...).</param>
public sealed record ListingDetails(
    string? Address,
    string? Price,
    string? MinIncome,
    IReadOnlyList<KeyValuePair<string, string>> Features,
    string? Phone,
    string? Email,
    string? PlanUrl)
{
    public string? Feature(string label) =>
        Features.FirstOrDefault(f => f.Key.Equals(label, StringComparison.OrdinalIgnoreCase)).Value;
}
