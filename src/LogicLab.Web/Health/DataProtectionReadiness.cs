using Azure.Storage.Blobs;
using Microsoft.AspNetCore.DataProtection;

namespace LogicLab.Web.Health;

internal sealed class DataProtectionReadiness(
    IDataProtectionProvider dataProtectionProvider,
    IServiceProvider services)
{
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector(
        "LogicLab.Readiness");

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        var payload = Guid.NewGuid().ToString("N");
        if (protector.Unprotect(protector.Protect(payload)) != payload)
        {
            return false;
        }

        var keyBlob = services.GetService<BlobClient>();
        return keyBlob is null
            || (await keyBlob.ExistsAsync(cancellationToken)).Value;
    }
}
