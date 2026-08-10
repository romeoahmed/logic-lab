using System.Security.Cryptography;
using System.Text.Json;
using LogicLab.Application.Workspaces;
using Microsoft.AspNetCore.DataProtection;

namespace LogicLab.Web.Projects;

internal sealed class DataProtectionProjectCatalogCursorProtector
    : IProjectCatalogCursorProtector
{
    private const string Purpose =
        "LogicLab.Web.Projects.ProjectCatalogCursor.v1";

    private readonly IDataProtector protector;

    public DataProtectionProjectCatalogCursorProtector(
        IDataProtectionProvider dataProtectionProvider)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        protector = dataProtectionProvider.CreateProtector(Purpose);
    }

    public ProjectCatalogCursor Protect(ProjectCatalogCursorState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var payload = JsonSerializer.Serialize(new CursorPayload(
            state.SubjectId.Value,
            state.OrderingContractVersion,
            state.PolicyId,
            state.PolicyRevision,
            [.. state.LastDisplayNameSortKey],
            state.LastDurableProjectId.Value));
        return new ProjectCatalogCursor(protector.Protect(payload));
    }

    public bool TryUnprotect(
        ProjectCatalogCursor cursor,
        out ProjectCatalogCursorState? state)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        try
        {
            var payload = JsonSerializer.Deserialize<CursorPayload>(
                protector.Unprotect(cursor.Value));
            if (payload is null ||
                string.IsNullOrEmpty(payload.SubjectId) ||
                string.IsNullOrEmpty(payload.OrderingContractVersion) ||
                string.IsNullOrEmpty(payload.PolicyId) ||
                string.IsNullOrEmpty(payload.PolicyRevision) ||
                payload.LastDisplayNameSortKey is not { Length: > 0 } ||
                string.IsNullOrEmpty(payload.LastDurableProjectId))
            {
                state = null;
                return false;
            }

            state = new ProjectCatalogCursorState(
                new AuthenticatedSubjectId(payload.SubjectId),
                payload.OrderingContractVersion,
                payload.PolicyId,
                payload.PolicyRevision,
                payload.LastDisplayNameSortKey,
                new DurableProjectId(payload.LastDurableProjectId));
            return true;
        }
        catch (CryptographicException)
        {
            state = null;
            return false;
        }
        catch (JsonException)
        {
            state = null;
            return false;
        }
        catch (ArgumentException)
        {
            state = null;
            return false;
        }
    }

    private sealed record CursorPayload(
        string SubjectId,
        string OrderingContractVersion,
        string PolicyId,
        string PolicyRevision,
        byte[] LastDisplayNameSortKey,
        string LastDurableProjectId);
}
