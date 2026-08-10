using LogicLab.Application.Workspaces;
using LogicLab.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace LogicLab.Infrastructure.Tests;

internal sealed class LogicLabPersistenceServiceCollectionExtensionsTests
{
    [Test]
    public async Task AddLogicLabSqlitePersistence_AllDurableSeamsShareOneRepository()
    {
        var services = new ServiceCollection();
        services.AddLogicLabSqlitePersistence(
            "Data Source=:memory:",
            durableCommandReceiptCount: 16);
        await using var provider = services.BuildServiceProvider();

        var commandRepository = provider
            .GetRequiredService<IDurableProjectRepository>();
        var catalogRepository = provider
            .GetRequiredService<IDurableProjectCatalogRepository>();
        var loader = provider.GetRequiredService<IDurableProjectLoader>();

        using (Assert.Multiple())
        {
            await Assert.That(ReferenceEquals(commandRepository, catalogRepository))
                .IsTrue();
            await Assert.That(ReferenceEquals(commandRepository, loader)).IsTrue();
        }
    }
}
