using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace PisoWatcher.Services;

/// <summary>
/// Validación TLS del portal. Si Monitor:AllowExpiredCertificate está activo, acepta un certificado
/// cuyo ÚNICO fallo sea estar caducado (el portal no siempre lo renueva a tiempo). Cualquier otro
/// problema (nombre que no coincide, emisor no fiable, revocado...) se sigue rechazando.
/// </summary>
public static class CertificatePolicy
{
    public static bool Validate(X509Chain? chain, SslPolicyErrors errors, bool allowExpired, ILogger logger)
    {
        if (errors == SslPolicyErrors.None)
            return true;

        var onlyExpired = allowExpired
            && errors == SslPolicyErrors.RemoteCertificateChainErrors
            && chain is not null
            && chain.ChainStatus.Length > 0
            && chain.ChainStatus.All(s => s.Status == X509ChainStatusFlags.NotTimeValid);

        if (onlyExpired)
            logger.LogWarning("El certificado SSL del portal está caducado; se acepta porque es el único fallo");
        else
            logger.LogError("Certificado SSL del portal rechazado: {Errors} {Status}",
                errors, string.Join(", ", chain?.ChainStatus.Select(s => s.Status) ?? []));

        return onlyExpired;
    }
}
