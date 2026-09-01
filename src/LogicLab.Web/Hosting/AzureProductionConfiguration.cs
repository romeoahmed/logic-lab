using Npgsql;

namespace LogicLab.Web.Hosting;

internal sealed record AzureProductionConfiguration(
    Uri PublicOrigin,
    Guid ManagedIdentityClientId,
    Uri DataProtectionBlobUri)
{
    internal const string DataProtectionApplicationName = "LogicLab.Web";

    public static AzureProductionConfiguration Load(
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var publicOrigin = ReadHttpsUri(configuration, "Azure:PublicOrigin");
        if (publicOrigin.AbsolutePath != "/"
            || !string.IsNullOrEmpty(publicOrigin.UserInfo)
            || !string.IsNullOrEmpty(publicOrigin.Query)
            || !string.IsNullOrEmpty(publicOrigin.Fragment))
        {
            throw new InvalidOperationException(
                "Azure:PublicOrigin must be an HTTPS origin without a path, query, or fragment.");
        }

        var managedIdentityClientId = configuration["Azure:ManagedIdentityClientId"];
        if (!Guid.TryParse(managedIdentityClientId, out var clientId))
        {
            throw new InvalidOperationException(
                "Azure:ManagedIdentityClientId must be a GUID.");
        }

        var dataProtectionBlobUri = ReadHttpsUri(
            configuration,
            "Azure:DataProtectionBlobUri");
        if (dataProtectionBlobUri.AbsolutePath == "/"
            || !string.IsNullOrEmpty(dataProtectionBlobUri.UserInfo)
            || !string.IsNullOrEmpty(dataProtectionBlobUri.Query)
            || !string.IsNullOrEmpty(dataProtectionBlobUri.Fragment))
        {
            throw new InvalidOperationException(
                "Azure:DataProtectionBlobUri must identify a blob without embedded credentials, query, or fragment.");
        }

        if (string.IsNullOrWhiteSpace(
                configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            throw new InvalidOperationException(
                "APPLICATIONINSIGHTS_CONNECTION_STRING must be configured.");
        }

        var database = new NpgsqlConnectionStringBuilder(connectionString);
        if (!string.IsNullOrEmpty(database.Password)
            || string.IsNullOrWhiteSpace(database.Username)
            || database.SslMode != SslMode.VerifyFull)
        {
            throw new InvalidOperationException(
                "The production database connection must be passwordless and use SSL Mode=VerifyFull.");
        }

        return new AzureProductionConfiguration(
            publicOrigin,
            clientId,
            dataProtectionBlobUri);
    }

    private static Uri ReadHttpsUri(
        IConfiguration configuration,
        string key)
    {
        if (!Uri.TryCreate(configuration[key], UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort)
        {
            throw new InvalidOperationException(
                $"{key} must be an HTTPS URI using the default port.");
        }

        return uri;
    }
}
