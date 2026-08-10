using LogicLab.Application.Workspaces;
using LogicLab.Web.Projects;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;

namespace LogicLab.Web.Tests;

internal sealed class DataProtectionProjectCatalogCursorProtectorTests
{
    [Test]
    public async Task Protect_SameKeyRingAndPurpose_RoundTripsOwnedCursorState()
    {
        var provider = new EphemeralDataProtectionProvider();
        var writer = new DataProtectionProjectCatalogCursorProtector(provider);
        var reader = new DataProtectionProjectCatalogCursorProtector(provider);
        var originalKey = "项目"u8.ToArray();
        var original = new ProjectCatalogCursorState(
            new AuthenticatedSubjectId("subject/用户"),
            "1",
            "workspace-policy",
            "7",
            originalKey,
            new DurableProjectId("project-7"));

        var cursor = writer.Protect(original);
        originalKey[0] = 0;
        var succeeded = reader.TryUnprotect(cursor, out var restored);

        using (Assert.Multiple())
        {
            await Assert.That(succeeded).IsTrue();
            await Assert.That(restored?.SubjectId.Value).IsEqualTo("subject/用户");
            await Assert.That(restored?.OrderingContractVersion).IsEqualTo("1");
            await Assert.That(restored?.PolicyId).IsEqualTo("workspace-policy");
            await Assert.That(restored?.PolicyRevision).IsEqualTo("7");
            await Assert.That(restored?.LastDisplayNameSortKey)
                .IsEquivalentTo("项目"u8.ToArray());
            await Assert.That(restored?.LastDurableProjectId.Value)
                .IsEqualTo("project-7");
        }
    }

    [Test]
    public async Task TryUnprotect_TamperedCursor_FailsClosed()
    {
        var protector = new DataProtectionProjectCatalogCursorProtector(
            new EphemeralDataProtectionProvider());
        var cursor = protector.Protect(State());
        var replacement = cursor.Value[^1] == 'A' ? 'B' : 'A';
        var tampered = new ProjectCatalogCursor(
            $"{cursor.Value[..^1]}{replacement}");

        var succeeded = protector.TryUnprotect(tampered, out var restored);

        using (Assert.Multiple())
        {
            await Assert.That(succeeded).IsFalse();
            await Assert.That(restored).IsNull();
        }
    }

    [Test]
    public async Task TryUnprotect_MalformedCursor_FailsClosed()
    {
        var protector = new DataProtectionProjectCatalogCursorProtector(
            new EphemeralDataProtectionProvider());

        var succeeded = protector.TryUnprotect(
            new ProjectCatalogCursor("not-a-protected-payload"),
            out var restored);

        using (Assert.Multiple())
        {
            await Assert.That(succeeded).IsFalse();
            await Assert.That(restored).IsNull();
        }
    }

    [Test]
    public async Task TryUnprotect_LostKeyRing_FailsClosed()
    {
        var writer = new DataProtectionProjectCatalogCursorProtector(
            new EphemeralDataProtectionProvider());
        var reader = new DataProtectionProjectCatalogCursorProtector(
            new EphemeralDataProtectionProvider());
        var cursor = writer.Protect(State());

        var succeeded = reader.TryUnprotect(cursor, out var restored);

        using (Assert.Multiple())
        {
            await Assert.That(succeeded).IsFalse();
            await Assert.That(restored).IsNull();
        }
    }

    [Test]
    public async Task TryUnprotect_RetainedOldKeyAfterRotation_RoundTrips()
    {
        var keyDirectoryPath = Path.Combine(
            Path.GetTempPath(),
            $"logiclab-cursor-keys-{Guid.CreateVersion7():N}");
        var keyDirectory = Directory.CreateDirectory(keyDirectoryPath);
        try
        {
            ProjectCatalogCursor cursor;
            await using (var writerServices = CreateKeyServices(keyDirectory))
            {
                var writer = new DataProtectionProjectCatalogCursorProtector(
                    writerServices.GetRequiredService<IDataProtectionProvider>());
                cursor = writer.Protect(State());
                writerServices.GetRequiredService<IKeyManager>().CreateNewKey(
                    DateTimeOffset.UtcNow.AddMinutes(-1),
                    DateTimeOffset.UtcNow.AddDays(90));
            }

            await using var readerServices = CreateKeyServices(keyDirectory);
            var reader = new DataProtectionProjectCatalogCursorProtector(
                readerServices.GetRequiredService<IDataProtectionProvider>());

            var succeeded = reader.TryUnprotect(cursor, out var restored);

            using (Assert.Multiple())
            {
                await Assert.That(succeeded).IsTrue();
                await Assert.That(restored?.LastDurableProjectId.Value)
                    .IsEqualTo("project-a");
            }
        }
        finally
        {
            Directory.Delete(keyDirectoryPath, recursive: true);
        }
    }

    private static ProjectCatalogCursorState State()
    {
        return new ProjectCatalogCursorState(
            new AuthenticatedSubjectId("subject-1"),
            "1",
            "workspace-policy",
            "1",
            "Alpha"u8.ToArray(),
            new DurableProjectId("project-a"));
    }

    private static ServiceProvider CreateKeyServices(DirectoryInfo keyDirectory)
    {
        var services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName("LogicLab.Cursor.Tests")
            .PersistKeysToFileSystem(keyDirectory);
        return services.BuildServiceProvider();
    }
}
