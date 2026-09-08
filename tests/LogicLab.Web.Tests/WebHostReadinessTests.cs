using System.Net;
using LogicLab.Infrastructure.Identity;
using LogicLab.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LogicLab.Web.Tests;

[RequiresPostgreSql]
[ClassDataSource<LogicLabWebFactory>]
internal sealed class WebHostReadinessTests(LogicLabWebFactory factory)
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Get_MigrationHistoryChanges_RejectsUntilExpectedSchemaIsRestored(
        bool identity,
        bool additionalMigration,
        CancellationToken cancellationToken)
    {
        await using var database = await PostgreSqlIdentityTestDatabase.CreateAsync();
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(
            PostgreSqlIdentityTestDatabase.ConnectionStringEnvironmentVariable))
        {
            Database = new NpgsqlConnectionStringBuilder(database.DataSource.ConnectionString).Database,
        };
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting(
            "ConnectionStrings:LogicLab",
            connection.ConnectionString));
        using var client = host.CreateClient();
        await using var scope = host.Services.CreateAsyncScope();
        var persistence = scope.ServiceProvider.GetRequiredService<LogicLabDbContext>();
        await persistence.Database.MigrateAsync(cancellationToken);
        await AssertReadinessAsync(HttpStatusCode.OK);

        DbContext context = identity
            ? scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>()
            : persistence;
        var history = context.GetService<IHistoryRepository>();
        if (additionalMigration)
        {
            await context.Database.ExecuteSqlRawAsync(
                history.GetInsertScript(new HistoryRow("20990101000000_FutureSchema", "10.0.0")),
                cancellationToken);
        }
        else
        {
            await context.GetService<IMigrator>().MigrateAsync("0", cancellationToken);
        }

        await AssertReadinessAsync(HttpStatusCode.ServiceUnavailable);
        using var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative), cancellationToken);
        await Assert.That(live.StatusCode).IsEqualTo(HttpStatusCode.OK);

        if (additionalMigration)
        {
            await context.Database.ExecuteSqlRawAsync(
                history.GetDeleteScript("20990101000000_FutureSchema"),
                cancellationToken);
        }
        else
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        await AssertReadinessAsync(HttpStatusCode.OK);

        async Task AssertReadinessAsync(HttpStatusCode expected)
        {
            using var response = await client.GetAsync(
                new Uri("/health/ready", UriKind.Relative), cancellationToken);
            await Assert.That(response.StatusCode).IsEqualTo(expected);
            await Assert.That(await response.Content.ReadAsStringAsync(cancellationToken))
                .IsEqualTo(expected == HttpStatusCode.OK ? "Healthy" : "Unhealthy");
        }
    }
}
