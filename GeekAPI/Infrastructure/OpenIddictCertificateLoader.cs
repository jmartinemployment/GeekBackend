using System.Security.Cryptography.X509Certificates;
using OpenIddict.Server;

namespace GeekAPI.Infrastructure;

internal static class OpenIddictCertificateLoader
{
    public static void ConfigureCertificates(
        OpenIddictServerBuilder options,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger logger)
    {
        var signingMaterial = configuration["OPENIDDICT_SIGNING_CERT"]
            ?? Environment.GetEnvironmentVariable("OPENIDDICT_SIGNING_CERT");
        var signingPassword = configuration["OPENIDDICT_SIGNING_CERT_PASSWORD"]
            ?? Environment.GetEnvironmentVariable("OPENIDDICT_SIGNING_CERT_PASSWORD");
        var encryptionMaterial = configuration["OPENIDDICT_ENCRYPTION_CERT"]
            ?? Environment.GetEnvironmentVariable("OPENIDDICT_ENCRYPTION_CERT");
        var encryptionPassword = configuration["OPENIDDICT_ENCRYPTION_CERT_PASSWORD"]
            ?? Environment.GetEnvironmentVariable("OPENIDDICT_ENCRYPTION_CERT_PASSWORD");

        if (!string.IsNullOrWhiteSpace(signingMaterial))
        {
            var signingCert = LoadCertificate(signingMaterial, signingPassword);
            options.AddSigningCertificate(signingCert);

            if (!string.IsNullOrWhiteSpace(encryptionMaterial))
            {
                var encryptionCert = LoadCertificate(encryptionMaterial, encryptionPassword);
                options.AddEncryptionCertificate(encryptionCert);
            }
            else
            {
                options.AddEncryptionCertificate(signingCert);
            }

            logger.LogInformation("OpenIddict using X509 certificates from environment configuration.");
            return;
        }

        if (environment.IsProduction())
        {
            logger.LogWarning(
                "OPENIDDICT_SIGNING_CERT is not set in production. Using development certificates — set OPENIDDICT_SIGNING_CERT (PEM, base64 PFX, or file path) before go-live.");
        }

        options.AddDevelopmentEncryptionCertificate()
            .AddDevelopmentSigningCertificate();
    }

    private static X509Certificate2 LoadCertificate(string material, string? password)
    {
        var trimmed = material.Trim();
        if (trimmed.Contains("-----BEGIN", StringComparison.Ordinal))
            return X509Certificate2.CreateFromPem(trimmed);

        if (File.Exists(trimmed))
        {
            return string.IsNullOrWhiteSpace(password)
                ? X509CertificateLoader.LoadPkcs12FromFile(trimmed, null)
                : X509CertificateLoader.LoadPkcs12FromFile(trimmed, password);
        }

        var bytes = Convert.FromBase64String(trimmed);
        return string.IsNullOrWhiteSpace(password)
            ? X509CertificateLoader.LoadPkcs12(bytes, null)
            : X509CertificateLoader.LoadPkcs12(bytes, password);
    }
}
